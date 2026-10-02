using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace CodexQuotaHub.Desktop;

public enum UiThemeMode { Dark, Light }
public enum UiLanguage { TraditionalChinese, English, SimplifiedChinese }

public static class UiPresentation
{
    private sealed record Translation(string Traditional, string English, string Simplified);

    private static readonly IReadOnlyDictionary<string, Translation> Words =
        new Dictionary<string, Translation>(StringComparer.Ordinal)
        {
            ["Subtitle"] = new("Codex 多帳號額度總覽與重置提醒", "Multi-account quota overview and reset alerts for Codex.", "Codex 多账号额度总览与重置提醒"),
            ["Loading"] = new("正在載入本機快照…", "Loading saved snapshots…", "正在加载本地快照…"),
            ["Refresh"] = new("立即刷新", "Refresh now", "立即刷新"),
            ["Pause"] = new("暫停監察", "Pause monitoring", "暂停监测"),
            ["Resume"] = new("恢復監察", "Resume monitoring", "恢复监测"),
            ["Settings"] = new("設定", "Settings", "设置"),
            ["View"] = new("檢視", "View", "视图"),
            ["Detailed"] = new("詳細", "Details", "详细"),
            ["Compact"] = new("簡潔", "Compact", "简洁"),
            ["TotalTitle"] = new("所有帳號每日 Token 合計", "Daily tokens across all accounts", "所有账号每日 Token 合计"),
            ["RecentSourceDates"] = new("近 7 日 · 來源日期", "Last 7 days · source dates", "近 7 天 · 来源日期"),
            ["TotalNote"] = new("僅加總已有資料的帳號；涵蓋數顯示該日有資料的帳號／已記錄帳號。數值為最後確認記錄。", "Only accounts with data are included. Coverage shows accounts with data / saved accounts. Values are the last confirmed records.", "仅汇总已有数据的账号；覆盖数显示当日有数据的账号／已记录账号。数值为最后确认的记录。"),
            ["Account"] = new("帳號", "Account", "账号"),
            ["ManualReset"] = new("手動重置", "Manual resets", "手动重置"),
            ["FiveHourAvailable"] = new("5h 可用量", "5h available", "5h 可用量"),
            ["SevenDayAvailable"] = new("7d 可用量", "7d available", "7d 可用量"),
            ["Empty"] = new("尚無快照。請登入 Codex 後按「立即刷新」。", "No snapshots yet. Sign in to Codex, then select Refresh now.", "暂无快照。请登录 Codex 后点击“立即刷新”。"),
            ["AccountDailyTitle"] = new("每日 Token · 近 7 日", "Daily tokens · last 7 days", "每日 Token · 近 7 天"),
            ["ExpandAccount"] = new("展開或折疊帳號額度", "Expand or collapse account quotas", "展开或折叠账号额度"),
            ["CreditTitle"] = new("手動重置額度", "Manual reset credits", "手动重置额度"),
            ["Close"] = new("關閉", "Close", "关闭"),
            ["SettingsTitle"] = new("Codex Quota Hub 設定", "Codex Quota Hub settings", "Codex Quota Hub 设置"),
            ["IntervalLabel"] = new("自動刷新間隔", "Automatic refresh interval", "自动刷新间隔"),
            ["ThemeLabel"] = new("外觀", "Appearance", "外观"),
            ["LanguageLabel"] = new("語言", "Language", "语言"),
            ["TelegramEnable"] = new("啟用 Telegram 到期通知", "Enable Telegram due alerts", "启用 Telegram 到期通知"),
            ["TelegramBotToken"] = new("Bot Token", "Bot token", "Bot Token"),
            ["TelegramChatId"] = new("接收對話 ID", "Destination chat ID", "接收聊天 ID"),
            ["TelegramTokenHint"] = new("留空沿用已儲存的 Token；Token 會以 Windows 帳戶加密保存。", "Leave blank to keep the saved token. It is encrypted for this Windows account.", "留空沿用已保存的 Token；Token 会按 Windows 账号加密保存。"),
            ["TelegramTokenSaved"] = new("Bot Token 已儲存；欄位留空會沿用。", "Bot token saved; leave the field blank to keep it.", "Bot Token 已保存；留空将沿用。"),
            ["TelegramLookupInstruction"] = new("向您的 Telegram Bot 傳送「{0}」，然後按「尋找對話」。", "Send “{0}” to your Telegram bot, then select Find chat.", "向您的 Telegram Bot 发送“{0}”，然后点击“查找聊天”。"),
            ["TelegramFindChat"] = new("尋找對話", "Find chat", "查找聊天"),
            ["TelegramTest"] = new("發送測試", "Send test", "发送测试"),
            ["TelegramClear"] = new("清除設定", "Clear setup", "清除设置"),
            ["TelegramWorking"] = new("正在聯絡 Telegram…", "Contacting Telegram…", "正在连接 Telegram…"),
            ["TelegramChatFound"] = new("已找到私人對話；請發送測試並儲存。", "Private chat found. Send a test, then save.", "已找到私人聊天；请发送测试并保存。"),
            ["TelegramTestSent"] = new("測試訊息已發送；請到手機確認。", "Test message sent. Check your phone.", "测试消息已发送；请在手机确认。"),
            ["TelegramTestMessage"] = new("Codex Quota Hub：Telegram 測試通知。", "Codex Quota Hub: Telegram test notification.", "Codex Quota Hub：Telegram 测试通知。"),
            ["TelegramCleared"] = new("Telegram 設定將於儲存後清除。", "Telegram setup will be cleared when you save.", "保存后将清除 Telegram 设置。"),
            ["TelegramStoredTokenUnavailable"] = new("已儲存 Token 無法解密；請重新輸入。", "Saved token cannot be decrypted. Enter it again.", "已保存的 Token 无法解密；请重新输入。"),
            ["TelegramInvalidToken"] = new("請輸入有效的 Bot Token。", "Enter a valid bot token.", "请输入有效的 Bot Token。"),
            ["TelegramInvalidChat"] = new("請輸入數字對話 ID，或先按「尋找對話」。", "Enter a numeric chat ID, or select Find chat.", "请输入数字聊天 ID，或先点击“查找聊天”。"),
            ["TelegramChatNotFound"] = new("尚未找到代碼；請先向 Bot 傳送畫面上的代碼，再重試。", "Code not found. Send the shown code to your bot, then retry.", "尚未找到代码；请先向 Bot 发送页面上的代码，再重试。"),
            ["TelegramUnauthorized"] = new("Bot Token 被拒絕；請檢查或重新產生。", "Bot token was rejected. Check or regenerate it.", "Bot Token 被拒绝；请检查或重新生成。"),
            ["TelegramForbidden"] = new("Bot 無權傳送訊息；請先在 Telegram 開啟與 Bot 的對話。", "The bot cannot send here. Start a chat with it in Telegram first.", "Bot 无权发送消息；请先在 Telegram 打开与 Bot 的聊天。"),
            ["TelegramBadRequest"] = new("Telegram 拒絕接收對話 ID；請重新尋找或檢查。", "Telegram rejected the chat ID. Find or check it again.", "Telegram 拒绝接收聊天 ID；请重新查找或检查。"),
            ["TelegramRateLimited"] = new("Telegram 暫時限制發送，稍後再試。", "Telegram is rate limiting requests. Try later.", "Telegram 暂时限制发送，请稍后再试。"),
            ["TelegramWebhookConflict"] = new("此 Bot 已設定 Webhook，無法自動尋找對話；請手動輸入對話 ID 或改用新 Bot。", "This bot has a webhook. Enter a chat ID manually or use a new bot.", "此 Bot 已设置 Webhook，无法自动查找聊天；请手动输入聊天 ID 或改用新 Bot。"),
            ["TelegramTimeout"] = new("Telegram 連線逾時，請稍後再試。", "Telegram timed out. Try again later.", "Telegram 连接超时，请稍后再试。"),
            ["TelegramNetwork"] = new("無法連接 Telegram，請檢查網絡。", "Could not reach Telegram. Check the network.", "无法连接 Telegram，请检查网络。"),
            ["TelegramInvalidResponse"] = new("Telegram 回應無法辨識，請稍後再試。", "Telegram returned an unexpected response. Try later.", "Telegram 返回了无法识别的响应，请稍后再试。"),
            ["TelegramApiError"] = new("Telegram 發生錯誤，請稍後再試。", "Telegram returned an error. Try later.", "Telegram 发生错误，请稍后再试。"),
            ["TelegramDueMessage"] = new("Codex Quota Hub｜預定重置時間已到\n帳號：{0}\n額度：{1}\n預定時間：{2}（本機時間）\n{3}請刷新以確認最新可用量。", "Codex Quota Hub | Scheduled reset time reached\nAccount: {0}\nQuota: {1}\nScheduled: {2} (local time)\n{3}Refresh to confirm current availability.", "Codex Quota Hub｜预计重置时间已到\n账号：{0}\n额度：{1}\n预计时间：{2}（本地时间）\n{3}请刷新以确认最新可用量。"),
            ["TelegramResetCreditsLine"] = new("可用手動重置額度（最後確認）：{0} 次", "Available manual reset credits (last confirmed): {0}", "可用手动重置额度（最后确认）：{0} 次"),
            ["EditAlias"] = new("編輯帳號別名", "Edit account alias", "编辑账号别名"),
            ["AliasTitle"] = new("帳號別名", "Account alias", "账号别名"),
            ["AliasLabel"] = new("別名", "Alias", "别名"),
            ["AliasHint"] = new("最多 40 字。別名會原樣傳送至 Telegram；留空並儲存可移除別名，改為顯示部分遮蔽的電郵。", "Up to 40 characters. The alias is sent to Telegram as entered. Save an empty field to remove it and show a partly masked email.", "最多 40 字。别名会原样发送至 Telegram；留空并保存可移除别名，改为显示部分遮蔽的邮箱。"),
            ["AliasSaveError"] = new("無法儲存帳號別名：{0}", "Could not save the account alias: {0}", "无法保存账号别名：{0}"),
            ["TelegramSendFailed"] = new("Telegram 通知暫時無法送出；程式會重試。請到設定測試連線。", "Telegram alert could not be sent. The app will retry; check it in Settings.", "Telegram 通知暂时无法发送；程序会重试。请在设置中测试连接。"),
            ["Quota"] = new("用量", "Usage", "用量"),
            ["IntervalHint"] = new("查詢失敗時會暫時延長間隔；倒數每秒由本機更新。", "After a failed query, the interval increases temporarily. The countdown updates locally each second.", "查询失败时会暂时延长间隔；倒计时每秒在本地更新。"),
            ["Save"] = new("儲存", "Save", "保存"),
            ["Cancel"] = new("取消", "Cancel", "取消"),
            ["Dark"] = new("黑暗模式", "Dark", "深色模式"),
            ["Light"] = new("白天模式", "Light", "浅色模式"),
            ["TraditionalChinese"] = new("繁體中文", "Traditional Chinese", "繁体中文"),
            ["English"] = new("英文", "English", "英语"),
            ["SimplifiedChinese"] = new("簡體中文", "Simplified Chinese", "简体中文"),
            ["Interval60"] = new("60 秒", "60 seconds", "60 秒"),
            ["Interval300"] = new("5 分鐘", "5 minutes", "5 分钟"),
            ["Interval900"] = new("15 分鐘", "15 minutes", "15 分钟"),
            ["Open"] = new("開啟", "Open", "打开"),
            ["Exit"] = new("退出", "Exit", "退出"),
            ["QueryingAccount"] = new("正在確認目前帳號與用量…", "Checking the current account and usage…", "正在确认当前账号与用量…"),
            ["SavedStatus"] = new("已更新目前帳號：{0}", "Current account updated: {0}", "已更新当前账号：{0}"),
            ["SignedOutStatus"] = new("目前沒有登入帳號；顯示最後成功快照。", "No account is signed in; showing the last saved snapshots.", "当前没有登录账号；显示最后成功的快照。"),
            ["ChangedStatus"] = new("查詢期間身份改變；本輪未保存資料，將於下次刷新確認。", "The account changed during the query; nothing was saved. The next refresh will check again.", "查询期间身份发生变化；本次未保存数据，将在下次刷新时确认。"),
            ["UnverifiedStatus"] = new("無法確認帳號或用量：{0}。已保留最後快照。", "Could not verify the account or usage: {0}. The last snapshots were kept.", "无法确认账号或用量：{0}。已保留最后的快照。"),
            ["SnapshotReadError"] = new("暫時無法讀取本機快照：{0}", "Could not read saved snapshots: {0}", "暂时无法读取本地快照：{0}"),
            ["DailyReadError"] = new("暫時無法讀取每日 Token 記錄：{0}", "Could not read daily token records: {0}", "暂时无法读取每日 Token 记录：{0}"),
            ["DatabaseError"] = new("資料庫暫不可用：{0}", "Database unavailable: {0}", "数据库暂不可用：{0}"),
            ["NotificationError"] = new("提醒記錄暫不可用：{0}", "Notification records unavailable: {0}", "提醒记录暂不可用：{0}"),
            ["ReasonCommand"] = new("找不到 Codex 命令", "Codex command not found", "找不到 Codex 命令"),
            ["ReasonInitialize"] = new("連線初始化失敗", "Connection initialization failed", "连接初始化失败"),
            ["ReasonAccount"] = new("無法讀取目前帳號", "Could not read current account", "无法读取当前账号"),
            ["ReasonIdentity"] = new("帳號身份資料不足", "Account identity is incomplete", "账号身份数据不足"),
            ["ReasonLimits"] = new("額度資料無法確認", "Quota data could not be verified", "额度数据无法确认"),
            ["ReasonChanged"] = new("查詢期間帳號改變", "Account changed during query", "查询期间账号发生变化"),
            ["ReasonTimeout"] = new("查詢逾時", "Query timed out", "查询超时"),
            ["ReasonProtocol"] = new("資料格式無法辨識", "Unrecognized data format", "无法识别数据格式"),
            ["ReasonUnavailable"] = new("資料來源暫不可用", "Data source temporarily unavailable", "数据来源暂不可用"),
            ["MonitoringPaused"] = new("監察已暫停；本機倒數仍會更新。", "Monitoring is paused; local countdowns continue.", "监测已暂停；本地倒计时仍会更新。"),
            ["SettingsSaveError"] = new("設定無法儲存：{0}", "Could not save settings: {0}", "无法保存设置：{0}"),
            ["Querying"] = new("正在查詢…", "Querying…", "正在查询…"),
            ["Paused"] = new("已暫停", "Paused", "已暂停"),
            ["AwaitingFirst"] = new("等待首次查詢…", "Waiting for first query…", "等待首次查询…"),
            ["NextAutomatic"] = new("下次自動查詢：{0}", "Next automatic query: {0}", "下次自动查询：{0}"),
            ["QueryingShort"] = new("查詢中", "Querying", "查询中"),
            ["UpdatedShort"] = new("已更新", "Updated", "已更新"),
            ["SignedOutShort"] = new("尚未登入", "Signed out", "尚未登录"),
            ["IdentityPending"] = new("身份待確認", "Identity pending", "身份待确认"),
            ["SourceUnavailable"] = new("來源暫不可用", "Source unavailable", "来源暂不可用"),
            ["LoadingShort"] = new("讀取中", "Loading", "读取中"),
            ["NextShort"] = new("{0} · 下次 {1}", "{0} · next {1}", "{0} · 下次 {1}"),
            ["CurrentUpdated"] = new("目前帳號 · 已更新", "Current account · updated", "当前账号 · 已更新"),
            ["OfflineUnavailable"] = new("離線快照 · 來源暫不可用", "Saved snapshot · source unavailable", "离线快照 · 来源暂不可用"),
            ["OfflineSnapshot"] = new("離線快照", "Saved snapshot", "离线快照"),
            ["FiveHours"] = new("5 小時", "5 hours", "5 小时"),
            ["Weekly"] = new("每週", "Weekly", "每周"),
            ["Minutes"] = new("{0} 分鐘", "{0} minutes", "{0} 分钟"),
            ["ResetDueTitle"] = new("預定重置時間已到", "Scheduled reset time reached", "预计重置时间已到"),
            ["ResetDueBody"] = new("{0} 的 {1} 額度已到預定重置時間，登入後可確認最新用量。", "The {1} quota for {0} reached its scheduled reset time. Sign in to confirm current usage.", "{0} 的 {1} 额度已到预计重置时间，登录后可确认最新用量。"),
            ["AlreadyRunning"] = new("Codex Quota Hub 已在執行，請從系統匣開啟。", "Codex Quota Hub is already running. Open it from the tray.", "Codex Quota Hub 已在运行，请从系统托盘打开。"),
            ["UnsupportedArgument"] = new("不支援的啟動參數。", "Unsupported startup argument.", "不支持的启动参数。"),
            ["StartupError"] = new("無法啟動 Codex Quota Hub：{0}", "Could not start Codex Quota Hub: {0}", "无法启动 Codex Quota Hub：{0}"),
            ["Unknown"] = new("未知", "Unknown", "未知"),
            ["NotProvided"] = new("未提供", "Unavailable", "未提供"),
            ["Pending"] = new("待確認", "Pending", "待确认"),
            ["WorkspaceFingerprint"] = new("Workspace 指紋 {0}", "Workspace ID {0}", "Workspace 指纹 {0}"),
            ["LastUpdated"] = new("最後成功更新：{0}", "Last successful update: {0}", "最后成功更新：{0}"),
            ["ResetGridCount"] = new("手動重置 {0} 次", "Manual resets: {0}", "手动重置 {0} 次"),
            ["ResetGridUnknown"] = new("手動重置 —", "Manual resets: —", "手动重置 —"),
            ["ResetDetailedCount"] = new("手動重置：{0} 次", "Manual resets: {0}", "手动重置：{0} 次"),
            ["ResetDetailedUnknown"] = new("手動重置：未提供", "Manual resets: unavailable", "手动重置：未提供"),
            ["ResetTooltipCount"] = new("手動重置額度：最後確認 {0} 次\n{1}", "Manual reset credits: last confirmed {0}\n{1}", "手动重置额度：最后确认 {0} 次\n{1}"),
            ["ResetTooltipUnknown"] = new("手動重置額度：來源未提供，並非 0", "Manual reset credits: unavailable from source, not zero", "手动重置额度：来源未提供，并非 0"),
            ["OtherWindows"] = new("另有 {0} 項額度", "{0} other quotas", "另有 {0} 项额度"),
            ["LastTokenRead"] = new("最後取得 {0}", "Last retrieved {0}", "最后获取 {0}"),
            ["NoRecentTokens"] = new("尚未取得近 7 日資料", "No data for the last 7 days", "尚未获取近 7 天数据"),
            ["MultipleItems"] = new("{0} · {1} 項", "{0} · {1} items", "{0} · {1} 项"),
            ["AvailablePercent"] = new("可用 {0}%", "{0}% available", "可用 {0}%"),
            ["ResetShort"] = new("重置 {0}", "Resets {0}", "重置 {0}"),
            ["ResetTimeUnknown"] = new("重置時間未知", "Reset time unknown", "重置时间未知"),
            ["CountdownUnavailable"] = new("倒數 未提供", "Countdown unavailable", "倒计时未提供"),
            ["CountdownShort"] = new("倒數 {0}", "Countdown {0}", "倒计时 {0}"),
            ["QuotaUnavailable"] = new("{0} 額度未提供", "{0} quota unavailable", "{0} 额度未提供"),
            ["ClockTooltip"] = new("彩色：已過時間；深色：剩餘時間", "Colored: elapsed; dark: remaining", "彩色：已过时间；深色：剩余时间"),
            ["ClockTooltipLight"] = new("彩色：已過時間；淺色：剩餘時間", "Colored: elapsed; pale: remaining", "彩色：已过时间；浅色：剩余时间"),
            ["ResetCountdown"] = new("{0} 重置倒數\n{1}\n{2}", "{0} reset countdown\n{1}\n{2}", "{0} 重置倒计时\n{1}\n{2}"),
            ["QuotaCountdown"] = new("{0} 額度與倒數\n{1}\n{2}", "{0} quota and countdown\n{1}\n{2}", "{0} 额度与倒计时\n{1}\n{2}"),
            ["QuotaLine"] = new("{0}：可用 {1}% · {2}", "{0}: {1}% available · {2}", "{0}：可用 {1}% · {2}"),
            ["DaysCompact"] = new("{0}天{1:00}:{2:00}", "{0}d {1:00}:{2:00}", "{0}天{1:00}:{2:00}"),
            ["DaysCountdown"] = new("剩餘 {0} 天 {1:00}:{2:00}:{3:00}", "Remaining {0}d {1:00}:{2:00}:{3:00}", "剩余 {0} 天 {1:00}:{2:00}:{3:00}"),
            ["DaysShort"] = new("{0} 天 {1:00}:{2:00}:{3:00}", "{0}d {1:00}:{2:00}:{3:00}", "{0} 天 {1:00}:{2:00}:{3:00}"),
            ["ScheduledResetUnknown"] = new("預定重置：未知", "Scheduled reset: unknown", "预计重置：未知"),
            ["ScheduledReset"] = new("預定重置：{0}", "Scheduled reset: {0}", "预计重置：{0}"),
            ["ResetDuePending"] = new("已到預定重置時間，待確認", "Scheduled reset time reached; pending confirmation", "已到预计重置时间，待确认"),
            ["CreditsCount"] = new("{0} 次", "{0}", "{0} 次"),
            ["CreditsUnavailable"] = new("來源未提供手動重置額度資料。", "The source did not provide manual reset credits.", "来源未提供手动重置额度数据。"),
            ["CreditsZero"] = new("最後確認可用額度為 0 次。", "Last confirmed available credits: 0.", "最后确认可用额度为 0 次。"),
            ["CreditsCountOnly"] = new("來源只提供可用總數，沒有逐筆名稱、說明或到期時間。", "The source provided only the total, without names, descriptions, or expiration times.", "来源只提供可用总数，没有逐笔名称、说明或到期时间。"),
            ["CreditsNoDetails"] = new("來源沒有回傳逐筆明細，無法列出到期時間。", "The source returned no individual credits, so expiration times cannot be listed.", "来源没有返回逐笔明细，无法列出到期时间。"),
            ["CreditsPartial"] = new("已顯示 {0} / {1} 筆明細；其餘額度的到期時間未提供。", "Showing {0} / {1} credits; expiration times for the rest were not provided.", "已显示 {0} / {1} 笔明细；其余额度的到期时间未提供。"),
            ["CreditsOvercount"] = new("來源回傳 {0} 筆明細，可用總數為 {1} 次；請以可用總數為準。", "The source returned {0} credit details but reports {1} available; use the total as authoritative.", "来源返回 {0} 笔明细，可用总数为 {1} 次；请以可用总数为准。"),
            ["CreditsAll"] = new("已顯示全部 {0} 筆明細。", "Showing all {0} credits.", "已显示全部 {0} 笔明细。"),
            ["NameUnavailable"] = new("名稱未提供", "Name unavailable", "名称未提供"),
            ["DescriptionUnavailable"] = new("說明未提供", "Description unavailable", "说明未提供"),
            ["CreditAvailable"] = new("可用", "Available", "可用"),
            ["CreditRedeeming"] = new("兌換中", "Redeeming", "兑换中"),
            ["CreditRedeemed"] = new("已兌換", "Redeemed", "已兑换"),
            ["CreditUnknown"] = new("狀態未知", "Status unknown", "状态未知"),
            ["ExpiresAt"] = new("到期：{0}（本機時間）", "Expires: {0} (local time)", "到期：{0}（本地时间）"),
            ["ExpiresLabel"] = new("到期：", "Expires:", "到期："),
            ["NeverExpires"] = new("到期：不會到期", "Expires: never", "到期：不会过期"),
            ["SourceDate"] = new("來源日期 {0}", "Source date {0}", "来源日期 {0}"),
            ["TokenAccountMissing"] = new("尚未取得此帳號的每日 Token 資料；— 不代表 0。", "No daily token data for this account; — does not mean zero.", "尚未获取此账号的每日 Token 数据；— 不代表 0。"),
            ["TokenLastRead"] = new("{0} Token\n最後取得：{1}", "{0} tokens\nLast retrieved: {1}", "{0} Token\n最后获取：{1}"),
            ["Coverage"] = new("{0}/{1} 帳號", "{0}/{1} accounts", "{0}/{1} 账号"),
            ["TokenTotal"] = new("已記錄帳號合計 {0} Token\n涵蓋 {1}/{2} 帳號；缺資料未計入。", "Saved accounts total: {0} tokens\nCoverage: {1}/{2}; missing data excluded.", "已记录账号合计 {0} Token\n覆盖 {1}/{2} 账号；缺失数据未计入。"),
            ["TokenTotalMissing"] = new("尚未取得任何帳號資料；— 不代表 0。", "No account data available; — does not mean zero.", "尚未获取任何账号数据；— 不代表 0。")
        };

