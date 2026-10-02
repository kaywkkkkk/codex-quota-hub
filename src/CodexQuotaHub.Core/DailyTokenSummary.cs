namespace CodexQuotaHub.Core;

public sealed record DailyTokenTotal(
    DateOnly Date,
    decimal? Tokens,
    int CoveredAccounts,
    int TotalAccounts);

public static class DailyTokenSummary
{
    public static IReadOnlyList<DateOnly> RecentDates(DateOnly latest, int count)
    {
        if (count is < 1 or > 366)
            throw new ArgumentOutOfRangeException(nameof(count));
        return Enumerable.Range(0, count).Select(offset => latest.AddDays(-offset)).ToArray();
    }

    public static IReadOnlyList<DailyTokenTotal> SumByDate(
        IReadOnlyList<UsageSnapshot> snapshots, IReadOnlyList<DateOnly> dates)
    {
        return dates.Select(date =>
        {
            var available = snapshots
                .Select(snapshot => snapshot.DailyTokens?.FirstOrDefault(day => day.Date == date))
                .Where(day => day is not null)
                .ToArray();
            return new DailyTokenTotal(date,
                available.Length == 0 ? null : available.Sum(day => (decimal)day!.Tokens),
                available.Length, snapshots.Count);
        }).ToArray();
    }
}
