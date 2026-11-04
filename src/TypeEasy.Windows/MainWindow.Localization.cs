using System.Windows.Automation;
using System.Windows.Controls;
using System.Text.RegularExpressions;

namespace TypeEasy.Windows;

public partial class MainWindow
{
    private void ApplyLanguage(bool rebuildCourses = true)
    {
        UiText.Language = prefs.Language;
        DictionaryButton.Content = UiText.T("五笔86词典  F2", "Wubi 86 dictionary  F2", "Dictionnaire Wubi 86  F2");
        prefs.Language = UiText.Language;
        Title = "ShelterType";
        BrandLabel.Text = "ShelterType";
        SetButtonLabel(SpeedButton, UiText.T("测速", "Speed test", "Test de vitesse"));
        SetButtonLabel(StatisticsButton, UiText.T("统计", "Statistics", "Statistiques"));
        SetButtonLabel(SettingsButton, UiText.T("设置", "Settings", "Réglages"));
        PersonalPracticeButton.Content = UiText.T("专属练习", "Personal practice", "Exercices ciblés");
        AutomationProperties.SetName(PersonalPracticeButton, PersonalPracticeButton.Content.ToString());
        RefreshUpdateNotice();
        WubiTab.Content = UiText.Mode("五笔");
        EnglishTab.Content = UiText.Mode("英文");
        ChineseTab.Content = UiText.T("拼音", "Pinyin", "Pinyin");
        RestartCourseButton.Content=UiText.T("重新练习","Practise again","Recommencer");
        SearchPlaceholder.Text = UiText.T("搜索课程", "Search courses", "Rechercher un cours");
        AutomationProperties.SetName(SearchBox, SearchPlaceholder.Text);
        AutomationProperties.SetName(InputBox, UiText.T("练习输入框", "Practice input", "Zone de saisie"));
        if (!ready) CourseTitle.Text = UiText.T("载入课程…", "Loading courses…", "Chargement des cours…");
        if (rebuildCourses && ready)
        {
            // Rebuild presentation only; keep the open session and its partial input intact.
            rebuildingCourses = true;
            try { SearchBox.Clear(); } finally { rebuildingCourses = false; }
            FilterCourses(session?.Course.Id, false);
            UpdateCourseLabels();
            if (session is not null)
                FeedbackLabel.Text = session.LastInputCorrect == false
                    ? WrongKeyMessage(session.LastActual, session.LastExpected) : ResumeMessage(session.ResumeNotice);
            Render();
        }
        ApplyLayout();
    }

    private static void SetButtonLabel(Button button, string label)
    {
        button.ToolTip = label;
        AutomationProperties.SetName(button, label);
    }

    private void UpdateCourseLabels()
    {
        if (session is null) return;
        var title = CourseLabels.Title(session.Course);
        CourseTitle.Text = Regex.IsMatch(title, @"^\d+$")
            ? CourseLabels.Group(session.Course.Group.Split('/').Last()) + " " + title : title;
        CourseTitle.ToolTip = CourseLabels.Path(session.Course);
        if(session.Course.SourceUrl.Length>0)CourseTitle.ToolTip+="\n"+session.Course.Author+"\n"+session.Course.SourceUrl;
        CourseSubtitle.Text = UiText.Mode(session.Course.Category) + " / " + CourseLabels.Group(session.Course.Group);
    }

    private static string ResumeMessage(string? message) => message switch
    {
        null => "",
        "进度文件和备份无法读取，已开始新练习。" => UiText.T(message, "The progress file and backup could not be read. A new practice session has started.", "Le fichier de progression et sa sauvegarde sont illisibles. Un nouvel exercice a commencé."),
        "进度文件损坏，已从最近的备份恢复。" => UiText.T(message, "Progress was restored from the latest backup.", "La progression a été restaurée depuis la dernière sauvegarde."),
        "课程内容已变化，已开始新进度，避免接续到错误位置。" => UiText.T(message, "The course text has changed. Progress has restarted at the beginning.", "Le contenu du cours a changé. L’exercice reprend au début."),
        "保存的进度无效，已从最近的备份恢复。" => UiText.T(message, "Invalid progress was restored from the latest backup.", "La progression invalide a été restaurée depuis la dernière sauvegarde."),
        "保存的进度位置无效，已开始新练习。" => UiText.T(message, "The saved position is invalid. A new practice session has started.", "La position enregistrée est invalide. Un nouvel exercice a commencé."),
        _ => message
    };

    private static string ReferenceTitle(TeachingReference entry) => entry.File switch
    {
        "keyboard-original.png" => UiText.T("键盘分区", "Keyboard zones", "Zones du clavier"),
        "hands-original.png" => UiText.T("双手指法", "Finger placement", "Placement des doigts"),
        "hands-highlight-original.png" => UiText.T("手指提示", "Finger guide", "Guide des doigts"),
        "wubi86-keyboard.png" => UiText.T("五笔86键盘", "Wubi 86 keyboard", "Clavier Wubi 86"),
        "wubi86-roots.png" => UiText.T("五笔86字根", "Wubi 86 roots", "Composants Wubi 86"),
        "wubi98-keyboard.png" => UiText.T("五笔98键盘", "Wubi 98 keyboard", "Clavier Wubi 98"),
        "wubi98-roots.png" => UiText.T("五笔98字根", "Wubi 98 roots", "Composants Wubi 98"),
        "root-atlas86.png" => UiText.T("五笔86字根总表", "Wubi 86 root atlas", "Table des composants Wubi 86"),
        "root-atlas98.png" => UiText.T("五笔98字根总表", "Wubi 98 root atlas", "Table des composants Wubi 98"),
        _ => CourseLabels.CleanTitle(entry.Title)
    };
}
