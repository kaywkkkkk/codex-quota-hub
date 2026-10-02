namespace CodexQuotaHub.Core;

public sealed record TelegramPreferences(bool Enabled, string ChatId, string ProtectedToken,
    DateTimeOffset? ActiveSinceUtc);

public sealed record TelegramReset(string Email, string WorkspaceId, string LimitId,
    string WindowName, int? WindowDurationMins, DateTimeOffset ResetsAtUtc, int Attempts);
