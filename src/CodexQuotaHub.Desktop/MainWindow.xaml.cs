using System.ComponentModel;
using System.IO;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CodexQuotaHub.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CodexQuotaHub.Desktop;

public partial class MainWindow : Window
{
    private enum DisplayMode { Detailed, Grid, Compact }

    private readonly SnapshotStore _store;
    private readonly RefreshCoordinator _coordinator;
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ToolStripMenuItem _openMenu;
    private readonly Forms.ToolStripMenuItem _refreshMenu;
    private readonly Forms.ToolStripMenuItem _pauseMenu;
    private readonly Forms.ToolStripMenuItem _settingsMenu;
    private readonly Forms.ToolStripMenuItem _exitMenu;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<string> _dueRefreshRequested = new();
    private readonly HashSet<string> _notificationsChecked = new();
    private readonly HashSet<string> _telegramQueued = new();
    private readonly TelegramClient _telegramClient = new();
    private TelegramPreferences _telegramPreferences = new(false, "", "", null);
    private string _telegramToken = "";
    private DateTimeOffset _nextTelegramCheckUtc = DateTimeOffset.MinValue;
    private bool _telegramSending;
    private bool _telegramErrorShown;
    private IReadOnlyList<UsageSnapshot> _snapshots = [];
    private IReadOnlyList<AccountCardViewModel> _cards = [];
    private DateOnly? _tokenDisplayDate;
    private AccountIdentity? _currentIdentity;
    private RefreshStatus? _lastStatus;
    private string? _lastReason;
    private string? _statusOverrideKey;
    private string? _statusOverrideDetail;
    private int _baseIntervalSeconds;
    private int _nextIntervalSeconds;
    private DateTimeOffset? _nextRefreshAtUtc;
    private bool _isRefreshing;
    private bool _paused;
    private bool _exiting;
    private DisplayMode _displayMode = DisplayMode.Detailed;
    private double _expandedWidth = 980;
    private double _expandedHeight = 740;

