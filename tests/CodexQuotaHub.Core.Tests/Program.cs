using CodexQuotaHub.Core;
using System.Text.Json;

var failures = new List<string>();
await Run("切換期間不寫入", TestChangedDuringCycleAsync);
await Run("帳號與 Workspace 分開保存且重啟後可讀", TestPersistenceAsync);
await Run("App Server 手動重置額度解析", TestResetCreditsParsingAsync);
await Run("每日 Token 解析、分帳保存與缺資料保留", TestDailyTokenUsageAsync);
await Run("資料庫寫入具交易性", TestTransactionAsync);
await Run("離線倒數不自行重置用量", TestCountdownAsync);
await Run("到期提醒及設定可持久保存", TestNotificationPersistenceAsync);
await Run("Telegram 通知佇列可重試且跨次啟動去重", TestTelegramQueueAsync);
await Run("帳號別名按 Workspace 保存且不受刷新覆蓋", TestAccountAliasAsync);
await Run("雙擊啟動時可從 Codex 安裝目錄找到命令", TestCodexExecutableDiscoveryAsync);
await Run("改名後保留舊資料庫並正確選擇新版路徑", TestDatabasePathCompatibilityAsync);

if (failures.Count > 0)
{
    foreach (var failure in failures) Console.Error.WriteLine(failure);
    return 1;
}
Console.WriteLine("全部 11 項測試通過。");
return 0;

async Task Run(string name, Func<Task> test)
{
    try
    {
        await test();
        Console.WriteLine($"通過：{name}");
    }
    catch (Exception error)
    {
        failures.Add($"失敗：{name}：{error.Message}");
    }
}

async Task TestDatabasePathCompatibilityAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), "codex-quota-hub-paths-" + Guid.NewGuid().ToString("N"));
    var currentPath = Path.Combine(directory, "CodexQuotaHub", "meter.db");
    var legacyPath = Path.Combine(directory, "CodexMeter", "meter.db");
    try
    {
        if (ApplicationPaths.GetDefaultDatabasePath(directory) != currentPath)
            throw new InvalidOperationException("A fresh installation must use the new product directory.");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        await File.WriteAllTextAsync(legacyPath, "existing account data");
        if (ApplicationPaths.GetDefaultDatabasePath(directory) != legacyPath ||
            await File.ReadAllTextAsync(legacyPath) != "existing account data" || File.Exists(currentPath))
            throw new InvalidOperationException("The rename must reuse the legacy database without changing or copying it.");
        Directory.CreateDirectory(Path.GetDirectoryName(currentPath)!);
        await File.WriteAllTextAsync(currentPath, "new account data");
        if (ApplicationPaths.GetDefaultDatabasePath(directory) != currentPath ||
            await File.ReadAllTextAsync(legacyPath) != "existing account data")
            throw new InvalidOperationException("An existing new database must take priority and leave legacy data intact.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

async Task TestChangedDuringCycleAsync()
{
    using var fixture = new DatabaseFixture();
    var personal = Snapshot("same@example.test", "personal-id", "plus", 30);
    var business = Snapshot("same@example.test", "business-id", "team", 40);
    var coordinator = new RefreshCoordinator(new QueueProbe(
        ProbeResult.Verified(personal), ProbeResult.Verified(business)), fixture.Store);
    var result = await coordinator.RefreshAsync();
    Check(result.Status == RefreshStatus.ChangedDuringCycle, "應識別同一使用者的 Workspace 切換");
    Check(fixture.Store.ReadAll().Count == 0, "切換期間不應產生快照");

    var failed = new RefreshCoordinator(new QueueProbe(
        ProbeResult.Verified(personal), ProbeResult.Unverified("timeout")), fixture.Store);
    Check((await failed.RefreshAsync()).Status == RefreshStatus.Unverified, "查詢失敗應標示未確認");
    Check(fixture.Store.ReadAll().Count == 0, "失敗結果不應產生快照");

    fixture.Store.Save(personal with { AvailableResetCredits = 2 });
    var signedOut = new RefreshCoordinator(new QueueProbe(
        ProbeResult.SignedOut(), ProbeResult.SignedOut()), fixture.Store);
    Check((await signedOut.RefreshAsync()).Status == RefreshStatus.SignedOut, "登出應可辨識");
    Check(fixture.Store.ReadAll().Count == 1, "登出後應保留最後快照");
    Check(fixture.Store.ReadAll()[0].AvailableResetCredits == 2,
        "登出後應保留最後確認的手動重置額度");
}

async Task TestPersistenceAsync()
{
    using var fixture = new DatabaseFixture(legacy: true);
    var expiry = DateTimeOffset.Parse("2026-10-23T00:00:00+00:00");
    var credit = new ResetCredit("credit-personal", "codexRateLimits", "available",
        expiry, "Full reset (Weekly + 5 hr)", "Reset an eligible Codex rate-limit window.");
    var personal = Snapshot("same@example.test", "personal-id", "plus", 30)
        with { AvailableResetCredits = 2, ResetCredits = [credit] };
    var business = Snapshot("same@example.test", "business-id", "team", 40)
        with { AvailableResetCredits = 0, ResetCredits = [] };
    var otherUser = Snapshot("other@example.test", "business-id", "team", 50);
    foreach (var snapshot in new[] { personal, business, otherUser })
    {
        var coordinator = new RefreshCoordinator(new QueueProbe(
            ProbeResult.Verified(snapshot), ProbeResult.Verified(snapshot)), fixture.Store);
        Check((await coordinator.RefreshAsync()).Status == RefreshStatus.Saved, "穩定查詢應保存");
    }

    var reopened = new SnapshotStore(fixture.Path).ReadAll();
    Check(reopened.Count == 3, "重開資料庫後應仍有三個使用者／Workspace 組合");
    Check(reopened.Single(x => x.Identity.Email == "same@example.test" &&
                               x.Identity.WorkspaceId == "personal-id").Windows[0].UsedPercent == 30,
        "Personal 快照不可被 Business 覆蓋");
    Check(reopened.Single(x => x.Identity.Email == "other@example.test").Windows[0].UsedPercent == 50,
        "同一 Business 的不同使用者不可合併");
    Check(reopened.Single(x => x.Identity.WorkspaceId == "personal-id").AvailableResetCredits == 2,
        "升級舊資料庫後應按 Workspace 保存正數額度");
    Check(reopened.Single(x => x.Identity.Email == "same@example.test" &&
                               x.Identity.WorkspaceId == "business-id").AvailableResetCredits == 0,
        "0 次額度應與未知分開保存");
    Check(reopened.Single(x => x.Identity.Email == "other@example.test").AvailableResetCredits is null,
        "來源未提供額度時應保存未知狀態");
    var creditStore = new SnapshotStore(fixture.Path);
    Check(creditStore.GetAvailableResetCredits("same@example.test", "personal-id") == 2,
        "通知應讀取該帳號與 Workspace 最新確認的正數額度");
    Check(creditStore.GetAvailableResetCredits("same@example.test", "business-id") == 0,
        "通知應保留已確認的 0 次額度");
    Check(creditStore.GetAvailableResetCredits("other@example.test", "business-id") is null,
        "最新查詢未提供額度時，通知不可推算數字");
    var savedPersonal = reopened.Single(x => x.Identity.WorkspaceId == "personal-id");
    Check(savedPersonal.ResetCredits is { Count: 1 } &&
          savedPersonal.ResetCredits[0].Id == credit.Id &&
          savedPersonal.ResetCredits[0].Title == credit.Title &&
          savedPersonal.ResetCredits[0].Description == credit.Description &&
          savedPersonal.ResetCredits[0].ExpiresAtUtc == expiry,
        "升級舊資料庫後應按帳號與 Workspace 保存逐筆到期時間、名稱與說明");
    Check(reopened.Single(x => x.Identity.Email == "same@example.test" &&
                               x.Identity.WorkspaceId == "business-id").ResetCredits is { Count: 0 },
        "已取得空明細應與未提供明細分開保存");
    Check(reopened.Single(x => x.Identity.Email == "other@example.test").ResetCredits is null,
        "未提供明細不可誤存為空明細");

    fixture.Store.Save(personal with { AvailableResetCredits = null, ResetCredits = null });
    var afterMissing = new SnapshotStore(fixture.Path).ReadAll();
    Check(afterMissing.Single(x => x.Identity.WorkspaceId == "personal-id").AvailableResetCredits is null,
        "新的已確認查詢未提供額度時，不可誤留舊數字");
    Check(creditStore.GetAvailableResetCredits("same@example.test", "personal-id") is null &&
          creditStore.GetAvailableResetCredits("same@example.test", "business-id") == 0,
        "較新的查詢未提供額度時，通知不可沿用舊值或影響另一 Workspace");
    Check(afterMissing.Single(x => x.Identity.WorkspaceId == "personal-id").ResetCredits is null,
        "新的已確認查詢未提供明細時，不可誤留舊到期時間");
}

Task TestResetCreditsParsingAsync()
{
    static (long? Count, IReadOnlyList<ResetCredit>? Details) Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return (AppServerProbe.ReadAvailableResetCredits(document.RootElement),
            AppServerProbe.ReadResetCreditDetails(document.RootElement));
    }

    Check(Parse("""{"rateLimitResetCredits":{"availableCount":2,"credits":[]}}""").Count == 2,
        "應以 availableCount 為準，不以明細筆數推算");
    Check(Parse("""{"rateLimitResetCredits":{"availableCount":0,"credits":null}}""").Count == 0,
        "0 應是已確認數值");
    Check(Parse("""{"rateLimitResetCredits":null}""").Count is null,
        "來源未提供時應為未知");
    Check(Parse("""{"rateLimitResetCredits":{"credits":[{},{}]}}""").Count is null,
        "缺少 availableCount 時不可由明細推算");
    Check(Parse("""{"rateLimitResetCredits":{"availableCount":-1}}""").Count is null,
        "無效數值不可當作可用額度");
    var expiry = DateTimeOffset.Parse("2026-10-23T00:00:00+00:00");
    var details = Parse($$$"""
        {"rateLimitResetCredits":{"availableCount":3,"credits":[
          {"id":"first","resetType":"codexRateLimits","status":"available",
           "expiresAt":{{{expiry.ToUnixTimeSeconds()}}},"title":"Full reset","description":"Weekly + 5 hr"},
          {"id":"second","resetType":"codexRateLimits","status":"available",
           "expiresAt":null,"title":null,"description":null}]}}
        """);
    Check(details.Count == 3 && details.Details is { Count: 2 } &&
          details.Details[0].ExpiresAtUtc == expiry &&
          details.Details[0].Title == "Full reset" &&
          details.Details[0].Description == "Weekly + 5 hr" &&
          details.Details[1].ExpiresAtUtc is null,
        "逐筆資料應保留到期時間與來源名稱說明；明細不足不可改寫總數");
    Check(Parse("""{"rateLimitResetCredits":{"availableCount":1,"credits":null}}""").Details is null,
        "只有總數時應標示逐筆明細未提供");
    Check(Parse("""{"rateLimitResetCredits":{"availableCount":0,"credits":[]}}""").Details is { Count: 0 },
        "空明細應與未提供明細區分");
    return Task.CompletedTask;
}

Task TestDailyTokenUsageAsync()
{
    var observed = DateTimeOffset.Parse("2026-09-25T12:00:00+00:00");
    static IReadOnlyList<DailyTokenUsage>? Parse(string json, DateTimeOffset observed)
    {
        using var document = JsonDocument.Parse(json);
        return AppServerProbe.ReadDailyTokens(document.RootElement, observed);
    }

    var days = Parse("""{"dailyUsageBuckets":[{"startDate":"2026-09-24","tokens":1234567},{"startDate":"2026-09-25","tokens":0}]}""", observed);
    Check(days is { Count: 2 } && days[0].Date == new DateOnly(2026, 9, 24) &&
          days[0].Tokens == 1234567 && days[1].Tokens == 0,
        "應保留來源日期及已確認的 0 Token");
    Check(Parse("""{"dailyUsageBuckets":null}""", observed) is null,
        "未提供每日資料不可當作 0");
    Check(Parse("""{"dailyUsageBuckets":[{"startDate":"2026-09-25","tokens":-1}]}""", observed) is null,
        "負數 Token 應拒絕");
    Check(Parse("""{"dailyUsageBuckets":[{"startDate":"2026-09-25","tokens":1},{"startDate":"2026-09-25","tokens":2}]}""", observed) is null,
        "重複日期不可任意覆蓋");

    using var fixture = new DatabaseFixture(legacy: true);
    var personal = Snapshot("same@example.test", "personal-id", "plus", 30)
        with { DailyTokens = days };
    var business = Snapshot("same@example.test", "business-id", "team", 40)
        with { DailyTokens = [new DailyTokenUsage(new DateOnly(2026, 9, 24), 500, observed)] };
    fixture.Store.Save(personal);
    fixture.Store.Save(business);
    var reopened = new SnapshotStore(fixture.Path).ReadAll();
    Check(reopened.Single(x => x.Identity.WorkspaceId == "personal-id").DailyTokens!
              .Single(x => x.Date == new DateOnly(2026, 9, 24)).Tokens == 1234567 &&
          reopened.Single(x => x.Identity.WorkspaceId == "business-id").DailyTokens!
              .Single(x => x.Date == new DateOnly(2026, 9, 24)).Tokens == 500,
        "相同電郵的不同 Workspace 應各自保存每日 Token");
    var totals = DailyTokenSummary.SumByDate(reopened,
        [new DateOnly(2026, 9, 24), new DateOnly(2026, 9, 25), new DateOnly(2026, 9, 26)]);
    Check(totals[0].Tokens == 1235067 && totals[0].CoveredAccounts == 2 &&
          totals[0].TotalAccounts == 2,
        "總計應加總相同來源日期且計算涵蓋帳號數");
    Check(totals[1].Tokens == 0 && totals[1].CoveredAccounts == 1 &&
          totals[2].Tokens is null && totals[2].CoveredAccounts == 0,
        "已確認的 0 與沒有資料必須分開顯示");

    fixture.Store.Save(personal with { DailyTokens = null });
    Check(new SnapshotStore(fixture.Path).ReadAll()
            .Single(x => x.Identity.WorkspaceId == "personal-id").DailyTokens!
            .Single(x => x.Date == new DateOnly(2026, 9, 24)).Tokens == 1234567,
        "來源暫未提供時應保留最後確認的每日 Token");
    fixture.Store.Save(personal with
    {
        DailyTokens = [new DailyTokenUsage(new DateOnly(2026, 9, 24), 222, observed.AddHours(1))]
    });
    var updated = new SnapshotStore(fixture.Path).ReadAll()
        .Single(x => x.Identity.WorkspaceId == "personal-id").DailyTokens!;
    Check(updated.Single(x => x.Date == new DateOnly(2026, 9, 24)).Tokens == 222 &&
          updated.Single(x => x.Date == new DateOnly(2026, 9, 25)).Tokens == 0,
        "重查應更新同日數值，不可刪去未回傳的已確認日期");
    return Task.CompletedTask;
}

Task TestTransactionAsync()
{
    using var fixture = new DatabaseFixture();
    var original = Snapshot("same@example.test", "personal-id", "plus", 30) with
    {
        AvailableResetCredits = 1,
        ResetCredits = [new ResetCredit("original", "codexRateLimits", "available",
            DateTimeOffset.UtcNow.AddDays(10), "Original", "Original description")]
    };
    fixture.Store.Save(original);
    var duplicate = original with
    {
        Windows = new[] { original.Windows[0], original.Windows[0] }
    };
    try
    {
        fixture.Store.Save(duplicate);
        throw new Exception("重複窗口應導致 SQLite 拒絕寫入");
    }
    catch (Microsoft.Data.Sqlite.SqliteException)
    {
        // The failed transaction must leave the prior snapshot intact.
    }
    var saved = new SnapshotStore(fixture.Path).ReadAll();
    Check(saved.Count == 1 && saved[0].Windows.Count == 1 &&
          saved[0].Windows[0].UsedPercent == 30, "寫入失敗應保留原快照");
    var savedCredits = saved[0].ResetCredits;
    Check(savedCredits is { Count: 1 } && savedCredits[0].Title == "Original",
        "交易失敗不應覆蓋原有逐筆重置明細");
    return Task.CompletedTask;
}

Task TestCountdownAsync()
{
    var now = DateTimeOffset.Parse("2026-09-24T00:00:00+00:00");
    var future = CountdownCalculator.Calculate(now.AddSeconds(90), now);
    Check(future.Status == CountdownStatus.Counting && future.Remaining == TimeSpan.FromSeconds(90),
        "倒數應以絕對時間計算");
    var due = CountdownCalculator.Calculate(now.AddSeconds(-1), now);
    Check(due.Status == CountdownStatus.DueUnconfirmed && due.Remaining == TimeSpan.Zero,
        "到期只能標示待確認");
    Check(CountdownCalculator.Calculate(null, now).Status == CountdownStatus.Unknown,
        "缺少重置時間應標示未知");
    return Task.CompletedTask;
}

Task TestNotificationPersistenceAsync()
{
    using var fixture = new DatabaseFixture();
    var now = DateTimeOffset.Parse("2026-09-24T12:00:00+00:00");
    var snapshot = Snapshot("same@example.test", "personal-id", "plus", 30);
    var due = snapshot.Windows[0] with { ResetsAtUtc = now.AddSeconds(-1) };
    var future = snapshot.Windows[0] with { ResetsAtUtc = now.AddHours(1) };
    Check(!fixture.Store.TryRecordNotification(snapshot.Identity, future, now), "未到期不能提醒");
    Check(fixture.Store.TryRecordNotification(snapshot.Identity, due, now), "到期應記錄第一次提醒");
    Check(!fixture.Store.TryRecordNotification(snapshot.Identity, due, now), "同一週期不可重複提醒");

    fixture.Store.SetRefreshIntervalSeconds(300);
    Check(fixture.Store.GetUiTheme() == "dark" && fixture.Store.GetUiLanguage() == "zh-TW",
        "舊資料庫應預設黑暗模式與繁體中文");
    fixture.Store.SetDesktopPreferences(900, "light", "en");
    var reopened = new SnapshotStore(fixture.Path);
    Check(!reopened.TryRecordNotification(snapshot.Identity, due, now.AddMinutes(1)),
        "重啟後不可重複提醒");
    Check(reopened.GetRefreshIntervalSeconds() == 900 &&
          reopened.GetUiTheme() == "light" && reopened.GetUiLanguage() == "en",
        "刷新間隔、主題及語言應一併持久保存");
    reopened.SetDesktopPreferences(60, "dark", "zh-CN");
    var reopenedAgain = new SnapshotStore(fixture.Path);
    Check(reopenedAgain.GetUiTheme() == "dark" && reopenedAgain.GetUiLanguage() == "zh-CN",
        "簡體中文與黑暗模式應在重啟後恢復");
    try
    {
        reopenedAgain.SetDesktopPreferences(300, "light", "invalid");
        throw new Exception("無效語言應被拒絕");
    }
    catch (ArgumentOutOfRangeException) { }
    Check(reopenedAgain.GetRefreshIntervalSeconds() == 60 &&
          reopenedAgain.GetUiTheme() == "dark" && reopenedAgain.GetUiLanguage() == "zh-CN",
        "無效設定不得覆蓋已保存的選擇");
    Check(reopened.TryRecordNotification(snapshot.Identity, future, now.AddHours(1)),
        "新的重置週期應可提醒");
    return Task.CompletedTask;
}

Task TestTelegramQueueAsync()
{
    using var fixture = new DatabaseFixture();
    var activated = DateTimeOffset.Parse("2026-09-24T12:00:00+00:00");
    var snapshot = Snapshot("same@example.test", "personal-id", "plus", 30);
    var expiredBeforeEnable = snapshot.Windows[0] with { ResetsAtUtc = activated.AddMinutes(-1) };
    var due = snapshot.Windows[0] with { ResetsAtUtc = activated.AddMinutes(1) };
    var now = activated.AddMinutes(2);
    fixture.Store.SetTelegramPreferences(new TelegramPreferences(true, "12345", "encrypted-token", activated));
    Check(!fixture.Store.TryQueueTelegramReset(snapshot.Identity, expiredBeforeEnable, now, activated),
        "啟用前已到期的舊週期不可補發");
    Check(!fixture.Store.TryQueueTelegramReset(snapshot.Identity, due, activated, activated),
        "未到期不可加入發送佇列");
    Check(fixture.Store.TryQueueTelegramReset(snapshot.Identity, due, now, activated),
        "到期應加入 Telegram 佇列");
    Check(!fixture.Store.TryQueueTelegramReset(snapshot.Identity, due, now, activated),
        "同一週期不可重複加入佇列");
    var reopened = new SnapshotStore(fixture.Path);
    var prefs = reopened.GetTelegramPreferences();
    Check(prefs.Enabled && prefs.ChatId == "12345" &&
          prefs.ProtectedToken == "encrypted-token" && prefs.ActiveSinceUtc == activated,
        "設定應於重啟後恢復");
    var pending = reopened.ReadPendingTelegramReset(now, activated);
    Check(pending is not null && pending.Email == snapshot.Identity.Email && pending.Attempts == 0,
        "未送出的通知應於重啟後繼續等待發送");
    var retryAt = now.AddMinutes(3);
    reopened.CompleteTelegramReset(pending!, now, false, retryAt);
    Check(reopened.ReadPendingTelegramReset(now.AddMinutes(1), activated) is null,
        "網絡失敗後不應立即重試");
    pending = reopened.ReadPendingTelegramReset(retryAt, activated);
    Check(pending is { Attempts: 1 }, "失敗記錄應於指定時間重試");
    reopened.CompleteTelegramReset(pending!, retryAt, true);
    Check(new SnapshotStore(fixture.Path).ReadPendingTelegramReset(retryAt.AddDays(1), activated) is null,
        "成功送出後重啟也不可再次發送");
    var nextCycle = due with { ResetsAtUtc = due.ResetsAtUtc!.Value.AddHours(5) };
    Check(reopened.TryQueueTelegramReset(snapshot.Identity, nextCycle,
            nextCycle.ResetsAtUtc.Value.AddSeconds(1), activated),
        "下個重置週期應可獨立發送");
    Check(reopened.ReadPendingTelegramReset(nextCycle.ResetsAtUtc.Value.AddSeconds(1),
        activated.AddDays(1)) is null, "重新設定收件者後不可補發舊佇列");
    return Task.CompletedTask;
}

Task TestAccountAliasAsync()
{
    using var fixture = new DatabaseFixture(legacy: true);
    var personal = Snapshot("same@example.test", "personal-id", "plus", 30);
    var business = Snapshot("same@example.test", "business-id", "team", 40);
    fixture.Store.Save(personal);
    fixture.Store.Save(business);
    fixture.Store.SetAccountAlias(personal.Identity.Email, personal.Identity.WorkspaceId, "私人額度");
    fixture.Store.SetAccountAlias(business.Identity.Email, business.Identity.WorkspaceId, "公司額度");
    fixture.Store.Save(personal with { ObservedAtUtc = personal.ObservedAtUtc.AddMinutes(1) });
    var reopened = new SnapshotStore(fixture.Path);
    var aliases = reopened.ReadAccountAliases();
    Check(aliases[(personal.Identity.Email, personal.Identity.WorkspaceId)] == "私人額度" &&
          aliases[(business.Identity.Email, business.Identity.WorkspaceId)] == "公司額度",
        "相同電郵的不同 Workspace 應有獨立別名；用量刷新不得覆蓋別名");
    reopened.SetAccountAlias(personal.Identity.Email, personal.Identity.WorkspaceId, "  新別名  ");
    Check(reopened.GetAccountAlias(personal.Identity.Email, personal.Identity.WorkspaceId) == "新別名",
        "別名應去除首尾空白");
    reopened.SetAccountAlias(personal.Identity.Email, personal.Identity.WorkspaceId, "");
    Check(reopened.GetAccountAlias(personal.Identity.Email, personal.Identity.WorkspaceId) is null &&
          reopened.GetAccountAlias(business.Identity.Email, business.Identity.WorkspaceId) == "公司額度",
        "清除其中一個別名不得影響其他 Workspace");
    try
    {
        reopened.SetAccountAlias(business.Identity.Email, business.Identity.WorkspaceId, "錯誤\n別名");
        throw new Exception("跨行別名應被拒絕");
    }
    catch (ArgumentException) { }
    Check(reopened.GetAccountAlias(business.Identity.Email, business.Identity.WorkspaceId) == "公司額度",
        "無效別名不得覆蓋既有值");
    return Task.CompletedTask;
}

Task TestCodexExecutableDiscoveryAsync()
{
    var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
        "codex-quota-hub-discovery-" + Guid.NewGuid().ToString("N"));
    try
    {
        var older = System.IO.Path.Combine(directory, "OpenAI", "Codex", "bin", "older", "codex.exe");
        var newer = System.IO.Path.Combine(directory, "OpenAI", "Codex", "bin", "newer", "codex.exe");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(older)!);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(newer)!);
        File.WriteAllBytes(older, []);
        File.WriteAllBytes(newer, []);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-1));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        Check(CodexExecutableLocator.Resolve("codex", directory) == newer,
            "沒有 PATH 時應找到較新的 Codex 安裝版本");
        Check(CodexExecutableLocator.Resolve("codex.exe", directory) == newer,
            "預設 exe 名稱也應自動尋找");
        Check(CodexExecutableLocator.Resolve(older, directory) == older,
            "明確指定的命令路徑不可被覆蓋");
        Check(CodexExecutableLocator.Resolve("codex", System.IO.Path.Combine(directory, "missing")) == "codex",
            "沒有安裝目錄時仍應保留 PATH 查找");
    }
    finally
    {
        var tempRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        var resolved = System.IO.Path.GetFullPath(directory);
        var relative = System.IO.Path.GetRelativePath(tempRoot, resolved);
        if (relative.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative))
            throw new InvalidOperationException("測試資料夾不在預期的暫存目錄內。");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }
    return Task.CompletedTask;
}

