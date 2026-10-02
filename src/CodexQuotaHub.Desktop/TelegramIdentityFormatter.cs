namespace CodexQuotaHub.Desktop;

public static class TelegramIdentityFormatter
{
    public static string Display(string email, string? alias) =>
        string.IsNullOrWhiteSpace(alias) ? MaskEmail(email) : alias.Trim();

    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 && at < email.Length - 1
            ? $"{email[0]}***{email[at..]}"
            : "***";
    }
}
