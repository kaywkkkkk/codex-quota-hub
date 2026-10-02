using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexQuotaHub.Desktop;

public sealed record TelegramResult(bool Success, string ErrorCode = "", TimeSpan? RetryAfter = null);

public sealed record TelegramChatLookup(TelegramResult Result, string? ChatId = null);

public static class TelegramTokenProtection
{
    public static string Protect(string token) => Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string protectedToken) => Encoding.UTF8.GetString(
        ProtectedData.Unprotect(Convert.FromBase64String(protectedToken), null,
            DataProtectionScope.CurrentUser));
}

public sealed class TelegramClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public TelegramClient(HttpClient? http = null)
    {
        _ownsClient = http is null;
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
    }

    public static bool ValidToken(string token)
    {
        var separator = token.IndexOf(':');
        return separator > 0 && separator < token.Length - 1 && token.Length <= 256 &&
               token[..separator].All(char.IsAsciiDigit) &&
               token[(separator + 1)..].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    }

    public static bool ValidChatId(string chatId) =>
        long.TryParse(chatId, System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out var id) && id != 0;

    public async Task<TelegramResult> SendAsync(string token, string chatId, string message,
        CancellationToken cancellationToken = default)
    {
        if (!ValidToken(token) || !ValidChatId(chatId) || string.IsNullOrWhiteSpace(message))
            return new TelegramResult(false, "InvalidConfiguration");
        var (result, document) = await CallAsync(token, "sendMessage", new Dictionary<string, string>
        {
            ["chat_id"] = chatId,
            ["text"] = message
        }, cancellationToken);
        document?.Dispose();
        return result;
    }

    public async Task<TelegramChatLookup> FindPrivateChatAsync(string token, string code,
        CancellationToken cancellationToken = default)
    {
        if (!ValidToken(token)) return new TelegramChatLookup(new TelegramResult(false, "InvalidConfiguration"));
        var (result, document) = await CallAsync(token, "getUpdates", new Dictionary<string, string>
        {
            ["limit"] = "100",
            ["timeout"] = "0"
        }, cancellationToken);
        if (!result.Success || document is null) return new TelegramChatLookup(result);
        using (document)
        {
            if (!document.RootElement.TryGetProperty("result", out var updates) ||
                updates.ValueKind != JsonValueKind.Array)
                return new TelegramChatLookup(new TelegramResult(false, "InvalidResponse"));
            foreach (var update in updates.EnumerateArray().Reverse())
            {
                if (update.ValueKind != JsonValueKind.Object ||
                    !update.TryGetProperty("message", out var item) ||
                    item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("text", out var text) ||
                    text.ValueKind != JsonValueKind.String ||
                    text.GetString() != code ||
                    !item.TryGetProperty("chat", out var chat) ||
                    chat.ValueKind != JsonValueKind.Object ||
                    !chat.TryGetProperty("type", out var type) ||
                    type.ValueKind != JsonValueKind.String || type.GetString() != "private" ||
                    !chat.TryGetProperty("id", out var id) ||
                    id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var number))
                    continue;
                return new TelegramChatLookup(result,
                    number.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return new TelegramChatLookup(new TelegramResult(false, "ChatNotFound"));
    }

    private async Task<(TelegramResult Result, JsonDocument? Document)> CallAsync(
        string token, string method, IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        try
        {
            using var form = new FormUrlEncodedContent(fields);
            using var response = await _http.PostAsync(
                new Uri($"https://api.telegram.org/bot{token}/{method}"), form, cancellationToken);
            if (response.StatusCode is >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest)
                return (new TelegramResult(false, "RedirectRejected"), null);
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                return (new TelegramResult(false, "InvalidResponse"), null);
            }
            if (response.IsSuccessStatusCode && root.TryGetProperty("ok", out var ok) &&
                ok.ValueKind == JsonValueKind.True)
                return (new TelegramResult(true), document);

            var retry = root.TryGetProperty("parameters", out var parameters) &&
                        parameters.ValueKind == JsonValueKind.Object &&
                        parameters.TryGetProperty("retry_after", out var seconds) &&
                        seconds.TryGetInt32(out var count) && count > 0
                ? TimeSpan.FromSeconds(Math.Min(count, 3600)) : (TimeSpan?)null;
            document.Dispose();
            var error = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Unauthorized",
                HttpStatusCode.Forbidden => "Forbidden",
                HttpStatusCode.BadRequest => "BadRequest",
                HttpStatusCode.Conflict => "WebhookConflict",
                HttpStatusCode.TooManyRequests => "RateLimited",
                _ => "ApiError"
            };
            return (new TelegramResult(false, error, retry), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (new TelegramResult(false, "Timeout"), null);
        }
        catch (HttpRequestException)
        {
            return (new TelegramResult(false, "Network"), null);
        }
        catch (JsonException)
        {
            return (new TelegramResult(false, "InvalidResponse"), null);
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
