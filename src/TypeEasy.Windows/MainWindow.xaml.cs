using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using TypeEasy.Core;

namespace TypeEasy.Windows;

public partial class MainWindow : Window
{
    private readonly string store;
    private readonly Preferences prefs = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private List<Course> courses = new();
    private PracticeSession? session;
    private string category = "五笔";
    private bool ready;
    private bool rebuildingCourses;
    private DateTime lastActivity = DateTime.UtcNow;
    private bool composing;
    private bool darkTheme;
    private bool focusMode;
    private readonly FileStream? instanceLock;

    public MainWindow()
    {
        store = Path.Combine(AppContext.BaseDirectory, Environment.GetCommandLineArgs().Contains("--test-profile") ? "TestData" : "UserData");
        Directory.CreateDirectory(store);
        prefs = ReadPrefs();
        UiText.Language = prefs.Language;
        try { instanceLock = new FileStream(Path.Combine(store, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { MessageBox.Show(UiText.T("程序已在运行。", "The app is already running.", "L’application est déjà ouverte."), "ShelterType"); Application.Current.Shutdown(); return; }
        InitializeComponent();
        ApplyLanguage(false);
        ApplyTheme();
        SourceInitialized += (_,_) => SetTitleTheme();
        SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        TextCompositionManager.AddPreviewTextInputStartHandler(InputBox,(_,_)=>composing=true);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(InputBox,(_,_)=>composing=true);
        DataObject.AddPastingHandler(InputBox, (_, e) => e.CancelCommand());
        timer.Tick += (_, _) => { if (session is null) return; if (session.IsRunning && (DateTime.UtcNow - lastActivity).TotalSeconds > 30) Guard(() => session.Pause()); UpdateStats(); };
        Deactivated += (_, _) => { KeyboardGuide.StopDemo(); if(session is not null) Guard(() => session.Pause()); };
        Closing += (_, _) => { updateLifetime.Cancel(); KeyboardGuide.StopDemo(); SystemEvents.UserPreferenceChanged -= SystemThemeChanged; if(session is not null) Guard(() => session.Dispose()); SavePrefs(); instanceLock?.Dispose(); };
        SizeChanged += (_, _) => UpdateStats();
        Loaded += async (_, _) => { ready = true; LoadCatalog(); ApplyLayout(); timer.Start(); await CheckForUpdatesInBackgroundAsync(); };
    }

    private Preferences ReadPrefs()
    {
        try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(store, "preferences.json"))) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(); }
    }
    private void SavePrefs()
    {
        try { SavePrefsCore(); }
        catch (Exception ex) when(ex is IOException or UnauthorizedAccessException) { if (StatusLabel is not null) {StatusLabel.Text = UiText.T("设置保存失败：", "Could not save settings: ", "Impossible d’enregistrer les réglages : ") + ex.Message;StatusLabel.Visibility=Visibility.Visible;} }
    }
    private void SavePrefsCore()
    {
        var path = Path.Combine(store, "preferences.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(prefs));
        File.Move(path + ".tmp", path, true);
    }
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { SaveLabel.Text = ""; StatusLabel.Text = UiText.T("无法读写记录：", "Could not read or save records: ", "Impossible de lire ou d’enregistrer les données : ") + ex.Message;StatusLabel.Visibility=Visibility.Visible; if(session is not null) InputBox.IsEnabled=false; }
    }
    private SolidColorBrush Brush(string name) => (SolidColorBrush)Application.Current.Resources[name];
    private void LoadCatalog()
    {
        Guard(() => {
            session?.Dispose(); session = null;
            var root = !string.IsNullOrWhiteSpace(prefs.DataRoot) ? prefs.DataRoot : AppContext.BaseDirectory;
            courses = CourseCatalog.Load(root);
            var reviews=Path.Combine(store,"Reviews");
            if(Directory.Exists(reviews)) foreach(var file in Directory.EnumerateFiles(reviews,"*.json")) {
                try {var review=JsonSerializer.Deserialize<Course>(File.ReadAllText(file)); var source=review is null?null:courses.FirstOrDefault(c=>review.Id=="review:"+c.Id); if(review is not null && review.Items.Count>0 && source is not null) {review.InputMode=source.InputMode;review.Group="错键巩固/"+source.Variant;review.TeachingImage=source.TeachingImage;courses.Add(review);}}
                catch(JsonException) { }
            }
            var previous = courses.FirstOrDefault(c => c.Id == prefs.LastCourse);
            category = previous?.Category ?? "五笔";
            FilterCourses(previous?.Id);
            File.WriteAllLines(Path.Combine(store,"课程导入说明.txt"),CourseCatalog.Warnings);
            if(courses.Count == 0) { CourseTitle.Text=UiText.T("请选择课程目录", "Choose a course folder", "Choisissez un dossier de cours"); CourseSubtitle.Text=UiText.T("设置 → 课程目录", "Settings → Course folder", "Réglages → Dossier de cours"); InputBox.IsEnabled=false; }
        });
    }
    private void FilterCourses(string? selectedId = null, bool openSelected = true)
    {
        if(!ready || rebuildingCourses) return;
        var query=SearchBox.Text.Trim();
        var visible=courses.Where(c=>c.Category == category && (c.Title.Contains(query,StringComparison.OrdinalIgnoreCase) || c.Variant.Contains(query,StringComparison.OrdinalIgnoreCase) || c.Group.Contains(query,StringComparison.OrdinalIgnoreCase) || CourseLabels.Path(c).Contains(query,StringComparison.CurrentCultureIgnoreCase))).ToList();
        var chosen=visible.FirstOrDefault(c=>c.Id==selectedId) ?? visible.FirstOrDefault(c=>c.Group.Contains("入门")) ?? visible.FirstOrDefault();
        var roots=new List<CourseNode>();
        foreach(var course in visible) {
            var nodes=roots;
            foreach(var part in (string.IsNullOrEmpty(course.Group)?"其他课程":course.Group).Split('/')) {
                var group=nodes.FirstOrDefault(n=>n.Course is null && n.Key==part);
                if(group is null) {group=new CourseNode{Key=part,Title=CourseLabels.Group(part)};nodes.Add(group);}
                if(course==chosen || query.Length>0) group.Expanded=true;
                nodes=group.Children;
            }
            nodes.Add(new CourseNode {Key=course.Id,Title=CourseLabels.Title(course),Description=CourseLabels.Path(course),Course=course,Selected=course==chosen});
        }
        void SortGroups(List<CourseNode> nodes) {int Rank(CourseNode n)=>n.Course is not null?3:n.Key.Contains("入门")?0:n.Key.Contains("拼音")||n.Key.Contains("键盘")?1:2;var ordered=nodes.OrderBy(Rank).ToList();nodes.Clear();nodes.AddRange(ordered);foreach(var node in nodes) SortGroups(node.Children);}
        SortGroups(roots);
        rebuildingCourses=true;
        try { CourseTree.ItemsSource=roots; } finally { rebuildingCourses=false; }
        foreach(var button in new[]{WubiTab,EnglishTab,ChineseTab}) button.Background=Brush((string)button.Tag == category ? "Selected":"Panel");
        if(openSelected && chosen is not null) OpenCourse(chosen);
    }
    private void Category_Click(object sender,RoutedEventArgs e) { category=(string)((Button)sender).Tag; SearchBox.Text=""; FilterCourses(); }
    private void Search_Changed(object sender,TextChangedEventArgs e) => FilterCourses(session?.Course.Id);
    private void Course_Changed(object sender,RoutedPropertyChangedEventArgs<object> e)
    {
        if(!rebuildingCourses && e.NewValue is CourseNode {Course: {} c}) OpenCourse(c);
    }
    private void OpenCourse(Course c)
    {
        if(session?.Course==c) return;
        Guard(()=> {
            KeyboardGuide.StopDemo();
            session?.Dispose(); session=PracticeSession.Open(c,Path.Combine(store,"Progress"),Path.Combine(store,"Analytics"),Path.Combine(store,"Heatmaps")); composing=false;
            ExerciseScroll.ScrollToTop();LessonScroll.ScrollToTop();
            prefs.LastCourse=c.Id; SavePrefs(); InputBox.IsEnabled=true; InputBox.Text="";
            InputMethod.SetIsInputMethodEnabled(InputBox,UsesIme(c));
            UpdateCourseLabels();
            FeedbackLabel.Text=ResumeMessage(session.ResumeNotice);
            FeedbackLabel.Foreground=Brush("Muted"); Render();
            // Do not steal focus while the user is filtering the course list.
            if(!SearchBox.IsKeyboardFocusWithin) InputBox.Focus();
        });
    }
    private void Render()
    {
        if(session is null) return;
        var item=session.CurrentItem; var wubi=session.Course.Category=="五笔"; var phonetic=session.Course.InputMode=="pinyin";
        PositionLabel.Text=session.IsComplete ? UiText.T("已完成", "Completed", "Terminé") : $"{session.State.ItemIndex+1:N0} / {session.Course.Items.Count:N0}";
        PromptLabel.FontSize=wubi||phonetic?32:18;
        PromptLabel.Text=item is null ? UiText.T("练习完成", "Practice complete", "Exercice terminé") : wubi||phonetic ? CourseLabels.Prompt(item.Prompt) : "";
        PromptLabel.Visibility=string.IsNullOrEmpty(PromptLabel.Text)?Visibility.Collapsed:Visibility.Visible;
        LessonImage.Visibility=Visibility.Collapsed;
        if(!string.IsNullOrEmpty(item?.ImagePath)) {var img=LocalImage(item.ImagePath); if(img is not null) {LessonImage.Source=img;LessonImage.Visibility=Visibility.Visible;}}
        ExpectedLabel.Inlines.Clear();Run? activeRun=null;
        if(item is not null) {
            for(int i=0;i<item.Expected.Length;i++) {
                string t=item.Expected[i]==' ' ? "␣" : item.Expected[i]=='\n' ? "↵" : item.Expected[i].ToString();
                var run=new Run(t) { Foreground=Brush(i<session.State.CharacterIndex?"Accent":"Ink") };
                if(i==session.State.CharacterIndex) { run.Background=Brush("Selected"); run.TextDecorations=TextDecorations.Underline; activeRun=run; }
                ExpectedLabel.Inlines.Add(run);
            }
        }
        bool introductory=session.Course.Group.Contains("入门") || session.Course.Id.StartsWith("builtin/keyboard/");
        NextLabel.Text=session.IsComplete ? "" : introductory||phonetic ? CourseLabels.Hint(item?.Hint ?? "") : "";
        InputBox.IsEnabled=!session.IsComplete;
        if(session.IsComplete) FeedbackLabel.Text="";
        var concealed=!prefs.ShowHints&&(wubi||phonetic);
        if(concealed && item is not null) {ExpectedLabel.Text=session.State.CharacterIndex==0?"…":item.Expected[..session.State.CharacterIndex];NextLabel.Text="";}
        KeyboardGuide.SetTarget(concealed?null:session.ExpectedCharacter,session.Course.Id.Contains("numpad",StringComparison.OrdinalIgnoreCase));
        KeyboardGuide.InvalidateVisual();
        ApplyLayout();
        if(item is not null) {InputBox.Text=item.Expected[..session.State.CharacterIndex]; InputBox.CaretIndex=InputBox.Text.Length;}
        SaveLabel.Text="";
        UpdateStats();
        if(!concealed && activeRun is not null) Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{if(activeRun.Parent is not null && LessonScroll.ScrollableHeight>0) activeRun.BringIntoView();}));
    }
    private void UpdateStats()
    {
        if(session is null || StatsLabel is null) return;
        var elapsed=session.Elapsed;
        StatsLabel.Text=UiText.Format("{0:0} 字符/分    {1:P0} 正确    {2:00}:{3:00}", "{0:0} chars/min    {1:P0} accuracy    {2:00}:{3:00}", "{0:0} car./min    {1:P0} de précision    {2:00}:{3:00}", session.CharactersPerMinute, session.Accuracy, (int)elapsed.TotalMinutes, elapsed.Seconds);
        if(ProgressFill.Parent is FrameworkElement parent) ProgressFill.Width=Math.Max(0,parent.ActualWidth*session.Progress);
    }
    private void Input_Text(object sender,TextCompositionEventArgs e)
    {
        composing=false;
        e.Handled=true;
        if(session is null || string.IsNullOrEmpty(e.Text)) return;
        Guard(()=> {
            StopDemoForTyping();
            lastActivity=DateTime.UtcNow;
            session.Feed(session.Course.Category=="五笔" ? e.Text.ToLowerInvariant():e.Text);
            InputBox.Text="";
            FeedbackLabel.Text=session.LastInputCorrect==false ? WrongKeyMessage(session.LastActual, session.LastExpected) : "";
            FeedbackLabel.Foreground=Brush(session.LastInputCorrect==false?"Error":"Muted");
            Render();
        });
    }
    private static string Visible(string value) => UiText.Key(value);
    private static string WrongKeyMessage(string actual,string expected) => UiText.Format("{0} → 应为 {1}", "{0} → expected {1}", "{0} → attendu : {1}",Visible(actual),Visible(expected));
    private void Input_Key(object sender,KeyEventArgs e)
    {
        if(session is null) return;
        if(composing && UsesIme(session.Course)) return;
        StopDemoForTyping();
        if(e.Key==Key.Back) { e.Handled=true; Guard(()=>{session.Backspace(); InputBox.Clear(); FeedbackLabel.Text=""; Render();}); }
        else if(e.Key==Key.Space && (!UsesIme(session.Course) || (!composing && session.ExpectedCharacter==' '))) {e.Handled=true; Guard(()=>{lastActivity=DateTime.UtcNow; session.Feed(" "); FeedbackLabel.Text=session.LastInputCorrect==false?WrongKeyMessage(" ",session.LastExpected):""; FeedbackLabel.Foreground=Brush(session.LastInputCorrect==false?"Error":"Muted"); Render();});}
        else if(e.Key==Key.Enter && session.ExpectedCharacter=='\n') {e.Handled=true; Guard(()=>{lastActivity=DateTime.UtcNow;session.Feed("\n"); Render();});}
        else if(e.Key==Key.Escape) {e.Handled=true; Guard(()=>session.Pause()); FeedbackLabel.Text="";}
        else if((Keyboard.Modifiers & ModifierKeys.Control)!=0 && e.Key is Key.V or Key.X or Key.Z) e.Handled=true;
    }
    private void Input_Focus(object sender,KeyboardFocusChangedEventArgs e) { lastActivity=DateTime.UtcNow; }
    private void Input_Blur(object sender,KeyboardFocusChangedEventArgs e) { if(session is not null) Guard(()=>session.Pause()); }
    private void Review_Click(object sender,RoutedEventArgs e)
    {
        if(session is null) return;
        var review=session.BuildReviewCourse(); if(review.Items.Count==0) return;
        review.InputMode=session.Course.InputMode; review.Group="错键巩固/"+session.Course.Variant;
        for(int i=0;i<review.Items.Count;i++) review.Items[i].ImagePath=session.Course.Items.FirstOrDefault(x=>x.Prompt==review.Items[i].Prompt && x.Expected==review.Items[i].Expected)?.ImagePath??"";
        var folder=Path.Combine(store,"Reviews"); Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(review.Id)))+".json");
        Guard(()=>{File.WriteAllText(path+".tmp",JsonSerializer.Serialize(review));File.Move(path+".tmp",path,true);});
        if(!courses.Any(c=>c.Id==review.Id)) courses.Add(review); else courses[courses.FindIndex(c=>c.Id==review.Id)]=review;
        category=review.Category; SearchBox.Text=""; FilterCourses(review.Id);
    }
    private void Folder_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFolderDialog {Title=UiText.T("选择 KINGSOFT 或 Data 目录", "Choose the KINGSOFT or Data folder", "Choisissez le dossier KINGSOFT ou Data"),Multiselect=false};
        if(dialog.ShowDialog(this)==true) {prefs.DataRoot=dialog.FolderName; prefs.LastCourse=""; SavePrefs(); LoadCatalog();}
    }
    private void ApplyTheme()
    {
        bool dark=prefs.Theme!="light";
        if(prefs.Theme=="system") {using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); dark=key?.GetValue("AppsUseLightTheme") is int n && n==0;}
        darkTheme=dark; SetTitleTheme();
        var colors=dark?new[]{"#111719","#192225","#111A1D","#E9F0EE","#99ACA8","#304044","#78DCC0","#25483F","#FF9D91"}:new[]{"#EEF2EF","#FFFFFF","#F3F6F3","#182B26","#5C7169","#CCD9D2","#207D63","#D9EDE2","#B73529"};
        var names=new[]{"Bg","Panel","Field","Ink","Muted","Line","Accent","Selected","Error"};
        for(int i=0;i<names.Length;i++) Application.Current.Resources[names[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        FeedbackLabel.Foreground=Brush("Muted");
        if(WubiTab is not null) foreach(var b in new[]{WubiTab,EnglishTab,ChineseTab}) b.Background=Brush((string)b.Tag==category?"Selected":"Panel");
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    private void SetTitleTheme() {var handle=new WindowInteropHelper(this).Handle; if(handle!=IntPtr.Zero) {int value=darkTheme?1:0; DwmSetWindowAttribute(handle,20,ref value,4);}}
    private void SystemThemeChanged(object sender,UserPreferenceChangedEventArgs e) {if(prefs.Theme=="system") Dispatcher.BeginInvoke(()=>{ApplyTheme();Render();});}
    private static bool UsesIme(Course course) => course.Category=="中文" && course.InputMode!="pinyin";
    private void StopDemoForTyping() {if(KeyboardGuide.IsDemonstrating) KeyboardGuide.StopDemo();}
    private void ApplyLayout()
    {
        bool sidebar=prefs.ShowSidebar&&!focusMode;
        SidebarPanel.Visibility=sidebar?Visibility.Visible:Visibility.Collapsed;
        SidebarColumn.Width=new GridLength(sidebar?260:0);SidebarGap.Width=new GridLength(sidebar?20:0);
        SidebarButton.Content=sidebar?UiText.T("☰ 课程", "☰ Courses", "☰ Cours"):"☰";
        NavigationActions.Visibility=focusMode?Visibility.Collapsed:Visibility.Visible;
        BrandLabel.Visibility=focusMode?Visibility.Collapsed:Visibility.Visible;
        FocusButton.Content=focusMode?UiText.T("退出专注", "Exit focus", "Quitter"):UiText.T("专注", "Focus", "Concentration");
        CourseSubtitle.Visibility=!sidebar&&!focusMode?Visibility.Visible:Visibility.Collapsed;
        SaveLabel.Visibility=Visibility.Collapsed;
        KeyboardGuide.ShowHands=prefs.ShowHands;
        KeyboardPanel.Visibility=prefs.ShowKeyboard && (!focusMode||prefs.FocusShowKeyboard) && session is not null && !UsesIme(session.Course)?Visibility.Visible:Visibility.Collapsed;
        NextLabel.Visibility=prefs.ShowHints&&!focusMode&&!string.IsNullOrEmpty(NextLabel.Text)?Visibility.Visible:Visibility.Collapsed;
        StatsLabel.Visibility=prefs.ShowStats?Visibility.Visible:Visibility.Collapsed;
        PracticeLayout.MaxWidth=sidebar?double.PositiveInfinity:980;
        PracticeLayout.HorizontalAlignment=HorizontalAlignment.Stretch;
    }
    private void Sidebar_Click(object sender,RoutedEventArgs e) {if(focusMode){focusMode=false;prefs.ShowSidebar=true;}else prefs.ShowSidebar=!prefs.ShowSidebar;SavePrefs();ApplyLayout();}
    private void Focus_Click(object sender,RoutedEventArgs e) {focusMode=!focusMode;ApplyLayout();InputBox.Focus();}
    private void Window_Key(object sender,KeyEventArgs e) {if(e.Key==Key.F11){e.Handled=true;Focus_Click(this,new RoutedEventArgs());}else if(e.Key==Key.Escape&&focusMode){e.Handled=true;focusMode=false;ApplyLayout();}}
    private void Speed_Click(object sender,RoutedEventArgs e) {if(session is not null)Guard(()=>session.Pause());var window=new SpeedTestWindow(store,courses){Owner=this};window.ShowDialog();}
    private void Statistics_Click(object sender,RoutedEventArgs e) {if(session is not null)Guard(()=>session.Pause());var window=new StatisticsWindow(store,courses){Owner=this};window.ShowDialog();}
    private void Settings_Click(object sender,RoutedEventArgs e)
    {
        if(session is not null)Guard(()=>session.Pause());
        var summary=UiText.Format("{0} 门课程", "{0} courses", "{0} cours", courses.Count);
        var oldLanguage=UiText.Language;
        var window=new SettingsWindow(prefs,()=>{if(oldLanguage!=prefs.Language){ApplyLanguage();oldLanguage=UiText.Language;}ApplyTheme();Render();ApplyLayout();SavePrefs();},summary,string.Join("\n",CourseCatalog.Warnings),session?.State.Errors>0){Owner=this};
        window.ShowDialog();
        switch(window.RequestedAction){
            case "folder":Folder_Click(this,new RoutedEventArgs());break;
            case "reference":Reference_Click(this,new RoutedEventArgs());break;
            case "demo":focusMode=false;prefs.ShowKeyboard=true;prefs.ShowHands=true;ApplyLayout();KeyboardGuide.ToggleDemo();break;
            case "review":Review_Click(this,new RoutedEventArgs());break;
            case "reset":if(session is not null)Guard(()=>{session.Reset();FeedbackLabel.Text="";Render();});break;
            case "update":OpenUpdate_Click(this,new RoutedEventArgs());break;
        }
        SavePrefs();
    }
    private static BitmapImage? LocalImage(string path)
    {
        var root=Path.GetFullPath(AppContext.BaseDirectory);var full=Path.GetFullPath(Path.Combine(root,path));
        if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;
        var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.UriSource=new Uri(full);bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.EndInit();bitmap.Freeze();return bitmap;
    }
    private void Reference_Click(object sender,RoutedEventArgs e)
    {
        var index=Path.Combine(AppContext.BaseDirectory,"Teaching","index.json");
        if(!File.Exists(index)) {FeedbackLabel.Text=UiText.T("教学图片尚未就绪。", "Teaching images are unavailable.", "Les images pédagogiques sont indisponibles.");return;}
        var entries=JsonSerializer.Deserialize<List<TeachingReference>>(File.ReadAllText(index))??new();
        var dialog=new Window {Title=UiText.T("原版教学图", "Original teaching images", "Images pédagogiques d’origine"),Width=980,Height=640,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("Bg"),Foreground=Brush("Ink")};
        var layout=new DockPanel{Margin=new Thickness(16)};var tabs=new WrapPanel{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(tabs,Dock.Top);layout.Children.Add(tabs);
        var image=new Image{Stretch=Stretch.Uniform};layout.Children.Add(new ScrollViewer {Content=image,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        foreach(var entry in entries){var button=new Button{Content=ReferenceTitle(entry),Margin=new Thickness(0,0,6,6),Padding=new Thickness(10,6,10,6)};button.Click+=(_,_)=>image.Source=LocalImage("Teaching/"+entry.File);tabs.Children.Add(button);}
        if(entries.Count>0) image.Source=LocalImage("Teaching/"+entries[0].File);dialog.Content=layout;dialog.ShowDialog();
    }
}
public sealed class TeachingReference {public string Title {get;set;}=""; public string File {get;set;}="";}
public sealed class CourseNode {public string Key {get;set;}=""; public string Title {get;set;}=""; public string? Description {get;set;} public Course? Course {get;set;} public List<CourseNode> Children {get;set;}=new();public bool Expanded {get;set;} public bool Selected {get;set;}}
public sealed class Preferences
{
    public bool CheckForUpdates {get;set;}=true;
    public DateTimeOffset? LastUpdateCheckUtc {get;set;}
    public string Language {get;set;}="zh-CN";
    public string Theme {get;set;}="dark";
    public string DataRoot {get;set;}="";
    public string LastCourse {get;set;}="";
    public bool ShowSidebar {get;set;}=true;
    public bool ShowKeyboard {get;set;}=true;
    public bool ShowHands {get;set;}=true;
    public bool ShowHints {get;set;}=true;
    public bool ShowStats {get;set;}=true;
    public bool FocusShowKeyboard {get;set;}=false;
}
