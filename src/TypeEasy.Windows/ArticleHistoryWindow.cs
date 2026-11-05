using System.IO;
using System.Globalization;
using System.Windows.Threading;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using TypeEasy.Core;

namespace TypeEasy.Windows;

/// <summary>Position heatmaps use at most 600 UTF-16 positions on screen; article versions remain distinct.</summary>
public sealed class ArticleHistoryWindow : Window
{
    private const int PageSize=600;
    private readonly ArticleHeatmapStore store;
    private readonly Dictionary<string,Course> coursesById;
    private readonly TextBox search=new(){MinWidth=220,Margin=new Thickness(0,4,12,0)};
    private readonly ComboBox articles=StatsUi.Choice(500),kind=StatsUi.Choice(145),view=StatsUi.Choice(135),scale=StatsUi.Choice(145),display=StatsUi.Choice(120);
    private readonly TextBlock summary=StatsUi.Text("",12,"Muted"),pageLabel=StatsUi.Text("",12,"Muted"),message=StatsUi.Text("",12,"Muted");
    private readonly TextBlock articleText=StatsUi.Text("",22);
    private readonly WrapPanel legend=new(){Margin=new Thickness(0,5,0,8)};
    private readonly ScrollViewer articleScroll;
    private readonly HeatmapBackdrop backdrop=new();
    private readonly Button previous,next;
    private List<ArticleHeatmapRecord> records=new();
    private ArticleHeatmapSnapshot? snapshot;
    private List<int> pageOffsets=new(){0};
    private int page,renderVersion;
    private int[] textElements=Array.Empty<int>();
    private readonly List<HeatCell> cells=new();
    private sealed record HeatCell(Run Run,Color Color,bool HasData,bool BreaksLine);
    private bool loading,manualScale;
    private string? preferredCourse,preferredFingerprint;
    private readonly HeatmapSource? preferredSource;
    private sealed record ArticleChoice(ArticleHeatmapRecord Record,string Label);

