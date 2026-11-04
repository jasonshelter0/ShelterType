using System.ComponentModel;
using System.Globalization;
using System.Windows;
using TypeEasy.Core;

namespace TypeEasy.Windows;

public sealed class CourseNode : INotifyPropertyChanged
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public Course? Course { get; set; }
    public List<CourseNode> Children { get; set; } = new();
    public bool Expanded { get; set; }
    public bool Selected { get; set; }
    public CourseProgressSummary Progress { get; private set; } = new(CourseProgressStatus.NotStarted, 0, 0);
    public Visibility ProgressVisibility => Course is null ? Visibility.Collapsed : Visibility.Visible;
    public string PercentText => Progress.Status != CourseProgressStatus.InProgress ? ""
        : Progress.Fraction is > 0 and < .01 ? "<1%"
        : Math.Min(99, Math.Floor(Progress.Fraction * 100)).ToString("0", CultureInfo.InvariantCulture) + "%";
    public string StatusText => Progress.Status switch
    {
        CourseProgressStatus.Completed => UiText.T("已完成", "Completed", "Terminé"),
        CourseProgressStatus.InProgress => UiText.T("进行中", "In progress", "En cours"),
        CourseProgressStatus.Unknown => UiText.T("进度暂不可读", "Progress unavailable", "Progression indisponible"),
        _ => UiText.T("未完成", "Not started", "Non commencé")
    };
    public string ProgressDescription => Progress.Status == CourseProgressStatus.InProgress
        ? UiText.Format("{0}  {1}  ({2:N0}/{3:N0} 字符)", "{0}  {1}  ({2:N0}/{3:N0} characters)", "{0}  {1}  ({2:N0}/{3:N0} caractères)",
            StatusText, PercentText, Progress.CompletedCharacters, Progress.TotalCharacters) : StatusText;
    public string ToolTipText => Course is null ? Description ?? Title : (Description ?? Title) + "\n" + ProgressDescription;
    public string AccessibleName => Course is null ? Title : Title + ", " + ProgressDescription;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetProgress(CourseProgressSummary value)
    {
        if (Progress == value) return;
        Progress = value;
        foreach (var name in new[] { nameof(Progress), nameof(PercentText), nameof(StatusText), nameof(ProgressDescription), nameof(ToolTipText), nameof(AccessibleName) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
