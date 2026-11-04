using System.IO;
using TypeEasy.Core;

namespace TypeEasy.Windows;

public partial class MainWindow
{
    private readonly Dictionary<string, CourseProgressSummary> courseProgress = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CourseNode> visibleCourseNodes = new(StringComparer.Ordinal);

    private void LoadCourseProgress()
    {
        courseProgress.Clear();
        var directory = Path.Combine(store, "Progress");
        foreach (var course in courses)
            courseProgress[course.Id] = PracticeSession.ReadProgress(course, directory);
    }

    private CourseProgressSummary ProgressFor(Course course)
    {
        if (session?.Course == course) return session.ProgressSummary;
        if (!courseProgress.TryGetValue(course.Id, out var value))
            courseProgress[course.Id] = value = PracticeSession.ReadProgress(course, Path.Combine(store, "Progress"));
        return value;
    }

    private void RefreshActiveCourseProgress()
    {
        if (session is null) return;
        var value = session.ProgressSummary;
        courseProgress[session.Course.Id] = value;
        if (visibleCourseNodes.TryGetValue(session.Course.Id, out var node)) node.SetProgress(value);
    }
}