    public ArticleHistoryWindow(string userDataRoot,List<Course> courses,string? courseId=null,string? fingerprint=null,HeatmapSource? source=null)
    {
        store=new ArticleHeatmapStore(Path.Combine(userDataRoot,"Heatmaps"));coursesById=courses.GroupBy(c=>c.Id).ToDictionary(g=>g.Key,g=>g.First());preferredCourse=courseId;preferredFingerprint=fingerprint;preferredSource=source;
        StatsUi.Window(this,UiText.T("文章热力图","Text heatmap","Carte de frappe"),1120,850);MinHeight=700;
        var outer=new DockPanel{Margin=new Thickness(24)};
        var header=new DockPanel{Margin=new Thickness(0,0,0,15)};var refresh=StatsUi.Button(UiText.T("刷新","Refresh","Actualiser"),(_,_)=>LoadRecords());DockPanel.SetDock(refresh,Dock.Right);header.Children.Add(refresh);
        var ai=StatsUi.IconButton(AppIconKind.Ai,UiText.T("AI 解读","AI coach","Analyse IA"),(_,_)=>{if(snapshot is not {} value)return;var summary=JsonSerializer.Serialize(new{scope="article position history",value.Mode,positions=value.Positions.Count,opportunities=value.Positions.Sum(x=>x.Opportunities),errors=value.Positions.Sum(x=>x.Errors),slow=value.Positions.Where(x=>x.TimingSamples>0).OrderByDescending(x=>x.MedianSeconds).Take(12).Select(x=>new{character=x.Offset<value.Text.Length?value.Text[x.Offset].ToString():"",milliseconds=Math.Round(x.MedianSeconds*1000),x.TimingSamples,x.Errors})});AiPanel.Open(this,userDataRoot,summary,value.Title);});DockPanel.SetDock(ai,Dock.Right);header.Children.Add(ai);header.Children.Add(StatsUi.Text(UiText.T("文章热力图","Text heatmap","Carte de frappe"),25));DockPanel.SetDock(header,Dock.Top);outer.Children.Add(header);
        var filters=new StackPanel();var first=new WrapPanel();System.Windows.Automation.AutomationProperties.SetName(search,UiText.T("搜索文章","Search texts","Rechercher un texte"));search.ToolTip=UiText.T("按名称或类别搜索","Search by title or mode","Rechercher par titre ou mode");
        first.Children.Add(StatsUi.Field(UiText.T("搜索","Search","Rechercher"),search));first.Children.Add(StatsUi.Field(UiText.T("文章与版本","Text and version","Texte et version"),articles));filters.Children.Add(first);
        StatsUi.NamedItems(articles,nameof(ArticleChoice.Label));
        var second=new WrapPanel();second.Children.Add(StatsUi.Field(UiText.T("来源","Source","Source"),kind));second.Children.Add(StatsUi.Field(UiText.T("查看","View","Vue"),view));second.Children.Add(StatsUi.Field(UiText.T("色标","Scale","Échelle"),scale));second.Children.Add(StatsUi.Field(UiText.T("显示","Display","Affichage"),display));filters.Children.Add(second);
        kind.Items.Add(UiText.T("课程练习","Practice","Entraînement"));kind.Items.Add(UiText.T("限时测试","Timed test","Test chronométré"));kind.Width=175;kind.SelectedIndex=0;view.Items.Add(UiText.T("输入速度","Speed","Vitesse"));view.Items.Add(UiText.T("错误率","Error rate","Taux d’erreur"));view.SelectedIndex=0;scale.Items.Add(UiText.T("本篇相对","Relative","Relatif au texte"));scale.Items.Add(UiText.T("绝对耗时","Absolute time","Temps absolu"));scale.Width=160;scale.SelectedIndex=0;display.Items.Add(UiText.T("连续","Continuous","Continu"));display.Items.Add(UiText.T("分段","Stepped","Par paliers"));display.Width=140;display.SelectedIndex=0;
        filters.Children.Add(summary);filters.Children.Add(legend);DockPanel.SetDock(filters,Dock.Top);outer.Children.Add(filters);
        var bottom=new StackPanel();var pager=new DockPanel{Margin=new Thickness(0,12,0,0)};
        next=StatsUi.Button(UiText.T("下一页","Next","Suivante"),(_,_)=>{if(page+1<pageOffsets.Count){page++;RenderPage();}});previous=StatsUi.Button(UiText.T("上一页","Previous","Précédente"),(_,_)=>{if(page>0){page--;RenderPage();}});
        DockPanel.SetDock(next,Dock.Right);DockPanel.SetDock(previous,Dock.Right);pager.Children.Add(next);pager.Children.Add(previous);pager.Children.Add(pageLabel);pageLabel.VerticalAlignment=VerticalAlignment.Center;bottom.Children.Add(pager);
        bottom.Children.Add(StatsUi.Explanation(UiText.T(
            "悬停查看每个位置的样本。红下划线表示曾出错；错误率为错误次数除以输入机会。练习、测试和不同文本版本分别保存。\n颜色取该位置最近最多 31 次有效耗时的中位数。本篇基线是各位置中位数的中位数；不足 10 个位置时使用绝对耗时。少于 3 个样本显示淡色，无样本为灰色。\n耗时包含中间错键纠正。首键、中断后首键及超过 5 秒的间断不计时，仍记录错误。输入法批量提交按字均分，标为估算。\n连续显示仅平滑相邻字的边缘，字中心保留自身数值颜色，不修改数据，也不跨灰色缺口或换行。分段显示为五档颜色。这些记录不解释错误原因。",
            "Hover over a character to see its samples. A red underline marks a past error; error rate is errors divided by input attempts. Practice, tests and text versions are stored separately.\nColor uses the median of the last 31 valid timings at that position. The text baseline is the median of these position medians; fewer than 10 timed positions use absolute time. Fewer than 3 samples appear pale; no samples appear gray.\nTiming includes correcting intervening errors. The first key, the first key after an interruption and gaps over 5 seconds are not timed; errors still count. IME batches share their elapsed time equally and are marked as estimates.\nContinuous display blends only adjacent character edges; each center keeps its own value’s color. It does not change data or bridge gray gaps or lines. Stepped display uses five color levels. The records do not establish causes of errors.",
            "Survolez un caractère pour voir ses données. Un soulignement rouge signale une erreur passée ; le taux d’erreur est le nombre d’erreurs divisé par les tentatives. Entraînements, tests et versions du texte sont séparés.\nLa couleur utilise la médiane des 31 derniers temps valides à cette position. La référence du texte est la médiane de ces médianes ; avec moins de 10 positions mesurées, le temps absolu est utilisé. Moins de 3 mesures donnent une couleur pâle ; aucune mesure donne du gris.\nLe temps inclut la correction des erreurs intermédiaires. La première frappe, celle après une interruption et les pauses de plus de 5 secondes ne sont pas chronométrées ; les erreurs restent comptées. Le temps d’une saisie groupée par l’IME est réparti entre les caractères et indiqué comme estimé.\nLe mode continu relie seulement les bords des caractères voisins ; chaque centre garde la couleur de sa propre valeur. Les données restent inchangées, sans liaison à travers le gris ou entre les lignes. Le mode par paliers utilise cinq couleurs. Ces données n’expliquent pas les causes des erreurs."),UiText.T("如何阅读","Reading the heatmap","Lire la carte")));
        bottom.Children.Add(message);DockPanel.SetDock(bottom,Dock.Bottom);outer.Children.Add(bottom);
        articleText.FontFamily=new FontFamily("Microsoft YaHei UI");articleText.LineHeight=36;articleText.TextWrapping=TextWrapping.Wrap;
        var articleSurface=new Grid();articleSurface.Children.Add(backdrop);articleSurface.Children.Add(articleText);
        articleScroll=new ScrollViewer{Content=articleSurface,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        outer.Children.Add(StatsUi.Card(articleScroll,new Thickness(0)));Content=outer;
        search.TextChanged+=(_,_)=>{if(!loading)FilterArticles();};kind.SelectionChanged+=(_,_)=>{if(!loading)FilterArticles();};articles.SelectionChanged+=(_,_)=>{if(!loading)LoadSelected();};
        view.SelectionChanged+=(_,_)=>{if(!loading)RenderPage();};scale.SelectionChanged+=(_,_)=>{if(!loading){manualScale=true;RenderPage();}};
        display.SelectionChanged+=(_,_)=>{if(!loading)RenderPage();};
        articleText.SizeChanged+=(_,_)=>ScheduleGradients();
        Loaded+=(_,_)=>LoadRecords();
    }
    private HeatmapSource SelectedSource=>kind.SelectedIndex==1?HeatmapSource.Test:HeatmapSource.Practice;
    private string RecoveryMessage=>store.RecoveryNotice switch
    {
        "文章位置记录损坏，已恢复最近备份。"=>UiText.T("记录损坏，已恢复最近备份。","Damaged records restored from the latest backup.","Données endommagées restaurées depuis la dernière sauvegarde."),
        "文章位置记录无法读取，未从旧成绩推测位置数据。"=>UiText.T("无法读取文章位置记录。","Could not read the text-position records.","Impossible de lire les données par position."),
        var other=>other??""
    };
    private string ArticleTitle(ArticleHeatmapRecord record)=>coursesById.TryGetValue(record.CourseId,out var course)&&record.Title==course.Title
        ?CourseLabels.ContextTitle(course):CourseLabels.CleanTitle(record.Title)+" ("+UiText.Mode(record.Mode)+")";
    private void LoadRecords()
    {
        try
        {
            records=store.ListArticles().OrderByDescending(x=>x.UpdatedAt).ToList();
            if(preferredCourse is null&&articles.SelectedItem is ArticleChoice selected){preferredCourse=selected.Record.CourseId;preferredFingerprint=selected.Record.SourceFingerprint;}
            loading=true;
            var desired=preferredSource??records.FirstOrDefault()?.Source??HeatmapSource.Practice;
            if(articles.SelectedItem is null)kind.SelectedIndex=desired==HeatmapSource.Test?1:0;
            loading=false;FilterArticles();
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){loading=false;message.Text=UiText.T("读取记录失败：","Could not read records: ","Lecture impossible : ")+e.Message;}
    }
    private void FilterArticles()
    {
        var keep=articles.SelectedItem as ArticleChoice;var query=search.Text.Trim();
        var matches=records.Where(r=>r.Source==SelectedSource&&(query.Length==0||(ArticleTitle(r)+" "+UiText.Mode(r.Mode)+" "+r.Title).Contains(query,StringComparison.CurrentCultureIgnoreCase))).ToList();
        var versions=matches.GroupBy(x=>x.CourseId).ToDictionary(x=>x.Key,x=>x.Select(y=>y.SourceFingerprint).Distinct().Count());
        var duplicateTitles=matches.GroupBy(ArticleTitle).Where(g=>g.Select(r=>r.CourseId).Distinct().Count()>1).Select(g=>g.Key).ToHashSet();
        var choices=matches.Select(r=>
        {
            var title=ArticleTitle(r);
            if(duplicateTitles.Contains(title))title+=" — "+(coursesById.TryGetValue(r.CourseId,out var c)?CourseLabels.Path(c):r.CourseId);
            if(versions[r.CourseId]>1)title+=UiText.Format("（版本 {0}，{1}）"," (version {0}, {1})"," (version {0}, {1})",r.UpdatedAt.ToLocalTime().ToString("g",UiText.Culture),r.SourceFingerprint[..Math.Min(6,r.SourceFingerprint.Length)]);
            return new ArticleChoice(r,title);
        }).ToList();
        loading=true;articles.ItemsSource=choices;
        articles.SelectedItem=choices.FirstOrDefault(x=>x.Record.CourseId==(preferredCourse??keep?.Record.CourseId)&&x.Record.SourceFingerprint==(preferredFingerprint??keep?.Record.SourceFingerprint))??choices.FirstOrDefault();
        loading=false;preferredCourse=preferredFingerprint=null;LoadSelected();
    }
    private void LoadSelected()
    {
        page=0;snapshot=null;pageOffsets=new(){0};
        if(articles.SelectedItem is not ArticleChoice chosen){RenderPage();return;}
        try
        {
            snapshot=store.Read(chosen.Record.CourseId,chosen.Record.SourceFingerprint,chosen.Record.Source);
            textElements=StringInfo.ParseCombiningCharacters(snapshot.Text);
            if(snapshot.Text.Length>0)
            {
                pageOffsets=new List<int>();
                for(int offset=0;offset<snapshot.Text.Length;)
                {
                    pageOffsets.Add(offset);var end=Math.Min(snapshot.Text.Length,offset+PageSize);
                    if(end<snapshot.Text.Length)
                    {
                        var boundary=Array.BinarySearch(textElements,end);
                        if(boundary<0){boundary=~boundary;end=textElements[Math.Max(0,boundary-1)];if(end<=offset)end=boundary<textElements.Length?textElements[boundary]:snapshot.Text.Length;}
                    }
                    offset=end;
                }
            }
            if(!manualScale){loading=true;scale.SelectedIndex=ValidPositions().Count>=10?0:1;loading=false;}
            RenderPage();
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){snapshot=null;RenderPage();message.Text=UiText.T("读取文章失败：","Could not read the text: ","Lecture du texte impossible : ")+e.Message;}
    }
    private List<ArticlePositionSummary> ValidPositions()=>snapshot?.Positions.Where(p=>p.TimingSamples>0&&double.IsFinite(p.MedianSeconds)&&p.MedianSeconds>0).ToList()??new();
    private static double Median(IEnumerable<double> source)
    {var values=source.Order().ToArray();return values.Length==0?0:values.Length%2==1?values[values.Length/2]:(values[values.Length/2-1]+values[values.Length/2])/2;}
    private Brush Resource(string key)=>TryFindResource(key) as Brush??Brushes.Gray;
    private void RenderPage()
    {
        renderVersion++;backdrop.SetBands(Array.Empty<HeatBand>());cells.Clear();articleText.Inlines.Clear();legend.Children.Clear();previous.IsEnabled=next.IsEnabled=false;scale.IsEnabled=view.SelectedIndex==0;
        if(snapshot is null||snapshot.Text.Length==0)
        {
            articleText.Text=search.Text.Length>0?UiText.T("没有匹配的文章记录。","No matching text records.","Aucun texte correspondant."):UiText.T("暂无文章位置记录。\n\n练习文章或完成限时测试后可查看。此数据从 0.4 起记录，旧成绩无法补出逐位置耗时。","No text-position records yet.\n\nPractise a text or complete a timed test to see them here. Recording began in version 0.4; earlier results cannot supply per-position timings.","Aucune donnée par position.\n\nEntraînez-vous sur un texte ou terminez un test chronométré. Ces données existent depuis la version 0.4 ; les anciens résultats ne permettent pas de reconstituer les temps par position.");
            articleText.SetResourceReference(TextBlock.ForegroundProperty,"Muted");summary.Text="";pageLabel.Text="";message.Text=RecoveryMessage;return;
        }
        articleText.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
        var valid=ValidPositions();var baseline=Median(valid.Select(x=>x.MedianSeconds));bool relative=scale.SelectedIndex==0&&valid.Count>=10&&baseline>0;bool errors=view.SelectedIndex==1;
        AddLegend(errors,relative,baseline,valid.Count);
        var text=snapshot.Text;page=Math.Clamp(page,0,pageOffsets.Count-1);var start=pageOffsets[page];var end=page+1<pageOffsets.Count?pageOffsets[page+1]:text.Length;
        var positions=snapshot.Positions.ToDictionary(x=>x.Offset);
        for(int element=Array.BinarySearch(textElements,start);element<textElements.Length&&textElements[element]<end;element++)
        {
            int index=textElements[element];int width=(element+1<textElements.Length?textElements[element+1]:text.Length)-index;
            var value=text.Substring(index,width);positions.TryGetValue(index,out var p);
            var hasData=errors?p is {Opportunities:>0}:p is {TimingSamples:>0}&&double.IsFinite(p.MedianSeconds)&&p.MedianSeconds>0;
            var n=errors?p?.Opportunities??0:p?.TimingSamples??0;
            double measure=errors?(p is null||p.Opportunities==0?0:(double)p.Errors/p.Opportunities):p?.MedianSeconds??0;
            if(relative&&!errors)measure/=baseline;
            var run=new Run(value);var background=(Resource("Panel") as SolidColorBrush)?.Color??Colors.Black;
            var color=hasData?HeatmapPalette.Mix(background,HeatmapPalette.At(HeatmapPalette.Position(measure,errors,relative),display.SelectedIndex==0),n>=3?.90:.40):((Resource("Field") as SolidColorBrush)?.Color??background);
            run.Background=new SolidColorBrush(color);
            run.Foreground=hasData?new SolidColorBrush(color.R+color.G+color.B>440?Color.FromRgb(22,35,38):Color.FromRgb(236,244,244)):Resource("Muted");
            if(Enumerable.Range(index,width).Any(i=>positions.TryGetValue(i,out var part)&&part.Errors>0))run.TextDecorations=new TextDecorationCollection{new TextDecoration(TextDecorationLocation.Underline,new Pen(Resource("Error"),1.4),1,TextDecorationUnit.Pixel,TextDecorationUnit.Pixel)};
            var detail=width==1?Tip(index,value,p):UiText.Format("{0}：位置 {1}–{2} 合并显示，颜色取首位置\n","{0}: positions {1}–{2} share a glyph; color uses the first position\n","{0} : positions {1}–{2} regroupées ; couleur de la première position\n",value,index+1,index+width)+string.Join("\n\n",Enumerable.Range(index,width).Select(i=>Tip(i,value,positions.GetValueOrDefault(i))));
            var tooltip=new ToolTip{Content=StatsUi.Text(detail,12),Padding=new Thickness(12)};tooltip.SetResourceReference(Control.BackgroundProperty,"Panel");tooltip.SetResourceReference(Control.ForegroundProperty,"Ink");tooltip.SetResourceReference(Control.BorderBrushProperty,"Line");run.ToolTip=tooltip;articleText.Inlines.Add(run);
            cells.Add(new HeatCell(run,color,hasData,value.Contains('\r')||value.Contains('\n')));
        }
        ScheduleGradients();
        previous.IsEnabled=page>0;next.IsEnabled=page+1<pageOffsets.Count;pageLabel.Text=UiText.Format("第 {0} / {1} 页，位置 {2:N0}–{3:N0} / {4:N0}","Page {0} / {1}, positions {2:N0}–{3:N0} / {4:N0}","Page {0} / {1}, positions {2:N0}–{3:N0} / {4:N0}",page+1,pageOffsets.Count,start+1,end,text.Length);
        var total=snapshot.Positions.Sum(x=>x.Opportunities);var wrong=snapshot.Positions.Sum(x=>x.Errors);
        summary.Text=UiText.Format("{0:N0} 个位置有耗时样本，{1:N0}/{2:N0} 次错误","{0:N0} timed positions, {1:N0}/{2:N0} errors","{0:N0} positions mesurées, {1:N0}/{2:N0} erreurs",valid.Count,wrong,total);
        summary.ToolTip=UiText.Format("更新于 {0}","Updated {0}","Mise à jour : {0}",snapshot.UpdatedAt.ToLocalTime().ToString("g",UiText.Culture));
        message.Text=RecoveryMessage;articleScroll.ScrollToTop();
    }
    private void AddLegend(bool errors,bool relative,double baseline,int count)
    {
        var bar=new StackPanel{Width=350,Margin=new Thickness(0,2,16,5)};
        bar.Children.Add(new Border{Height=13,CornerRadius=new CornerRadius(3),Background=HeatmapPalette.Legend(display.SelectedIndex==0)});
        var labels=new Grid{Margin=new Thickness(0,3,0,0)};
        var left=StatsUi.Text(errors?UiText.T("错误 0%","Errors 0%","Erreurs 0 %"):relative?UiText.Format("快 ≤{0:0.00}×","Fast ≤{0:0.00}×","Rapide ≤{0:0.00}×",.5):UiText.Format("快 ≤{0:0.00}秒","Fast ≤{0:0.00}s","Rapide ≤{0:0.00}s",.1),11,"Muted");
        var middle=StatsUi.Text(errors?UiText.T("15%","15%","15 %"):relative?UiText.Format("基线 {0:0.00}×","Baseline {0:0.00}×","Référence {0:0.00}×",1):UiText.Format("{0:0.00}秒","{0:0.00}s","{0:0.00}s",.4),11,"Muted");middle.HorizontalAlignment=HorizontalAlignment.Center;
        var right=StatsUi.Text(errors?UiText.T("≥30%","≥30%","≥30 %"):relative?UiText.Format("慢 ≥{0:0.00}×","Slow ≥{0:0.00}×","Lent ≥{0:0.00}×",2):UiText.Format("慢 ≥{0:0.00}秒","Slow ≥{0:0.00}s","Lent ≥{0:0.00}s",1.6),11,"Muted");right.HorizontalAlignment=HorizontalAlignment.Right;
        labels.Children.Add(left);labels.Children.Add(middle);labels.Children.Add(right);bar.Children.Add(labels);legend.Children.Add(bar);
        var note=errors?UiText.T("灰：无记录；淡色：少于 3 次机会","Gray: no attempts; pale: fewer than 3","Gris : aucune tentative ; pâle : moins de 3")
            :(relative?UiText.Format("本篇基线 {0:0.00} 秒\n","Text baseline {0:0.00}s\n","Référence du texte {0:0.00}s\n",baseline):count<10?UiText.T("基线样本不足，使用绝对耗时\n","Too few positions for a baseline; using absolute time\n","Trop peu de positions pour une référence ; temps absolu\n"):"")
            +UiText.T("灰：无耗时样本；淡色：少于 3 个","Gray: no timings; pale: fewer than 3 samples","Gris : aucune mesure ; pâle : moins de 3");
        var info=StatsUi.Text(note,11,"Muted");info.VerticalAlignment=VerticalAlignment.Center;legend.Children.Add(info);
    }
    private void ScheduleGradients()
    {
        if(cells.Count==0)return;
        var version=renderVersion;
        Dispatcher.BeginInvoke(()=>{if(version==renderVersion)ApplyGradients();},DispatcherPriority.Loaded);
    }
    private void ApplyGradients()
    {
        // Build actual glyph geometry after wrapping. A continuous group is painted once,
        // rather than as separate antialiased Run backgrounds that leave visible vertical seams.
        var geometry=new List<(HeatCell Cell,Rect Bounds)>();var bands=new List<HeatBand>();
        foreach(var cell in cells)
        {

            if(cell.BreaksLine||!cell.Run.ContentStart.HasValidLayout)continue;
            var begin=cell.Run.ContentStart.GetCharacterRect(LogicalDirection.Forward);var end=cell.Run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            if(begin.IsEmpty||end.IsEmpty||Math.Abs(begin.Y-end.Y)>1)continue;
            var x=Math.Min(begin.Left,end.Left);var edge=Math.Max(begin.Right,end.Right);
            if(edge<=x)continue;
            var rect=new Rect(x,begin.Y,edge-x,Math.Max(begin.Height,end.Height));

            geometry.Add((cell,rect));
        }
        var measured=geometry.Select(x=>x.Cell).ToHashSet();
        foreach(var cell in cells)
        {
            if(measured.Contains(cell)){if(cell.Run.Background is not null)cell.Run.Background=null;}
            else if(cell.Run.Background is null)cell.Run.Background=new SolidColorBrush(cell.Color);
        }
        // Physical ordering also respects bidirectional runs; adjacency is never inferred by skipping a gray glyph.
        var ordered=geometry.OrderBy(x=>Math.Round(x.Bounds.Y)).ThenBy(x=>x.Bounds.X).ToArray();
        var group=new List<(HeatCell Cell,Rect Bounds)>();
        void Flush()
        {
            if(group.Count==0)return;
            var first=group[0];var last=group[^1];var rect=new Rect(first.Bounds.Left,group.Min(x=>x.Bounds.Top),last.Bounds.Right-first.Bounds.Left,group.Max(x=>x.Bounds.Bottom)-group.Min(x=>x.Bounds.Top));
            if(!first.Cell.HasData)bands.Add(new HeatBand(rect,new SolidColorBrush(first.Cell.Color)));
            else
            {
                var brush=new LinearGradientBrush{StartPoint=new Point(0,.5),EndPoint=new Point(1,.5),ColorInterpolationMode=ColorInterpolationMode.SRgbLinearInterpolation};
                for(int i=0;i<group.Count;i++)
                {
                    var current=group[i];var left=(current.Bounds.Left-rect.Left)/rect.Width;var right=(current.Bounds.Right-rect.Left)/rect.Width;var width=right-left;
                    if(display.SelectedIndex==0)
                    {
                        var leftColor=i==0?current.Cell.Color:HeatmapPalette.Mix(group[i-1].Cell.Color,current.Cell.Color,.5);
                        var rightColor=i==group.Count-1?current.Cell.Color:HeatmapPalette.Mix(current.Cell.Color,group[i+1].Cell.Color,.5);
                        brush.GradientStops.Add(new GradientStop(leftColor,left));brush.GradientStops.Add(new GradientStop(current.Cell.Color,left+width*.12));
                        brush.GradientStops.Add(new GradientStop(current.Cell.Color,right-width*.12));brush.GradientStops.Add(new GradientStop(rightColor,right));
                    }
                    else
                    {
                        brush.GradientStops.Add(new GradientStop(current.Cell.Color,left));brush.GradientStops.Add(new GradientStop(current.Cell.Color,right));
                    }
                }
                brush.Freeze();bands.Add(new HeatBand(rect,brush));
            }
            group.Clear();
        }
        foreach(var entry in ordered)
        {
            if(group.Count>0)
            {
                var previous=group[^1];
                if(previous.Cell.HasData!=entry.Cell.HasData||Math.Abs(previous.Bounds.Y-entry.Bounds.Y)>1||Math.Abs(previous.Bounds.Right-entry.Bounds.Left)>1.5)Flush();
            }
            group.Add(entry);
        }
        Flush();backdrop.SetBands(bands);
    }
    private static string Tip(int offset,string text,ArticlePositionSummary? p)
    {
        var title=UiText.Format("位置 {0:N0}：{1}","Position {0:N0}: {1}","Position {0:N0} : {1}",offset+1,StatsUi.Key(text));
        if(p is null)return title+"\n"+UiText.T("尚无输入记录。","No input recorded.","Aucune saisie enregistrée.");
        var timing=p.TimingSamples==0?UiText.T("暂无有效耗时样本","No valid timing samples","Aucune mesure de temps valide")
            :UiText.Format("中位数 {0:0.000} 秒（最近最多 31 次）\n均值 {1:0.000} 秒，{2:N0} 个有效样本","Median {0:0.000}s (up to 31 recent samples)\nMean {1:0.000}s, {2:N0} valid samples","Médiane {0:0.000}s (31 dernières mesures au plus)\nMoyenne {1:0.000}s, {2:N0} mesures valides",p.MedianSeconds,p.MeanSeconds,p.TimingSamples);
        return title+"\n"+timing+"\n"+UiText.Format("错误 {0:N0} / 机会 {1:N0}\n批量估算 {2:N0} 次，未计时 {3:N0} 次","Errors {0:N0} / attempts {1:N0}\nBatch estimates {2:N0}, untimed {3:N0}","Erreurs {0:N0} / tentatives {1:N0}\nEstimations groupées {2:N0}, non chronométrées {3:N0}",p.Errors,p.Opportunities,p.EstimatedBatchSamples,p.SkippedTimingSamples)
            +(p.TimingSamples is >0 and <3?"\n"+UiText.T("样本较少。","Small sample.","Peu de mesures."):"");
    }
}






