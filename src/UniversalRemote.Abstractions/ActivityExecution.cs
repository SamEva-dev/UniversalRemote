namespace UniversalRemote.Remote.Abstractions;

/// <summary>Controls whether a macro stops or continues after a command is not positively accepted.</summary>
public enum ActivityFailurePolicy
{
    /// <summary>Safest default: stop after the first Failed or Unknown command result.</summary>
    StopOnFirstNonAccepted,

    /// <summary>Continue with later steps without retrying the failed or uncertain command.</summary>
    ContinueAfterNonAccepted
}

public enum ActivityRunStatus
{
    Completed,
    CompletedWithIssues,
    StoppedOnNonAccepted,
    Cancelled
}

public enum ActivityStepRunStatus
{
    Accepted,
    DelayCompleted,
    Failed,
    Unknown,
    Cancelled,
    NotRun
}

/// <summary>Sanitized execution outcome for one persisted Activity step.</summary>
public sealed record ActivityStepRunReport(
    int Position,
    ActivityStepRunStatus Status,
    Guid? DeviceId = null,
    RemoteAction? Action = null,
    TimeSpan? Delay = null,
    RemoteErrorCode? Error = null,
    DeliveryState? Delivery = null);

/// <summary>Complete local macro report. It never contains provider endpoints, payloads or secrets.</summary>
public sealed record ActivityRunReport(
    Guid ActivityId,
    string ActivityName,
    ActivityFailurePolicy FailurePolicy,
    ActivityRunStatus Status,
    IReadOnlyList<ActivityStepRunReport> Steps);

/// <summary>Runs one Activity sequentially. Implementations must never replay a command implicitly.</summary>
public interface IActivityRunner
{
    Task<ActivityRunReport> RunAsync(
        Activity activity,
        ActivityFailurePolicy failurePolicy = ActivityFailurePolicy.StopOnFirstNonAccepted,
        CancellationToken cancellationToken = default);
}
