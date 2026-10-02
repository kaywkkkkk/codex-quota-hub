using System.Globalization;
using CodexQuotaHub.Core;
using Brush = System.Windows.Media.Brush;

namespace CodexQuotaHub.Desktop;

public sealed record DailyTokenDisplayRow(
    string DateText,
    string TokensText,
    string CoverageText,
    string TooltipText,
    Brush ValueBrush);

public static class DailyTokenDisplay
{
    private static Brush KnownBrush => UiPresentation.Brush("TokenKnown");
    private static Brush TotalBrush => UiPresentation.Brush("TokenTotal");
    private static Brush MissingBrush => UiPresentation.Brush("TokenMissing");

    public static IReadOnlyList<DailyTokenDisplayRow> ForAccount(
        UsageSnapshot snapshot, IReadOnlyList<DateOnly> dates) =>
        dates.Select(date =>
        {
            var day = snapshot.DailyTokens?.FirstOrDefault(item => item.Date == date);
            return new DailyTokenDisplayRow(
                DateLabel(date),
                day is null ? "—" : FormatShort(day.Tokens),
                string.Empty,
                day is null
                    ? UiPresentation.Text("SourceDate", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) +
                      "\n" + UiPresentation.Text("TokenAccountMissing")
                    : UiPresentation.Text("SourceDate", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) +
                      "\n" + UiPresentation.Text("TokenLastRead",
                          day.Tokens.ToString("N0", UiPresentation.Culture),
                          day.ObservedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", UiPresentation.Culture)),
                day is null ? MissingBrush : KnownBrush);
        }).ToArray();

    public static IReadOnlyList<DailyTokenDisplayRow> ForTotals(
        IReadOnlyList<DailyTokenTotal> totals) =>
        totals.Select(day => new DailyTokenDisplayRow(
            DateLabel(day.Date),
            day.Tokens is decimal tokens ? FormatShort(tokens) : "—",
            UiPresentation.Text("Coverage", day.CoveredAccounts, day.TotalAccounts),
            day.Tokens is decimal exact
                ? UiPresentation.Text("SourceDate", day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) +
                  "\n" + UiPresentation.Text("TokenTotal", exact.ToString("N0", UiPresentation.Culture),
                      day.CoveredAccounts, day.TotalAccounts)
                : UiPresentation.Text("SourceDate", day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) +
                  "\n" + UiPresentation.Text("TokenTotalMissing"),
            day.Tokens is null ? MissingBrush : TotalBrush)).ToArray();

    private static string DateLabel(DateOnly date) =>
        date.ToString("MM/dd", CultureInfo.InvariantCulture);

    private static string FormatShort(decimal tokens) => tokens switch
    {
        >= 1_000_000_000m => (tokens / 1_000_000_000m).ToString("0.##", UiPresentation.Culture) + "B",
        >= 1_000_000m => (tokens / 1_000_000m).ToString("0.##", UiPresentation.Culture) + "M",
        >= 1_000m => (tokens / 1_000m).ToString("0.##", UiPresentation.Culture) + "K",
        _ => tokens.ToString("N0", UiPresentation.Culture)
    };
}
