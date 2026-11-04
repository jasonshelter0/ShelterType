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
    private readonly StackPanel body=new();
    private readonly ComboBox practiceMode=StatsUi.Choice(180),calendarYear=StatsUi.Choice(105),testMode=StatsUi.Choice(170),duration=StatsUi.Choice(120),source=StatsUi.Choice(340);
    private readonly TextBlock status=StatsUi.Text("",12,"Muted");
    private bool refreshing;
    private sealed record ModeChoice(string? Value,string Label);
    private sealed record SourceChoice(string Id,string Title,string Fingerprint);
    private sealed record DurationChoice(int Seconds,string Label);
    private string? PracticeMode=>(practiceMode.SelectedItem as ModeChoice)?.Value;
    private string? TestMode=>(testMode.SelectedItem as ModeChoice)?.Value;

    public StatisticsWindow(string userDataRoot,List<Course> availableCourses)
    {
        root=userDataRoot;courses=availableCourses;analytics=new AnalyticsStore(Path.Combine(root,"Analytics"));
        StatsUi.Window(this,UiText.T("个人统计","Statistics","Statistiques"),1100,850);
        var outer=new DockPanel{Margin=new Thickness(24)};
        var header=new DockPanel{Margin=new Thickness(0,0,0,18)};
        var refresh=StatsUi.Button(UiText.T("刷新","Refresh","Actualiser"),(_,_)=>Refresh());DockPanel.SetDock(refresh,Dock.Right);header.Children.Add(refresh);
        var articles=StatsUi.Button(UiText.T("文章历史","Article history","Historique des textes"),(_,_)=>new ArticleHistoryWindow(root,courses){Owner=this}.ShowDialog());DockPanel.SetDock(articles,Dock.Right);header.Children.Add(articles);
        header.Children.Add(StatsUi.Text(UiText.T("个人统计","Statistics","Statistiques"),26));DockPanel.SetDock(header,Dock.Top);outer.Children.Add(header);
        DockPanel.SetDock(status,Dock.Bottom);outer.Children.Add(status);outer.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});Content=outer;
        StatsUi.NamedItems(practiceMode,nameof(ModeChoice.Label));StatsUi.NamedItems(testMode,nameof(ModeChoice.Label));StatsUi.NamedItems(duration,nameof(DurationChoice.Label));StatsUi.NamedItems(source,nameof(SourceChoice.Title));
        practiceMode.Items.Add(new ModeChoice(null,UiText.T("全部类别","All categories","Toutes catégories")));
        foreach(var mode in courses.Select(TypingMode.ForCourse).Distinct().Order())practiceMode.Items.Add(new ModeChoice(mode,UiText.Mode(mode)));
        foreach(var mode in courses.Where(IsTextCourse).Select(TypingMode.ForCourse).Distinct().Order())testMode.Items.Add(new ModeChoice(mode,UiText.Mode(mode)));
        practiceMode.SelectedIndex=0;testMode.SelectedIndex=testMode.Items.Count>0?0:-1;
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
        LoadSources();
        if(latest is not null)source.SelectedItem=source.Items.Cast<SourceChoice>().FirstOrDefault(x=>x.Id==latest.SourceId&&x.Fingerprint==latest.SourceFingerprint)??source.SelectedItem;
        calendarYear.SelectionChanged+=(_,_)=>Refresh();practiceMode.SelectionChanged+=(_,_)=>Refresh();testMode.SelectionChanged+=(_,_)=>{if(!refreshing){LoadSources();Refresh();}};duration.SelectionChanged+=(_,_)=>Refresh();source.SelectionChanged+=(_,_)=>Refresh();Loaded+=(_,_)=>Refresh();
    }
    private static bool IsTextCourse(Course c)=>c.Category is "英文" or "中文"&&(c.Id.Contains("/e_general/",StringComparison.OrdinalIgnoreCase)||c.Variant.Contains("文章"));
    private void LoadSources()
    {
        var keep=source.SelectedItem as SourceChoice;refreshing=true;
        try
        {
            var current=courses.Where(IsTextCourse).Where(x=>TypingMode.ForCourse(x)==TestMode).OrderByDescending(x=>x.Id.Contains("/e_general/",StringComparison.OrdinalIgnoreCase)).Select(x=>new SourceChoice(x.Id,CourseLabels.ContextTitle(x),SpeedTestSession.SourceFingerprintFor(x)));
            var historical=analytics.ReadSpeedTests().Where(x=>x.Mode==TestMode).Select(x=>new SourceChoice(x.SourceId,
                string.Join(" / ",new[]{UiText.Mode(x.Mode),CourseLabels.Group(x.SourceGroup),CourseLabels.CleanTitle(x.SourceTitle)}.Where(s=>s.Length>0).Distinct())
                +UiText.T("（历史版本）"," (historical)"," (version historique)"),x.SourceFingerprint));
            var list=current.Concat(historical).DistinctBy(x=>(x.Id,x.Fingerprint)).ToList();source.ItemsSource=list;source.SelectedItem=list.FirstOrDefault(x=>x.Id==keep?.Id&&x.Fingerprint==keep?.Fingerprint)??list.FirstOrDefault();
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
            body.Children.Clear();var progress=new List<PracticeState>();var unreadable=0;var directory=Path.Combine(root,"Progress");
            if(Directory.Exists(directory))foreach(var path in Directory.EnumerateFiles(directory,"*.json"))
            {
                try{var value=JsonSerializer.Deserialize<PracticeState>(File.ReadAllText(path));if(value is not null&&double.IsFinite(value.ElapsedSeconds)&&value.ElapsedSeconds>=0&&value.CorrectInputs>=0&&value.Errors>=0)progress.Add(value);else unreadable++;}
                catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){unreadable++;}
            }
            var current=new StackPanel();current.Children.Add(StatsUi.Text(UiText.T("课程进度累计","Current course totals","Totaux des cours actuels"),17));
            current.Children.Add(StatsUi.Text(UiText.Format("{0:N0} 门课程    {1:N0} 次正确    {2:N0} 次错误    {3:0.0} 小时","{0:N0} courses    {1:N0} correct    {2:N0} errors    {3:0.0} hours","{0:N0} cours    {1:N0} corrects    {2:N0} erreurs    {3:0.0} heures",progress.Count,progress.Sum(x=>x.CorrectInputs),progress.Sum(x=>x.Errors),progress.Sum(x=>x.ElapsedSeconds)/3600),18));body.Children.Add(StatsUi.Card(current));
            BuildDailyCalendar(analytics.ReadDays());body.Children.Add(StatsUi.Card(new KeyboardStatisticsPanel(analytics,PracticeMode)));BuildTestCharts();
            body.Children.Add(StatsUi.Explanation(UiText.T("课程累计来自当前进度；重新开始会重置该课程。每日历史从0.3起独立累计。限时趋势只比较同类别、同文章版本、同时长的完整测试。","Course totals reflect current checkpoints and reset when a course restarts. Daily history accumulates separately from version 0.3. Test trends compare completed tests of the same category, text version and duration.","Les totaux reflètent la progression actuelle et sont remis à zéro au redémarrage d’un cours. L’historique quotidien est distinct depuis la version 0.3. Les tendances comparent les tests terminés de même catégorie, version de texte et durée."),UiText.T("说明","About these totals","À propos des totaux")));
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
        var filters=new WrapPanel{Margin=new Thickness(0,10,0,8)};filters.Children.Add(StatsUi.Field(UiText.T("类别","Category","Catégorie"),testMode));filters.Children.Add(StatsUi.Field(UiText.T("时长","Duration","Durée"),duration));filters.Children.Add(StatsUi.Field(UiText.T("文章","Article","Texte"),source));panel.Children.Add(filters);
        var seconds=(duration.SelectedItem as DurationChoice)?.Seconds??60;var selected=source.SelectedItem as SourceChoice;
        var all=analytics.ReadSpeedTests(true);var runs=all.Where(x=>selected is not null&&x.Status==SpeedTestStatus.Completed&&x.Mode==TestMode&&x.DurationSeconds==seconds&&x.SourceId==selected.Id&&x.SourceFingerprint==selected.Fingerprint).OrderBy(x=>x.FinishedAt).ToList();var shown=runs.TakeLast(20).ToArray();
        var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition());
        var speed=new NativeChart{Unit=UiText.T("净字符/分","Net chars/min","Caractères nets/min"),EmptyText=UiText.T("暂无可比较的测试。","No comparable tests yet.","Aucun test comparable.")};
        var accuracy=new NativeChart{Unit=UiText.T("正确率 %","Accuracy %","Précision %"),Maximum=100,EmptyText=speed.EmptyText,Margin=new Thickness(18,0,0,0)};
        speed.Values=shown.Select(x=>new ChartValue(x.FinishedAt.ToLocalTime().ToString("MM/dd",UiText.Culture),x.CharactersPerMinute,UiText.Format("{0:g}  {1:0.0} 净字符/分","{0:g}  {1:0.0} net chars/min","{0:g}  {1:0.0} caractères nets/min",x.FinishedAt.ToLocalTime(),x.CharactersPerMinute))).ToArray();
        accuracy.Values=shown.Select(x=>new ChartValue(x.FinishedAt.ToLocalTime().ToString("MM/dd",UiText.Culture),x.Accuracy*100,UiText.Format("{0:g}  {1:P1} 正确","{0:g}  {1:P1} accuracy","{0:g}  {1:P1} de précision",x.FinishedAt.ToLocalTime(),x.Accuracy))).ToArray();row.Children.Add(speed);Grid.SetColumn(accuracy,1);row.Children.Add(accuracy);panel.Children.Add(row);
        panel.Children.Add(StatsUi.Text(UiText.Format("{0} 次符合条件，显示最近 {1} 次。","{0} matching tests, showing the latest {1}.","{0} tests correspondants, affichage des {1} derniers.",runs.Count,shown.Length),11,"Muted"));body.Children.Add(StatsUi.Card(panel));
    }
}
