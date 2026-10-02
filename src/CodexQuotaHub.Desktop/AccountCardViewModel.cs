using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using CodexQuotaHub.Core;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Geometry = System.Windows.Media.Geometry;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using StreamGeometry = System.Windows.Media.StreamGeometry;
using SweepDirection = System.Windows.Media.SweepDirection;

namespace CodexQuotaHub.Desktop;

public sealed class AccountCardViewModel : INotifyPropertyChanged
{
    private string _stateText;
    private bool _isExpanded;
    private string _fiveHourCountdownText = string.Empty;
    private string _weeklyCountdownText = string.Empty;

    public AccountCardViewModel(
        UsageSnapshot snapshot, string stateText, DateTimeOffset nowUtc, bool isExpanded = true,
        IReadOnlyList<DateOnly>? tokenDates = null, string? alias = null)
    {
        Snapshot = snapshot;
        _isExpanded = isExpanded;
        Email = snapshot.Identity.Email;
        DisplayName = string.IsNullOrWhiteSpace(alias) ? Email : alias;
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.Identity.WorkspaceId)))[..8];
        var planType = snapshot.Identity.PlanType?.Trim().ToLowerInvariant();
        PlanLabel = planType switch
        {
            "plus" => "Plus",
            "prolite" => "Pro 5x",
            "pro" => "Pro 20x",
            "team" or "business" or "self_serve_business_usage_based" => "Team",
            "self_serve_business_prolite" => "Team 5x",
            _ => string.IsNullOrWhiteSpace(snapshot.Identity.PlanType)
                ? UiPresentation.Text("Unknown") : snapshot.Identity.PlanType.Trim()
        };
        WorkspaceText = UiPresentation.Text("WorkspaceFingerprint", fingerprint);
        (PlanBadgeBackground, PlanBadgeBorder, PlanBadgeForeground) = planType switch
        {
            "plus" or "prolite" or "pro" =>
                (BadgePalette.PersonalBackground, BadgePalette.PersonalBorder, BadgePalette.PersonalForeground),
            "team" or "business" or "self_serve_business_usage_based" or "self_serve_business_prolite" =>
                (BadgePalette.TeamBackground, BadgePalette.TeamBorder, BadgePalette.TeamForeground),
            _ => (BadgePalette.OtherBackground, BadgePalette.OtherBorder, BadgePalette.OtherForeground)
        };
        PlanAccentBrush = PlanBadgeBorder;
        IdentityTooltip = $"{Email}\n{PlanLabel} · Workspace {fingerprint}";
        UpdatedText = UiPresentation.Text("LastUpdated",
            snapshot.ObservedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", UiPresentation.Culture));
        ResetCreditCompactText = snapshot.AvailableResetCredits?.ToString(
            System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        ResetCreditGridText = snapshot.AvailableResetCredits is long gridCount
            ? UiPresentation.Text("ResetGridCount", gridCount) : UiPresentation.Text("ResetGridUnknown");
        ResetCreditDetailedText = snapshot.AvailableResetCredits is long detailedCount
            ? UiPresentation.Text("ResetDetailedCount", detailedCount)
            : UiPresentation.Text("ResetDetailedUnknown");
        ResetCreditTooltip = snapshot.AvailableResetCredits is long tooltipCount
            ? UiPresentation.Text("ResetTooltipCount", tooltipCount, UpdatedText)
            : UiPresentation.Text("ResetTooltipUnknown");
        (ResetCreditBackground, ResetCreditBorder, ResetCreditForeground) =
            snapshot.AvailableResetCredits switch
            {
                null => (ResetCreditPalette.UnknownBackground, ResetCreditPalette.UnknownBorder,
                    ResetCreditPalette.UnknownForeground),
                0 => (ResetCreditPalette.ZeroBackground, ResetCreditPalette.ZeroBorder,
                    ResetCreditPalette.ZeroForeground),
                _ => (ResetCreditPalette.PositiveBackground, ResetCreditPalette.PositiveBorder,
                    ResetCreditPalette.PositiveForeground)
            };
        _stateText = stateText;
        Windows = snapshot.Windows.Select(window => new WindowCardViewModel(window, nowUtc)).ToList();
        FiveHourWindows = Windows.Where(window => window.Source.WindowDurationMins == 300).ToList();
        WeeklyWindows = Windows.Where(window => window.Source.WindowDurationMins == 10080).ToList();
        FiveHourQuota = new QuotaSummaryViewModel("5h", UiPresentation.Text("FiveHours"), FiveHourWindows, nowUtc);
        WeeklyQuota = new QuotaSummaryViewModel("7d", UiPresentation.Text("Weekly"), WeeklyWindows, nowUtc);
        var otherCount = Windows.Count - FiveHourWindows.Count - WeeklyWindows.Count;
        OtherWindowsText = otherCount > 0 ? UiPresentation.Text("OtherWindows", otherCount) : string.Empty;
        tokenDates ??= DailyTokenSummary.RecentDates(
            DateOnly.FromDateTime(nowUtc.ToLocalTime().DateTime), 7);
        DailyTokenRows = DailyTokenDisplay.ForAccount(snapshot, tokenDates);
        var lastTokenRead = snapshot.DailyTokens?
            .Where(day => tokenDates.Contains(day.Date))
            .Select(day => (DateTimeOffset?)day.ObservedAtUtc)
            .Max();
        DailyTokenUpdatedText = lastTokenRead is DateTimeOffset readAt
            ? UiPresentation.Text("LastTokenRead", readAt.ToLocalTime().ToString("MM-dd HH:mm", UiPresentation.Culture))
            : UiPresentation.Text("NoRecentTokens");
        Update(nowUtc);
    }

    public UsageSnapshot Snapshot { get; }
    public string Email { get; }
    public string DisplayName { get; }
    public string PlanLabel { get; }
    public string WorkspaceText { get; }
    public Brush PlanBadgeBackground { get; }
    public Brush PlanBadgeBorder { get; }
    public Brush PlanBadgeForeground { get; }
    public Brush PlanAccentBrush { get; }
    public string IdentityTooltip { get; }
    public string UpdatedText { get; }
    public string ResetCreditCompactText { get; }
    public string ResetCreditGridText { get; }
    public string ResetCreditDetailedText { get; }
    public string ResetCreditTooltip { get; }
    public Brush ResetCreditBackground { get; }
    public Brush ResetCreditBorder { get; }
    public Brush ResetCreditForeground { get; }
    public IReadOnlyList<WindowCardViewModel> Windows { get; }
    public IReadOnlyList<WindowCardViewModel> FiveHourWindows { get; }
    public IReadOnlyList<WindowCardViewModel> WeeklyWindows { get; }
    public QuotaSummaryViewModel FiveHourQuota { get; }
    public QuotaSummaryViewModel WeeklyQuota { get; }
    public string OtherWindowsText { get; }
    public IReadOnlyList<DailyTokenDisplayRow> DailyTokenRows { get; }
    public string DailyTokenUpdatedText { get; }
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }
    public string FiveHourCountdownText
    {
        get => _fiveHourCountdownText;
        private set => SetCountdown(ref _fiveHourCountdownText, value);
    }
    public string WeeklyCountdownText
    {
        get => _weeklyCountdownText;
        private set => SetCountdown(ref _weeklyCountdownText, value);
    }
    public string StateText
    {
        get => _stateText;
        private set
        {
            if (_stateText == value) return;
            _stateText = value;
            OnPropertyChanged();
        }
    }

    public void Update(DateTimeOffset nowUtc)
    {
        foreach (var window in Windows) window.Update(nowUtc);
        FiveHourQuota.Update(nowUtc);
        WeeklyQuota.Update(nowUtc);
        FiveHourCountdownText = FormatCountdown(FiveHourWindows);
        WeeklyCountdownText = FormatCountdown(WeeklyWindows);
    }

    private void SetCountdown(ref string field, string value, [CallerMemberName] string? property = null)
    {
        if (field == value) return;
        field = value;
        OnPropertyChanged(property);
    }

    private static string FormatCountdown(IReadOnlyList<WindowCardViewModel> windows) => windows.Count switch
    {
        0 => UiPresentation.Text("NotProvided"),
        1 => windows[0].ShortCountdownText,
        _ => string.Join(" · ", windows.Select(window =>
            $"{window.Source.LimitId} {window.ShortCountdownText}"))
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

public sealed class QuotaSummaryViewModel : INotifyPropertyChanged
{
    private readonly IReadOnlyList<WindowCardViewModel> _windows;
    private readonly WindowCardViewModel? _primary;
    private double _availablePercent;
    private string _availableText = string.Empty;
    private string _compactAvailableText = string.Empty;
    private Brush _availabilityBrush = Brushes.Gray;
    private string _countdownText = string.Empty;
    private string _compactCountdownText = string.Empty;
    private string _tooltipText = string.Empty;
    private bool _isPending;
    private Geometry _clockElapsedGeometry = Geometry.Empty;
    private Brush _clockElapsedBrush = ClockPalette.Elapsed;

    public QuotaSummaryViewModel(
        string shortLabel, string label, IReadOnlyList<WindowCardViewModel> windows,
        DateTimeOffset nowUtc)
    {
        ShortLabel = shortLabel;
        Label = windows.Count > 1 ? UiPresentation.Text("MultipleItems", label, windows.Count) : label;
        _windows = windows;
        _primary = windows.FirstOrDefault(window =>
            string.Equals(window.Source.LimitId, "codex", StringComparison.OrdinalIgnoreCase))
            ?? windows.FirstOrDefault();
        HasData = _primary is not null;
        ResetText = _primary?.Source.ResetsAtUtc is DateTimeOffset reset
            ? UiPresentation.Text("ResetShort", reset.ToLocalTime().ToString("MM-dd HH:mm", UiPresentation.Culture))
            : UiPresentation.Text("ResetTimeUnknown");
        Update(nowUtc);
    }

    public string ShortLabel { get; }
    public string Label { get; }
    public bool HasData { get; }
    public double AvailablePercent
    {
        get => _availablePercent;
        private set => Set(ref _availablePercent, value);
    }
    public string AvailableText
    {
        get => _availableText;
        private set => Set(ref _availableText, value);
    }
    public string CompactAvailableText
    {
        get => _compactAvailableText;
        private set => Set(ref _compactAvailableText, value);
    }
    public Brush AvailabilityBrush
    {
        get => _availabilityBrush;
        private set => Set(ref _availabilityBrush, value);
    }
    public string ResetText { get; }
    public string CountdownText
    {
        get => _countdownText;
        private set => Set(ref _countdownText, value);
    }
    public string CompactCountdownText
    {
        get => _compactCountdownText;
        private set => Set(ref _compactCountdownText, value);
    }
    public string TooltipText
    {
        get => _tooltipText;
        private set => Set(ref _tooltipText, value);
    }
    public bool IsPending
    {
        get => _isPending;
        private set => Set(ref _isPending, value);
    }
    public Geometry ClockElapsedGeometry
    {
        get => _clockElapsedGeometry;
        private set
        {
            if (ReferenceEquals(_clockElapsedGeometry, value)) return;
            _clockElapsedGeometry = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ClockElapsedGeometry)));
        }
    }
    public Brush ClockElapsedBrush
    {
        get => _clockElapsedBrush;
        private set
        {
            if (ReferenceEquals(_clockElapsedBrush, value)) return;
            _clockElapsedBrush = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ClockElapsedBrush)));
        }
    }

    public void Update(DateTimeOffset nowUtc)
    {
        AvailablePercent = _primary?.AvailablePercent ?? 0;
        AvailableText = _primary is null ? UiPresentation.Text("NotProvided")
            : UiPresentation.Text("AvailablePercent", AvailablePercent.ToString("0.##", UiPresentation.Culture));
        CompactAvailableText = _primary is null ? "—" : $"{AvailablePercent:0.##}%";
        AvailabilityBrush = _primary?.AvailabilityBrush ?? Brushes.Gray;
        var countdown = CountdownCalculator.Calculate(_primary?.Source.ResetsAtUtc, nowUtc);
        CountdownText = countdown.Status == CountdownStatus.DueUnconfirmed ? string.Empty
            : _primary is null ? UiPresentation.Text("CountdownUnavailable")
            : UiPresentation.Text("CountdownShort", _primary.ShortCountdownText);
        IsPending = countdown.Status == CountdownStatus.DueUnconfirmed;
        CompactCountdownText = countdown.Status switch
        {
            CountdownStatus.Counting => FormatCompactCountdown(countdown.Remaining!.Value),
            CountdownStatus.DueUnconfirmed => string.Empty,
            _ => UiPresentation.Text("Unknown")
        };
        var durationMinutes = _primary?.Source.WindowDurationMins;
        var elapsedFraction = countdown.Status switch
        {
            CountdownStatus.DueUnconfirmed when durationMinutes > 0 => 1d,
            CountdownStatus.Counting when durationMinutes > 0 =>
                1d - Math.Clamp(countdown.Remaining!.Value.TotalMinutes / durationMinutes.Value, 0d, 1d),
            _ => 0d
        };
        ClockElapsedGeometry = ClockPalette.ElapsedSector(elapsedFraction);
        ClockElapsedBrush = ClockPalette.ForElapsed(elapsedFraction);
        var clockLegend = UiPresentation.Text(
            UiPresentation.Theme == UiThemeMode.Light ? "ClockTooltipLight" : "ClockTooltip");
        TooltipText = _windows.Count switch
        {
            0 => UiPresentation.Text("QuotaUnavailable", ShortLabel),
            1 when countdown.Status == CountdownStatus.DueUnconfirmed => ResetText,
            1 => UiPresentation.Text("ResetCountdown", ShortLabel,
                _primary!.ShortCountdownText, clockLegend),
            _ => UiPresentation.Text("QuotaCountdown", ShortLabel,
                string.Join("\n", _windows.Select(window => UiPresentation.Text("QuotaLine",
                    window.Source.LimitId,
                    window.AvailablePercent.ToString("0.##", UiPresentation.Culture),
                    window.CountdownStatus == CountdownStatus.DueUnconfirmed
                        ? window.ResetText : window.ShortCountdownText))), clockLegend)
        };
    }

    private static string FormatCompactCountdown(TimeSpan remaining) => remaining.TotalDays >= 1
        ? UiPresentation.Text("DaysCompact", (int)remaining.TotalDays, remaining.Hours, remaining.Minutes)
        : $"{remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class BadgePalette
{
    public static Brush PersonalBackground => UiPresentation.Brush("BadgePersonalBackground");
    public static Brush PersonalBorder => UiPresentation.Brush("BadgePersonalBorder");
    public static Brush PersonalForeground => UiPresentation.Brush("BadgePersonalForeground");
    public static Brush TeamBackground => UiPresentation.Brush("BadgeTeamBackground");
    public static Brush TeamBorder => UiPresentation.Brush("BadgeTeamBorder");
    public static Brush TeamForeground => UiPresentation.Brush("BadgeTeamForeground");
    public static Brush OtherBackground => UiPresentation.Brush("BadgeOtherBackground");
    public static Brush OtherBorder => UiPresentation.Brush("BadgeOtherBorder");
    public static Brush OtherForeground => UiPresentation.Brush("BadgeOtherForeground");
}

