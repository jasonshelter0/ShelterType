using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TypeEasy.Core;

namespace TypeEasy.Windows;

/// <summary>A native timed test. Only committed IME text reaches the portable session.</summary>
public sealed class SpeedTestWindow : Window
{
    private readonly AlignedTyping alignedTyping=new();
    private readonly string root;
    private readonly List<Course> courses;
    private readonly AnalyticsStore analytics;
    private readonly ComboBox mode=StatsUi.Choice(150),duration=StatsUi.Choice(135),source=StatsUi.Choice(470);
    private readonly TextBlock countdown=StatsUi.Text("01:00",33,"Accent"),reference=StatsUi.Text("",24),feedback=StatsUi.Text("",13,"Muted"),resultText=StatsUi.Text("",18),positionText=StatsUi.Text("",12,"Muted");
    private readonly TextBox input=new(){Height=52,FontSize=22,ContextMenu=null,IsUndoEnabled=false,IsEnabled=false,AllowDrop=false};
    private readonly Button start,cancel,heatmap;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private SpeedTestSession? session;
    private DictionaryWindow? dictionaryWindow;
    private bool composing,loading,renderedResult;
    private sealed record CourseChoice(Course? Course,string Label);
    private List<Course> eligibleCourses = new();
    private string? lastRandomId;
    private readonly TextBlock passageTitle=StatsUi.Text("",16);
    public SpeedTestWindow(string userDataRoot,List<Course> availableCourses)
    {
        root=userDataRoot;courses=availableCourses;analytics=new AnalyticsStore(Path.Combine(root,"Analytics"));
        StatsUi.Window(this,UiText.T("限时测试","Timed test","Test chronométré"),1040,740);
        var layout=new DockPanel{Margin=new Thickness(26)};
        var header=new DockPanel{Margin=new Thickness(0,0,0,16)};
        var trend=StatsUi.Button(UiText.T("个人统计","Statistics","Statistiques"),(_,_)=>{var dialog=new StatisticsWindow(root,courses){Owner=this};dialog.ShowDialog();});DockPanel.SetDock(trend,Dock.Right);header.Children.Add(trend);
        var dictionary=StatsUi.Button(UiText.T("词典  F2","Dictionary  F2","Dictionnaire  F2"),(_,_)=>OpenDictionary());DockPanel.SetDock(dictionary,Dock.Right);header.Children.Add(dictionary);
        var heading=new StackPanel();heading.Children.Add(StatsUi.Text(UiText.T("限时测试","Timed test","Test chronométré"),26));heading.Children.Add(StatsUi.Text(UiText.T("首键开始计时，切换窗口不暂停。","The first key starts the timer. Switching windows does not pause it.","La première frappe lance le chrono. Changer de fenêtre ne le suspend pas."),12,"Muted"));header.Children.Add(heading);DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);
        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalContentAlignment=HorizontalAlignment.Stretch};var stack=new StackPanel{HorizontalAlignment=HorizontalAlignment.Stretch};scroll.Content=stack;layout.Children.Add(scroll);
        var fields=new WrapPanel();fields.Children.Add(StatsUi.Field(UiText.T("语言","Language","Langue"),mode));fields.Children.Add(StatsUi.Field(UiText.T("时长","Duration","Durée"),duration));fields.Children.Add(StatsUi.Field(UiText.T("文章","Text","Texte"),source));stack.Children.Add(StatsUi.Card(fields));
        foreach(var label in new[]{UiText.Mode("英文"),UiText.Mode("中文")})mode.Items.Add(label);mode.SelectedIndex=0;foreach(var minutes in new[]{1,3,5,10})duration.Items.Add(UiText.Format("{0} 分钟","{0} min","{0} min",minutes));duration.SelectedIndex=0;StatsUi.NamedItems(source,nameof(CourseChoice.Label));
        var test=new StackPanel();var timeRow=new DockPanel();DockPanel.SetDock(countdown,Dock.Right);timeRow.Children.Add(countdown);timeRow.Children.Add(passageTitle);test.Children.Add(timeRow);
        reference.LineHeight=35;reference.Margin=new Thickness(0,18,0,12);
        test.Children.Add(new ScrollViewer{Content=reference,Height=66,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});test.Children.Add(positionText);
        input.Margin=new Thickness(0,14,0,8);System.Windows.Automation.AutomationProperties.SetName(input,UiText.T("测试输入框","Test typing field","Zone de saisie du test"));test.Children.Add(input);test.Children.Add(feedback);stack.Children.Add(StatsUi.Card(test));
        var actions=new WrapPanel{Margin=new Thickness(0,0,0,14)};start=StatsUi.Button(UiText.T("准备开始","Get ready","Préparer"),(_,_)=>Prepare());cancel=StatsUi.Button(UiText.T("中止","Stop","Arrêter"),(_,_)=>Guard(()=>{CancelRun();}));cancel.IsEnabled=false;actions.Children.Add(start);actions.Children.Add(cancel);heatmap=StatsUi.Button(UiText.T("本篇热力图","Text heatmap","Carte du texte"),(_,_)=>OpenHeatmap());heatmap.IsEnabled=false;heatmap.Visibility=Visibility.Collapsed;actions.Children.Add(heatmap);stack.Children.Add(actions);
        resultText.Margin=new Thickness(0,0,0,12);resultText.TextWrapping=TextWrapping.NoWrap;resultText.FontSize=16;resultText.LineHeight=28;
        stack.Children.Add(new ScrollViewer{Content=resultText,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled});stack.Children.Add(StatsUi.Explanation(UiText.T(
            "错键不前进，可直接重输；退格回退一个已输入字符。净字符/分包含空格和标点。中文按提交的文字计数；英文等效词/分按 5 字符折算。中止或提前打完的记录不进入完整测试趋势。",
            "A wrong key does not advance; type it again. Backspace moves back one character. Net characters/min includes spaces and punctuation. Chinese counts committed text; English words/min uses 5 characters per word. Stopped tests and texts finished early are excluded from full-test trends.",
            "Une erreur ne fait pas avancer : retapez le caractère. Retour arrière recule d’un caractère. Les caractères nets/min incluent espaces et ponctuation. Le chinois compte le texte validé ; un mot anglais équivaut à 5 caractères. Les tests arrêtés ou terminés avant l’heure sont exclus des tendances des tests complets."),UiText.T("测试规则","Test rules","Règles du test")));
        Content=layout;LoadSources();mode.SelectionChanged+=(_,_)=>LoadSources();source.SelectionChanged+=(_,_)=>PreviewSource();duration.SelectionChanged+=(_,_)=>PreviewSource();
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.F2){e.Handled=true;if(!composing)OpenDictionary();}else if(e.Key==Key.Enter&&Keyboard.Modifiers==ModifierKeys.None&&TryPrepareFromEnter())e.Handled=true;};
        input.SizeChanged+=(_,_)=>{if(!composing&&session is {IsFinished:false})ShowState();};
        input.PreviewTextInput+=OnCommittedText;input.PreviewKeyDown+=OnInputKey;
        input.LostKeyboardFocus+=(_,_)=>session?.InterruptTiming();Deactivated+=(_,_)=>session?.InterruptTiming();
        TextCompositionManager.AddPreviewTextInputStartHandler(input,(_,_)=>composing=true);TextCompositionManager.AddPreviewTextInputUpdateHandler(input,(_,_)=>composing=true);
        DataObject.AddPastingHandler(input,(_,e)=>{e.CancelCommand();feedback.Text=UiText.T("测试期间不能粘贴。","Pasting is disabled during tests.","Le collage est désactivé pendant les tests.");});
        CommandManager.AddPreviewExecutedHandler(input,(_,e)=>{if(e.Command==ApplicationCommands.Paste||e.Command==ApplicationCommands.Cut||e.Command==ApplicationCommands.Undo||e.Command==ApplicationCommands.Redo)e.Handled=true;});
        timer.Tick+=(_,_)=>Guard(()=>{session?.Tick();ShowState(false);});
        Closing+=(_,_)=>{timer.Stop();try{session?.Dispose();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){MessageBox.Show(this,UiText.T("未能保存测试记录：","Could not save the test: ","Impossible d’enregistrer le test : ")+e.Message,UiText.T("保存失败","Save failed","Échec de l’enregistrement"),MessageBoxButton.OK,MessageBoxImage.Warning);}};
    }
    private bool TryPrepareFromEnter()
    {
        if(composing||session is {IsFinished:false}||!start.IsEnabled||Keyboard.FocusedElement is ComboBox||mode.IsDropDownOpen||source.IsDropDownOpen||duration.IsDropDownOpen)return false;
        Prepare();return true;
    }
    private void OpenDictionary()
    {
        session?.InterruptTiming();
        if(dictionaryWindow is null)
        {
            var query=Chinese&&session is {IsFinished:false}&&session.Position<session.ReferenceText.Length
                ?session.ReferenceText[session.Position].ToString():"";
            dictionaryWindow=new DictionaryWindow(query){Owner=this};
            dictionaryWindow.Closed+=(_,_)=>{dictionaryWindow=null;if(IsVisible&&input.IsEnabled)input.Focus();};
            dictionaryWindow.Show();
        }
        dictionaryWindow.Activate();
    }
    private int Seconds=>new[]{60,180,300,600}[Math.Max(0,duration.SelectedIndex)];
    private bool Chinese=>mode.SelectedIndex==1;
    private void LoadSources()
    {
        if(loading)return;loading=true;
        var category=Chinese?"中文":"英文";
        var list=courses.Where(c=>c.Category==category&&(c.Id.Contains("/e_general/",StringComparison.OrdinalIgnoreCase)||c.Variant.Contains("文章")))
            .Where(c=>c.Items.Count>0&&!c.Items.Any(i=>i.ImagePath.Length>0))
            .OrderByDescending(c=>c.Id.Contains("/e_general/",StringComparison.OrdinalIgnoreCase)).ThenBy(c=>c.Title).ToList();
        eligibleCourses=list;
        var choices=list.Select(c=>new CourseChoice(c,CourseLabels.ContextTitle(c))).ToList();
        if(list.Count>0)choices.Insert(0,new CourseChoice(null,UiText.T("随机文章","Random text","Texte aléatoire")));
        source.ItemsSource=choices;if(choices.Count>0)source.SelectedIndex=0;loading=false;PreviewSource();
    }
    private void PreviewSource()
    {
        if(loading||session is {IsFinished:false})return;
        countdown.Text=TimeSpan.FromSeconds(Seconds).ToString(@"mm\:ss");
        if(source.SelectedItem is CourseChoice choice){var text=choice.Course is null?"":string.Concat(choice.Course.Items.Select(i=>i.Expected));reference.Text=choice.Course is null?UiText.T("开始时随机选择文章","A text will be chosen when you start","Un texte sera choisi au démarrage"):text[..Math.Min(220,text.Length)];positionText.Text=choice.Course is null?"":UiText.Format("共 {0:N0} 字符","{0:N0} characters","{0:N0} caractères",text.Length);feedback.Text="";start.IsEnabled=true;}
        else{reference.Text=UiText.T("没有可用文章，请先在主窗口加载课程。","No texts available. Load the courses in the main window first.","Aucun texte disponible. Chargez les cours dans la fenêtre principale.");start.IsEnabled=false;}
    }
    private void Prepare()
    {
        Guard(()=>
        {
            if(source.SelectedItem is not CourseChoice choice)return;
            var pool=eligibleCourses.Where(c=>c.Id!=lastRandomId).ToList();
            if(pool.Count==0)pool=eligibleCourses;
            var course=choice.Course??pool[Random.Shared.Next(pool.Count)];
            if(choice.Course is null)lastRandomId=course.Id;
            passageTitle.Text=CourseLabels.Title(course);passageTitle.ToolTip=course.Author+"\n"+course.SourceUrl;
            session?.Dispose();session=new SpeedTestSession(course,Seconds,analytics);renderedResult=false;composing=false;
            mode.IsEnabled=duration.IsEnabled=source.IsEnabled=false;start.IsEnabled=false;cancel.IsEnabled=true;heatmap.IsEnabled=false;heatmap.Visibility=Visibility.Collapsed;input.IsEnabled=true;input.Clear();resultText.Text="";
            InputMethod.SetIsInputMethodEnabled(input,course.Category=="中文");
            feedback.Text=UiText.T("请开始输入。","Start typing.","Commencez à taper.");
            ShowState();input.Focus();timer.Start();
        });
    }
    private void OnCommittedText(object sender,TextCompositionEventArgs e)
    {
        composing=false;e.Handled=true;if(session is null||string.IsNullOrEmpty(e.Text))return;
        Guard(()=>{session.Feed(e.Text);input.Clear();Feedback();ShowState();});
    }
    private void OnInputKey(object sender,KeyEventArgs e)
    {
        if(session is null||session.IsFinished)return;
        if((Keyboard.Modifiers&ModifierKeys.Control)!=0&&e.Key is Key.V or Key.X or Key.Z or Key.Y){e.Handled=true;return;}
        if(e.Key==Key.Insert&&(Keyboard.Modifiers&ModifierKeys.Shift)!=0){e.Handled=true;return;}
        if(composing&&Chinese)return;
        if(e.Key==Key.Back){e.Handled=true;Guard(()=>{session.Backspace();input.Clear();feedback.Text="";ShowState();});}
        else if(e.Key==Key.Space&&(!Chinese||session.ExpectedCharacter==' ')){e.Handled=true;Guard(()=>{session.Feed(" ");input.Clear();Feedback();ShowState();});}
        else if(e.Key==Key.Enter&&session.ExpectedCharacter=='\n'){e.Handled=true;Guard(()=>{session.Feed("\n");input.Clear();Feedback();ShowState();});}
    }
    private void CancelRun()
    {
        if(session is null)return;
        if(session.HasStarted){session.Cancel();ShowState();return;}
        session.Dispose();session=null;timer.Stop();input.IsEnabled=false;
        mode.IsEnabled=duration.IsEnabled=source.IsEnabled=true;start.IsEnabled=true;cancel.IsEnabled=false;feedback.Text="";PreviewSource();
    }
    private void Feedback()
    {
        if(session is null)return;
        feedback.Text=session.LastInputCorrect==false?UiText.Format("应输入 {0}，刚才输入 {1}。","Expected {0}; typed {1}.","Attendu : {0} ; saisi : {1}.",StatsUi.Key(session.LastExpected),StatsUi.Key(session.LastActual)):"";
        feedback.SetResourceReference(TextBlock.ForegroundProperty,session.LastInputCorrect==false?"Error":"Muted");
    }
    private void ShowState(bool updateText=true)
    {
        if(session is null||renderedResult)return;
        var remaining=session.Remaining;var seconds=Math.Max(0,(int)Math.Ceiling(remaining.TotalSeconds));countdown.Text=$"{seconds/60:00}:{seconds%60:00}";
        positionText.Text=UiText.Format("净前进 {0:N0} / {1:N0} 字符","Net progress: {0:N0} / {1:N0} characters","Progression nette : {0:N0} / {1:N0} caractères",session.Position,session.ReferenceText.Length);
        if(updateText)
        {
            alignedTyping.Render(reference,input,session.ReferenceText,session.Position,session.LastInputCorrect==false?session.LastActual:"");
        }
        if(!session.IsFinished||renderedResult)return;
        renderedResult=true;timer.Stop();input.IsEnabled=false;mode.IsEnabled=duration.IsEnabled=source.IsEnabled=true;start.IsEnabled=true;start.Content=UiText.T("再测一次","Try again","Recommencer");cancel.IsEnabled=false;
        var result=session.Result;heatmap.IsEnabled=result is not null&&result.Status!=SpeedTestStatus.Cancelled;heatmap.Visibility=heatmap.IsEnabled?Visibility.Visible:Visibility.Collapsed;if(result is null){feedback.Text=UiText.T("尚未计时，没有新增记录。","The timer did not start; no record was added.","Le chrono n’a pas démarré ; aucun résultat ajouté.");return;}
        var label=result.Status switch{SpeedTestStatus.Completed=>UiText.T("测试完成","Test complete","Test terminé"),SpeedTestStatus.Exhausted=>UiText.T("文章已打完（未满时长）","Text finished before the time limit","Texte terminé avant la limite"),_=>UiText.T("测试已中止","Test stopped","Test arrêté")};
        var equivalent=result.Mode==TypingMode.ForCourse(new Course{Category="英文"})?UiText.Format("（{0:0.0} 等效词/分）"," ({0:0.0} equivalent words/min)"," ({0:0.0} mots équivalents/min)",result.EnglishEquivalentWpm):"";
        resultText.Text=label+"\n"+UiText.Format("{0:0.0} 净字符/分{1}    正确率 {2:P1}","{0:0.0} net characters/min{1}    Accuracy {2:P1}","{0:0.0} caractères nets/min{1}    Précision {2:P1}",result.CharactersPerMinute,equivalent,result.Accuracy)
            +"\n"+UiText.Format("{0:N0} 净字符，{1:N0} 次错误，{2:N0} 次退格，用时 {3:0.0} 秒","{0:N0} net characters, {1:N0} errors, {2:N0} backspaces, {3:0.0} s","{0:N0} caractères nets, {1:N0} erreurs, {2:N0} retours arrière, {3:0.0} s",result.NetCharacters,result.Errors,result.Backspaces,result.ElapsedSeconds);
        Dispatcher.BeginInvoke(()=>resultText.BringIntoView(),DispatcherPriority.Loaded);
        positionText.Text=UiText.T("记录已保存。","Result saved.","Résultat enregistré.");feedback.Text=result.Status==SpeedTestStatus.Completed?"":UiText.T("此记录不计入完整测试趋势。","Excluded from full-test trends.","Exclu des tendances des tests complets.");
        feedback.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
    }
    private void OpenHeatmap()
    {
        if(session?.Result is not {} result)return;
        new ArticleHistoryWindow(root,courses,result.SourceId,result.SourceFingerprint,HeatmapSource.Test){Owner=this}.ShowDialog();
    }
    private void Guard(Action action)
    {
        try{action();}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {feedback.Text=UiText.T("操作未完成：","Operation failed: ","Échec de l’opération : ")+e.Message;feedback.SetResourceReference(TextBlock.ForegroundProperty,"Error");}
    }
}