static UsageSnapshot Snapshot(string email, string workspace, string plan, double used) =>
    new(new AccountIdentity(email, workspace, "chatgpt", plan), DateTimeOffset.UtcNow,
        new[] { new LimitWindow("codex", "primary", used, 300, DateTimeOffset.UtcNow.AddHours(3)) });

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

sealed class QueueProbe(params ProbeResult[] replies) : IUsageProbe
{
    private readonly Queue<ProbeResult> _replies = new(replies);

    public Task<ProbeResult> ProbeAsync(
        CancellationToken cancellationToken = default, bool includeDailyTokens = true) =>
        Task.FromResult(_replies.Dequeue());
}

sealed class DatabaseFixture : IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "codex-quota-hub-test-" + Guid.NewGuid().ToString("N"));
    public string Path => System.IO.Path.Combine(_directory, "meter.db");
    public SnapshotStore Store { get; }

    public DatabaseFixture(bool legacy = false)
    {
        Directory.CreateDirectory(_directory);
        if (legacy)
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE accounts (
                    email TEXT NOT NULL,
                    workspace_id TEXT NOT NULL,
                    account_type TEXT NOT NULL,
                    plan_type TEXT,
                    last_success_utc_ms INTEGER NOT NULL,
                    PRIMARY KEY (email, workspace_id)
                );
                """;
            command.ExecuteNonQuery();
        }
        Store = new SnapshotStore(Path);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
        Directory.Delete(_directory);
    }
}
