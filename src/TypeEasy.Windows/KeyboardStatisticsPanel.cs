using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using TypeEasy.Core;

namespace TypeEasy.Windows;

public sealed class KeyboardStatisticsPanel : StackPanel
{
    private readonly IReadOnlyList<KeyboardKeySummary> data;
    private readonly IReadOnlyList<KeyboardErrorPath> contextualPaths, aggregatePaths;
    private readonly TeachingKeyboard keyboard=new(){ShowHands=false,Margin=new Thickness(0,4,0,4)};
    private readonly ComboBox metric=StatsUi.Choice(190),scope=StatsUi.Choice(190);
    private readonly TextBlock legend=StatsUi.Text("",11,"Muted"),selection=StatsUi.Text("",15),selectionNote=StatsUi.Text("",11,"Muted");
    private readonly TextBlock filterLabel=StatsUi.Text("",12,"Muted"),rowCount=StatsUi.Text("",11,"Muted"),contextDetail=StatsUi.Text("",12,"Muted");
    private readonly ListView paths=new(){Height=285,Margin=new Thickness(0,7,0,6)};
    private readonly Button clearFilter,previous,next;
    private readonly StackPanel unmappedPanel=new();
    private readonly TextBox search=new(){MinWidth=150};
    private readonly WrapPanel chips=new(){Margin=new Thickness(0,8,0,0)};
    private readonly TextBlock pageLabel=StatsUi.Text("",11,"Muted");
    private readonly Dictionary<string,double> offsets=new();
    private string? selected;
    private KeyboardErrorPath? activePath;
    private bool rebuilding;
    private string rowView="";
    private int page;
    private KeyboardMetric SelectedMetric=>(KeyboardMetric)Math.Max(0,metric.SelectedIndex);
    private sealed record PathRow(KeyboardErrorPath Path,string Text)
    {
        public string Expected=>CharacterLabel(Path.Expected);
        public string Actual=>CharacterLabel(Path.Actual);
        public long Count=>Path.Count;
        public string Category=>UiText.Mode(Path.Mode);
        public string Context=>Path.Context is {} c?UiText.Key(c.Before)+"〔"+UiText.Key(c.Expected)+"〕"+UiText.Key(c.After):"—";
    }

