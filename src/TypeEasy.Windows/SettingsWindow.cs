using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TypeEasy.Windows;

public sealed class SettingsWindow : Window
{
    public string? RequestedAction { get; private set; }
    private readonly Preferences preferences;
    private readonly Action changed;
    private readonly string summary, warnings;
    private readonly bool canReview;
    private readonly bool canShuffle;
    private bool building;
    private sealed record LanguageChoice(string Code, string Name)
    {
        public override string ToString() => Name;
    }

    public SettingsWindow(Preferences preferences, Action changed, string summary, string warnings, bool canReview, bool canShuffle=false)
    {
        this.preferences = preferences; this.changed = changed; this.summary = summary; this.warnings = warnings; this.canReview = canReview;this.canShuffle=canShuffle;
        Width = 580; Height = 700; MinWidth = 500; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
        SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "Ink");
        SourceInitialized += (_, _) => AboutWindow.ApplyTitleTheme(this);
        Activated += (_, _) => AboutWindow.ApplyTitleTheme(this);
        Rebuild();
    }
    private void Rebuild()
    {
        building = true;
        try
        {
            Title = UiText.T("设置", "Settings", "Paramètres");
            Language = System.Windows.Markup.XmlLanguage.GetLanguage(UiText.Culture.IetfLanguageTag);
            var root = new DockPanel { Margin = new Thickness(24) };
            var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
            var done = Button(UiText.T("完成", "Done", "Terminé"), (_, _) => Close());
            DockPanel.SetDock(done, Dock.Right); footer.Children.Add(done);
            var about = Button(UiText.T("关于", "About", "À propos"), (_, _) => new AboutWindow { Owner = this }.ShowDialog());
            about.HorizontalAlignment = HorizontalAlignment.Left; footer.Children.Add(about);
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var body = new StackPanel();
            root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            Content = root;
            void Heading(string title) => body.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 12) });
            Heading(UiText.T("外观与语言", "Appearance and language", "Apparence et langue"));
            var languageRow = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var language = StatsUi.Choice(190); language.Margin = new Thickness(0); StatsUi.NamedItems(language, nameof(LanguageChoice.Name));
            foreach (var choice in new[] { new LanguageChoice("zh-CN", "简体中文"), new LanguageChoice("en", "English"), new LanguageChoice("fr", "Français") }) language.Items.Add(choice);
            language.SelectedItem = language.Items.Cast<LanguageChoice>().FirstOrDefault(c => c.Code == preferences.Language) ?? language.Items[0];
            DockPanel.SetDock(language, Dock.Right); languageRow.Children.Add(language);
            languageRow.Children.Add(new TextBlock { Text = UiText.T("界面语言", "Interface language", "Langue de l’interface"), VerticalAlignment = VerticalAlignment.Center });
            body.Children.Add(languageRow);
            language.SelectionChanged += (_, _) =>
            {
                if (building || language.SelectedItem is not LanguageChoice choice || preferences.Language == choice.Code) return;
                preferences.Language = choice.Code; UiText.Language = choice.Code; changed(); Rebuild(); AboutWindow.ApplyTitleTheme(this);
            };
            var themes = new WrapPanel();
            foreach (var (value, label) in new[] { ("dark", UiText.T("深色", "Dark", "Sombre")), ("light", UiText.T("浅色", "Light", "Clair")), ("system", UiText.T("跟随系统", "System", "Système")) })
            {
                var radio = new RadioButton { Content = label, GroupName = "theme", IsChecked = preferences.Theme == value, Margin = new Thickness(0, 0, 24, 4), Padding = new Thickness(4, 0, 0, 0) };
                radio.SetResourceReference(ForegroundProperty, "Ink");
                radio.Checked += (_, _) => { if (building) return; preferences.Theme = value; changed(); AboutWindow.ApplyTitleTheme(this); };
                themes.Children.Add(radio);
            }
            body.Children.Add(themes);
            Heading(UiText.T("练习界面", "Practice view", "Affichage de l’exercice"));
            void Check(string label, bool initial, Action<bool> set)
            {
                var check = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsChecked = initial, Margin = new Thickness(0, 0, 0, 11), Padding = new Thickness(4, 0, 0, 0) };
                check.SetResourceReference(ForegroundProperty, "Ink");
                check.Click += (_, _) => { set(check.IsChecked == true); changed(); };
                body.Children.Add(check);
            }
            Check(UiText.T("课程侧栏", "Lesson sidebar", "Volet des cours"), preferences.ShowSidebar, v => preferences.ShowSidebar = v);
            Check(UiText.T("键盘", "Keyboard", "Clavier"), preferences.ShowKeyboard, v => preferences.ShowKeyboard = v);
            Check(UiText.T("手指示意", "Hand guide", "Guide des mains"), preferences.ShowHands, v => preferences.ShowHands = v);
            Check(UiText.T("编码与课程提示", "Codes and lesson hints", "Codes et indications"), preferences.ShowHints, v => preferences.ShowHints = v);
            Check(UiText.T("实时成绩", "Live results", "Résultats en direct"), preferences.ShowStats, v => preferences.ShowStats = v);
            Check(UiText.T("专注时保留键盘", "Keep keyboard in focus mode", "Conserver le clavier en mode concentration"), preferences.FocusShowKeyboard, v => preferences.FocusShowKeyboard = v);
            var shortcut = new TextBlock { Text = UiText.T("F11 切换专注，Esc 退出。", "F11 toggles focus mode; Esc exits it.", "F11 active ou quitte la concentration ; Échap la quitte."), FontSize = 11, TextWrapping = TextWrapping.Wrap };
            shortcut.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); body.Children.Add(shortcut);
            Heading(UiText.T("课程与教学", "Lessons and learning", "Cours et apprentissage"));
            var actions = new WrapPanel();
            void ActionButton(string title, string action, bool enabled = true)
            {
                var button = Button(title, (_, _) => { RequestedAction = action; Close(); }); button.IsEnabled = enabled;
                button.Margin = new Thickness(0, 0, 8, 8); actions.Children.Add(button);
            }
            ActionButton(UiText.T("课程目录", "Lesson folder", "Dossier des cours"), "folder");
            ActionButton(UiText.T("乱序练习", "Shuffle lesson", "Mélanger le cours"), "shuffle", canShuffle);
            ActionButton(UiText.T("86 字根键盘", "86 root keyboard", "Clavier des radicaux 86"), "roots");
            ActionButton(UiText.T("教学参考", "Learning reference", "Référence pédagogique"), "reference");
            ActionButton(UiText.T("指法演示", "Finger demonstration", "Démonstration des doigts"), "demo");
            ActionButton(UiText.T("本课错键巩固", "Review this lesson", "Réviser ce cours"), "review", canReview);
            var reset = Button(UiText.T("本课重新开始", "Restart this lesson", "Recommencer ce cours"), (_, _) => { });
            var armed = false;
            reset.Click += (_, _) => { if (!armed) { armed = true; reset.Content = UiText.T("确认清除本课进度？", "Clear this lesson’s progress?", "Effacer la progression de ce cours ?"); return; } RequestedAction = "reset"; Close(); };
            reset.Margin = new Thickness(0, 0, 8, 8); actions.Children.Add(reset); body.Children.Add(actions);
            var match = Regex.Match(summary, @"\d+");
            var detailTitle = match.Success ? UiText.Format("{0} 门本地课程", "{0} local lessons", "{0} cours locaux", match.Value) : UiText.T("课程信息", "Lesson information", "Informations sur les cours");
            var details = string.IsNullOrWhiteSpace(warnings) ? UiText.T("课程导入正常。", "Lessons loaded successfully.", "Les cours ont été chargés.")
                : string.Join(Environment.NewLine, warnings.Split('\n').Select(s => CourseLabels.Hint(s.Trim())));
            var detail = new Expander { Header = detailTitle, Margin = new Thickness(0, 8, 0, 0), Content = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) } };
            detail.SetResourceReference(ForegroundProperty, "Muted"); body.Children.Add(detail);
            Heading(UiText.T("软件更新", "Software updates", "Mises à jour"));
            Check(UiText.T("自动检查更新", "Check for updates automatically", "Rechercher automatiquement les mises à jour"), preferences.CheckForUpdates, v => preferences.CheckForUpdates = v);
            var update = Button(UiText.T("检查更新", "Check for updates", "Rechercher une mise à jour"), (_, _) => { RequestedAction = "update"; Close(); });
            update.HorizontalAlignment = HorizontalAlignment.Left; body.Children.Add(update);
        }
        finally { building = false; }
    }
    private static Button Button(string label, RoutedEventHandler handler)
    {
        var button = new Button { Content = label, Padding = new Thickness(12, 8, 12, 8), MinWidth = 80 };
        button.Click += handler; return button;
    }
}
