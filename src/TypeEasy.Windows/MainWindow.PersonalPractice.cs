using System.IO;
using System.Windows;
using TypeEasy.Core;

namespace TypeEasy.Windows;

public partial class MainWindow
{
    private PersonalPracticeRepository PersonalPracticeStore() => new(Path.Combine(store, "PersonalPractice"));

    private void LoadPersonalPracticeCourses()
    {
        var repository = PersonalPracticeStore();
        var saved = repository.Load();
        courses.RemoveAll(c => c.Id.StartsWith("personal:", StringComparison.Ordinal));
        courses.AddRange(saved.Select(p => p.Course));
        if (repository.RecoveryNotice is not null)
        {
            StatusLabel.Text = UiText.T("部分专属练习未能读取，原记录已保留。", "Some personal practice could not be read. Existing records were kept.", "Certains exercices ciblés sont illisibles. Les données existantes ont été conservées.");
            StatusLabel.Visibility = Visibility.Visible;
        }
    }

    private void PersonalPractice_Click(object sender, RoutedEventArgs e)
    {
        Guard(() =>
        {
            // Include the last few keystrokes, which may still be in the analytics batch.
            session?.Pause();
            RefreshActiveCourseProgress();
            var repository = PersonalPracticeStore();
            var sourceCourses = courses.Where(c => !c.Id.StartsWith("personal:", StringComparison.Ordinal)
                && !c.Id.StartsWith("review:", StringComparison.Ordinal)).ToArray();
            var analytics = new AnalyticsStore(Path.Combine(store, "Analytics"));
            var candidates = new List<PersonalPracticePlan>();
            foreach (var mode in sourceCourses.Select(TypingMode.ForCourse).Distinct())
            {
                foreach (var kind in new[] { PersonalPracticeKind.Confusions, PersonalPracticeKind.Context })
                {
                    var candidate = PersonalPracticeGenerator.Generate(analytics, sourceCourses, mode, kind);
                    if (candidate is not null) candidates.Add(candidate);
                }
            }
            var saved = repository.Load();
            var progressDirectory = Path.Combine(store, "Progress");
            var window = new PersonalPracticeWindow(candidates, saved, progressDirectory) { Owner = this };
            window.ShowDialog();
            if (window.SelectedPlan is null) return;

            var selected = repository.Save(window.SelectedPlan, progressDirectory, window.StartNewRound);
            LoadPersonalPracticeCourses();
            category = selected.Course.Category;
            rebuildingCourses = true;
            try { SearchBox.Clear(); } finally { rebuildingCourses = false; }
            FilterCourses(selected.Course.Id);
        });
    }
}
