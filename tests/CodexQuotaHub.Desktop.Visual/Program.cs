using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexQuotaHub.Core;
using CodexQuotaHub.Desktop;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using Brush = System.Windows.Media.Brush;
using ComboBox = System.Windows.Controls.ComboBox;
using Popup = System.Windows.Controls.Primitives.Popup;
using Point = System.Windows.Point;
using ProgressBar = System.Windows.Controls.ProgressBar;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using Size = System.Windows.Size;
using Thumb = System.Windows.Controls.Primitives.Thumb;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using Track = System.Windows.Controls.Primitives.Track;

namespace CodexQuotaHub.Desktop.Visual;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length is < 2 or > 3 ||
            (args.Length == 3 && args[2] is not ("detailed" or "grid" or "compact" or "credits")))
            throw new ArgumentException("Expected database path, PNG output path and optional view mode.");

        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/CodexQuotaHub;component/ScrollBarStyles.xaml", UriKind.Relative)
        });
        var store = new SnapshotStore(args[0]);
        var requestedTheme = Environment.GetEnvironmentVariable("CODEX_QUOTA_HUB_VISUAL_THEME") is "light"
            ? "light" : "dark";
        var requestedLanguage = Environment.GetEnvironmentVariable("CODEX_QUOTA_HUB_VISUAL_LANGUAGE") switch
        {
            "en" => "en",
            "zh-CN" => "zh-CN",
            _ => "zh-TW"
        };
        store.SetDesktopPreferences(60, requestedTheme, requestedLanguage);
        var snapshots = store.ReadAll();
        if (snapshots.Count == 0)
        {
            var now = DateTimeOffset.UtcNow;
            var samples = new[]
            {
                Sample("plus@example.test", "personal-plus", "plus", 72, 31, now, 2,
                    [new ResetCredit("plus-credit", "codexRateLimits", "available", now.AddDays(15),
                        "Full reset (Weekly + 5 hr)", "Reset an eligible Codex rate-limit window.")]),
                Sample("pro5@example.test", "personal-pro5", "prolite", 37, 19, now, 0, []),
                Sample("pro20@example.test", "personal-pro20", "pro", 63, 11, now),
                Sample("team@example.test", "business-standard", "team", 24, 83, now, 1,
                    [new ResetCredit("team-credit", "codexRateLimits", "available", null,
                        "Full reset", "No expiry")]),
                Sample("team5@example.test", "business-premium", "self_serve_business_prolite", 100, 48, now, 3)
            };
            foreach (var sample in samples) store.Save(sample);
            snapshots = store.ReadAll();
        }
        var snapshot = snapshots[0];
        var window = new MainWindow(args[0], probe: new FixedProbe(snapshot))
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -2000,
            Top = 0,
            ShowInTaskbar = false
        };
        window.Loaded += (_, _) =>
        {
            var mode = args.Length == 3 ? args[2] : "detailed";
            var buttonName = mode switch
            {
                "grid" => "GridViewButton",
                "compact" => "CompactViewButton",
                _ => null
            };
            if (buttonName is not null)
                ((System.Windows.Controls.Button)window.FindName(buttonName))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (args[1] != "--verify")
                window.Height = mode switch
                {
                    "detailed" => 1460,
                    "grid" => 870,
                    "compact" => 540,
                    _ => window.Height
                };

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                window.UpdateLayout();
                if (args[1] == "--verify")
                {
                    VerifyViews(window, mode, args[0]);
                    Environment.Exit(0);
                }

                ResetCreditsWindow? dialog = null;
                if (mode == "credits")
                {
                    var card = ((ItemsControl)window.FindName("AccountList"))
                        .Items.Cast<AccountCardViewModel>().First();
                    dialog = new ResetCreditsWindow(card)
                    {
                        Owner = window,
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        Left = -2000,
                        Top = 0,
                        ShowInTaskbar = false
                    };
                    dialog.Show();
                    dialog.UpdateLayout();
                }
                var width = (int)Math.Ceiling(dialog?.ActualWidth ?? window.ActualWidth);
                var height = (int)Math.Ceiling(dialog?.ActualHeight ?? window.ActualHeight);
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                if (dialog is null) bitmap.Render(window);
                else bitmap.Render(dialog);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var output = File.Create(args[1]))
                    encoder.Save(output);
                Environment.Exit(0);
            };
            timer.Start();
        };
        app.Run(window);
    }

    private static void VerifyViews(MainWindow window, string mode, string databasePath)
    {
        var mainGrid = window.Content as Grid
            ?? throw new InvalidOperationException("Main window root layout is missing.");
        if (mainGrid.RowDefinitions.Count != 4 ||
            mainGrid.Children.Cast<UIElement>().Any(child => Grid.GetRow(child) >= 4))
            throw new InvalidOperationException("Footer space remains in the main window.");
        var detailed = (System.Windows.Controls.ItemsControl)window.FindName("AccountList");
        var totals = (ItemsControl)window.FindName("DailyTokenTotals");
        var grid = (ItemsControl)window.FindName("AccountGrid");
        var compact = (System.Windows.Controls.ItemsControl)window.FindName("CompactList");
        var visibleName = mode switch
        {
            "grid" => "GridView",
            "compact" => "CompactView",
            _ => "DetailedView"
        };
        var visible = (FrameworkElement)window.FindName(visibleName);
        var cards = detailed.Items.Cast<AccountCardViewModel>().ToList();
        var settings = (Button)window.FindName("SettingsButton");
        var viewButton = (Button)window.FindName("DetailedViewButton");
        var compactButton = (Button)window.FindName("CompactViewButton");
        var gridTemplate = (DataTemplate)window.FindResource("GridAccountTemplate");
        var gridCard = (Border)gridTemplate.LoadContent();
        var detailedTemplate = (DataTemplate)window.FindResource("AccountTemplate");
        var detailedCard = (Border)detailedTemplate.LoadContent();
        var compactTemplate = (DataTemplate)window.FindResource("CompactAccountTemplate");
        var compactCard = (Border)compactTemplate.LoadContent();
        var planTag = FindDescendant<Border>(compactCard);
        var tagText = planTag?.Child as TextBlock;
        var interval = (TextBlock)window.FindName("IntervalText");
        var byEmail = cards.ToDictionary(card => card.Email);
        var expectedPlans = new Dictionary<string, string>
        {
            ["plus@example.test"] = "Plus",
            ["pro5@example.test"] = "Pro 5x",
            ["pro20@example.test"] = "Pro 20x",
            ["team@example.test"] = "Team",
            ["team5@example.test"] = "Team 5x"
        };
        if (visible.Visibility != Visibility.Visible ||
            detailed.Items.Count != 5 || grid.Items.Count != 5 || compact.Items.Count != 5 ||
            totals.Items.Count != 7 ||
            gridCard.Width != 292 || gridCard.Height != 180 ||
            planTag is null || planTag.CornerRadius.TopLeft != 7 ||
            planTag.Height != 22 || planTag.MinWidth != 43 ||
            tagText is null ||
            BindingOperations.GetBinding(tagText, TextBlock.TextProperty)?.Path.Path != "PlanLabel" ||
            !HasCreditButtonBinding(detailedCard, "ResetCreditDetailedText") ||
            !HasCreditButtonBinding(gridCard, "ResetCreditGridText") ||
            !HasCreditButtonBinding(compactCard, "ResetCreditCompactText") ||
            compactButton.Content?.ToString() != UiPresentation.Text("Compact") ||
            !interval.Text.Contains(':') || cards.Any(card => !card.IsExpanded) ||
            Math.Abs(settings.TranslatePoint(new Point(0, 0), window).Y -
                     viewButton.TranslatePoint(new Point(0, 0), window).Y) > 1 ||
            cards.Any(card => !card.FiveHourQuota.HasData || !card.WeeklyQuota.HasData ||
                              string.IsNullOrWhiteSpace(card.FiveHourQuota.CompactCountdownText) ||
                              string.IsNullOrWhiteSpace(card.WeeklyQuota.CompactCountdownText) ||
                              card.FiveHourQuota.ClockElapsedGeometry == Geometry.Empty ||
                              card.WeeklyQuota.ClockElapsedGeometry == Geometry.Empty ||
                              string.IsNullOrWhiteSpace(card.FiveHourQuota.TooltipText) ||
                              string.IsNullOrWhiteSpace(card.WeeklyQuota.TooltipText)) ||
            expectedPlans.Any(pair => !byEmail.TryGetValue(pair.Key, out var card) ||
                                      card.PlanLabel != pair.Value) ||
            byEmail["plus@example.test"].FiveHourQuota.AvailablePercent != 28 ||
            byEmail["plus@example.test"].WeeklyQuota.AvailablePercent != 69 ||
            new[] { "plus@example.test", "pro5@example.test", "pro20@example.test" }
                .Any(email => byEmail[email].PlanBadgeBackground != BadgePalette.PersonalBackground ||
                              byEmail[email].PlanAccentBrush != BadgePalette.PersonalBorder) ||
            new[] { "team@example.test", "team5@example.test" }
                .Any(email => byEmail[email].PlanBadgeBackground != BadgePalette.TeamBackground ||
                              byEmail[email].PlanAccentBrush != BadgePalette.TeamBorder) ||
            ((SolidColorBrush)BadgePalette.PersonalBorder).Color.R <=
                ((SolidColorBrush)BadgePalette.PersonalBorder).Color.B ||
            ((SolidColorBrush)BadgePalette.TeamBorder).Color.B <=
                ((SolidColorBrush)BadgePalette.TeamBorder).Color.R ||
            cards.Any(card => card.FiveHourWindows[0].Title != "5H" ||
                              card.WeeklyWindows[0].Title != "7D") ||
            byEmail["team5@example.test"].FiveHourQuota.AvailablePercent != 0 ||
            byEmail["team5@example.test"].FiveHourQuota.AvailabilityBrush !=
                AvailabilityPalette.Red ||
            byEmail["plus@example.test"].ResetCreditCompactText != "2" ||
            byEmail["pro5@example.test"].ResetCreditCompactText != "0" ||
            byEmail["pro20@example.test"].ResetCreditCompactText != "—" ||
            byEmail["plus@example.test"].ResetCreditDetailedText != UiPresentation.Text("ResetDetailedCount", 2) ||
            byEmail["pro5@example.test"].ResetCreditGridText != UiPresentation.Text("ResetGridCount", 0) ||
            byEmail["pro20@example.test"].ResetCreditDetailedText != UiPresentation.Text("ResetDetailedUnknown") ||
            cards.Any(card => card.ResetCreditCompactText.Contains('次')) ||
            byEmail["plus@example.test"].ResetCreditForeground != ResetCreditPalette.PositiveForeground ||
            byEmail["pro5@example.test"].ResetCreditForeground != ResetCreditPalette.ZeroForeground ||
            byEmail["pro20@example.test"].ResetCreditForeground != ResetCreditPalette.UnknownForeground)
            throw new InvalidOperationException($"View verification failed: {mode}.");
        if (totals.Items[0] is not DailyTokenDisplayRow { } firstTotal ||
            firstTotal.CoverageText != UiPresentation.Text("Coverage", 4, 5) ||
            cards.Any(card => card.DailyTokenRows.Count != 7) ||
            byEmail["pro20@example.test"].DailyTokenRows.Any(day => day.TokensText != "—") ||
            byEmail["plus@example.test"].DailyTokenRows[0].TokensText == "—")
            throw new InvalidOperationException("Daily Token display or coverage is incorrect.");
        VerifyAppearanceAndLayout(window, detailedCard, gridCard);
        VerifyScrollBar(window, mode);
        var now = DateTimeOffset.UtcNow;
        foreach (var alias in new[] { "business", "self_serve_business_usage_based" })
        {
            var card = new AccountCardViewModel(
                Sample("alias@example.test", "business-alias", alias, 10, 20, now), "已更新", now);
            if (card.PlanLabel != "Team" || card.PlanBadgeBackground != BadgePalette.TeamBackground)
                throw new InvalidOperationException($"Business plan alias failed: {alias}.");
        }
        var other = new AccountCardViewModel(
            Sample("other@example.test", "other-plan", "ent26", 10, 20, now), "已更新", now);
        if (other.PlanLabel != "ent26" || other.PlanBadgeBackground != BadgePalette.OtherBackground)
            throw new InvalidOperationException("Unknown plan fallback failed.");
        if (mode == "detailed")
        {
            var firstContainer = detailed.ItemContainerGenerator.ContainerFromIndex(0)
                ?? throw new InvalidOperationException("First account was not rendered.");
            var toggle = FindDescendant<ToggleButton>(firstContainer)
                ?? throw new InvalidOperationException("Account collapse button was not rendered.");
            var windows = FindDescendant<ItemsControl>(firstContainer)
                ?? throw new InvalidOperationException("Account windows were not rendered.");
            toggle.IsChecked = false;
            window.UpdateLayout();
            if (cards[0].IsExpanded || windows.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Collapsing the account did not hide its windows.");
            toggle.IsChecked = true;
            window.UpdateLayout();
            if (!cards[0].IsExpanded || windows.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Expanding the account did not restore its windows.");
        }
        VerifyCreditDialog(window, mode, detailed, grid, compact, cards[0]);
        VerifyDueAvailability(window);
        if (mode == "compact") VerifyClockProgression();
        if (mode == "detailed") VerifySettingsChange(window, databasePath);
        VerifyAliasEditor(window, mode, databasePath);
        Console.WriteLine($"VerifiedView={mode} Accounts={cards.Count} GridCard={gridCard.Width}x{gridCard.Height}");
    }

    private static void VerifyAppearanceAndLayout(Window window, Border detailedCard, Border gridCard)
    {
        var title = (TextBlock)window.FindName("TitleText");
        var subtitle = (TextBlock)window.FindName("TitleDescription");
        if (window.Title != "Codex Quota Hub" || title.Text != "Codex Quota Hub" ||
            subtitle.Text != UiPresentation.Text("Subtitle") || title.ActualWidth > window.ActualWidth - 50)
            throw new InvalidOperationException("The product title or subtitle is incorrect or does not fit the window.");
        var expectedTheme = Environment.GetEnvironmentVariable("CODEX_QUOTA_HUB_VISUAL_THEME") is "light"
            ? UiThemeMode.Light : UiThemeMode.Dark;
        var expectedLanguage = Environment.GetEnvironmentVariable("CODEX_QUOTA_HUB_VISUAL_LANGUAGE") switch
        {
            "en" => UiLanguage.English,
            "zh-CN" => UiLanguage.SimplifiedChinese,
            _ => UiLanguage.TraditionalChinese
        };
        var refresh = (Button)window.FindName("RefreshButton");
        var header = (Grid)((StackPanel)detailedCard.Child).Children[0];
        var right = header.Children.OfType<Grid>().Single(child => Grid.GetColumn(child) == 2);
        var detailedReset = right.Children.OfType<Button>().Single();
        var gridHeader = ((Grid)gridCard.Child).Children.OfType<Grid>().First();
        var gridReset = gridHeader.Children.OfType<Button>()
            .Single(button => Grid.GetRow(button) == 0);
        var settingsWindow = new SettingsWindow(60, expectedTheme, expectedLanguage)
        {
            Owner = window,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -2000,
            Top = 0,
            ShowInTaskbar = false
        };
        settingsWindow.Show();
        settingsWindow.UpdateLayout();
        var themeChoice = (ComboBox)settingsWindow.FindName("ThemeChoice");
        var languageChoice = (ComboBox)settingsWindow.FindName("LanguageChoice");
        var telegramEnabled = (CheckBox)settingsWindow.FindName("TelegramEnabled");
        var telegramToken = (PasswordBox)settingsWindow.FindName("TelegramBotToken");
        var telegramChat = (TextBox)settingsWindow.FindName("TelegramChatId");
        var telegramTest = (Button)settingsWindow.FindName("TestTelegramButton");
        themeChoice.ApplyTemplate();
        var toggle = (ToggleButton)themeChoice.Template.FindName("DropDownToggle", themeChoice);
        toggle.ApplyTemplate();
        var selectedSurface = (Border)toggle.Template.FindName("SelectedSurface", toggle);
        var selectionText = (ContentPresenter)themeChoice.Template.FindName("SelectionText", themeChoice);
        var popup = (Popup)themeChoice.Template.FindName("PART_Popup", themeChoice);
        var dropDownSurface = (Border)popup.Child;
        var selectedItem = (ComboBoxItem)themeChoice.SelectedItem;
        selectedItem.ApplyTemplate();
        var itemSurface = (Border)selectedItem.Template.FindName("ItemSurface", selectedItem);
        themeChoice.IsDropDownOpen = true;
        var popupOpened = popup.IsOpen;
        themeChoice.IsDropDownOpen = false;
        var creditsWindow = new ResetCreditsWindow(
            ((ItemsControl)window.FindName("AccountList")).Items.Cast<AccountCardViewModel>().First());
        var settingsThemeIndex = themeChoice.SelectedIndex;
        var settingsLanguageIndex = languageChoice.SelectedIndex;
        var selectedText = selectionText.Content?.ToString();
        var selectedContrast = Contrast(
            System.Windows.Documents.TextElement.GetForeground(selectionText), selectedSurface.Background);
        var optionContrast = Contrast(
            System.Windows.Documents.TextElement.GetForeground(itemSurface), itemSurface.Background);
        var popupContrast = Contrast(themeChoice.Foreground, dropDownSurface.Background);
        var telegramTextContrast = Contrast(telegramChat.Foreground, telegramChat.Background);
        var telegramTokenContrast = Contrast(telegramToken.Foreground, telegramToken.Background);
        var telegramSettingsFit = telegramTest.TranslatePoint(new Point(0, 0), settingsWindow).Y
            < settingsWindow.ActualHeight - 45;
        settingsWindow.Close();
        var creditsTitle = creditsWindow.Title;
        creditsWindow.Close();
        if (UiPresentation.Theme != expectedTheme || UiPresentation.Language != expectedLanguage ||
            window.Background != UiPresentation.Brush("Brush0D1117") ||
            refresh.Content?.ToString() != UiPresentation.Text("Refresh") ||
            right.RowDefinitions.Count != 2 || Grid.GetRow(detailedReset) != 1 ||
            Grid.GetColumn(gridReset) != 1 || Grid.GetRow(gridReset) != 0 ||
            settingsThemeIndex != (expectedTheme == UiThemeMode.Light ? 1 : 0) ||
            !popupOpened || selectedText != UiPresentation.Text(
                expectedTheme == UiThemeMode.Light ? "Light" : "Dark") ||
            selectedContrast < 4.5 || optionContrast < 4.5 || popupContrast < 4.5 ||
            telegramEnabled.Content?.ToString() != UiPresentation.Text("TelegramEnable") ||
            telegramTextContrast < 4.5 || telegramTokenContrast < 4.5 || !telegramSettingsFit ||
            settingsLanguageIndex != (expectedLanguage switch
            {
                UiLanguage.English => 1,
                UiLanguage.SimplifiedChinese => 2,
                _ => 0
            }) ||
            creditsTitle != UiPresentation.Text("CreditTitle"))
            throw new InvalidOperationException("Theme, language, settings, or reset button layout is incorrect.");
        if (expectedTheme == UiThemeMode.Dark && expectedLanguage == UiLanguage.TraditionalChinese)
            VerifyTelegramClient();
    }

    private static void VerifyTelegramClient()
    {
        const string token = "12345:test_token";
        var protectedToken = TelegramTokenProtection.Protect(token);
        if (protectedToken.Contains(token, StringComparison.Ordinal) ||
            TelegramTokenProtection.Unprotect(protectedToken) != token)
            throw new InvalidOperationException("Telegram token protection failed.");
        using var http = new HttpClient(new FakeTelegramHandler());
        using var client = new TelegramClient(http);
        var result = Task.Run(async () =>
        {
            var chat = await client.FindPrivateChatAsync(token, "CM123456");
            var send = await client.SendAsync(token, "789", "Test");
            return (chat, send);
        }).GetAwaiter().GetResult();
        if (result.chat.ChatId != "789" || !result.send.Success ||
            TelegramClient.ValidToken("not-a-token") || TelegramClient.ValidChatId("somebody"))
            throw new InvalidOperationException("Telegram chat lookup or send request failed.");
    }

    private sealed class FakeTelegramHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host != "api.telegram.org" || request.Method != HttpMethod.Post)
                throw new InvalidOperationException("Unexpected Telegram endpoint.");
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var json = request.RequestUri.AbsolutePath.EndsWith("/getUpdates", StringComparison.Ordinal)
                ? """{"ok":true,"result":[{"message":{"text":"CM123456","chat":{"id":111,"type":"group"}}},{"message":{"text":"CM123456","chat":{"id":789,"type":"private"}}}]}"""
                : """{"ok":true,"result":{"message_id":1}}""";
            if (request.RequestUri.AbsolutePath.EndsWith("/sendMessage", StringComparison.Ordinal) &&
                (!body.Contains("chat_id=789", StringComparison.Ordinal) ||
                 !body.Contains("text=Test", StringComparison.Ordinal)))
                throw new InvalidOperationException("Telegram message fields are incorrect.");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            };
        }
    }

    private static double Contrast(Brush foreground, Brush background)
    {
        static double Luminance(System.Windows.Media.Color color)
        {
            static double Channel(byte value)
            {
                var normalized = value / 255d;
                return normalized <= 0.04045 ? normalized / 12.92
                    : Math.Pow((normalized + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) +
                   0.0722 * Channel(color.B);
        }

        var first = Luminance(((SolidColorBrush)foreground).Color);
        var second = Luminance(((SolidColorBrush)background).Color);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static void VerifyScrollBar(MainWindow window, string mode)
    {
        var viewer = mode switch
        {
            "grid" => (ScrollViewer)window.FindName("GridView"),
            "compact" => FindDescendant<ScrollViewer>((FrameworkElement)window.FindName("CompactView"))
                ?? throw new InvalidOperationException("Compact view scroll area was not rendered."),
            _ => (ScrollViewer)window.FindName("DetailedView")
        };
        VerifyScrollBarStyle(viewer, mode, window);
    }

    private static void VerifyScrollBarStyle(ScrollViewer viewer, string context, Window window)
    {
        var scrollBar = FindDescendant<ScrollBar>(viewer)
            ?? throw new InvalidOperationException($"{context} scrollbar was not rendered.");
        scrollBar.ApplyTemplate();
        var rail = scrollBar.Template.FindName("ScrollRail", scrollBar) as Border;
        var track = scrollBar.Template.FindName("PART_Track", scrollBar) as Track;
        var thumb = track?.Thumb;
        thumb?.ApplyTemplate();
        var surface = thumb?.Template.FindName("ThumbSurface", thumb) as Border;
        if (rail is null || track is null || thumb is null || surface is null ||
            rail.Background != UiPresentation.Brush("ScrollTrack") ||
            surface.Background != UiPresentation.Brush("ScrollThumb") ||
            rail.CornerRadius.TopLeft != 7 || scrollBar.ActualWidth > 14)
            throw new InvalidOperationException($"{context} scrollbar does not use the current theme style.");

        if (viewer.ScrollableHeight > 0)
        {
            viewer.ScrollToEnd();
            window.UpdateLayout();
            if (viewer.VerticalOffset <= 0)
                throw new InvalidOperationException($"{context} no longer scrolls.");
            viewer.ScrollToTop();
            window.UpdateLayout();
        }
    }

    private static void VerifySettingsChange(MainWindow window, string databasePath)
    {
        var targetTheme = UiPresentation.Theme == UiThemeMode.Light ? UiThemeMode.Dark : UiThemeMode.Light;
        var targetLanguage = UiPresentation.Language == UiLanguage.English
            ? UiLanguage.SimplifiedChinese : UiLanguage.English;
        var initialBackground = window.Background;
        var opened = false;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().Single();
            opened = true;
            ((ComboBox)dialog.FindName("ThemeChoice")).SelectedIndex =
                targetTheme == UiThemeMode.Light ? 1 : 0;
            ((ComboBox)dialog.FindName("LanguageChoice")).SelectedIndex = targetLanguage switch
            {
                UiLanguage.English => 1,
                UiLanguage.SimplifiedChinese => 2,
                _ => 0
            };
            ((PasswordBox)dialog.FindName("TelegramBotToken")).Password = "12345:test_token";
            ((TextBox)dialog.FindName("TelegramChatId")).Text = "789";
            ((CheckBox)dialog.FindName("TelegramEnabled")).IsChecked = true;
            ((Button)dialog.FindName("SaveButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }), DispatcherPriority.Background);
        ((Button)window.FindName("SettingsButton"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var store = new SnapshotStore(databasePath);
        var expectedThemeKey = targetTheme == UiThemeMode.Light ? "light" : "dark";
        var expectedLanguageKey = targetLanguage == UiLanguage.English ? "en" : "zh-CN";
        var telegram = store.GetTelegramPreferences();
        var firstCard = ((ItemsControl)window.FindName("AccountList"))
            .Items.Cast<AccountCardViewModel>().First();
        if (!opened || UiPresentation.Theme != targetTheme ||
            UiPresentation.Language != targetLanguage ||
            window.Background == initialBackground ||
            store.GetUiTheme() != expectedThemeKey ||
            store.GetUiLanguage() != expectedLanguageKey ||
            !telegram.Enabled || telegram.ChatId != "789" ||
            telegram.ProtectedToken.Contains("12345:test_token", StringComparison.Ordinal) ||
            TelegramTokenProtection.Unprotect(telegram.ProtectedToken) != "12345:test_token" ||
            ((Button)window.FindName("RefreshButton")).Content?.ToString() != UiPresentation.Text("Refresh") ||
            firstCard.ResetCreditDetailedText != UiPresentation.Text("ResetDetailedCount", 2))
            throw new InvalidOperationException("Changing theme and language did not update or persist the UI.");
        VerifyScrollBar(window, "detailed");
    }

    private static void VerifyClockProgression()
    {
        var now = DateTimeOffset.UtcNow;
        var quotaWindow = new WindowCardViewModel(
            new LimitWindow("codex", "primary", 10, 300, now.AddHours(5)), now);
        var quota = new QuotaSummaryViewModel("5h", "5 小時", [quotaWindow], now);
        var initialColor = ((SolidColorBrush)quota.ClockElapsedBrush).Color;
        var nearReset = now.AddHours(4);
        quotaWindow.Update(nearReset);
        quota.Update(nearReset);
        var nearResetColor = ((SolidColorBrush)quota.ClockElapsedBrush).Color;
        if (nearResetColor.G <= initialColor.G || nearResetColor.G <= nearResetColor.R)
            throw new InvalidOperationException("Clock color did not progress from orange toward green.");

        var due = now.AddHours(5).AddSeconds(1);
        quotaWindow.Update(due);
        quota.Update(due);
        if (quota.ClockElapsedBrush != ClockPalette.Complete ||
            quota.CompactCountdownText != string.Empty ||
            quota.TooltipText != quota.ResetText)
            throw new InvalidOperationException("Due clock or tooltip still shows the finished countdown.");
    }

    private static void VerifyDueAvailability(MainWindow host)
    {
        var now = DateTimeOffset.UtcNow;
        var resetAt = now.AddSeconds(1);
        var snapshot = new UsageSnapshot(
            new AccountIdentity("reset@example.test", "reset-workspace", "chatgpt", "plus"), now,
            [
                new LimitWindow("codex", "primary", 80, 300, resetAt),
                new LimitWindow("codex", "secondary", 40, 10080, now.AddDays(2))
            ]);
        var card = new AccountCardViewModel(snapshot, "已更新", now);
        var window = card.FiveHourWindows.Single();
        var windowChanges = new HashSet<string?>();
        var quotaChanges = new HashSet<string?>();
        window.PropertyChanged += (_, change) => windowChanges.Add(change.PropertyName);
        card.FiveHourQuota.PropertyChanged += (_, change) => quotaChanges.Add(change.PropertyName);
        if (window.AvailablePercent != 20 || card.FiveHourQuota.AvailablePercent != 20 ||
            card.WeeklyQuota.AvailablePercent != 60)
            throw new InvalidOperationException("Availability before reset is incorrect.");

        card.Update(resetAt);
        if (window.AvailablePercent != 100 || card.FiveHourQuota.AvailablePercent != 100 ||
            window.AvailableText != UiPresentation.Text("AvailablePercent", "100") ||
            card.FiveHourQuota.AvailableText != UiPresentation.Text("AvailablePercent", "100") ||
            card.FiveHourQuota.CompactAvailableText != "100%" ||
            window.AvailabilityBrush != AvailabilityPalette.Green ||
            card.FiveHourQuota.AvailabilityBrush != AvailabilityPalette.Green ||
            card.WeeklyQuota.AvailablePercent != 60 ||
            card.StateText != "已更新" ||
            !card.FiveHourQuota.IsPending || card.WeeklyQuota.IsPending ||
            window.CountdownStatus != CountdownStatus.DueUnconfirmed ||
            window.CountdownText != string.Empty ||
            window.ShortCountdownText != string.Empty ||
            card.FiveHourQuota.CountdownText != string.Empty ||
            card.FiveHourQuota.CompactCountdownText != string.Empty ||
            card.FiveHourQuota.TooltipText != card.FiveHourQuota.ResetText ||
            window.Source.UsedPercent != 80 ||
            !windowChanges.Contains(nameof(WindowCardViewModel.AvailablePercent)) ||
            !windowChanges.Contains(nameof(WindowCardViewModel.AvailableText)) ||
            !windowChanges.Contains(nameof(WindowCardViewModel.AvailabilityBrush)) ||
            !windowChanges.Contains(nameof(WindowCardViewModel.CountdownText)) ||
            !quotaChanges.Contains(nameof(QuotaSummaryViewModel.AvailablePercent)) ||
            !quotaChanges.Contains(nameof(QuotaSummaryViewModel.AvailableText)) ||
            !quotaChanges.Contains(nameof(QuotaSummaryViewModel.CompactAvailableText)) ||
            !quotaChanges.Contains(nameof(QuotaSummaryViewModel.AvailabilityBrush)) ||
            !quotaChanges.Contains(nameof(QuotaSummaryViewModel.CountdownText)) ||
            !quotaChanges.Contains(nameof(QuotaSummaryViewModel.IsPending)))
            throw new InvalidOperationException("Due quota display did not hide countdown text while retaining 100% and the last snapshot.");

        foreach (var (templateKey, data, expectedText) in new (string, object, string)[]
                 {
                     ("WindowTemplate", window, window.AvailableText),
                     ("GridQuotaTemplate", card.FiveHourQuota, card.FiveHourQuota.AvailableText),
                     ("CompactQuotaTemplate", card.FiveHourQuota, card.FiveHourQuota.CompactAvailableText)
                 })
        {
            var element = (FrameworkElement)((DataTemplate)host.FindResource(templateKey)).LoadContent();
            element.DataContext = data;
            element.Measure(new Size(500, 100));
            element.Arrange(new Rect(0, 0, 500, 100));
            element.UpdateLayout();
            var bar = FindDescendant<ProgressBar>(element);
            var texts = FindDescendants<TextBlock>(element).ToList();
            var countdownLabel = texts.SingleOrDefault(text =>
                BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path == "CountdownText");
            var hasVisiblePending = texts.Any(text => text.Visibility == Visibility.Visible &&
                text.Text?.Contains(UiPresentation.Text("Pending"),
                    StringComparison.OrdinalIgnoreCase) == true);
            if (bar?.Value != 100 || texts.All(text => text.Text != expectedText) ||
                hasVisiblePending ||
                (templateKey != "CompactQuotaTemplate" &&
                 (countdownLabel?.Visibility != Visibility.Collapsed ||
                  texts.All(text => text.Text != window.ResetText &&
                                    text.Text != card.FiveHourQuota.ResetText))))
                throw new InvalidOperationException($"Due UI binding is incorrect: {templateKey}.");
        }

        var refreshed = snapshot with
        {
            Windows = [snapshot.Windows[0] with { UsedPercent = 10, ResetsAtUtc = resetAt.AddHours(5) },
                       snapshot.Windows[1]]
        };
        var refreshedCard = new AccountCardViewModel(refreshed, "已更新", resetAt);
        if (refreshedCard.FiveHourQuota.AvailablePercent != 90 ||
            refreshedCard.FiveHourWindows[0].CountdownStatus != CountdownStatus.Counting ||
            refreshedCard.FiveHourQuota.IsPending)
            throw new InvalidOperationException("A confirmed refresh did not replace the pending display.");

        var unknown = new WindowCardViewModel(
            new LimitWindow("codex", "no-reset-time", 65, 300, null), resetAt);
        if (unknown.AvailablePercent != 35 || unknown.CountdownStatus != CountdownStatus.Unknown)
            throw new InvalidOperationException("Missing reset time must retain the last confirmed availability.");
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in FindDescendants<T>(child)) yield return nested;
        }
    }

    private static void VerifyAliasEditor(MainWindow window, string mode, string databasePath)
    {
        var listName = mode switch
        {
            "grid" => "AccountGrid",
            "compact" => "CompactList",
            _ => "AccountList"
        };
        var list = (ItemsControl)window.FindName(listName);
        var container = list.ItemContainerGenerator.ContainerFromIndex(0)
            ?? throw new InvalidOperationException("Account alias button was not rendered.");
        var button = FindDescendants<Button>(container).SingleOrDefault(item =>
            item.ToolTip?.ToString() == UiPresentation.Text("EditAlias"))
            ?? throw new InvalidOperationException($"Alias editor button is missing: {mode}.");
        var dialogVerified = false;
        var dialogMeasurements = "";
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = System.Windows.Application.Current.Windows.OfType<AliasWindow>().Single();
            var input = (TextBox)dialog.FindName("AliasText");
            var identity = (TextBlock)dialog.FindName("AccountIdentityText");
            var hint = (TextBlock)dialog.FindName("AliasHintText");
            var save = (Button)dialog.FindName("SaveButton");
            dialog.UpdateLayout();
            var hintBottom = hint.TranslatePoint(new Point(0, hint.ActualHeight), dialog).Y;
            var saveTop = save.TranslatePoint(new Point(0, 0), dialog).Y;
            var saveBottom = save.TranslatePoint(new Point(0, save.ActualHeight), dialog).Y;
            dialogMeasurements = $"identity={identity.Text} max={input.MaxLength} hintBottom={hintBottom} saveTop={saveTop} saveBottom={saveBottom} height={dialog.ActualHeight}";
            dialogVerified = identity.Text.Contains("plus@example.test") && input.MaxLength == 40 &&
                hintBottom <= saveTop && saveBottom <= dialog.ActualHeight - 5;
            input.Text = "私人別名";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }), DispatcherPriority.Background);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var cards = ((ItemsControl)window.FindName("AccountList"))
            .Items.Cast<AccountCardViewModel>().ToDictionary(card => card.Email);
        var alias = new SnapshotStore(databasePath).GetAccountAlias(
            "plus@example.test", "personal-plus");
        var telegramLabel = TelegramIdentityFormatter.Display("plus@example.test", alias);
        var masked = TelegramIdentityFormatter.Display("plus@example.test", null);
        var creditStore = new SnapshotStore(databasePath);
        var message = TelegramMessageFormatter.Due(telegramLabel, "5h", "2026-09-26 12:00",
            creditStore.GetAvailableResetCredits("plus@example.test", "personal-plus"));
        var zeroMessage = TelegramMessageFormatter.Due(masked, "5h", "2026-09-26 12:00",
            creditStore.GetAvailableResetCredits("pro5@example.test", "personal-pro5"));
        var unknownMessage = TelegramMessageFormatter.Due(masked, "5h", "2026-09-26 12:00",
            creditStore.GetAvailableResetCredits("pro20@example.test", "personal-pro20"));
        var unknownExpected = UiPresentation.Text("TelegramDueMessage", masked, "5h",
            "2026-09-26 12:00", "");
        if (!dialogVerified || alias != "私人別名" || cards["plus@example.test"].DisplayName != "私人別名" ||
            !cards["plus@example.test"].IdentityTooltip.Contains("plus@example.test") ||
            telegramLabel != "私人別名" || masked != "p***@example.test" ||
            !message.Contains("私人別名") || message.Contains("plus@example.test") ||
            !message.Contains(UiPresentation.Text("TelegramResetCreditsLine", 2)) ||
            !zeroMessage.Contains(UiPresentation.Text("TelegramResetCreditsLine", 0)) ||
            message.Contains("待確認") || message.Contains("待确认") ||
            message.Contains("pending confirmation", StringComparison.OrdinalIgnoreCase) ||
            unknownMessage != unknownExpected)
            throw new InvalidOperationException($"Account alias display or Telegram fallback failed: {mode}; {dialogMeasurements}; alias={alias}; display={cards["plus@example.test"].DisplayName}; masked={masked}.");
    }

    private static bool HasCreditButtonBinding(DependencyObject root, string path)
    {
        if (root is Button button &&
            BindingOperations.GetBinding(button, ContentControl.ContentProperty)?.Path.Path == path)
            return true;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (HasCreditButtonBinding(VisualTreeHelper.GetChild(root, index), path)) return true;
        return false;
    }

    private static void VerifyCreditDialog(
        MainWindow window, string mode, ItemsControl detailed, ItemsControl grid,
        ItemsControl compact, AccountCardViewModel firstCard)
    {
        var list = mode switch { "grid" => grid, "compact" => compact, _ => detailed };
        var container = list.ItemContainerGenerator.ContainerFromIndex(0)
            ?? throw new InvalidOperationException("First credit button was not rendered.");
        var button = FindDescendants<Button>(container).SingleOrDefault(item =>
            item.ToolTip?.ToString() != UiPresentation.Text("EditAlias"))
            ?? throw new InvalidOperationException("Credit button was not rendered.");
        if (button.ActualWidth <= 0 || button.ActualHeight <= 0 || !button.IsHitTestVisible)
            throw new InvalidOperationException($"Credit button is not clickable: {mode}.");
        var wasExpanded = firstCard.IsExpanded;
        var dialogVerified = false;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = System.Windows.Application.Current.Windows
                .OfType<ResetCreditsWindow>().Single();
            var count = ((TextBlock)dialog.FindName("CountText")).Text;
            var summary = ((TextBlock)dialog.FindName("SummaryText")).Text;
            var account = ((TextBlock)dialog.FindName("AccountText")).Text;
            var rows = (ItemsControl)dialog.FindName("CreditList");
            dialog.UpdateLayout();
            var creditsScrollArea = FindDescendant<ScrollViewer>(dialog)
                ?? throw new InvalidOperationException("Credit details scroll area was not rendered.");
            VerifyScrollBarStyle(creditsScrollArea, "credit details", dialog);
            dialogVerified = count == UiPresentation.Text("CreditsCount", 2) && summary.Contains("1 / 2") &&
                account.Contains("plus@example.test") && rows.Items.Count == 1 &&
                rows.ItemContainerGenerator.ContainerFromIndex(0) is not null &&
                rows.Items[0] is ResetCreditDisplay item &&
                item.Title == "Full reset (Weekly + 5 hr)" &&
                item.Description == "Reset an eligible Codex rate-limit window." &&
                item.ExpirationText.Contains(UiPresentation.Text("ExpiresLabel"));
            dialog.Close();
        }), DispatcherPriority.Background);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!dialogVerified || firstCard.IsExpanded != wasExpanded)
            throw new InvalidOperationException($"Credit details did not open correctly: {mode}.");

        if (mode == "detailed")
        {
            var byEmail = detailed.Items.Cast<AccountCardViewModel>()
                .ToDictionary(card => card.Email);
            var zero = new ResetCreditsWindow(byEmail["pro5@example.test"]);
            var unknown = new ResetCreditsWindow(byEmail["pro20@example.test"]);
            var noExpiry = new ResetCreditsWindow(byEmail["team@example.test"]);
            try
            {
                if (((TextBlock)zero.FindName("CountText")).Text != UiPresentation.Text("CreditsCount", 0) ||
                    ((TextBlock)zero.FindName("SummaryText")).Text != UiPresentation.Text("CreditsZero") ||
                    ((ItemsControl)zero.FindName("CreditList")).Items.Count != 0 ||
                    ((TextBlock)unknown.FindName("CountText")).Text != UiPresentation.Text("Unknown") ||
                    ((TextBlock)unknown.FindName("SummaryText")).Text != UiPresentation.Text("CreditsUnavailable") ||
                    ((ItemsControl)noExpiry.FindName("CreditList")).Items[0] is not
                        ResetCreditDisplay { ExpirationText: var expiration } ||
                    expiration != UiPresentation.Text("NeverExpires"))
                    throw new InvalidOperationException("Credit detail fallback states are incorrect.");
            }
            finally
            {
                zero.Close();
                unknown.Close();
                noExpiry.Close();
            }
        }
    }

    private static UsageSnapshot Sample(
        string email, string workspace, string plan, double fiveHourUsed, double weeklyUsed,
        DateTimeOffset now, long? resetCredits = null,
        IReadOnlyList<ResetCredit>? creditDetails = null)
    {
        var latest = DateOnly.FromDateTime(now.ToLocalTime().DateTime);
        IReadOnlyList<DailyTokenUsage>? dailyTokens = plan == "pro" ? null :
            Enumerable.Range(0, 7).Select(offset => new DailyTokenUsage(
                latest.AddDays(-offset),
                (long)Math.Round((100 - fiveHourUsed) * 100_000 / (offset + 1)), now)).ToArray();
        return new UsageSnapshot(new AccountIdentity(email, workspace, "chatgpt", plan), now,
            new[]
            {
                new LimitWindow("codex", "primary", fiveHourUsed, 300, now.AddHours(2)),
                new LimitWindow("codex", "secondary", weeklyUsed, 10080, now.AddDays(3))
            }, resetCredits, creditDetails, dailyTokens);
    }

    private sealed class FixedProbe(UsageSnapshot snapshot) : IUsageProbe
    {
        public Task<ProbeResult> ProbeAsync(
            CancellationToken cancellationToken = default, bool includeDailyTokens = true) =>
            Task.FromResult(ProbeResult.Verified(snapshot));
    }
}
