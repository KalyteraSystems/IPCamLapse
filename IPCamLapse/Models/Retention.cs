namespace IPCamLapse.Models;

/// <summary>What a retention run would delete, for the policy as currently saved.</summary>
/// <param name="Enabled">False when the retention period is zero, i.e. cleanup never runs.</param>
/// <param name="EligibleSessions">Sessions old enough to be removed.</param>
/// <param name="EstimatedBytes">Bytes those sessions occupy, including their metadata files.</param>
/// <param name="EstimateIsPartial">
/// True when a file could not be measured, so the byte total is a floor rather than an exact figure.
/// </param>
public sealed record RetentionPreview(
    bool Enabled,
    int EligibleSessions,
    long EstimatedBytes,
    bool EstimateIsPartial)
{
    public static RetentionPreview Disabled { get; } = new(false, 0, 0, false);
}

/// <summary>What a retention run actually deleted.</summary>
/// <param name="FailedSessionIds">
/// Sessions still present after the run. Their metadata is left in place so a later run retries them.
/// </param>
public sealed record RetentionResult(
    int DeletedSessions,
    long DeletedBytes,
    IReadOnlyList<string> FailedSessionIds,
    bool EstimateIsPartial)
{
    public static RetentionResult Empty { get; } = new(0, 0, Array.Empty<string>(), false);

    public int FailedSessions => FailedSessionIds.Count;
}

/// <summary>The outcome of deleting one session's metadata and directory.</summary>
public sealed record SessionDeletionResult(bool Deleted, string? Error = null)
{
    public static SessionDeletionResult Success { get; } = new(true);

    /// <summary>The session was not there to begin with, which is not a failure.</summary>
    public static SessionDeletionResult NotFound { get; } = new(false);

    public static SessionDeletionResult Failed(string error) => new(false, error);

    /// <summary>True when something is still on disk, so the caller must not report success.</summary>
    public bool IsFailure => Error is not null;
}
