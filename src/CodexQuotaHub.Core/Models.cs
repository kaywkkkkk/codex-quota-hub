namespace CodexQuotaHub.Core;

public sealed record AccountIdentity(
    string Email,
    string WorkspaceId,
    string AccountType,
    string? PlanType);

public sealed record LimitWindow(
    string LimitId,
    string WindowName,
    double UsedPercent,
    int? WindowDurationMins,
    DateTimeOffset? ResetsAtUtc);

public sealed record ResetCredit(
    string Id,
    string ResetType,
    string Status,
    DateTimeOffset? ExpiresAtUtc,
    string? Title,
    string? Description);

public sealed record DailyTokenUsage(
    DateOnly Date,
    long Tokens,
    DateTimeOffset ObservedAtUtc);

public sealed record UsageSnapshot(
    AccountIdentity Identity,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyList<LimitWindow> Windows,
    long? AvailableResetCredits = null,
    IReadOnlyList<ResetCredit>? ResetCredits = null,
    IReadOnlyList<DailyTokenUsage>? DailyTokens = null);

public enum ProbeStatus
{
    Verified,
    SignedOut,
    Unverified
}

public sealed record ProbeResult(ProbeStatus Status, UsageSnapshot? Snapshot, string? Reason)
{
    public static ProbeResult Verified(UsageSnapshot snapshot) => new(ProbeStatus.Verified, snapshot, null);
    public static ProbeResult SignedOut() => new(ProbeStatus.SignedOut, null, null);
    public static ProbeResult Unverified(string reason) => new(ProbeStatus.Unverified, null, reason);
}

public interface IUsageProbe
{
    Task<ProbeResult> ProbeAsync(
        CancellationToken cancellationToken = default, bool includeDailyTokens = true);
}

public enum RefreshStatus
{
    Saved,
    SignedOut,
    ChangedDuringCycle,
    Unverified
}

public sealed record RefreshResult(RefreshStatus Status, UsageSnapshot? Snapshot, string? Reason);

public enum CountdownStatus
{
    Counting,
    DueUnconfirmed,
    Unknown
}

public sealed record Countdown(CountdownStatus Status, TimeSpan? Remaining);
