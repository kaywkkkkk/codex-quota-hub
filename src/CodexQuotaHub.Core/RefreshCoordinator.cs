namespace CodexQuotaHub.Core;

public sealed class RefreshCoordinator(IUsageProbe probe, SnapshotStore store)
{
    public async Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        // Fresh App Server processes prevent a long-lived connection from retaining an old identity.
        var first = await probe.ProbeAsync(cancellationToken, includeDailyTokens: false);
        var second = await probe.ProbeAsync(cancellationToken, includeDailyTokens: true);

        if (first.Status == ProbeStatus.SignedOut && second.Status == ProbeStatus.SignedOut)
            return new RefreshResult(RefreshStatus.SignedOut, null, null);

        if (first.Status == ProbeStatus.Unverified || second.Status == ProbeStatus.Unverified)
            return new RefreshResult(
                RefreshStatus.Unverified, null, first.Reason ?? second.Reason ?? "probe_unverified");

        if (first.Status != second.Status ||
            first.Snapshot?.Identity != second.Snapshot?.Identity)
            return new RefreshResult(RefreshStatus.ChangedDuringCycle, null, null);

        if (second.Snapshot is null)
            return new RefreshResult(RefreshStatus.Unverified, null, "missing_snapshot");

        store.Save(second.Snapshot);
        return new RefreshResult(RefreshStatus.Saved, second.Snapshot, null);
    }
}
