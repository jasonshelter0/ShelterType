namespace TypeEasy.Core;

public enum CourseProgressStatus { NotStarted, InProgress, Completed, Unknown }

/// <summary>Position in the current course, not cumulative successful keystrokes.</summary>
public sealed record CourseProgressSummary(CourseProgressStatus Status, int CompletedCharacters, int TotalCharacters)
{
    public bool IsAvailable => Status != CourseProgressStatus.Unknown;
    public double Fraction => !IsAvailable ? 0 : TotalCharacters == 0 ? 1 : (double)CompletedCharacters / TotalCharacters;

    internal static CourseProgressSummary FromState(PracticeState state, int completed, int total, bool complete)
        => new(complete ? CourseProgressStatus.Completed
            : completed > 0 || state.TotalInputs > 0 || state.Backspaces > 0 ? CourseProgressStatus.InProgress
            : CourseProgressStatus.NotStarted, completed, total);
}