    private static readonly IReadOnlyDictionary<string, (string Dark, string Light)> Colors =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["Brush0D1117"] = ("#050A13", "#F4F7FA"),
            ["Brush0F1720"] = ("#0B1B2D", "#F7FAFC"),
            ["Brush111A23"] = ("#0B192A", "#FFFFFF"),
            ["Brush153029"] = ("#142D43", "#E4F3EC"),
            ["Brush17212C"] = ("#13283D", "#EDF3F7"),
            ["Brush19372F"] = ("#13283D", "#DDF1E8"),
            ["Brush20323B"] = ("#182D43", "#FFFFFF"),
            ["Brush238B73"] = ("#1E619B", "#147A64"),
            ["Brush2B3944"] = ("#263E53", "#DCE7ED"),
            ["Brush2B3A49"] = ("#254968", "#CBD9E2"),
            ["Brush30404E"] = ("#34485A", "#D6E1E8"),
            ["Brush315D50"] = ("#254968", "#B8D9C8"),
            ["Brush34495C"] = ("#1A3048", "#DCE6ED"),
            ["Brush40292E"] = ("#40292E", "#FBECEE"),
            ["Brush48C5A9"] = ("#79D6FF", "#0B8068"),
            ["Brush5B746F"] = ("#376386", "#A6C2B8"),
            ["Brush73D8C2"] = ("#79D6FF", "#137B63"),
            ["Brush8293A3"] = ("#8FA8BA", "#536A79"),
            ["Brush9FB0BF"] = ("#ABC2D4", "#536A79"),
            ["BrushA8C3BA"] = ("#ABC2D4", "#427260"),
            ["BrushA9B7C5"] = ("#ABC2D4", "#536A79"),
            ["BrushAAB7C2"] = ("#A6BECE", "#5A7080"),
            ["BrushD4DFE8"] = ("#DAEAF7", "#253C4B"),
            ["BrushE36B6B"] = ("#E36B6B", "#BC3645"),
            ["BrushE6EDF3"] = ("#EDF7FF", "#1B3040"),
            ["PrimaryButtonText"] = ("#FFFFFF", "#FFFFFF"),
            ["SecondaryButtonText"] = ("#FFFFFF", "#183244"),
            ["StatusSurface"] = ("#1A354B", "#E4F3EC"),
            ["TotalSurface"] = ("#0B192A", "#E4F3EC"),
            ["BadgePersonalBackground"] = ("#4A361F", "#FFF0D5"),
            ["BadgePersonalBorder"] = ("#F0B54A", "#A45E08"),
            ["BadgePersonalForeground"] = ("#FFDF98", "#75420A"),
            ["BadgeTeamBackground"] = ("#1C3856", "#E5F1FC"),
            ["BadgeTeamBorder"] = ("#69B2F1", "#3272AB"),
            ["BadgeTeamForeground"] = ("#D0EAFF", "#20527C"),
            ["BadgeOtherBackground"] = ("#1C3045", "#E8EFF4"),
            ["BadgeOtherBorder"] = ("#6D8AA3", "#6C8494"),
            ["BadgeOtherForeground"] = ("#FFFFFF", "#345063"),
            ["ResetPositiveBackground"] = ("#18322E", "#E5F4ED"),
            ["ResetPositiveBorder"] = ("#41655D", "#6EAD94"),
            ["ResetPositiveForeground"] = ("#73D8C2", "#176F58"),
            ["ResetZeroBackground"] = ("#342329", "#FCECEE"),
            ["ResetZeroBorder"] = ("#7C454D", "#D99AA0"),
            ["ResetZeroForeground"] = ("#E36B6B", "#B93543"),
            ["ResetUnknownBackground"] = ("#16283A", "#EDF2F6"),
            ["ResetUnknownBorder"] = ("#3B5870", "#CAD8E1"),
            ["ResetUnknownForeground"] = ("#9AB6C8", "#526B7B"),
            ["TokenKnown"] = ("#79D6FF", "#1B3040"),
            ["TokenTotal"] = ("#79D6FF", "#12755D"),
            ["TokenMissing"] = ("#8FA8BA", "#687E8B"),
            ["ScrollTrack"] = ("#102A43", "#E4ECF1"),
            ["ScrollThumb"] = ("#3B84B3", "#849CAC"),
            ["ScrollThumbHover"] = ("#5DAEDB", "#5D7F90"),
            ["ScrollThumbPressed"] = ("#79D6FF", "#267F70"),
            ["AvailabilityGreen"] = ("#48C5A9", "#168269"),
            ["AvailabilityYellow"] = ("#E8C66A", "#A46F00"),
            ["AvailabilityOrange"] = ("#E4A15B", "#B45C13"),
            ["AvailabilityRed"] = ("#E36B6B", "#C43C4D")
        };

    public static UiLanguage Language { get; private set; } = UiLanguage.TraditionalChinese;
    public static UiThemeMode Theme { get; private set; } = UiThemeMode.Dark;

    public static CultureInfo Culture => Language switch
    {
        UiLanguage.English => CultureInfo.GetCultureInfo("en-US"),
        UiLanguage.SimplifiedChinese => CultureInfo.GetCultureInfo("zh-CN"),
        _ => CultureInfo.GetCultureInfo("zh-TW")
    };

    public static void Apply(UiThemeMode theme, UiLanguage language)
    {
        Theme = theme;
        Language = language;
        var resources = System.Windows.Application.Current?.Resources;
        if (resources is null) return;
        foreach (var (key, colors) in Colors)
            resources[key] = MakeBrush(theme == UiThemeMode.Dark ? colors.Dark : colors.Light);
        foreach (var key in Words.Keys)
            resources["L_" + key] = Text(key);
    }

    public static string Text(string key, params object?[] args)
    {
        if (!Words.TryGetValue(key, out var word))
            throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown UI text key.");
        var format = Language switch
        {
            UiLanguage.English => word.English,
            UiLanguage.SimplifiedChinese => word.Simplified,
            _ => word.Traditional
        };
        return args.Length == 0 ? format : string.Format(Culture, format, args);
    }

    public static Brush Brush(string key)
    {
        if (System.Windows.Application.Current?.Resources[key] is Brush brush)
            return brush;
        var fallback = Colors[key].Dark;
        return MakeBrush(fallback);
    }

    private static Brush MakeBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
