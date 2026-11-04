using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using TypeEasy.Core;

namespace TypeEasy.Windows;

/// <summary>Read-only choice of a saved practice or a freshly calculated candidate.</summary>
public sealed class PersonalPracticeWindow : Window
{
    private readonly StackPanel body = new();
    private readonly List<Button> actions = new();
    private readonly List<FrameworkElement> cards = new();
    public PersonalPracticePlan? SelectedPlan { get; private set; }
    public bool StartNewRound { get; private set; }

    public PersonalPracticeWindow(IReadOnlyList<PersonalPracticePlan> candidates,
        IReadOnlyList<PersonalPracticePlan> saved, string progressDirectory)
    {
        StatsUi.Window(this, UiText.T("专属练习", "Personal practice", "Exercices ciblés"), 880, 720);
        var layout = new DockPanel { Margin = new Thickness(26) };
        var title = StatsUi.Text(UiText.T("专属练习", "Personal practice", "Exercices ciblés"), 26);
        title.Margin = new Thickness(0, 0, 0, 20); DockPanel.SetDock(title, Dock.Top); layout.Children.Add(title);
        layout.Children.Add(new ScrollViewer
        {
            Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        Content = layout;

        var slots = saved.Concat(candidates).Select(p => (p.Mode, p.Kind)).Distinct()
            .OrderBy(p => p.Mode, StringComparer.Ordinal).ThenBy(p => p.Kind).ToArray();
        foreach (var slot in slots)
        {
            var previous = saved.FirstOrDefault(p => p.Mode == slot.Mode && p.Kind == slot.Kind);
            var candidate = candidates.FirstOrDefault(p => p.Mode == slot.Mode && p.Kind == slot.Kind);
            var progress = previous is null
                ? new CourseProgressSummary(CourseProgressStatus.NotStarted, 0, candidate!.Course.Items.Sum(i => i.Expected.Length))
                : PracticeSession.ReadProgress(previous.Course, progressDirectory);
            AddCard(previous, candidate, progress);
        }
        if (slots.Length == 0)
            body.Children.Add(StatsUi.Text(UiText.T("积累错键后可生成练习", "Practice becomes available after recorded mistakes", "Les exercices seront disponibles après des erreurs enregistrées"), 14, "Muted"));
    }

    private void AddCard(PersonalPracticePlan? previous, PersonalPracticePlan? candidate, CourseProgressSummary progress)
    {
        // An unfinished (or unreadable) saved snapshot always wins over a new candidate.
        var completed = previous is not null && progress.Status == CourseProgressStatus.Completed;
        var nextRound = completed && candidate is not null;
        var shown = previous is null || nextRound ? candidate! : previous;
        var panel = new StackPanel();
        var header = new DockPanel();
        if (!completed || candidate is not null)
        {
            var label = previous is null ? UiText.T("开始", "Start", "Commencer")
                : nextRound ? UiText.T("生成下一组", "Next set", "Série suivante")
                : UiText.T("继续", "Continue", "Continuer");
            var chosen = nextRound || previous is null ? candidate! : previous;
            var action = StatsUi.Button(label, (_, _) => Choose(chosen, nextRound));
            action.Margin = new Thickness(16, 0, 0, 0); action.VerticalAlignment = VerticalAlignment.Top;
            action.SetResourceReference(Control.BorderBrushProperty, "Accent");
            AutomationProperties.SetName(action, label + " " + UiText.Mode(shown.Mode) + " " + KindLabel(shown.Kind));
            actions.Add(action); DockPanel.SetDock(action, Dock.Right); header.Children.Add(action);
        }
        var heading = new StackPanel();
        heading.Children.Add(StatsUi.Text(KindLabel(shown.Kind), 18));
        heading.Children.Add(StatsUi.Text(UiText.Mode(shown.Mode), 12, "Muted"));
        header.Children.Add(heading); panel.Children.Add(header);

        var progressRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 13, 0, 10) };
        var progressText = ProgressLabel(progress);
        progressRow.Children.Add(new CourseProgressIndicator { Status = progress.Status, Fraction = progress.Fraction, VerticalAlignment = VerticalAlignment.Center });
        var state = StatsUi.Text(progressText, 12, "Muted"); state.Margin = new Thickness(7, 0, 0, 0); progressRow.Children.Add(state);
        AutomationProperties.SetName(progressRow, progressText); panel.Children.Add(progressRow);
        if (nextRound)
        {
            var nextLabel = StatsUi.Text(UiText.T("下一组", "Next set", "Série suivante"), 12, "Muted");
            nextLabel.Margin = new Thickness(0, 0, 0, 6); panel.Children.Add(nextLabel);
        }
        foreach (var target in shown.Targets.Take(3)) panel.Children.Add(TargetRow(target, shown.Kind));
        if (shown.Targets.Count > 3)
        {
            var extra = new StackPanel();
            foreach (var target in shown.Targets.Skip(3)) extra.Children.Add(TargetRow(target, shown.Kind));
            var count = shown.Targets.Count - 3;
            var more = new Expander
            {
                Header = StatsUi.Text(UiText.Format("另 {0} 项", "{0} more", "{0} autres", count), 12, "Muted"),
                Content = extra, Margin = new Thickness(0, 7, 0, 0)
            };
            more.SetResourceReference(Control.ForegroundProperty, "Ink"); panel.Children.Add(more);
        }
        var card = StatsUi.Card(panel); cards.Add(card); body.Children.Add(card);
    }

