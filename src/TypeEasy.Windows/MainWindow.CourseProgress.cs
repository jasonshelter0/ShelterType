using System.IO;
using TypeEasy.Core;

namespace TypeEasy.Windows;

public partial class MainWindow
{
    private void AdvanceAfterCompletion()
    {
        if(session is null||!session.IsComplete)return;
        var completed=session.Course;
        RefreshActiveCourseProgress();
        var peers=courses.Where(c=>c.Category==completed.Category&&c.Variant==completed.Variant&&c.Id!=completed.Id).ToList();
        var index=courses.IndexOf(completed);
        var next=peers.OrderBy(c=>c.Group==completed.Group?0:1)
            .ThenBy(c=>courses.IndexOf(c)>index?0:1).ThenBy(c=>courses.IndexOf(c))
            .FirstOrDefault(c=>ProgressFor(c).Status is CourseProgressStatus.NotStarted or CourseProgressStatus.InProgress);
        if(next is null)return;
        // Defer until the committed input has been fully handled, including IME batches.
        Dispatcher.BeginInvoke(new Action(()=>{if(session?.Course!=completed||!session.IsComplete)return;SearchBox.Text="";FilterCourses(next.Id);InputBox.Focus();}));
    }
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
