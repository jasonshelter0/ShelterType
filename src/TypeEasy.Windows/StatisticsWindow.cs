using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using TypeEasy.Core;

namespace TypeEasy.Windows;

public sealed class StatisticsWindow : Window
{
    private readonly string root;
    private readonly List<Course> courses;
    private readonly AnalyticsStore analytics;
    private readonly StackPanel body=new(), filtersBar=new(), sideActions=new();
    private readonly ComboBox practiceMode=StatsUi.Choice(180),calendarYear=StatsUi.Choice(105),testMode=StatsUi.Choice(170),duration=StatsUi.Choice(120),source=StatsUi.Choice(340);
    private readonly TextBlock status=StatsUi.Text("",12,"Muted");
    private bool refreshing;
    private int section,historyPage;
    private readonly List<Button> sectionsButtons=new();
    private sealed record HistoryRow(SpeedTestResult Run){
        public DateTimeOffset FinishedAt=>Run.FinishedAt;
        public string SourceTitle=>CourseLabels.CleanTitle(Run.SourceTitle);
        public double CharactersPerMinute=>Run.CharactersPerMinute;
        public double Accuracy=>Run.Accuracy;
        public string Status=>Run.Status switch{SpeedTestStatus.Completed=>UiText.T("完成","Completed","Terminé"),SpeedTestStatus.Exhausted=>UiText.T("文章打完","Text finished","Texte fini"),_=>UiText.T("中止","Stopped","Arrêté")};
    }
    private sealed record ModeChoice(string? Value,string Label);
    private sealed record SourceChoice(string Id,string Title,string Fingerprint);
    private sealed record DurationChoice(int Seconds,string Label);
    private string? PracticeMode=>(practiceMode.SelectedItem as ModeChoice)?.Value;
    private string? TestMode=>(testMode.SelectedItem as ModeChoice)?.Value;

