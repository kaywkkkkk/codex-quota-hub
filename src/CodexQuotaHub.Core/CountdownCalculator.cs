namespace CodexQuotaHub.Core;

public static class CountdownCalculator
{
    public static Countdown Calculate(DateTimeOffset? resetsAtUtc, DateTimeOffset nowUtc)
    {
        if (resetsAtUtc is null)
            return new Countdown(CountdownStatus.Unknown, null);

        var remaining = resetsAtUtc.Value - nowUtc;
        if (remaining <= TimeSpan.Zero)
            return new Countdown(CountdownStatus.DueUnconfirmed, TimeSpan.Zero);

        return new Countdown(CountdownStatus.Counting, remaining);
    }
}
