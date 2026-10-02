using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace CodexQuotaHub.Core;

public sealed class AppServerProbe : IUsageProbe
{
    private readonly string _codexExecutable;
    private readonly TimeSpan _timeout;

    public AppServerProbe(string codexExecutable = "codex", TimeSpan? timeout = null)
    {
        _codexExecutable = codexExecutable;
        _timeout = timeout ?? TimeSpan.FromSeconds(20);
    }

    public async Task<ProbeResult> ProbeAsync(
        CancellationToken cancellationToken = default, bool includeDailyTokens = true)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = CodexExecutableLocator.Resolve(_codexExecutable),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };
        process.StartInfo.ArgumentList.Add("app-server");

        try
        {
            if (!process.Start())
                return ProbeResult.Unverified("app_server_start_failed");
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return ProbeResult.Unverified("app_server_start_failed");
        }

        // Drain stderr without logging it; it may contain private account or environment data.
        var stderrDrain = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
        try
        {
            using var initialize = await RequestAsync(process, 0, "initialize", new
            {
                clientInfo = new { name = "codex_quota_hub_core", title = "Codex Quota Hub Core", version = "1.0.0" }
            }, timeout.Token);
            if (!HasResult(initialize.RootElement))
                return ProbeResult.Unverified("initialize_failed");

            await WriteAsync(process, new { method = "initialized", @params = new { } }, timeout.Token);

            using var accountBeforeReply = await RequestAsync(
                process, 1, "account/read", new { refreshToken = false }, timeout.Token);
            if (!TryReadAccount(accountBeforeReply.RootElement, out var before, out var signedOut))
                return ProbeResult.Unverified("account_read_failed");
            if (signedOut)
                return ProbeResult.SignedOut();
            if (before is null)
                return ProbeResult.Unverified("identity_missing");

            using var limitsReply = await RequestAsync(
                process, 2, "account/rateLimits/read", null, timeout.Token);
            if (!TryReadLimits(limitsReply.RootElement, out var workspaceId, out var windows,
                    out var availableResetCredits, out var resetCredits))
                return ProbeResult.Unverified("rate_limits_unverified");

            IReadOnlyList<DailyTokenUsage>? dailyTokens = null;
            if (includeDailyTokens)
            {
                // This optional endpoint must not prevent a verified quota snapshot from saving.
                using var usageTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                usageTimeout.CancelAfter(TimeSpan.FromSeconds(11));
                try
                {
                    using var usageReply = await RequestAsync(
                        process, 3, "account/usage/read", null, usageTimeout.Token);
                    if (HasResult(usageReply.RootElement))
                        dailyTokens = ReadDailyTokens(
                            usageReply.RootElement.GetProperty("result"), DateTimeOffset.UtcNow);

                    if (dailyTokens is not null)
                    {
                        // account/read lacks a Workspace ID. Confirm that usage was read
                        // under the same Workspace as the quota snapshot.
                        using var limitsAfterUsage = await RequestAsync(
                            process, 4, "account/rateLimits/read", null, timeout.Token);
                        if (!TryReadLimits(limitsAfterUsage.RootElement, out var confirmedWorkspace,
                                out _, out _, out _))
                            dailyTokens = null;
                        else if (confirmedWorkspace != workspaceId)
                            return ProbeResult.Unverified("identity_changed_within_probe");
                    }
                }
                catch (OperationCanceledException) when (!timeout.IsCancellationRequested &&
                                                         !cancellationToken.IsCancellationRequested)
                {
                    dailyTokens = null;
                }
                catch (Exception error) when (error is IOException or JsonException or
                                              InvalidOperationException)
                {
                    dailyTokens = null;
                }
            }

            // Catch a change between account/read and the later reads in this process.
            using var accountAfterReply = await RequestAsync(
                process, includeDailyTokens ? 5 : 3, "account/read",
                new { refreshToken = false }, timeout.Token);
            if (!TryReadAccount(accountAfterReply.RootElement, out var after, out var signedOutAfter) ||
                signedOutAfter || before != after)
                return ProbeResult.Unverified("identity_changed_within_probe");

            var identity = new AccountIdentity(
                before.Email, workspaceId!, before.AccountType, before.PlanType);
            return ProbeResult.Verified(new UsageSnapshot(
                identity, DateTimeOffset.UtcNow, windows!, availableResetCredits, resetCredits,
                dailyTokens));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProbeResult.Unverified("probe_timeout");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException)
        {
            return ProbeResult.Unverified("app_server_protocol_error");
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            try { await stderrDrain; }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }

    private static async Task<JsonDocument> RequestAsync(
        Process process, int id, string method, object? parameters, CancellationToken cancellationToken)
    {
        await WriteAsync(process, new { method, id, @params = parameters }, cancellationToken);
        for (var lines = 0; lines < 256; lines++)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null)
                throw new IOException("App Server ended before responding.");
            if (line.Length > 10_000_000)
                throw new InvalidOperationException("App Server reply is too large.");

            JsonDocument document;
            try { document = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }

            if (document.RootElement.TryGetProperty("id", out var replyId) &&
                replyId.ValueKind == JsonValueKind.Number && replyId.TryGetInt32(out var number) &&
                number == id)
                return document;

            document.Dispose(); // Notification or unrelated response.
        }
        throw new InvalidOperationException("Too many unrelated App Server messages.");
    }

    private static async Task WriteAsync(Process process, object request, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(request);
        await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private static bool HasResult(JsonElement reply) =>
        reply.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object;

    private static bool TryReadAccount(
        JsonElement reply, out AccountData? account, out bool signedOut)
    {
        account = null;
        signedOut = false;
        if (!HasResult(reply) || !reply.GetProperty("result").TryGetProperty("account", out var value))
            return false;
        if (value.ValueKind == JsonValueKind.Null)
        {
            signedOut = true;
            return true;
        }
        if (value.ValueKind != JsonValueKind.Object)
            return false;

        var type = GetString(value, "type");
        var email = GetString(value, "email")?.Trim().ToLowerInvariant();
        if (type != "chatgpt" || string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return false;

        account = new AccountData(email, type, GetString(value, "planType"));
        return true;
    }

    private static bool TryReadLimits(
        JsonElement reply, out string? workspaceId, out IReadOnlyList<LimitWindow>? windows,
        out long? availableResetCredits, out IReadOnlyList<ResetCredit>? resetCredits)
    {
        workspaceId = null;
        windows = null;
        availableResetCredits = null;
        resetCredits = null;
        if (!HasResult(reply))
            return false;

        var result = reply.GetProperty("result");
        workspaceId = GetString(result, "accountId")?.Trim();
        if (string.IsNullOrWhiteSpace(workspaceId))
            return false;

        availableResetCredits = ReadAvailableResetCredits(result);
        if (availableResetCredits is not null)
            resetCredits = ReadResetCreditDetails(result);

        var groups = new List<(string Id, JsonElement Data)>();
        if (result.TryGetProperty("rateLimitsByLimitId", out var byId) &&
            byId.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in byId.EnumerateObject())
                groups.Add((property.Name, property.Value));
        }
        if (groups.Count == 0 && result.TryGetProperty("rateLimits", out var legacy) &&
            legacy.ValueKind == JsonValueKind.Object)
            groups.Add((GetString(legacy, "limitId") ?? "legacy", legacy));
        if (groups.Count == 0)
            return false;

        var parsed = new List<LimitWindow>();
        foreach (var (limitId, group) in groups)
        {
            if (string.IsNullOrWhiteSpace(limitId) || group.ValueKind != JsonValueKind.Object)
                return false;
            var countBefore = parsed.Count;
            foreach (var property in group.EnumerateObject())
            {
                var value = property.Value;
                if (value.ValueKind != JsonValueKind.Object ||
                    !(value.TryGetProperty("usedPercent", out _) ||
                      value.TryGetProperty("windowDurationMins", out _) ||
                      value.TryGetProperty("resetsAt", out _)))
                    continue;

                if (!TryReadWindow(limitId, property.Name, value, out var window))
                    return false;
                parsed.Add(window!);
            }
            if (parsed.Count == countBefore)
                return false;
        }

        windows = parsed;
        return true;
    }

    internal static long? ReadAvailableResetCredits(JsonElement result)
    {
        // The detail array can be truncated. Only availableCount is authoritative.
        if (result.TryGetProperty("rateLimitResetCredits", out var resetCredits) &&
            resetCredits.ValueKind == JsonValueKind.Object &&
            resetCredits.TryGetProperty("availableCount", out var availableCount) &&
            availableCount.ValueKind == JsonValueKind.Number &&
            availableCount.TryGetInt64(out var count) && count >= 0)
            return count;
        return null;
    }

    internal static IReadOnlyList<ResetCredit>? ReadResetCreditDetails(JsonElement result)
    {
        if (!result.TryGetProperty("rateLimitResetCredits", out var summary) ||
            summary.ValueKind != JsonValueKind.Object ||
            !summary.TryGetProperty("credits", out var credits) ||
            credits.ValueKind != JsonValueKind.Array)
            return null;

        var parsed = new List<ResetCredit>();
        foreach (var item in credits.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var id = GetString(item, "id");
            var resetType = GetString(item, "resetType");
            var status = GetString(item, "status");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(resetType) ||
                string.IsNullOrWhiteSpace(status) ||
                !item.TryGetProperty("expiresAt", out var expiry))
                continue;

            DateTimeOffset? expiresAt = null;
            if (expiry.ValueKind != JsonValueKind.Null)
            {
                if (expiry.ValueKind != JsonValueKind.Number ||
                    !expiry.TryGetInt64(out var seconds) || seconds < 0)
                    continue;
                try { expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds); }
                catch (ArgumentOutOfRangeException) { continue; }
            }
            parsed.Add(new ResetCredit(id, resetType, status, expiresAt,
                GetString(item, "title"), GetString(item, "description")));
        }
        return parsed;
    }

    internal static IReadOnlyList<DailyTokenUsage>? ReadDailyTokens(
        JsonElement result, DateTimeOffset observedAtUtc)
    {
        if (!result.TryGetProperty("dailyUsageBuckets", out var buckets) ||
            buckets.ValueKind == JsonValueKind.Null)
            return null;
        if (buckets.ValueKind != JsonValueKind.Array)
            return null;

        var parsed = new List<DailyTokenUsage>();
        var dates = new HashSet<DateOnly>();
        foreach (var item in buckets.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("startDate", out var dateValue) ||
                dateValue.ValueKind != JsonValueKind.String ||
                !DateOnly.TryParseExact(dateValue.GetString(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                !dates.Add(date) ||
                !item.TryGetProperty("tokens", out var tokensValue) ||
                tokensValue.ValueKind != JsonValueKind.Number ||
                !tokensValue.TryGetInt64(out var tokens) || tokens < 0)
                return null;
            parsed.Add(new DailyTokenUsage(date, tokens, observedAtUtc));
        }
        return parsed;
    }

    private static bool TryReadWindow(
        string limitId, string windowName, JsonElement value, out LimitWindow? window)
    {
        window = null;
        if (!value.TryGetProperty("usedPercent", out var used) ||
            used.ValueKind != JsonValueKind.Number || !used.TryGetDouble(out var usedPercent) ||
            !double.IsFinite(usedPercent) || usedPercent < 0 || usedPercent > 100)
            return false;

        int? duration = null;
        if (value.TryGetProperty("windowDurationMins", out var durationValue) &&
            durationValue.ValueKind != JsonValueKind.Null)
        {
            if (durationValue.ValueKind != JsonValueKind.Number ||
                !durationValue.TryGetInt32(out var minutes) || minutes <= 0)
                return false;
            duration = minutes;
        }

        DateTimeOffset? resetsAt = null;
        if (value.TryGetProperty("resetsAt", out var resetsValue) &&
            resetsValue.ValueKind != JsonValueKind.Null)
        {
            if (resetsValue.ValueKind != JsonValueKind.Number ||
                !resetsValue.TryGetInt64(out var seconds) || seconds < 0)
                return false;
            try { resetsAt = DateTimeOffset.FromUnixTimeSeconds(seconds); }
            catch (ArgumentOutOfRangeException) { return false; }
        }

        window = new LimitWindow(limitId, windowName, usedPercent, duration, resetsAt);
        return true;
    }

    private static string? GetString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;

    private sealed record AccountData(string Email, string AccountType, string? PlanType);
}