public static class ResetCreditPalette
{
    public static Brush PositiveBackground => UiPresentation.Brush("ResetPositiveBackground");
    public static Brush PositiveBorder => UiPresentation.Brush("ResetPositiveBorder");
    public static Brush PositiveForeground => UiPresentation.Brush("ResetPositiveForeground");
    public static Brush ZeroBackground => UiPresentation.Brush("ResetZeroBackground");
    public static Brush ZeroBorder => UiPresentation.Brush("ResetZeroBorder");
    public static Brush ZeroForeground => UiPresentation.Brush("ResetZeroForeground");
    public static Brush UnknownBackground => UiPresentation.Brush("ResetUnknownBackground");
    public static Brush UnknownBorder => UiPresentation.Brush("ResetUnknownBorder");
    public static Brush UnknownForeground => UiPresentation.Brush("ResetUnknownForeground");
}

public static class ClockPalette
{
    private static readonly Brush DarkElapsed = MakeBrush(0xF2, 0x8C, 0x28);
    private static readonly Brush DarkComplete = MakeBrush(0x48, 0xC5, 0xA9);
    private static readonly Brush LightElapsed = MakeBrush(0xAD, 0x64, 0x1D);
    private static readonly Brush LightComplete = MakeBrush(0x2C, 0x87, 0x72);
    public static Brush Elapsed => UiPresentation.Theme == UiThemeMode.Light ? LightElapsed : DarkElapsed;
    public static Brush Complete => UiPresentation.Theme == UiThemeMode.Light ? LightComplete : DarkComplete;
    private static readonly Geometry FullCircle = MakeFullCircle();