    public KeyboardStatisticsPanel(AnalyticsStore store,string? mode):this(store.ReadKeys(),store.ReadConfusions(),store.ReadContextRisks(),mode) { }
    public KeyboardStatisticsPanel(IReadOnlyList<TypingKeySummary> keys,IReadOnlyList<TypingConfusion> confusions,IReadOnlyList<ContextRisk> contexts,string? mode)
    {
        data=KeyboardStatistics.Build(keys,confusions,contexts,mode);
        contextualPaths=KeyboardStatistics.ContextPaths(contexts,mode);aggregatePaths=KeyboardStatistics.AggregatePaths(confusions,mode);
        var header=new DockPanel();DockPanel.SetDock(metric,Dock.Right);header.Children.Add(metric);
        header.Children.Add(StatsUi.Text(UiText.T("错键与上下文","Mistakes and context","Erreurs et contexte"),17));Children.Add(header);
        foreach(var text in new[]{UiText.T("输入频次","Input frequency","Fréquence de saisie"),UiText.T("错误次数","Error count","Nombre d’erreurs"),
            UiText.T("错误率","Error rate","Taux d’erreur"),UiText.T("常见混淆","Common confusions","Confusions"),UiText.T("上下文关联","Context associations","Contexte")})metric.Items.Add(text);
        metric.SelectedIndex=4;
        Children.Add(legend);selection.Margin=new Thickness(0,12,0,3);Children.Add(selection);Children.Add(selectionNote);Children.Add(keyboard);
        var filters=new DockPanel();DockPanel.SetDock(scope,Dock.Right);filters.Children.Add(scope);
        scope.Items.Add(UiText.T("原文前后文","Source context","Contexte du texte"));
        scope.Items.Add(UiText.T("按误键汇总","Confusion totals","Totaux par confusion"));scope.SelectedIndex=0;
        clearFilter=StatsUi.Button(UiText.T("全部按键","All keys","Toutes les touches"),(_,_)=>{selected=null;RenderRows();RenderKeyboard();RenderUnmapped();});
        clearFilter.Margin=new Thickness(0,4,10,0);DockPanel.SetDock(clearFilter,Dock.Left);filters.Children.Add(clearFilter);
        filterLabel.VerticalAlignment=VerticalAlignment.Center;filters.Children.Add(filterLabel);Children.Add(filters);Children.Add(rowCount);
        var rowText=new FrameworkElementFactory(typeof(TextBlock));rowText.SetBinding(TextBlock.TextProperty,new Binding(nameof(PathRow.Text)));
        rowText.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);rowText.SetValue(TextBlock.FontSizeProperty,12.0);
        rowText.SetValue(TextBlock.ForegroundProperty,new DynamicResourceExtension("Ink"));
        var columns=new GridView();
        void Column(string label,string property,double width)=>columns.Columns.Add(new GridViewColumn{Header=label,DisplayMemberBinding=new Binding(property),Width=width});
        Column(UiText.T("应输入","Expected","Attendu"),nameof(PathRow.Expected),90);
        Column(UiText.T("误输入","Entered","Saisi"),nameof(PathRow.Actual),90);
        Column(UiText.T("次数","Count","Nombre"),nameof(PathRow.Count),70);
        Column(UiText.T("原文语境","Source context","Contexte"),nameof(PathRow.Context),250);
        Column(UiText.T("类别","Category","Catégorie"),nameof(PathRow.Category),170);
        paths.View=columns;paths.SetResourceReference(Control.BackgroundProperty,"Panel");paths.SetResourceReference(Control.ForegroundProperty,"Ink");paths.BorderThickness=new Thickness(0);
        VirtualizingPanel.SetIsVirtualizing(paths,true);VirtualizingPanel.SetVirtualizationMode(paths,VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(paths,true);ScrollViewer.SetVerticalScrollBarVisibility(paths,ScrollBarVisibility.Auto);
        System.Windows.Automation.AutomationProperties.SetName(paths,UiText.T("可选择的错键记录","Selectable mistake records","Erreurs sélectionnables"));
        Children.Add(paths);Children.Add(contextDetail);
        paths.SelectionChanged+=(_,_)=>{
            if(rebuilding)return;activePath=(paths.SelectedItem as PathRow)?.Path;
            // Row clicks do not rebuild the list or reset its scroll position.
            RenderSelection();RenderKeyboard();RenderUnmapped();
        };
        keyboard.KeySelected+=id=>{selected=id;RenderRows();RenderKeyboard();RenderUnmapped();};
        var unmappedHeader=new DockPanel();DockPanel.SetDock(search,Dock.Right);unmappedHeader.Children.Add(search);
        unmappedHeader.Children.Add(StatsUi.Text(UiText.T("提交字符","Submitted characters","Caractères validés"),14));
        search.ToolTip=UiText.T("搜索字符","Find a character","Rechercher un caractère");
        System.Windows.Automation.AutomationProperties.SetName(search,search.ToolTip.ToString());
        unmappedPanel.Children.Add(unmappedHeader);unmappedPanel.Children.Add(chips);
        var pager=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,6)};
        previous=StatsUi.Button(UiText.T("上一页","Previous","Précédent"),(_,_)=>{if(page>0){page--;RenderUnmapped();}});
        next=StatsUi.Button(UiText.T("下一页","Next","Suivant"),(_,_)=>{page++;RenderUnmapped();});
        pager.Children.Add(previous);pager.Children.Add(next);pager.Children.Add(pageLabel);unmappedPanel.Children.Add(pager);Children.Add(unmappedPanel);
        Children.Add(StatsUi.Explanation(UiText.T(
            "前后文取自原文。每行是一种具体误输入；汇总视图单独统计，不与上下文重复相加。圆点与实线为应按，叉号与虚线为误按。\n频次统计提交字符。大写与符号按标准键位示意，Shift、CapsLock 和物理按键事件未记录。数字键盘按课程映射。输入法只显示提交字符。“其他”是未细分的汇总桶。\n上下文只描述关联；至少20次机会、3次错误才作比较，较基线高10个百分点才标记。",
            "Contexts come from the source text. Each row is one specific wrong input; separate totals are not added to context counts. A dot and solid outline mark the expected key; a cross and dashed outline mark the entered key.\nFrequency counts submitted characters. Positions follow the standard keyboard and course layout; physical key, Shift and Caps Lock events are not recorded. IME commits remain text. Other is an unspecified aggregate.\nContext is association only. Comparisons require 20 attempts and 3 errors; a difference of 10 percentage points flags a context.",
            "Les contextes proviennent du texte d’origine. Chaque ligne décrit une saisie erronée ; les totaux distincts ne s’y ajoutent pas. Le point et le contour plein indiquent la touche attendue ; la croix et les tirets, la touche saisie.\nLa fréquence compte les caractères validés. Les positions suivent le clavier standard et le cours ; les événements physiques, Maj et Verr.maj ne sont pas enregistrés. Les saisies IME restent du texte. Autres désigne un groupe non détaillé.\nLe contexte indique une association seulement. Il faut 20 tentatives et 3 erreurs ; une hausse de 10 points signale un contexte."),UiText.T("统计口径","How counts work","Méthode de calcul")));
        metric.SelectionChanged+=(_,_)=>{
            if(SelectedMetric==KeyboardMetric.Confusions)scope.SelectedIndex=1;
            else if(SelectedMetric==KeyboardMetric.Context)scope.SelectedIndex=0;
            page=0;Render();
        };
        scope.SelectionChanged+=(_,_)=>{RenderRows();RenderKeyboard();RenderUnmapped();};
        search.TextChanged+=(_,_)=>{page=0;RenderUnmapped();};Render();
    }
    private double Normalize(KeyboardKeySummary item)
    {
        var value=item.Value(SelectedMetric);
        if(SelectedMetric==KeyboardMetric.ErrorRate)return Math.Clamp(value/.5,0,1);
        if(SelectedMetric==KeyboardMetric.Context)return Math.Clamp(value/.30,0,1);
        var maximum=data.Select(x=>x.Value(SelectedMetric)).DefaultIfEmpty(0).Max();
        return maximum<=0?0:Math.Log(1+value)/Math.Log(1+maximum);
    }
    private KeyboardHeatValue Heat(KeyboardKeySummary item)=>new(Normalize(item),item.HasData(SelectedMetric),item.LowSample(SelectedMetric),Tooltip(item));
    private void Render()
    {
        var max=data.Select(x=>x.Value(SelectedMetric)).DefaultIfEmpty(0).Max();
        var range=SelectedMetric==KeyboardMetric.ErrorRate?"0–50%+":SelectedMetric==KeyboardMetric.Context
            ?UiText.T("较基线 0–30+ 个百分点","Above baseline: 0–30+ percentage points","Écart à la référence : 0–30+ points")
            :UiText.Format("0–{0:N0} 次","0–{0:N0} occurrences","0–{0:N0} occurrences",max);
        legend.Text=range+"    "+UiText.T("蓝→橙：少→多；淡色：样本少","Blue to orange: low to high; faint: small sample","Bleu à orange : faible à élevé ; clair : petit échantillon");
        RenderRows();RenderKeyboard();RenderUnmapped();
    }
    private void RenderKeyboard()
    {
        var mapped=data.Where(x=>!x.Unmapped).ToArray();
        keyboard.Visibility=mapped.Length==0&&data.Count>0?Visibility.Collapsed:Visibility.Visible;
        keyboard.SetStatistics(mapped.ToDictionary(x=>x.KeyId,Heat),mapped.Any(x=>x.KeyId.StartsWith("Num",StringComparison.Ordinal)),selected);
        keyboard.SetErrorPath(activePath);
    }
    private void RenderRows()
    {
        var wantedContext=scope.SelectedIndex==0;
        var candidates=(wantedContext?contextualPaths:aggregatePaths).Where(p=>selected is null||p.MatchesKey(selected)).ToArray();
        var fallback=wantedContext&&candidates.Length==0;
        if(fallback)candidates=aggregatePaths.Where(p=>selected is null||p.MatchesKey(selected)).ToArray();
        var view=(wantedContext&&!fallback?"context:":"aggregate:")+selected;
        var item=data.FirstOrDefault(d=>d.KeyId==selected);
        filterLabel.Text=selected is null?UiText.T("全部错键","All mistakes","Toutes les erreurs")
            :item is null?UiText.Key(selected):DisplayLabel(item);
        clearFilter.Visibility=selected is null?Visibility.Collapsed:Visibility.Visible;
        rowCount.Text=fallback&&candidates.Length>0?UiText.T("没有前后文记录，显示误键汇总。","No context records; showing confusion totals.","Aucun contexte enregistré ; affichage des totaux.")
            :candidates.Length==0?UiText.T("暂无错键记录。","No recorded mistakes.","Aucune erreur enregistrée.")
            :UiText.Format("{0:N0} 条记录","{0:N0} records","{0:N0} observations",candidates.Length);
        var oldRows=paths.ItemsSource as PathRow[];
        if(oldRows is not null&&oldRows.Select(r=>r.Path.Id).SequenceEqual(candidates.Select(p=>p.Id))&&rowView==view){RenderSelection();return;}
        var scroll=FindVisual<ScrollViewer>(paths);
        if(scroll is not null&&rowView.Length>0)offsets[rowView]=scroll.VerticalOffset;
        var previousPath=activePath;var rows=candidates.Select(p=>new PathRow(p,RowText(p))).ToArray();
        rebuilding=true;
        try{
            paths.ItemsSource=rows;
            paths.SelectedItem=rows.FirstOrDefault(r=>r.Path.Id==previousPath?.Id)
                ??rows.FirstOrDefault(r=>r.Path.Mode==previousPath?.Mode&&r.Path.Expected==previousPath?.Expected&&r.Path.Actual==previousPath?.Actual)
                ??rows.FirstOrDefault();
            activePath=(paths.SelectedItem as PathRow)?.Path;
        }finally{rebuilding=false;}
        rowView=view;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{if(rowView==view)FindVisual<ScrollViewer>(paths)?.ScrollToVerticalOffset(offsets.GetValueOrDefault(view));}));
        RenderSelection();
    }
    private static string RowText(KeyboardErrorPath path)
    {
        var text=UiText.Format("应按 {0} → 误按 {1}    {2:N0} 次    {3}","Expected {0} → entered {1}    {2:N0} times    {3}","Attendu : {0} → saisi : {1}    {2:N0} fois    {3}",
            CharacterLabel(path.Expected),CharacterLabel(path.Actual),path.Count,UiText.Mode(path.Mode));
        if(path.Context is {} context)text+="\n"+UiText.T("原文：","Source: ","Texte : ")+UiText.Key(context.Before)+"〔"+UiText.Key(context.Expected)+"〕"+UiText.Key(context.After);
        return text;
    }
    private void RenderSelection()
    {
        selection.Inlines.Clear();contextDetail.Text="";selectionNote.Text="";selectionNote.Visibility=Visibility.Collapsed;
        if(activePath is null){selection.Text=UiText.T("选择错键记录查看对应位置","Select a mistake to see its key positions","Sélectionnez une erreur pour voir les touches");return;}
        var field=(TryFindResource("Field") as SolidColorBrush)?.Color??Colors.Black;var dark=field.R+field.G+field.B<384;
        selection.Inlines.Add(new Run(UiText.T("● 应按 ","● Expected ","● Attendu : ")+CharacterLabel(activePath.Expected)){Foreground=new SolidColorBrush(TeachingKeyboard.ErrorPathColor(true,dark)),FontWeight=FontWeights.SemiBold});
        selection.Inlines.Add(new Run("    →    "));
        selection.Inlines.Add(new Run(UiText.T("× 误按 ","× Entered ","× Saisi : ")+CharacterLabel(activePath.Actual)){Foreground=new SolidColorBrush(TeachingKeyboard.ErrorPathColor(false,dark)),FontWeight=FontWeights.SemiBold});
        selection.ToolTip=UiText.T("实线与圆点：应按；虚线与叉号：误按。","Solid outline and dot: expected; dashed outline and cross: entered.","Contour plein et point : attendu ; tirets et croix : saisi.");
        if(activePath.ExpectedPhysicalKey is null||activePath.ActualPhysicalKey is null)selectionNote.Text=UiText.T("提交字符；无法从这条记录确定全部物理键。","Submitted text; this record does not identify all physical keys.","Texte validé ; cette observation ne révèle pas toutes les touches physiques.");
        if(activePath.ExpectedRequiresShift||activePath.ActualCharacterRequiresShift)
            selectionNote.Text+=" "+UiText.Format("标准键位：应输入 {0}{1}，误输入 {2}{3}；实际 Shift / CapsLock 未记录。","Standard mapping: expected {0}{1}, entered {2}{3}; actual Shift / Caps Lock events are unknown.","Disposition standard : attendu {0}{1}, saisi {2}{3} ; événements Maj / Verr.maj inconnus.",
                CharacterLabel(activePath.Expected),activePath.ExpectedRequiresShift?" (+ Shift)":"",CharacterLabel(activePath.Actual),activePath.ActualCharacterRequiresShift?" (+ Shift)":"");
        selectionNote.Text=selectionNote.Text.Trim();selectionNote.Visibility=selectionNote.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
        if(activePath.Context is {} context){
            var flag=context.IsRisk?UiText.T("值得留意","Worth reviewing","À examiner"):context.IsEligible?UiText.T("未达提示阈值","Below review threshold","Seuil non atteint"):UiText.T("样本不足","Small sample","Petit échantillon");
            contextDetail.Text=UiText.Format("此误按 {0}/{1}；语境内全部错误 {2}/{1}；基线 {3}/{4}。{5}","This confusion {0}/{1}; all context errors {2}/{1}; baseline {3}/{4}. {5}","Cette confusion : {0}/{1} ; erreurs du contexte : {2}/{1} ; référence : {3}/{4}. {5}",
                activePath.Count,context.Opportunities,context.Errors,context.BaselineErrors,context.BaselineOpportunities,flag);
        }
    }
    private void RenderUnmapped()
    {
        chips.Children.Clear();var all=data.Where(x=>x.Unmapped).ToArray();unmappedPanel.Visibility=all.Length==0?Visibility.Collapsed:Visibility.Visible;
        var values=all.Where(x=>search.Text.Length==0||x.Label.Contains(search.Text,StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(x=>x.Value(SelectedMetric)).ThenBy(x=>x.Label).ToArray();
        const int size=48;var pages=Math.Max(1,(values.Length+size-1)/size);page=Math.Clamp(page,0,pages-1);
        var field=(TryFindResource("Field") as SolidColorBrush)?.Color??Colors.DarkSlateGray;var dark=field.R+field.G+field.B<384;
        foreach(var item in values.Skip(page*size).Take(size)){
            var heat=Heat(item);var color=TeachingKeyboard.StatisticsColor(field,heat);
            var expected=item.KeyId==activePath?.ExpectedId;var actual=item.KeyId==activePath?.ActualId;
            var label=(expected?"● ":actual?"× ":"")+DisplayLabel(item);
            var button=StatsUi.Button(label,(_,_)=>{selected=item.KeyId;RenderRows();RenderKeyboard();RenderUnmapped();});
            button.MinWidth=40;button.Padding=new Thickness(10,7,10,7);button.Margin=new Thickness(0,0,5,5);
            button.Background=new SolidColorBrush(color);button.Foreground=new SolidColorBrush(color.R+color.G+color.B>440?Colors.Black:Colors.White);
            if(expected||actual){button.BorderBrush=new SolidColorBrush(TeachingKeyboard.ErrorPathColor(expected,dark));button.BorderThickness=new Thickness(2);}
            button.ToolTip=heat.Tooltip;chips.Children.Add(button);
        }
        previous.IsEnabled=page>0;next.IsEnabled=page+1<pages;
        pageLabel.Text=UiText.Format("  {0}/{1} 页，{2} 个字符","  Page {0}/{1}, {2} characters","  Page {0}/{1}, {2} caractères",page+1,pages,values.Length);
    }
    private string Tooltip(KeyboardKeySummary item)
    {
        var rate=item.Opportunities==0?UiText.T("未知","unknown","inconnu"):item.ErrorRate.ToString("P1",UiText.Culture);
        var text=DisplayLabel(item)+"\n"+UiText.Format("提交 {0:N0} 次；目标机会 {1:N0} 次\n错误 {2:N0} 次，错误率 {3}",
            "Submitted {0:N0}; expected attempts {1:N0}\nErrors {2:N0}, error rate {3}",
            "Validés {0:N0} ; tentatives attendues {1:N0}\nErreurs {2:N0}, taux d’erreur {3}",item.Submitted,item.Opportunities,item.Errors,rate);
        if(SelectedMetric==KeyboardMetric.Confusions)text+="\n"+UiText.Format("最常见混淆 {0:N0} 次","Most frequent confusion: {0:N0} occurrences","Confusion la plus fréquente : {0:N0} occurrences",item.Value(KeyboardMetric.Confusions));
        if(SelectedMetric==KeyboardMetric.Context)text+="\n"+(item.Contexts.Any(c=>c.IsEligible)?UiText.Format("合格上下文最大正差值 +{0:0.0} 个百分点","Largest eligible increase above baseline: +{0:0.0} percentage points","Hausse admissible maximale par rapport à la référence : +{0:0.0} points",item.Value(KeyboardMetric.Context)*100):UiText.T("暂无达到比较样本量的上下文","No context yet meets the comparison sample size","Aucun contexte n’atteint encore la taille d’échantillon requise"));
        if(item.Opportunities==0)text+="\n"+UiText.T("该键无目标机会，错误率未知。","No expected attempts; error rate is unknown.","Aucune tentative attendue ; taux d’erreur inconnu.");
        if(item.ShiftOpportunities>0)text+="\n"+UiText.Format("含大写或符号 {0:N0} 次；未记录实际Shift事件。","Includes {0:N0} capital/symbol attempts; actual Shift presses are unknown.","Inclut {0:N0} tentatives de majuscules/symboles ; pressions réelles sur Maj inconnues.",item.ShiftOpportunities);
        if(!item.HasData(SelectedMetric))text+="\n"+UiText.T("此指标无数据","No data for this metric","Aucune donnée pour cet indicateur");
        else if(item.LowSample(SelectedMetric))text+="\n"+UiText.T("样本不足，谨慎比较","Small sample; compare cautiously","Petit échantillon ; comparer avec prudence");
        return text;
    }
    private static T? FindVisual<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){
            var child=VisualTreeHelper.GetChild(parent,i);if(child is T match)return match;
            if(FindVisual<T>(child) is {} nested)return nested;
        }
        return null;
    }
    private static string CharacterLabel(string value)=>value=="（其他）"?UiText.T("其他（未细分）","Other (unspecified)","Autres (non détaillés)"):UiText.Key(value);
    private static string DisplayLabel(KeyboardKeySummary item)
    {
        if(item.Unmapped||!item.KeyId.StartsWith("Num",StringComparison.Ordinal))return CharacterLabel(item.Label);
        var key=item.KeyId[3..] switch{"Add"=>"+","Subtract"=>"−","Multiply"=>"*","Divide"=>"/","Decimal"=>".","Enter"=>"\n",var digit=>digit};
        return UiText.T("数字键盘 ","Number pad ","Pavé numérique ")+UiText.Key(key);
    }
}
