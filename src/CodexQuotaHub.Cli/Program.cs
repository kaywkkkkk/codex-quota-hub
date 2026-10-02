using System.Security.Cryptography;
using System.Text;
using CodexQuotaHub.Core;

var exitCode = await RunAsync(args);
return exitCode;

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
    {
        PrintUsage();
        return 0;
    }

    var command = args[0];
    var databasePath = ApplicationPaths.GetDefaultDatabasePath();
    var codexExecutable = "codex";
    var intervalSeconds = 60;
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] == "--db" && i + 1 < args.Length)
            databasePath = args[++i];
        else if (args[i] == "--codex" && i + 1 < args.Length)
            codexExecutable = args[++i];
        else if (args[i] == "--interval" && i + 1 < args.Length &&
                 int.TryParse(args[++i], out var seconds) && seconds is >= 15 and <= 3600)
            intervalSeconds = seconds;
        else
        {
            Console.Error.WriteLine("參數無效。請執行 help 查看用法。");
            return 1;
        }
    }

    if (command is not ("refresh" or "list" or "watch"))
    {
        PrintUsage();
        return 1;
    }

    try
    {
        var store = new SnapshotStore(databasePath);
        if (command == "list")
        {
            PrintSnapshots(store.ReadAll());
            return 0;
        }

        var coordinator = new RefreshCoordinator(new AppServerProbe(codexExecutable), store);
        if (command == "refresh")
        {
            var result = await coordinator.RefreshAsync();
            PrintResult(result);
            PrintSnapshots(store.ReadAll());
            return result.Status is RefreshStatus.Unverified or RefreshStatus.ChangedDuringCycle ? 2 : 0;
        }

        using var stopped = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopped.Cancel();
        };
        Console.WriteLine($"開始每 {intervalSeconds} 秒查詢；按 Ctrl+C 停止。");
        var nextDelaySeconds = intervalSeconds;
        while (!stopped.IsCancellationRequested)
        {
            try
            {
                var result = await coordinator.RefreshAsync(stopped.Token);
                PrintResult(result);
                nextDelaySeconds = result.Status == RefreshStatus.Unverified
                    ? Math.Min(900, Math.Max(intervalSeconds, nextDelaySeconds * 2))
                    : intervalSeconds;
            }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested)
            {
                break;
            }
            try { await Task.Delay(TimeSpan.FromSeconds(nextDelaySeconds), stopped.Token); }
            catch (OperationCanceledException) { break; }
        }
        return 0;
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                  Microsoft.Data.Sqlite.SqliteException)
    {
        Console.Error.WriteLine($"無法存取快照資料庫：{error.Message}");
        return 1;
    }
}

static void PrintResult(RefreshResult result)
{
    var time = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
    switch (result.Status)
    {
        case RefreshStatus.Saved:
            Console.WriteLine($"[{time}] 已確認身份並保存 {result.Snapshot!.Identity.Email} 的快照。");
            break;
        case RefreshStatus.SignedOut:
            Console.WriteLine($"[{time}] 目前沒有登入帳號；保留所有既有快照。");
            break;
        case RefreshStatus.ChangedDuringCycle:
            Console.WriteLine($"[{time}] 查詢期間身份改變；本輪沒有保存資料。");
            break;
        case RefreshStatus.Unverified:
            Console.WriteLine($"[{time}] 無法確認身份或用量（{result.Reason}）；保留既有快照。");
            break;
    }
}

static void PrintSnapshots(IReadOnlyList<UsageSnapshot> snapshots)
{
    if (snapshots.Count == 0)
    {
        Console.WriteLine("尚無已確認的快照。");
        return;
    }

    var now = DateTimeOffset.UtcNow;
    foreach (var snapshot in snapshots)
    {
        var workspaceFingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.Identity.WorkspaceId)))[..8];
        Console.WriteLine();
        Console.WriteLine($"{snapshot.Identity.Email}｜方案 {snapshot.Identity.PlanType ?? "未知"}｜Workspace 指紋 {workspaceFingerprint}");
        Console.WriteLine($"最後成功更新：{snapshot.ObservedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}（快照）");
        Console.WriteLine(snapshot.AvailableResetCredits is long resetCredits
            ? $"手動重置額度：{resetCredits} 次（最後確認）"
            : "手動重置額度：未提供");
        foreach (var window in snapshot.Windows)
        {
            var countdown = CountdownCalculator.Calculate(window.ResetsAtUtc, now);
            var remaining = countdown.Status switch
            {
                CountdownStatus.Counting => FormatRemaining(countdown.Remaining!.Value),
                CountdownStatus.DueUnconfirmed => "已到預定重置時間，待確認",
                _ => "重置時間未知"
            };
            var resetTime = window.ResetsAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "未知";
            var duration = window.WindowDurationMins is null ? "未知" : $"{window.WindowDurationMins} 分鐘";
            Console.WriteLine($"  {window.LimitId}/{window.WindowName}｜已用 {window.UsedPercent:0.##}%｜窗口 {duration}｜重置 {resetTime}｜{remaining}");
        }
    }
}

static string FormatRemaining(TimeSpan remaining) =>
    $"剩餘 {(int)remaining.TotalDays} 天 {remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";

static void PrintUsage()
{
    Console.WriteLine("Codex Quota Hub 1.0");
    Console.WriteLine("Multi-account quota overview and reset alerts for Codex.");
    Console.WriteLine("  refresh [--db 路徑] [--codex 路徑]   讀取目前帳號並保存經確認的快照");
    Console.WriteLine("  list [--db 路徑]                     顯示所有快照與本機倒數");
    Console.WriteLine("  watch [--interval 秒] [--db 路徑] [--codex 路徑]   定期讀取");
    Console.WriteLine("  查詢間隔為 15–3600 秒；預設 60 秒。");
    Console.WriteLine($"預設資料庫：{ApplicationPaths.GetDefaultDatabasePath()}");
}
