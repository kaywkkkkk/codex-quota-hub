using Microsoft.Data.Sqlite;

namespace CodexQuotaHub.Core;

public sealed class SnapshotStore
{
    private readonly string _connectionString;

    public SnapshotStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));

        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS accounts (
                email TEXT NOT NULL,
                workspace_id TEXT NOT NULL,
                account_type TEXT NOT NULL,
                plan_type TEXT,
                last_success_utc_ms INTEGER NOT NULL,
                available_reset_credits_count INTEGER,
                reset_credit_details_provided INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (email, workspace_id)
            );
            CREATE TABLE IF NOT EXISTS windows (
                email TEXT NOT NULL,
                workspace_id TEXT NOT NULL,
                limit_id TEXT NOT NULL,
                window_name TEXT NOT NULL,
                used_percent REAL NOT NULL,
                window_duration_mins INTEGER,
                resets_at_utc_seconds INTEGER,
                PRIMARY KEY (email, workspace_id, limit_id, window_name),
                FOREIGN KEY (email, workspace_id) REFERENCES accounts(email, workspace_id)
                    ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS notified_resets (
                email TEXT NOT NULL,
                workspace_id TEXT NOT NULL,
                limit_id TEXT NOT NULL,
                window_name TEXT NOT NULL,
                resets_at_utc_seconds INTEGER NOT NULL,
                notified_at_utc_ms INTEGER NOT NULL,
                PRIMARY KEY (email, workspace_id, limit_id, window_name, resets_at_utc_seconds)
            );
            CREATE TABLE IF NOT EXISTS telegram_resets (
                email TEXT NOT NULL,
                workspace_id TEXT NOT NULL,
                limit_id TEXT NOT NULL,
                window_name TEXT NOT NULL,
                window_duration_mins INTEGER,
                resets_at_utc_seconds INTEGER NOT NULL,
                delivered_at_utc_ms INTEGER,
                attempts INTEGER NOT NULL DEFAULT 0,
                next_attempt_at_utc_ms INTEGER NOT NULL,
                PRIMARY KEY (email, workspace_id, limit_id, window_name, resets_at_utc_seconds)
            );
            CREATE TABLE IF NOT EXISTS reset_credit_details (
                email TEXT NOT NULL,
                workspace_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                credit_id TEXT NOT NULL,
                reset_type TEXT NOT NULL,
                status TEXT NOT NULL,
                expires_at_utc_seconds INTEGER,
                title TEXT,
                description TEXT,
                PRIMARY KEY (email, workspace_id, ordinal),
                FOREIGN KEY (email, workspace_id) REFERENCES accounts(email, workspace_id)
                    ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS daily_token_usage (
                email TEXT NOT NULL,
                workspace_id TEXT NOT NULL,
                start_date TEXT NOT NULL,
                tokens INTEGER NOT NULL,
                observed_at_utc_ms INTEGER NOT NULL,
                PRIMARY KEY (email, workspace_id, start_date),
                FOREIGN KEY (email, workspace_id) REFERENCES accounts(email, workspace_id)
                    ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS account_aliases (
                email TEXT NOT NULL,
                workspace_id TEXT NOT NULL,
                alias TEXT NOT NULL,
                PRIMARY KEY (email, workspace_id)
            );
            """;
        command.ExecuteNonQuery();
        using var columns = connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(accounts);";
        using var reader = columns.ExecuteReader();
        var hasResetCredits = false;
        var hasResetCreditDetailsProvided = false;
        while (reader.Read())
        {
            var name = reader.GetString(1);
            hasResetCredits |= name == "available_reset_credits_count";
            hasResetCreditDetailsProvided |= name == "reset_credit_details_provided";
        }
        reader.Close();
        if (!hasResetCredits)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE accounts ADD COLUMN available_reset_credits_count INTEGER;";
            migrate.ExecuteNonQuery();
        }
        if (!hasResetCreditDetailsProvided)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText =
                "ALTER TABLE accounts ADD COLUMN reset_credit_details_provided INTEGER NOT NULL DEFAULT 0;";
            migrate.ExecuteNonQuery();
        }
    }

    public void Save(UsageSnapshot snapshot)
    {
        if (snapshot.Windows.Count == 0)
            throw new ArgumentException("A verified snapshot must contain at least one window.", nameof(snapshot));
        if (snapshot.AvailableResetCredits < 0)
            throw new ArgumentOutOfRangeException(nameof(snapshot));
        if (snapshot.AvailableResetCredits is null && snapshot.ResetCredits is not null)
            throw new ArgumentException("Reset credit details require a confirmed count.", nameof(snapshot));
        if (snapshot.DailyTokens is not null &&
            (snapshot.DailyTokens.Any(day => day.Tokens < 0) ||
             snapshot.DailyTokens.Select(day => day.Date).Distinct().Count() != snapshot.DailyTokens.Count))
            throw new ArgumentException("Daily token data contains invalid or duplicate dates.", nameof(snapshot));

        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var account = connection.CreateCommand())
        {
            account.Transaction = transaction;
            account.CommandText = """
                INSERT INTO accounts (email, workspace_id, account_type, plan_type,
                    last_success_utc_ms, available_reset_credits_count, reset_credit_details_provided)
                VALUES ($email, $workspace, $type, $plan, $observed, $resetCredits, $detailsProvided)
                ON CONFLICT(email, workspace_id) DO UPDATE SET
                    account_type = excluded.account_type,
                    plan_type = excluded.plan_type,
                    last_success_utc_ms = excluded.last_success_utc_ms,
                    available_reset_credits_count = excluded.available_reset_credits_count,
                    reset_credit_details_provided = excluded.reset_credit_details_provided;
                """;
            account.Parameters.AddWithValue("$email", snapshot.Identity.Email);
            account.Parameters.AddWithValue("$workspace", snapshot.Identity.WorkspaceId);
            account.Parameters.AddWithValue("$type", snapshot.Identity.AccountType);
            account.Parameters.AddWithValue("$plan", (object?)snapshot.Identity.PlanType ?? DBNull.Value);
            account.Parameters.AddWithValue("$observed", snapshot.ObservedAtUtc.ToUnixTimeMilliseconds());
            account.Parameters.AddWithValue("$resetCredits",
                (object?)snapshot.AvailableResetCredits ?? DBNull.Value);
            account.Parameters.AddWithValue("$detailsProvided", snapshot.ResetCredits is null ? 0 : 1);
            account.ExecuteNonQuery();
        }

        using (var remove = connection.CreateCommand())
        {
            remove.Transaction = transaction;
            remove.CommandText = "DELETE FROM windows WHERE email = $email AND workspace_id = $workspace;";
            remove.Parameters.AddWithValue("$email", snapshot.Identity.Email);
            remove.Parameters.AddWithValue("$workspace", snapshot.Identity.WorkspaceId);
            remove.ExecuteNonQuery();
        }

        foreach (var window in snapshot.Windows)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO windows (
                    email, workspace_id, limit_id, window_name, used_percent,
                    window_duration_mins, resets_at_utc_seconds)
                VALUES ($email, $workspace, $limit, $name, $used, $duration, $resets);
                """;
            insert.Parameters.AddWithValue("$email", snapshot.Identity.Email);
            insert.Parameters.AddWithValue("$workspace", snapshot.Identity.WorkspaceId);
            insert.Parameters.AddWithValue("$limit", window.LimitId);
            insert.Parameters.AddWithValue("$name", window.WindowName);
            insert.Parameters.AddWithValue("$used", window.UsedPercent);
            insert.Parameters.AddWithValue("$duration", (object?)window.WindowDurationMins ?? DBNull.Value);
            insert.Parameters.AddWithValue("$resets", (object?)window.ResetsAtUtc?.ToUnixTimeSeconds() ?? DBNull.Value);
            insert.ExecuteNonQuery();
        }

        using (var remove = connection.CreateCommand())
        {
            remove.Transaction = transaction;
            remove.CommandText =
                "DELETE FROM reset_credit_details WHERE email = $email AND workspace_id = $workspace;";
            remove.Parameters.AddWithValue("$email", snapshot.Identity.Email);
            remove.Parameters.AddWithValue("$workspace", snapshot.Identity.WorkspaceId);
            remove.ExecuteNonQuery();
        }

        if (snapshot.ResetCredits is not null)
        {
            for (var index = 0; index < snapshot.ResetCredits.Count; index++)
            {
                var credit = snapshot.ResetCredits[index];
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO reset_credit_details (
                        email, workspace_id, ordinal, credit_id, reset_type, status,
                        expires_at_utc_seconds, title, description)
                    VALUES ($email, $workspace, $ordinal, $id, $type, $status,
                        $expires, $title, $description);
                    """;
                insert.Parameters.AddWithValue("$email", snapshot.Identity.Email);
                insert.Parameters.AddWithValue("$workspace", snapshot.Identity.WorkspaceId);
                insert.Parameters.AddWithValue("$ordinal", index);
                insert.Parameters.AddWithValue("$id", credit.Id);
                insert.Parameters.AddWithValue("$type", credit.ResetType);
                insert.Parameters.AddWithValue("$status", credit.Status);
                insert.Parameters.AddWithValue("$expires",
                    (object?)credit.ExpiresAtUtc?.ToUnixTimeSeconds() ?? DBNull.Value);
                insert.Parameters.AddWithValue("$title", (object?)credit.Title ?? DBNull.Value);
                insert.Parameters.AddWithValue("$description", (object?)credit.Description ?? DBNull.Value);
                insert.ExecuteNonQuery();
            }
        }

        // Missing daily data must never turn a previously confirmed value into zero.
        if (snapshot.DailyTokens is not null)
        {
            foreach (var day in snapshot.DailyTokens)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO daily_token_usage (
                        email, workspace_id, start_date, tokens, observed_at_utc_ms)
                    VALUES ($email, $workspace, $date, $tokens, $observed)
                    ON CONFLICT(email, workspace_id, start_date) DO UPDATE SET
                        tokens = excluded.tokens,
                        observed_at_utc_ms = excluded.observed_at_utc_ms;
                    """;
                insert.Parameters.AddWithValue("$email", snapshot.Identity.Email);
                insert.Parameters.AddWithValue("$workspace", snapshot.Identity.WorkspaceId);
                insert.Parameters.AddWithValue("$date", day.Date.ToString("yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$tokens", day.Tokens);
                insert.Parameters.AddWithValue("$observed", day.ObservedAtUtc.ToUnixTimeMilliseconds());
                insert.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    public IReadOnlyList<UsageSnapshot> ReadAll()
    {
        using var connection = Open();
        var snapshots = new List<UsageSnapshot>();
        using (var accounts = connection.CreateCommand())
        {
            accounts.CommandText = """
                SELECT email, workspace_id, account_type, plan_type, last_success_utc_ms,
                    available_reset_credits_count, reset_credit_details_provided
                FROM accounts ORDER BY last_success_utc_ms DESC, email, workspace_id;
                """;
            using var reader = accounts.ExecuteReader();
            while (reader.Read())
            {
                var identity = new AccountIdentity(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3));
                var observed = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4));
                snapshots.Add(new UsageSnapshot(identity, observed, ReadWindows(connection, identity),
                    reader.IsDBNull(5) ? null : reader.GetInt64(5),
                    reader.GetInt64(6) == 0 ? null : ReadResetCredits(connection, identity),
                    ReadDailyTokens(connection, identity)));
            }
        }

        return snapshots;
    }

    public IReadOnlyDictionary<(string Email, string WorkspaceId), string> ReadAccountAliases()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT email, workspace_id, alias FROM account_aliases;";
        using var reader = command.ExecuteReader();
        var aliases = new Dictionary<(string Email, string WorkspaceId), string>();
        while (reader.Read())
            aliases[(reader.GetString(0), reader.GetString(1))] = reader.GetString(2);
        return aliases;
    }

    public string? GetAccountAlias(string email, string workspaceId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT alias FROM account_aliases
            WHERE email = $email AND workspace_id = $workspace;
            """;
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$workspace", workspaceId);
        return command.ExecuteScalar() as string;
    }

    public void SetAccountAlias(string email, string workspaceId, string? alias)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(workspaceId))
            throw new ArgumentException("Account and workspace are required.");
        alias = alias?.Trim();
        if (!string.IsNullOrEmpty(alias) &&
            (alias.Length > 40 || alias.Any(char.IsControl)))
            throw new ArgumentException("Alias must be at most 40 characters and stay on one line.",
                nameof(alias));

        using var connection = Open();
        using var command = connection.CreateCommand();
        if (string.IsNullOrEmpty(alias))
        {
            command.CommandText = """
                DELETE FROM account_aliases WHERE email = $email AND workspace_id = $workspace;
                """;
        }
        else
        {
            command.CommandText = """
                INSERT INTO account_aliases (email, workspace_id, alias)
                VALUES ($email, $workspace, $alias)
                ON CONFLICT(email, workspace_id) DO UPDATE SET alias = excluded.alias;
                """;
            command.Parameters.AddWithValue("$alias", alias);
        }
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$workspace", workspaceId);
        command.ExecuteNonQuery();
    }

    public long? GetAvailableResetCredits(string email, string workspaceId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT available_reset_credits_count FROM accounts
            WHERE email = $email AND workspace_id = $workspace;
            """;
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$workspace", workspaceId);
        return command.ExecuteScalar() is long count ? count : null;
    }

    public bool TryRecordNotification(
        AccountIdentity identity, LimitWindow window, DateTimeOffset nowUtc)
    {
        if (window.ResetsAtUtc is null || nowUtc < window.ResetsAtUtc.Value)
            return false;

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO notified_resets (
                email, workspace_id, limit_id, window_name,
                resets_at_utc_seconds, notified_at_utc_ms)
            VALUES ($email, $workspace, $limit, $name, $resets, $notified);
            """;
        command.Parameters.AddWithValue("$email", identity.Email);
        command.Parameters.AddWithValue("$workspace", identity.WorkspaceId);
        command.Parameters.AddWithValue("$limit", window.LimitId);
        command.Parameters.AddWithValue("$name", window.WindowName);
        command.Parameters.AddWithValue("$resets", window.ResetsAtUtc.Value.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$notified", nowUtc.ToUnixTimeMilliseconds());
        return command.ExecuteNonQuery() == 1;
    }

    public TelegramPreferences GetTelegramPreferences()
    {
        var enabled = GetSetting("telegram_enabled") == "1";
        var chatId = GetSetting("telegram_chat_id") ?? "";
        var protectedToken = GetSetting("telegram_protected_token") ?? "";
        var since = long.TryParse(GetSetting("telegram_active_since_utc_ms"), out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : (DateTimeOffset?)null;
        return new TelegramPreferences(enabled, chatId, protectedToken, since);
    }

    public void SetTelegramPreferences(TelegramPreferences preferences)
    {
        if (preferences.Enabled && (string.IsNullOrWhiteSpace(preferences.ChatId) ||
                                    string.IsNullOrWhiteSpace(preferences.ProtectedToken) ||
                                    preferences.ActiveSinceUtc is null))
            throw new ArgumentException("Enabled Telegram notifications require a chat, token and activation time.",
                nameof(preferences));

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        SetSetting(connection, transaction, "telegram_enabled", preferences.Enabled ? "1" : "0");
        SetSetting(connection, transaction, "telegram_chat_id", preferences.ChatId);
        SetSetting(connection, transaction, "telegram_protected_token", preferences.ProtectedToken);
        SetSetting(connection, transaction, "telegram_active_since_utc_ms",
            preferences.ActiveSinceUtc?.ToUnixTimeMilliseconds().ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? "");
        transaction.Commit();
    }

    public bool TryQueueTelegramReset(AccountIdentity identity, LimitWindow window,
        DateTimeOffset nowUtc, DateTimeOffset activeSinceUtc)
    {
        if (window.ResetsAtUtc is null || nowUtc < window.ResetsAtUtc.Value ||
            window.ResetsAtUtc.Value < activeSinceUtc)
            return false;

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO telegram_resets (
                email, workspace_id, limit_id, window_name, window_duration_mins,
                resets_at_utc_seconds, next_attempt_at_utc_ms)
            VALUES ($email, $workspace, $limit, $name, $duration, $resets, $next);
            """;
        command.Parameters.AddWithValue("$email", identity.Email);
        command.Parameters.AddWithValue("$workspace", identity.WorkspaceId);
        command.Parameters.AddWithValue("$limit", window.LimitId);
        command.Parameters.AddWithValue("$name", window.WindowName);
        command.Parameters.AddWithValue("$duration", (object?)window.WindowDurationMins ?? DBNull.Value);
        command.Parameters.AddWithValue("$resets", window.ResetsAtUtc.Value.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$next", nowUtc.ToUnixTimeMilliseconds());
        return command.ExecuteNonQuery() == 1;
    }

    public TelegramReset? ReadPendingTelegramReset(DateTimeOffset nowUtc,
        DateTimeOffset activeSinceUtc)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT email, workspace_id, limit_id, window_name, window_duration_mins,
                   resets_at_utc_seconds, attempts
            FROM telegram_resets
            WHERE delivered_at_utc_ms IS NULL AND next_attempt_at_utc_ms <= $now
              AND resets_at_utc_seconds * 1000 >= $activeSince
            ORDER BY resets_at_utc_seconds, email, workspace_id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$now", nowUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$activeSince", activeSinceUtc.ToUnixTimeMilliseconds());
        using var reader = command.ExecuteReader();
        return reader.Read() ? new TelegramReset(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(5)), reader.GetInt32(6)) : null;
    }

    public void CompleteTelegramReset(TelegramReset reset, DateTimeOffset nowUtc, bool delivered,
        DateTimeOffset? retryAtUtc = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE telegram_resets SET
                delivered_at_utc_ms = CASE WHEN $delivered = 1 THEN $now ELSE NULL END,
                next_attempt_at_utc_ms = $retry,
                attempts = attempts + 1
            WHERE email = $email AND workspace_id = $workspace AND limit_id = $limit
              AND window_name = $name AND resets_at_utc_seconds = $resets
              AND delivered_at_utc_ms IS NULL;
            """;
        command.Parameters.AddWithValue("$delivered", delivered ? 1 : 0);
        command.Parameters.AddWithValue("$now", nowUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$retry", (retryAtUtc ?? nowUtc).ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$email", reset.Email);
        command.Parameters.AddWithValue("$workspace", reset.WorkspaceId);
        command.Parameters.AddWithValue("$limit", reset.LimitId);
        command.Parameters.AddWithValue("$name", reset.WindowName);
        command.Parameters.AddWithValue("$resets", reset.ResetsAtUtc.ToUnixTimeSeconds());
        command.ExecuteNonQuery();
    }

    public int GetRefreshIntervalSeconds()
    {
        var value = GetSetting("refresh_interval_seconds");
        return int.TryParse(value, out var seconds) && seconds is >= 15 and <= 3600
            ? seconds : 60;
    }

    public string GetUiTheme() => GetSetting("ui_theme") is "light" ? "light" : "dark";

    public string GetUiLanguage() => GetSetting("ui_language") switch
    {
        "en" => "en",
        "zh-CN" => "zh-CN",
        _ => "zh-TW"
    };

    public void SetDesktopPreferences(int intervalSeconds, string theme, string language)
    {
        if (intervalSeconds is < 15 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        if (theme is not ("dark" or "light"))
            throw new ArgumentOutOfRangeException(nameof(theme));
        if (language is not ("zh-TW" or "en" or "zh-CN"))
            throw new ArgumentOutOfRangeException(nameof(language));

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        SetSetting(connection, transaction, "refresh_interval_seconds",
            intervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetSetting(connection, transaction, "ui_theme", theme);
        SetSetting(connection, transaction, "ui_language", language);
        transaction.Commit();
    }

    public void SetRefreshIntervalSeconds(int seconds)
    {
        if (seconds is < 15 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(seconds));

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings (key, value) VALUES ('refresh_interval_seconds', $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$value", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private string? GetSetting(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private static void SetSetting(
        SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<LimitWindow> ReadWindows(SqliteConnection connection, AccountIdentity identity)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT limit_id, window_name, used_percent, window_duration_mins, resets_at_utc_seconds
            FROM windows WHERE email = $email AND workspace_id = $workspace
            ORDER BY limit_id, window_name;
            """;
        command.Parameters.AddWithValue("$email", identity.Email);
        command.Parameters.AddWithValue("$workspace", identity.WorkspaceId);
        using var reader = command.ExecuteReader();
        var windows = new List<LimitWindow>();
        while (reader.Read())
        {
            windows.Add(new LimitWindow(
                reader.GetString(0), reader.GetString(1), reader.GetDouble(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(4))));
        }
        return windows;
    }

    private static IReadOnlyList<ResetCredit> ReadResetCredits(
        SqliteConnection connection, AccountIdentity identity)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT credit_id, reset_type, status, expires_at_utc_seconds, title, description
            FROM reset_credit_details WHERE email = $email AND workspace_id = $workspace
            ORDER BY ordinal;
            """;
        command.Parameters.AddWithValue("$email", identity.Email);
        command.Parameters.AddWithValue("$workspace", identity.WorkspaceId);
        using var reader = command.ExecuteReader();
        var credits = new List<ResetCredit>();
        while (reader.Read())
            credits.Add(new ResetCredit(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        return credits;
    }

    private static IReadOnlyList<DailyTokenUsage> ReadDailyTokens(
        SqliteConnection connection, AccountIdentity identity)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT start_date, tokens, observed_at_utc_ms
            FROM daily_token_usage WHERE email = $email AND workspace_id = $workspace
            ORDER BY start_date DESC;
            """;
        command.Parameters.AddWithValue("$email", identity.Email);
        command.Parameters.AddWithValue("$workspace", identity.WorkspaceId);
        using var reader = command.ExecuteReader();
        var days = new List<DailyTokenUsage>();
        while (reader.Read())
            days.Add(new DailyTokenUsage(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture),
                reader.GetInt64(1),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2))));
        return days;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
        return connection;
    }
}