    public static Brush ForElapsed(double fraction)
    {
        var position = Math.Clamp(fraction, 0d, 1d);
        if (position <= 0) return Elapsed;
        if (position >= 1) return Complete;
        var hue = 29.7d + 136.9d * position;
        var saturation = 0.835d - 0.2d * position;
        var value = 0.949d - 0.1765d * position;
        var chroma = value * saturation;
        var second = chroma * (1 - Math.Abs((hue / 60) % 2 - 1));
        var offset = value - chroma;
        var (red, green, blue) = hue switch
        {
            < 60 => (chroma, second, 0d),
            < 120 => (second, chroma, 0d),
            < 180 => (0d, chroma, second),
            _ => (0d, second, chroma)
        };
        var contrastFactor = UiPresentation.Theme == UiThemeMode.Light ? 0.73 : 1d;
        return MakeBrush(
            (byte)Math.Round((red + offset) * 255 * contrastFactor),
            (byte)Math.Round((green + offset) * 255 * contrastFactor),
            (byte)Math.Round((blue + offset) * 255 * contrastFactor));
    }

    public static Geometry ElapsedSector(double fraction)
    {
        if (fraction <= 0) return Geometry.Empty;
        const double center = 11;
        const double radius = 9;
        if (fraction >= 1) return FullCircle;

        var angle = (-90 + fraction * 360) * Math.PI / 180;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(center, center), true, true);
            context.LineTo(new Point(center, center - radius), true, false);
            context.ArcTo(
                new Point(center + radius * Math.Cos(angle), center + radius * Math.Sin(angle)),
                new Size(radius, radius), 0, fraction > 0.5,
                SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Geometry MakeFullCircle()
    {
        var circle = new System.Windows.Media.EllipseGeometry(new Point(11, 11), 9, 9);
        circle.Freeze();
        return circle;
    }

    private static Brush MakeBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

public static class AvailabilityPalette
{
    public static Brush Green => UiPresentation.Brush("AvailabilityGreen");
    public static Brush Yellow => UiPresentation.Brush("AvailabilityYellow");
    public static Brush Orange => UiPresentation.Brush("AvailabilityOrange");
    public static Brush Red => UiPresentation.Brush("AvailabilityRed");

    public static Brush ForAvailable(double available) => available switch
    {
        <= 0 => Red,
        < 25 => Orange,
        < 50 => Yellow,
        _ => Green
    };

}

public sealed class WindowCardViewModel : INotifyPropertyChanged
{
    private double _availablePercent;
    private string _availableText = string.Empty;
    private Brush _availabilityBrush = Brushes.Gray;
    private string _countdownText = string.Empty;
    private string _shortCountdownText = string.Empty;

    public WindowCardViewModel(LimitWindow window, DateTimeOffset nowUtc)
    {
        Source = window;
        var duration = window.WindowDurationMins switch
        {
            300 => UiPresentation.Text("FiveHours"),
            10080 => UiPresentation.Text("Weekly"),
            int minutes => UiPresentation.Text("Minutes", minutes),
            null => window.WindowName
        };
        Title = window.WindowDurationMins switch
        {
            300 => "5H",
            10080 => "7D",
            _ => $"{window.LimitId} · {duration}"
        };
        ResetText = window.ResetsAtUtc is null
            ? UiPresentation.Text("ScheduledResetUnknown")
            : UiPresentation.Text("ScheduledReset",
                window.ResetsAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", UiPresentation.Culture));
        Update(nowUtc);
    }

    public LimitWindow Source { get; }
    public string Title { get; }
    public double AvailablePercent
    {
        get => _availablePercent;
        private set => Set(ref _availablePercent, value);
    }
    public string AvailableText
    {
        get => _availableText;
        private set => Set(ref _availableText, value);
    }
    public Brush AvailabilityBrush
    {
        get => _availabilityBrush;
        private set => Set(ref _availabilityBrush, value);
    }
    public string ResetText { get; }
    public CountdownStatus CountdownStatus { get; private set; }
    public string CountdownText
    {
        get => _countdownText;
        private set
        {
            if (_countdownText == value) return;
            _countdownText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountdownText)));
        }
    }
    public string ShortCountdownText
    {
        get => _shortCountdownText;
        private set
        {
            if (_shortCountdownText == value) return;
            _shortCountdownText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShortCountdownText)));
        }
    }

    public void Update(DateTimeOffset nowUtc)
    {
        var countdown = CountdownCalculator.Calculate(Source.ResetsAtUtc, nowUtc);
        CountdownStatus = countdown.Status;
        // Only the display resets here; the last verified usage remains in Source.
        AvailablePercent = countdown.Status == CountdownStatus.DueUnconfirmed
            ? 100 : Math.Clamp(100 - Source.UsedPercent, 0, 100);
        AvailableText = UiPresentation.Text("AvailablePercent",
            AvailablePercent.ToString("0.##", UiPresentation.Culture));
        AvailabilityBrush = AvailabilityPalette.ForAvailable(AvailablePercent);
        CountdownText = countdown.Status switch
        {
            CountdownStatus.Counting => Format(countdown.Remaining!.Value),
            CountdownStatus.DueUnconfirmed => string.Empty,
            _ => UiPresentation.Text("ResetTimeUnknown")
        };
        ShortCountdownText = countdown.Status switch
        {
            CountdownStatus.Counting => FormatShort(countdown.Remaining!.Value),
            CountdownStatus.DueUnconfirmed => string.Empty,
            _ => UiPresentation.Text("Unknown")
        };
    }

    private static string Format(TimeSpan remaining) => UiPresentation.Text("DaysCountdown",
        (int)remaining.TotalDays, remaining.Hours, remaining.Minutes, remaining.Seconds);

    private static string FormatShort(TimeSpan remaining) => remaining.TotalDays >= 1
        ? UiPresentation.Text("DaysShort", (int)remaining.TotalDays,
            remaining.Hours, remaining.Minutes, remaining.Seconds)
        : $"{remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