    public MainWindow(string? databasePath = null, string codexExecutable = "codex", IUsageProbe? probe = null)
    {
        databasePath ??= ApplicationPaths.GetDefaultDatabasePath();
        _store = new SnapshotStore(databasePath);
        LoadTelegramPreferences();
        UiPresentation.Apply(_store.GetUiTheme() == "light" ? UiThemeMode.Light : UiThemeMode.Dark,
            _store.GetUiLanguage() switch
            {
                "en" => UiLanguage.English,
                "zh-CN" => UiLanguage.SimplifiedChinese,
                _ => UiLanguage.TraditionalChinese
            });
        InitializeComponent();
        _coordinator = new RefreshCoordinator(probe ?? new AppServerProbe(codexExecutable), _store);
        _baseIntervalSeconds = _store.GetRefreshIntervalSeconds();
        _nextIntervalSeconds = _baseIntervalSeconds;

        var menu = new Forms.ContextMenuStrip();
        _openMenu = new Forms.ToolStripMenuItem(UiPresentation.Text("Open"));
        _openMenu.Click += (_, _) => Dispatcher.BeginInvoke(ShowWindow);
        menu.Items.Add(_openMenu);
        _refreshMenu = new Forms.ToolStripMenuItem(UiPresentation.Text("Refresh"));
        _refreshMenu.Click += (_, _) => Dispatcher.BeginInvoke(() => _ = RefreshAsync(manual: true));
        menu.Items.Add(_refreshMenu);
        _pauseMenu = new Forms.ToolStripMenuItem(UiPresentation.Text("Pause"));
        _pauseMenu.Click += (_, _) => Dispatcher.BeginInvoke(TogglePause);
        menu.Items.Add(_pauseMenu);
        _settingsMenu = new Forms.ToolStripMenuItem(UiPresentation.Text("Settings"));
        _settingsMenu.Click += (_, _) => Dispatcher.BeginInvoke(ShowSettings);
        menu.Items.Add(_settingsMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        _exitMenu = new Forms.ToolStripMenuItem(UiPresentation.Text("Exit"));
        _exitMenu.Click += (_, _) => Dispatcher.BeginInvoke(ExitApplication);
        menu.Items.Add(_exitMenu);
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Codex Quota Hub",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.MouseDoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowWindow);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_baseIntervalSeconds) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => OnClockTick();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        Closing += OnClosing;
        Loaded += async (_, _) =>
        {
            try { ReloadCards(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                          Microsoft.Data.Sqlite.SqliteException)
            {
                SetStatusOverride("SnapshotReadError", error.Message);
            }
            _clockTimer.Start();
            await RefreshAsync();
        };
        UpdateIntervalLabel();
        SetDisplayMode(_displayMode);
    }

    private async Task RefreshAsync(bool manual = false)
    {
        if (_exiting || _isRefreshing || (_paused && !manual)) return;
        _isRefreshing = true;
        _refreshTimer.Stop();
        _nextRefreshAtUtc = null;
        RefreshButton.IsEnabled = false;
        SetStatusOverride("QueryingAccount");
        UpdateIntervalLabel();
        try
        {
            var result = await _coordinator.RefreshAsync(_shutdown.Token);
            _lastStatus = result.Status;
            _lastReason = result.Reason;
            _currentIdentity = result.Status == RefreshStatus.Saved ? result.Snapshot!.Identity : null;
            _statusOverrideKey = null;
            UpdateStatusText();
            _nextIntervalSeconds = result.Status == RefreshStatus.Unverified
                ? Math.Min(900, Math.Max(_baseIntervalSeconds, _nextIntervalSeconds * 2))
                : _baseIntervalSeconds;
            ReloadCards();
        }
        catch (OperationCanceledException) when (_exiting)
        {
            return;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      Microsoft.Data.Sqlite.SqliteException)
        {
            _lastStatus = RefreshStatus.Unverified;
            _lastReason = null;
            _currentIdentity = null;
            _nextIntervalSeconds = Math.Min(900, Math.Max(_baseIntervalSeconds, _nextIntervalSeconds * 2));
            SetStatusOverride("DatabaseError", error.Message);
        }
        finally
        {
            _isRefreshing = false;
            RefreshButton.IsEnabled = true;
            _refreshTimer.Stop();
            _refreshTimer.Interval = TimeSpan.FromSeconds(_nextIntervalSeconds);
            if (!_paused && !_exiting)
            {
                _nextRefreshAtUtc = DateTimeOffset.UtcNow.Add(_refreshTimer.Interval);
                _refreshTimer.Start();
            }
            UpdateIntervalLabel();
        }
    }

    private void ReloadCards()
    {
        var expandedByAccount = _cards.ToDictionary(
            card => (card.Snapshot.Identity.Email, card.Snapshot.Identity.WorkspaceId),
            card => card.IsExpanded);
        _snapshots = _store.ReadAll();
        var aliases = _store.ReadAccountAliases();
        var now = DateTimeOffset.UtcNow;
        _tokenDisplayDate = DateOnly.FromDateTime(now.ToLocalTime().DateTime);
        var tokenDates = DailyTokenSummary.RecentDates(_tokenDisplayDate.Value, 7);
        _cards = _snapshots.Select(snapshot => new AccountCardViewModel(
            snapshot,
            _lastStatus == RefreshStatus.Saved && snapshot.Identity == _currentIdentity
                ? UiPresentation.Text("CurrentUpdated")
                : _lastStatus == RefreshStatus.Unverified
                    ? UiPresentation.Text("OfflineUnavailable")
                    : UiPresentation.Text("OfflineSnapshot"),
            now,
            expandedByAccount.TryGetValue(
                (snapshot.Identity.Email, snapshot.Identity.WorkspaceId), out var isExpanded)
                ? isExpanded : true,
            tokenDates,
            aliases.TryGetValue((snapshot.Identity.Email, snapshot.Identity.WorkspaceId), out var alias)
                ? alias : null)).ToList();
        DailyTokenTotals.ItemsSource = DailyTokenDisplay.ForTotals(
            DailyTokenSummary.SumByDate(_snapshots, tokenDates));
        AccountList.ItemsSource = _cards;
        AccountGrid.ItemsSource = _cards;
        CompactList.ItemsSource = _cards;
        EmptyText.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnClockTick()
    {
        var now = DateTimeOffset.UtcNow;
        if (_tokenDisplayDate != DateOnly.FromDateTime(now.ToLocalTime().DateTime))
        {
            try { ReloadCards(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                          Microsoft.Data.Sqlite.SqliteException)
            {
                SetStatusOverride("DailyReadError", error.Message);
            }
        }
        foreach (var card in _cards) card.Update(now);
        UpdateIntervalLabel();
        if (_paused || _exiting) return;

        // Persist desktop and Telegram deliveries independently for each reset cycle.
        try
        {
            var balloonShown = false;
            foreach (var snapshot in _snapshots)
            {
                foreach (var window in snapshot.Windows)
                {
                    if (window.ResetsAtUtc is null || now < window.ResetsAtUtc.Value) continue;
                    var key = ResetKey(snapshot.Identity, window);
                    if (_telegramPreferences.Enabled && _telegramPreferences.ActiveSinceUtc is { } since &&
                        window.ResetsAtUtc.Value >= since && !_telegramQueued.Contains(key))
                    {
                        _store.TryQueueTelegramReset(snapshot.Identity, window, now, since);
                        _telegramQueued.Add(key);
                    }
                    if (balloonShown || _notificationsChecked.Contains(key)) continue;
                    var shouldNotify = _store.TryRecordNotification(snapshot.Identity, window, now);
                    _notificationsChecked.Add(key);
                    if (!shouldNotify) continue;
                    var duration = DescribeDuration(window.WindowDurationMins, window.WindowName);
                    _tray.ShowBalloonTip(10000, UiPresentation.Text("ResetDueTitle"),
                        UiPresentation.Text("ResetDueBody", snapshot.Identity.Email, duration),
                        Forms.ToolTipIcon.Info);
                    balloonShown = true;
                }
            }
            if (_telegramPreferences.Enabled && !_telegramSending && now >= _nextTelegramCheckUtc)
            {
                _nextTelegramCheckUtc = now.AddSeconds(5);
                _ = DispatchTelegramAsync();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      Microsoft.Data.Sqlite.SqliteException)
        {
            SetStatusOverride("NotificationError", error.Message);
        }

        if (_currentIdentity is null || _isRefreshing) return;
        foreach (var snapshot in _snapshots.Where(x => x.Identity == _currentIdentity))
        {
            foreach (var window in snapshot.Windows)
            {
                if (window.ResetsAtUtc is null || now < window.ResetsAtUtc.Value.AddSeconds(5)) continue;
                var key = ResetKey(snapshot.Identity, window);
                if (_dueRefreshRequested.Add(key))
                {
                    _ = RefreshAsync();
                    return;
                }
            }
        }
    }

    private static string DescribeDuration(int? minutes, string name) => minutes switch
    {
        300 => UiPresentation.Text("FiveHours"),
        10080 => UiPresentation.Text("Weekly"),
        int value => UiPresentation.Text("Minutes", value),
        null => name
    };

    private void LoadTelegramPreferences()
    {
        _telegramPreferences = _store.GetTelegramPreferences();
        _telegramToken = "";
        if (!_telegramPreferences.Enabled) return;
        try { _telegramToken = TelegramTokenProtection.Unprotect(_telegramPreferences.ProtectedToken); }
        catch (Exception error) when (error is CryptographicException or FormatException)
        {
            _telegramPreferences = _telegramPreferences with { Enabled = false };
        }
    }

    private async Task DispatchTelegramAsync()
    {
        _telegramSending = true;
        try
        {
            if (_telegramPreferences.ActiveSinceUtc is not { } since) return;
            var pending = _store.ReadPendingTelegramReset(DateTimeOffset.UtcNow, since);
            if (pending is null) return;
            var duration = pending.WindowDurationMins is { } minutes
                ? DescribeDuration(minutes, "") : UiPresentation.Text("Quota");
            var scheduled = pending.ResetsAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm",
                UiPresentation.Culture);
            var alias = _store.GetAccountAlias(pending.Email, pending.WorkspaceId);
            var accountLabel = TelegramIdentityFormatter.Display(pending.Email, alias);
            var resetCredits = _store.GetAvailableResetCredits(pending.Email, pending.WorkspaceId);
            var message = TelegramMessageFormatter.Due(accountLabel, duration, scheduled, resetCredits);
            var result = await _telegramClient.SendAsync(
                _telegramToken, _telegramPreferences.ChatId, message, _shutdown.Token);
            var now = DateTimeOffset.UtcNow;
            var retry = result.Success ? (DateTimeOffset?)null : now.Add(
                result.RetryAfter ?? TimeSpan.FromSeconds(result.ErrorCode is "Unauthorized" or "Forbidden"
                    ? 900 : Math.Min(900, 30 * Math.Pow(2, Math.Min(pending.Attempts, 5)))));
            _store.CompleteTelegramReset(pending, now, result.Success, retry);
            if (result.Success) _telegramErrorShown = false;
            else if (!_telegramErrorShown)
            {
                _telegramErrorShown = true;
                _tray.ShowBalloonTip(10000, "Codex Quota Hub",
                    UiPresentation.Text("TelegramSendFailed"), Forms.ToolTipIcon.Warning);
            }
        }
        catch (OperationCanceledException) when (_exiting) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      Microsoft.Data.Sqlite.SqliteException)
        {
            SetStatusOverride("NotificationError", error.Message);
        }
        finally { _telegramSending = false; }
    }

    private static string ResetKey(AccountIdentity identity, LimitWindow window) =>
        $"{identity.Email}\u001f{identity.WorkspaceId}\u001f{window.LimitId}\u001f{window.WindowName}\u001f{window.ResetsAtUtc!.Value.ToUnixTimeSeconds()}";

    private void SetStatusOverride(string key, string? detail = null)
    {
        _statusOverrideKey = key;
        _statusOverrideDetail = detail;
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        if (_statusOverrideKey is string key)
        {
            StatusText.Text = _statusOverrideDetail is string detail
                ? UiPresentation.Text(key, detail) : UiPresentation.Text(key);
            return;
        }
        StatusText.Text = _lastStatus switch
        {
            RefreshStatus.Saved when _currentIdentity is not null =>
                UiPresentation.Text("SavedStatus", _currentIdentity.Email),
            RefreshStatus.SignedOut => UiPresentation.Text("SignedOutStatus"),
            RefreshStatus.ChangedDuringCycle => UiPresentation.Text("ChangedStatus"),
            RefreshStatus.Unverified => UiPresentation.Text("UnverifiedStatus", DescribeReason(_lastReason)),
            _ => UiPresentation.Text("Loading")
        };
    }

    private static string DescribeReason(string? reason) => UiPresentation.Text(reason switch
    {
        "app_server_start_failed" => "ReasonCommand",
        "initialize_failed" => "ReasonInitialize",
        "account_read_failed" => "ReasonAccount",
        "identity_missing" => "ReasonIdentity",
        "rate_limits_unverified" => "ReasonLimits",
        "identity_changed_within_probe" => "ReasonChanged",
        "probe_timeout" => "ReasonTimeout",
        "app_server_protocol_error" => "ReasonProtocol",
        _ => "ReasonUnavailable"
    });

    private void TogglePause()
    {
        _paused = !_paused;
        UpdateMenuLanguage();
        if (_paused)
        {
            _refreshTimer.Stop();
            _nextRefreshAtUtc = null;
            SetStatusOverride("MonitoringPaused");
        }
        else
        {
            _ = RefreshAsync();
        }
        UpdateIntervalLabel();
    }

    private void ShowSettings()
    {
        ShowWindow();
        var dialog = new SettingsWindow(_baseIntervalSeconds,
            UiPresentation.Theme, UiPresentation.Language, _store.GetTelegramPreferences()) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var selected = dialog.IntervalSeconds;
        var theme = dialog.SelectedTheme;
        var language = dialog.SelectedLanguage;
        var themeKey = theme == UiThemeMode.Light ? "light" : "dark";
        var languageKey = language switch
        {
            UiLanguage.English => "en",
            UiLanguage.SimplifiedChinese => "zh-CN",
            _ => "zh-TW"
        };
        try
        {
            var previous = _store.GetTelegramPreferences();
            var token = dialog.SelectedTelegramToken;
            var chatId = dialog.SelectedTelegramChatId;
            var changed = dialog.TelegramCredentialsChanged ||
                          chatId != previous.ChatId || token != _telegramToken;
            var activeSince = dialog.TelegramNotificationsEnabled &&
                (!previous.Enabled || changed || previous.ActiveSinceUtc is null)
                ? DateTimeOffset.UtcNow : previous.ActiveSinceUtc;
            var protectedToken = token.Length == 0 ? "" :
                changed ? TelegramTokenProtection.Protect(token) : previous.ProtectedToken;
            _store.SetTelegramPreferences(new TelegramPreferences(
                dialog.TelegramNotificationsEnabled, chatId, protectedToken, activeSince));
            _store.SetDesktopPreferences(selected, themeKey, languageKey);
            LoadTelegramPreferences();
            _telegramQueued.Clear();
            _nextTelegramCheckUtc = DateTimeOffset.MinValue;
            _telegramErrorShown = false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      Microsoft.Data.Sqlite.SqliteException or CryptographicException or FormatException)
        {
            System.Windows.MessageBox.Show(UiPresentation.Text("SettingsSaveError", error.Message), "Codex Quota Hub");
            return;
        }
        UiPresentation.Apply(theme, language);
        UpdateMenuLanguage();
        UpdateStatusText();
        try { ReloadCards(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      Microsoft.Data.Sqlite.SqliteException)
        {
            SetStatusOverride("SnapshotReadError", error.Message);
        }
        _baseIntervalSeconds = selected;
        _nextIntervalSeconds = selected;
        _refreshTimer.Stop();
        _refreshTimer.Interval = TimeSpan.FromSeconds(selected);
        if (!_paused && !_isRefreshing)
        {
            _nextRefreshAtUtc = DateTimeOffset.UtcNow.AddSeconds(selected);
            _refreshTimer.Start();
        }
        SetDisplayMode(_displayMode);
        UpdateIntervalLabel();
    }

    private void UpdateMenuLanguage()
    {
        _openMenu.Text = UiPresentation.Text("Open");
        _refreshMenu.Text = UiPresentation.Text("Refresh");
        _pauseMenu.Text = UiPresentation.Text(_paused ? "Resume" : "Pause");
        _settingsMenu.Text = UiPresentation.Text("Settings");
        _exitMenu.Text = UiPresentation.Text("Exit");
        PauseButton.Content = _pauseMenu.Text;
    }

    private void UpdateIntervalLabel()
    {
        IntervalText.Text = _isRefreshing ? UiPresentation.Text("Querying")
            : _paused ? UiPresentation.Text("Paused")
            : _nextRefreshAtUtc is null ? UiPresentation.Text("AwaitingFirst")
            : UiPresentation.Text("NextAutomatic", NextRefreshCountdown());
        UpdateCompactStatus();
    }

    private string NextRefreshCountdown()
    {
        if (_nextRefreshAtUtc is null) return "--:--";
        var seconds = Math.Max(0, (int)Math.Ceiling((_nextRefreshAtUtc.Value - DateTimeOffset.UtcNow).TotalSeconds));
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }

    private void UpdateCompactStatus()
    {
        var state = _isRefreshing ? UiPresentation.Text("QueryingShort")
            : _paused ? UiPresentation.Text("Paused") : _lastStatus switch
        {
            RefreshStatus.Saved => UiPresentation.Text("UpdatedShort"),
            RefreshStatus.SignedOut => UiPresentation.Text("SignedOutShort"),
            RefreshStatus.ChangedDuringCycle => UiPresentation.Text("IdentityPending"),
            RefreshStatus.Unverified => UiPresentation.Text("SourceUnavailable"),
            _ => UiPresentation.Text("LoadingShort")
        };
        CompactStatusText.Text = _paused || _isRefreshing ? state :
            _nextRefreshAtUtc is null ? state : UiPresentation.Text("NextShort", state, NextRefreshCountdown());
        CompactStatusText.ToolTip = StatusText.Text;
        CompactStatusText.Foreground = _lastStatus == RefreshStatus.Unverified
            ? AvailabilityPalette.Red : AvailabilityPalette.Green;
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void SetDisplayMode(DisplayMode mode)
    {
        if (_displayMode != mode)
        {
            if (mode == DisplayMode.Compact)
            {
                _expandedWidth = Width;
                _expandedHeight = Height;
                Width = 760;
                Height = 420;
            }
            else if (_displayMode == DisplayMode.Compact)
            {
                Width = _expandedWidth;
                Height = _expandedHeight;
            }
        }
        _displayMode = mode;
        DetailedView.Visibility = mode == DisplayMode.Detailed ? Visibility.Visible : Visibility.Collapsed;
        GridView.Visibility = mode == DisplayMode.Grid ? Visibility.Visible : Visibility.Collapsed;
        CompactView.Visibility = mode == DisplayMode.Compact ? Visibility.Visible : Visibility.Collapsed;
        StatusBorder.Visibility = mode == DisplayMode.Compact ? Visibility.Collapsed : Visibility.Visible;
        TitleDescription.Visibility = mode == DisplayMode.Compact ? Visibility.Collapsed : Visibility.Visible;
        CompactStatusText.Visibility = mode == DisplayMode.Compact ? Visibility.Visible : Visibility.Collapsed;
        IntervalText.Visibility = mode == DisplayMode.Compact ? Visibility.Collapsed : Visibility.Visible;
        TitleText.FontSize = mode == DisplayMode.Compact ? 22 : 24;
        DetailedViewButton.Background = UiPresentation.Brush(
            mode == DisplayMode.Detailed ? "Brush238B73" : "Brush34495C");
        GridViewButton.Background = UiPresentation.Brush(
            mode == DisplayMode.Grid ? "Brush238B73" : "Brush34495C");
        CompactViewButton.Background = UiPresentation.Brush(
            mode == DisplayMode.Compact ? "Brush238B73" : "Brush34495C");
        DetailedViewButton.Foreground = UiPresentation.Brush(
            mode == DisplayMode.Detailed ? "PrimaryButtonText" : "SecondaryButtonText");
        GridViewButton.Foreground = UiPresentation.Brush(
            mode == DisplayMode.Grid ? "PrimaryButtonText" : "SecondaryButtonText");
        CompactViewButton.Foreground = UiPresentation.Brush(
            mode == DisplayMode.Compact ? "PrimaryButtonText" : "SecondaryButtonText");
        UpdateCompactStatus();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Dispatcher.BeginInvoke(() => _ = RefreshAsync());
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable)
            Dispatcher.BeginInvoke(() => _ = RefreshAsync());
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting) return;
        e.Cancel = true;
        Hide();
    }

    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        _shutdown.Cancel();
        _refreshTimer.Stop();
        _clockTimer.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _tray.Visible = false;
        _tray.Dispose();
        _telegramClient.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync(manual: true);
    private void PauseButton_Click(object sender, RoutedEventArgs e) => TogglePause();
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettings();
    private void ResetCreditsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AccountCardViewModel card }) return;
        new ResetCreditsWindow(card) { Owner = this }.ShowDialog();
    }
    private void EditAliasButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AccountCardViewModel card }) return;
        var identity = card.Snapshot.Identity;
        var dialog = new AliasWindow(card.IdentityTooltip,
            _store.GetAccountAlias(identity.Email, identity.WorkspaceId)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _store.SetAccountAlias(identity.Email, identity.WorkspaceId, dialog.ResultAlias);
            ReloadCards();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      ArgumentException or Microsoft.Data.Sqlite.SqliteException)
        {
            System.Windows.MessageBox.Show(UiPresentation.Text("AliasSaveError", error.Message),
                "Codex Quota Hub");
        }
    }
    private void DetailedViewButton_Click(object sender, RoutedEventArgs e) => SetDisplayMode(DisplayMode.Detailed);
    private void GridViewButton_Click(object sender, RoutedEventArgs e) => SetDisplayMode(DisplayMode.Grid);
    private void CompactViewButton_Click(object sender, RoutedEventArgs e) => SetDisplayMode(DisplayMode.Compact);
}