    private void Choose(PersonalPracticePlan plan, bool newRound)
    {
        SelectedPlan = plan; StartNewRound = newRound; DialogResult = true;
    }

    private static FrameworkElement TargetRow(PersonalPracticeTarget target, PersonalPracticeKind kind)
    {
        var row = new StackPanel { Margin = new Thickness(0, 3, 0, 5) };
        var text = StatsUi.Text("", 13);
        text.Inlines.Add(new Run(UiText.T("应输入 ", "Expected ", "Attendu : ")));
        var expected = new Run(UiText.Key(target.Expected)) { FontWeight = FontWeights.SemiBold };
        expected.SetResourceReference(TextElement.ForegroundProperty, "Accent"); text.Inlines.Add(expected);
        text.Inlines.Add(new Run(UiText.T(" → 误输入 ", " → entered ", " → saisi : ")));
        var actual = new Run(UiText.Key(target.Actual)) { FontWeight = FontWeights.SemiBold };
        actual.SetResourceReference(TextElement.ForegroundProperty, "Error"); text.Inlines.Add(actual);
        var count = new Run(UiText.Format("   {0:N0} 次", "   {0:N0} times", "   {0:N0} fois", target.Errors));
        count.SetResourceReference(TextElement.ForegroundProperty, "Muted"); text.Inlines.Add(count); row.Children.Add(text);
        if (kind == PersonalPracticeKind.Context)
            row.Children.Add(StatsUi.Text(UiText.T("原文：", "Source: ", "Texte : ") + UiText.Key(target.Before) + "〔" + UiText.Key(target.Expected) + "〕" + UiText.Key(target.After), 12, "Muted"));
        return row;
    }

    private static string KindLabel(PersonalPracticeKind kind) => kind == PersonalPracticeKind.Context
        ? UiText.T("语境巩固", "Context practice", "Entraînement en contexte")
        : UiText.T("易混输入", "Common mix-ups", "Confusions fréquentes");

    private static string ProgressLabel(CourseProgressSummary progress) => progress.Status switch
    {
        CourseProgressStatus.Completed => UiText.T("已完成", "Completed", "Terminé"),
        CourseProgressStatus.Unknown => UiText.T("进度暂不可读", "Progress unavailable", "Progression indisponible"),
        CourseProgressStatus.InProgress => UiText.Format("{0:N0} / {1:N0} 字符", "{0:N0} / {1:N0} characters", "{0:N0} / {1:N0} caractères", progress.CompletedCharacters, progress.TotalCharacters),
        _ => UiText.Format("{0:N0} 字符", "{0:N0} characters", "{0:N0} caractères", progress.TotalCharacters)
    };
}