    public StatisticsWindow(string userDataRoot,List<Course> availableCourses)
    {
        root=userDataRoot;courses=availableCourses;analytics=new AnalyticsStore(Path.Combine(root,"Analytics"));
        StatsUi.Window(this,UiText.T("个人统计","Statistics","Statistiques"),1100,850);
        MinWidth=1080;
        var outer=new DockPanel{Margin=new Thickness(20)};
        var sidebar=new DockPanel{Width=178,Margin=new Thickness(0,0,22,0)};
        DockPanel.SetDock(sidebar,Dock.Left);outer.Children.Add(sidebar);
        var sidebarBottom=new StackPanel();
        sidebarBottom.Children.Add(sideActions);
        var ai=StatsUi.Button(UiText.T("AI 解读","AI coach","Analyse IA"),(_,_)=>AiPanel.Open(this,root,section is 0 or 4?TestAnalytics.OverallSummary(analytics):TestAnalytics.CompactSummary(FilteredTests()),UiText.T("个人统计","Statistics","Statistiques")));
        ai.Margin=new Thickness(0,14,0,8);sidebarBottom.Children.Add(ai);
        var refresh=StatsUi.Button(UiText.T("刷新","Refresh","Actualiser"),(_,_)=>Refresh());refresh.Margin=new Thickness(0);sidebarBottom.Children.Add(refresh);
        DockPanel.SetDock(sidebarBottom,Dock.Bottom);sidebar.Children.Add(sidebarBottom);
        var navigation=new StackPanel();navigation.Children.Add(StatsUi.Text(UiText.T("个人统计","Statistics","Statistiques"),23));
        var sections=new[]{UiText.T("总览","Overview","Vue d’ensemble"),UiText.T("测试历史","Test history","Historique des tests"),UiText.T("错键分析","Mistakes","Erreurs"),UiText.T("文章热力图","Text heatmaps","Cartes des textes"),UiText.T("累计错键","Lifetime mistakes","Erreurs cumulées")};
        for(int i=0;i<sections.Length;i++){int index=i;var button=StatsUi.Button(sections[i],(_,_)=>{section=index;historyPage=0;Refresh();});button.SetResourceReference(StyleProperty,"SidebarNavButton");button.Margin=new Thickness(0,i==0?24:5,0,0);button.Padding=new Thickness(10,12,10,12);sectionsButtons.Add(button);navigation.Children.Add(button);}
        sidebar.Children.Add(navigation);
        var content=new DockPanel();outer.Children.Add(content);
        DockPanel.SetDock(status,Dock.Bottom);content.Children.Add(status);
        filtersBar.Margin=new Thickness(0,0,0,12);DockPanel.SetDock(filtersBar,Dock.Top);content.Children.Add(filtersBar);
        content.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});Content=outer;
        StatsUi.NamedItems(practiceMode,nameof(ModeChoice.Label));StatsUi.NamedItems(testMode,nameof(ModeChoice.Label));StatsUi.NamedItems(duration,nameof(DurationChoice.Label));StatsUi.NamedItems(source,nameof(SourceChoice.Title));
        practiceMode.Items.Add(new ModeChoice(null,UiText.T("全部类别","All categories","Toutes catégories")));
        foreach(var mode in courses.Select(TypingMode.ForCourse).Distinct().Order())practiceMode.Items.Add(new ModeChoice(mode,UiText.Mode(mode)));
        testMode.Items.Add(new ModeChoice(null,UiText.T("全部类别","All categories","Toutes catégories")));
        foreach(var mode in courses.Where(IsTextCourse).Select(TypingMode.ForCourse).Distinct().Order())testMode.Items.Add(new ModeChoice(mode,UiText.Mode(mode)));
        practiceMode.SelectedIndex=0;testMode.SelectedIndex=testMode.Items.Count>0?0:-1;
        duration.Items.Add(new DurationChoice(0,UiText.T("全部时长","All durations","Toutes durées")));
        foreach(var seconds in new[]{60,180,300,600})duration.Items.Add(new DurationChoice(seconds,UiText.Format("{0} 分钟","{0} min","{0} min",seconds/60)));
        duration.SelectedIndex=0;SpeedTestResult? latest=null;
        try
        {
            foreach(var mode in analytics.ReadDays().Select(x=>x.Mode).Distinct().Where(m=>!practiceMode.Items.Cast<ModeChoice>().Any(x=>x.Value==m)))practiceMode.Items.Add(new ModeChoice(mode,UiText.Mode(mode)));
            latest=analytics.ReadSpeedTests().FirstOrDefault(x=>x.Status==SpeedTestStatus.Completed);
        }
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){status.Text=e.Message;}
        testMode.SelectedItem=testMode.Items.Cast<ModeChoice>().FirstOrDefault(x=>x.Value==(latest?.Mode??"英文"))??testMode.SelectedItem;
        if(latest is not null)duration.SelectedItem=duration.Items.Cast<DurationChoice>().FirstOrDefault(x=>x.Seconds==latest.DurationSeconds)??duration.SelectedItem;
        testMode.SelectedIndex=0;duration.SelectedIndex=0;
        LoadSources();
        if(latest is not null)source.SelectedItem=source.Items.Cast<SourceChoice>().FirstOrDefault(x=>x.Id==latest.SourceId&&x.Fingerprint==latest.SourceFingerprint)??source.SelectedItem;
        source.SelectedIndex=0;
        calendarYear.SelectionChanged+=(_,_)=>Refresh();practiceMode.SelectionChanged+=(_,_)=>Refresh();testMode.SelectionChanged+=(_,_)=>{if(!refreshing){LoadSources();Refresh();}};duration.SelectionChanged+=(_,_)=>Refresh();source.SelectionChanged+=(_,_)=>Refresh();Loaded+=(_,_)=>Refresh();
    }
    private static bool IsTextCourse(Course c)=>c.Category is "英文" or "中文"&&(c.Id.Contains("/e_general/",StringComparison.OrdinalIgnoreCase)||c.Variant.Contains("文章"));
    private void LoadSources()
    {
        var keep=source.SelectedItem as SourceChoice;refreshing=true;
        try
        {
            var current=courses.Where(IsTextCourse).Where(x=>TestMode is null||TypingMode.ForCourse(x)==TestMode).OrderByDescending(x=>x.Id.Contains("/e_general/",StringComparison.OrdinalIgnoreCase)).Select(x=>new SourceChoice(x.Id,CourseLabels.ContextTitle(x),SpeedTestSession.SourceFingerprintFor(x)));
            var historical=analytics.ReadSpeedTests().Where(x=>TestMode is null||x.Mode==TestMode).Select(x=>new SourceChoice(x.SourceId,
                string.Join(" / ",new[]{UiText.Mode(x.Mode),CourseLabels.Group(x.SourceGroup),CourseLabels.CleanTitle(x.SourceTitle)}.Where(s=>s.Length>0).Distinct())
                +UiText.T("（历史版本）"," (historical)"," (version historique)"),x.SourceFingerprint));
            var list=current.Concat(historical).DistinctBy(x=>(x.Id,x.Fingerprint)).ToList();list.Insert(0,new SourceChoice("",UiText.T("全部文章","All texts","Tous les textes"),""));source.ItemsSource=list;source.SelectedItem=list.FirstOrDefault(x=>x.Id==keep?.Id&&x.Fingerprint==keep?.Fingerprint)??list.FirstOrDefault();
        }
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){status.Text=UiText.T("读取失败：","Read failed: ","Lecture impossible : ")+e.Message;}
        finally{refreshing=false;}
    }
    private void Refresh()
    {
        if(refreshing)return;refreshing=true;
        try
        {
            foreach(var control in new FrameworkElement[]{practiceMode,calendarYear,testMode,duration,source})if(control.Parent is Panel parent)parent.Children.Remove(control);
            foreach(var button in sectionsButtons)button.SetResourceReference(Control.BackgroundProperty,sectionsButtons.IndexOf(button)==section?"Selected":"Bg");
            body.Children.Clear();filtersBar.Children.Clear();sideActions.Children.Clear();var progress=new List<PracticeState>();var unreadable=0;var directory=Path.Combine(root,"Progress");
            if(Directory.Exists(directory))foreach(var path in Directory.EnumerateFiles(directory,"*.json"))
            {
                try{var value=JsonSerializer.Deserialize<PracticeState>(File.ReadAllText(path));if(value is not null&&double.IsFinite(value.ElapsedSeconds)&&value.ElapsedSeconds>=0&&value.CorrectInputs>=0&&value.Errors>=0)progress.Add(value);else unreadable++;}
                catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){unreadable++;}
            }
            if(section==0){
                var cards=new System.Windows.Controls.Primitives.UniformGrid{Columns=4,Margin=new Thickness(0,0,0,12)};
                void Number(string value,string label){var box=new StackPanel();box.Children.Add(StatsUi.Text(value,30,"Accent"));box.Children.Add(StatsUi.Text(label,12,"Muted"));cards.Children.Add(StatsUi.Card(box,new Thickness(0,0,10,0)));}
                Number(progress.Count.ToString("N0"),UiText.T("练习过的课程","Courses practised","Cours pratiqués"));
                var days=analytics.ReadDays();Number((days.Sum(x=>x.ActiveSeconds)/3600).ToString("0.0"),UiText.T("累计练习小时","Practice hours","Heures de pratique"));
                Number(analytics.ReadSpeedTests(true).Count.ToString("N0"),UiText.T("已保存测试","Saved tests","Tests enregistrés"));
                var correct=days.Sum(x=>x.CorrectInputs);var errors=days.Sum(x=>x.Errors);Number(correct+errors==0?"—":((double)correct/(correct+errors)).ToString("P1"),UiText.T("累计正确率","Overall accuracy","Précision globale"));
                body.Children.Add(cards);BuildDailyCalendar(days);
            }else if(section==1){BuildTestCharts();BuildHistory();}
            else if(section==2){
                BuildScope();var runs=FilteredTests();var details=new TestAnalytics(runs);
                body.Children.Add(StatsUi.Card(new KeyboardStatisticsPanel(details.Keys,details.Confusions,details.Contexts,null)));
                if(details.MissingDetails>0)body.Children.Add(StatsUi.Text(UiText.Format("{0} 次旧测试没有逐键记录，仍保留成绩。","{0} older tests have scores but no per-key records.","{0} anciens tests n’ont pas de détail par touche.",details.MissingDetails),12,"Muted"));
            }else if(section==4){body.Children.Add(StatsUi.Card(new KeyboardStatisticsPanel(analytics,null)));}else {
                BuildScope();var choice=source.SelectedItem as SourceChoice;
                body.Children.Add(StatsUi.Text(UiText.T("选择文章查看逐字速度与错误热力图。全部历史的汇总键盘见“错键分析”。","Choose a text for its position heatmap. All-test keyboard totals are in Mistakes.","Choisissez un texte pour sa carte. Les totaux clavier sont dans Erreurs."),14,"Muted"));
                sideActions.Children.Add(StatsUi.Button(UiText.T("打开文章热力图","Open text heatmap","Ouvrir la carte"),(_,_)=>new ArticleHistoryWindow(root,courses,choice?.Id.Length>0?choice.Id:null,choice?.Fingerprint,HeatmapSource.Test){Owner=this}.ShowDialog()));
            }
            status.Text=unreadable==0?"":UiText.Format("{0} 份进度无法读取。","{0} progress files could not be read.","{0} fichiers de progression illisibles.",unreadable);
            if(analytics.RecoveryNotice is not null)status.Text=UiText.T("统计文件曾从备份恢复，请核对记录。","Statistics were recovered from backup. Check your records.","Statistiques restaurées depuis une sauvegarde. Vérifiez les données.");
        }
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){status.Text=UiText.T("统计读取失败：","Statistics could not be read: ","Lecture des statistiques impossible : ")+e.Message;}
        finally{refreshing=false;}
    }
    private void BuildDailyCalendar(IReadOnlyList<DailyTypingSummary> days)
    {
        var today=DateTime.Today;var years=days.Select(d=>PracticeCalendarData.TryDate(d.Day,out var date)&&date<=today?date.Year:0).Where(y=>y>0).Append(today.Year).Distinct().OrderByDescending(y=>y).ToList();
        var year=calendarYear.SelectedItem is int previous&&years.Contains(previous)?previous:today.Year;calendarYear.ItemsSource=years;calendarYear.SelectedItem=year;var data=new PracticeCalendarData(year,days,today,PracticeMode);
        var panel=new StackPanel();var header=new DockPanel();var filters=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};filters.Children.Add(StatsUi.Field(UiText.T("类别","Category","Catégorie"),practiceMode));filters.Children.Add(StatsUi.Field(UiText.T("年份","Year","Année"),calendarYear));DockPanel.SetDock(filters,Dock.Right);header.Children.Add(filters);header.Children.Add(StatsUi.Text(UiText.T("每日练习","Daily practice","Pratique quotidienne"),17));panel.Children.Add(header);
        panel.Children.Add(new ScrollViewer{Content=new PracticeCalendar(data),HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,10,0,8),CanContentScroll=false});
        panel.Children.Add(StatsUi.Text(UiText.Format("{0} 年：{1} 个活跃日，{2:N1} 分钟，{3:N0} 次正确 / {4:N0} 次错误","{0}: {1} active days, {2:N1} minutes, {3:N0} correct / {4:N0} errors","{0} : {1} jours actifs, {2:N1} minutes, {3:N0} corrects / {4:N0} erreurs",year,data.ActiveDays,data.TotalSeconds/60,data.CorrectInputs,data.Errors),12));
        if(data.RecordedDays==0)panel.Children.Add(StatsUi.Text(UiText.T("本年度暂无记录。","No records this year.","Aucune donnée cette année."),11,"Muted"));body.Children.Add(StatsUi.Card(panel));
    }
    private void BuildTestCharts()
    {
        var panel=new StackPanel();panel.Children.Add(StatsUi.Text(UiText.T("限时测试趋势","Timed test trends","Évolution des tests chronométrés"),17));
        BuildScope();
        var runs=FilteredTests().OrderBy(x=>x.FinishedAt).ToList();var shown=runs.ToArray();
        var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition());
        var speed=new NativeChart{Unit=UiText.T("净字符/分","Net chars/min","Caractères nets/min"),EmptyText=UiText.T("暂无可比较的测试。","No comparable tests yet.","Aucun test comparable.")};
        var accuracy=new NativeChart{Unit=UiText.T("正确率 %","Accuracy %","Précision %"),Maximum=100,EmptyText=speed.EmptyText,Margin=new Thickness(18,0,0,0)};
        speed.Values=shown.Select(x=>new ChartValue(x.FinishedAt.ToLocalTime().ToString("MM/dd",UiText.Culture),x.CharactersPerMinute,UiText.Format("{0:g}  {1:0.0} 净字符/分","{0:g}  {1:0.0} net chars/min","{0:g}  {1:0.0} caractères nets/min",x.FinishedAt.ToLocalTime(),x.CharactersPerMinute))).ToArray();
        accuracy.Values=shown.Select(x=>new ChartValue(x.FinishedAt.ToLocalTime().ToString("MM/dd",UiText.Culture),x.Accuracy*100,UiText.Format("{0:g}  {1:P1} 正确","{0:g}  {1:P1} accuracy","{0:g}  {1:P1} de précision",x.FinishedAt.ToLocalTime(),x.Accuracy))).ToArray();row.Children.Add(speed);Grid.SetColumn(accuracy,1);row.Children.Add(accuracy);panel.Children.Add(row);
        panel.Children.Add(StatsUi.Text(UiText.Format("共 {0} 次已保存测试，显示 {1} 次。","{0} saved tests, showing {1}.","{0} tests enregistrés, {1} affichés.",runs.Count,shown.Length),11,"Muted"));body.Children.Add(StatsUi.Card(panel));
    }

    private List<SpeedTestResult> FilteredTests(){var seconds=(duration.SelectedItem as DurationChoice)?.Seconds??0;var article=source.SelectedItem as SourceChoice;return analytics.ReadSpeedTests(true).Where(x=>(TestMode is null||x.Mode==TestMode)&&(seconds==0||x.DurationSeconds==seconds)&&(article is null||article.Id.Length==0||(x.SourceId==article.Id&&x.SourceFingerprint==article.Fingerprint))).ToList();}
    private void BuildScope(){var filters=new WrapPanel();filters.Children.Add(StatsUi.Field(UiText.T("类别","Category","Catégorie"),testMode));filters.Children.Add(StatsUi.Field(UiText.T("时长","Duration","Durée"),duration));filters.Children.Add(StatsUi.Field(UiText.T("范围","Scope","Portée"),source));filtersBar.Children.Add(filters);}
    private void BuildHistory(){
        var runs=FilteredTests();const int size=15;historyPage=Math.Clamp(historyPage,0,Math.Max(0,(runs.Count-1)/size));
        var grid=new ListView{Height=360,BorderThickness=new Thickness(0)};
        grid.SetResourceReference(Control.BackgroundProperty,"Panel");grid.SetResourceReference(Control.ForegroundProperty,"Ink");
        var columns=new GridView();grid.View=columns;
        void Col(string name,string path,double width,string? format=null)=>columns.Columns.Add(new GridViewColumn{Header=name,DisplayMemberBinding=new System.Windows.Data.Binding(path){StringFormat=format},Width=width});
        Col(UiText.T("时间","Date","Date"),"FinishedAt",140,"{0:MM-dd HH:mm}");Col(UiText.T("文章","Text","Texte"),"SourceTitle",350);Col(UiText.T("速度","Speed","Vitesse"),"CharactersPerMinute",100,"{0:F1}");Col(UiText.T("正确率","Accuracy","Précision"),"Accuracy",100,"{0:P1}");Col(UiText.T("状态","Status","État"),"Status",140);
        grid.ItemsSource=runs.Skip(historyPage*size).Take(size).Select(r=>new HistoryRow(r));body.Children.Add(StatsUi.Card(grid));
        var actions=new WrapPanel();actions.Children.Add(StatsUi.Button("←",(_,_)=>{historyPage=Math.Max(0,historyPage-1);Refresh();}));actions.Children.Add(StatsUi.Text((historyPage+1)+" / "+Math.Max(1,(runs.Count+size-1)/size),14));actions.Children.Add(StatsUi.Button("→",(_,_)=>{if((historyPage+1)*size<runs.Count){historyPage++;Refresh();}}));
        actions.Children.Add(StatsUi.Button(UiText.T("本篇热力图","Text heatmap","Carte du texte"),(_,_)=>{if(grid.SelectedItem is HistoryRow {Run:var run})new ArticleHistoryWindow(root,courses,run.SourceId,run.SourceFingerprint,HeatmapSource.Test){Owner=this}.ShowDialog();}));actions.Children.Add(StatsUi.IconButton(AppIconKind.Ai,UiText.T("解读选中测试","Analyze selected test","Analyser le test"),(_,_)=>{if(grid.SelectedItem is HistoryRow {Run:var run})AiPanel.Open(this,root,TestAnalytics.CompactSummary(new[]{run}),run.SourceTitle);}));body.Children.Add(actions);
    }
}
