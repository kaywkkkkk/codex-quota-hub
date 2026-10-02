namespace CodexQuotaHub.Desktop;

public static class TelegramMessageFormatter
{
    public static string Due(string account, string duration, string scheduled, long? resetCredits)
    {
        var creditLine = resetCredits is long count
            ? UiPresentation.Text("TelegramResetCreditsLine", count) + "\n" : "";
        return UiPresentation.Text("TelegramDueMessage", account, duration, scheduled, creditLine);
    }
}
