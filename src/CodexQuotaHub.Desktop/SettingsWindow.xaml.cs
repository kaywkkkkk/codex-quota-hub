using System.Windows;
using System.Windows.Controls;
using System.Security.Cryptography;
using CodexQuotaHub.Core;

namespace CodexQuotaHub.Desktop;

public partial class SettingsWindow : Window
{
    private readonly string _lookupCode = "CM" + RandomNumberGenerator.GetInt32(100000, 1000000);
    private string _storedToken = "";
    private readonly TelegramClient _telegram = new();

    public SettingsWindow(int intervalSeconds, UiThemeMode theme, UiLanguage language,
        TelegramPreferences? telegram = null)
    {
        InitializeComponent();
        IntervalChoice.SelectedIndex = intervalSeconds switch { 300 => 1, 900 => 2, _ => 0 };
        ThemeChoice.SelectedIndex = theme == UiThemeMode.Light ? 1 : 0;
        LanguageChoice.SelectedIndex = language switch
        {
            UiLanguage.English => 1,
            UiLanguage.SimplifiedChinese => 2,
            _ => 0
        };
        if (telegram is not null)
        {
            TelegramEnabled.IsChecked = telegram.Enabled;
            TelegramChatId.Text = telegram.ChatId;
            try
            {
                if (!string.IsNullOrEmpty(telegram.ProtectedToken))
                {
                    _storedToken = TelegramTokenProtection.Unprotect(telegram.ProtectedToken);
                    SetTelegramStatus("TelegramTokenSaved");
                }
            }
            catch (Exception error) when (error is CryptographicException or FormatException)
            {
                SetTelegramStatus("TelegramStoredTokenUnavailable");
            }
        }
        LookupInstruction.Text = UiPresentation.Text("TelegramLookupInstruction", _lookupCode);
        Closed += (_, _) => _telegram.Dispose();
    }

    public int IntervalSeconds => int.Parse((string)((ComboBoxItem)IntervalChoice.SelectedItem).Tag);
    public UiThemeMode SelectedTheme => ThemeChoice.SelectedIndex == 1 ? UiThemeMode.Light : UiThemeMode.Dark;
    public UiLanguage SelectedLanguage => LanguageChoice.SelectedIndex switch
    {
        1 => UiLanguage.English,
        2 => UiLanguage.SimplifiedChinese,
        _ => UiLanguage.TraditionalChinese
    };

    public bool TelegramNotificationsEnabled => TelegramEnabled.IsChecked == true;
    public string SelectedTelegramChatId => TelegramChatId.Text.Trim();
    public string SelectedTelegramToken => TelegramBotToken.Password.Trim() is { Length: > 0 } entered
        ? entered : _storedToken;
    public bool TelegramCredentialsChanged { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (TelegramNotificationsEnabled && !CredentialsValid()) return;
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private bool CredentialsValid()
    {
        if (!TelegramClient.ValidToken(SelectedTelegramToken))
        {
            SetTelegramStatus("TelegramInvalidToken");
            return false;
        }
        if (!TelegramClient.ValidChatId(SelectedTelegramChatId))
        {
            SetTelegramStatus("TelegramInvalidChat");
            return false;
        }
        return true;
    }

    private void ClearTelegram_Click(object sender, RoutedEventArgs e)
    {
        TelegramEnabled.IsChecked = false;
        TelegramBotToken.Clear();
        TelegramChatId.Clear();
        _storedToken = "";
        TelegramCredentialsChanged = true;
        SetTelegramStatus("TelegramCleared");
    }

    private async void FindChat_Click(object sender, RoutedEventArgs e)
    {
        if (!TelegramClient.ValidToken(SelectedTelegramToken))
        {
            SetTelegramStatus("TelegramInvalidToken");
            return;
        }
        FindChatButton.IsEnabled = false;
        SetTelegramStatus("TelegramWorking");
        try
        {
            var found = await _telegram.FindPrivateChatAsync(SelectedTelegramToken, _lookupCode);
            if (found.ChatId is not null)
            {
                TelegramChatId.Text = found.ChatId;
                TelegramCredentialsChanged = true;
                SetTelegramStatus("TelegramChatFound");
            }
            else SetTelegramStatus(TelegramErrorKey(found.Result.ErrorCode));
        }
        finally { FindChatButton.IsEnabled = true; }
    }

    private async void TestTelegram_Click(object sender, RoutedEventArgs e)
    {
        if (!CredentialsValid()) return;
        TestTelegramButton.IsEnabled = false;
        SetTelegramStatus("TelegramWorking");
        try
        {
            var result = await _telegram.SendAsync(SelectedTelegramToken, SelectedTelegramChatId,
                UiPresentation.Text("TelegramTestMessage"));
            SetTelegramStatus(result.Success ? "TelegramTestSent" : TelegramErrorKey(result.ErrorCode));
        }
        finally { TestTelegramButton.IsEnabled = true; }
    }

    private static string TelegramErrorKey(string code) => code switch
    {
        "Unauthorized" => "TelegramUnauthorized",
        "Forbidden" => "TelegramForbidden",
        "BadRequest" => "TelegramBadRequest",
        "RateLimited" => "TelegramRateLimited",
        "WebhookConflict" => "TelegramWebhookConflict",
        "ChatNotFound" => "TelegramChatNotFound",
        "Timeout" => "TelegramTimeout",
        "Network" => "TelegramNetwork",
        "RedirectRejected" => "TelegramNetwork",
        "InvalidResponse" => "TelegramInvalidResponse",
        _ => "TelegramApiError"
    };

    private void SetTelegramStatus(string key) => TelegramStatus.Text = UiPresentation.Text(key);
}
