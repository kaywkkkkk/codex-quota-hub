# Codex Quota Hub 1.0

**Codex 多帳號額度總覽與重置提醒。**

[English](README.md) | **繁體中文** | [简体中文](README.zh-CN.md)

Codex Quota Hub 是 Windows 桌面用量監察工具，讓你在獨立視窗查看 Codex 帳號的可用額度、重置倒數及每日 Token 使用量。已確認的資料會保存在本機，離線時仍可查看最後一次查詢結果。

**1.0 是本專案的第一個公開發佈版本。** 提供 Windows x64 桌面程式、命令列工具及完整原始碼，採用 MIT 授權。

## 主要功能

- **額度與倒數**：顯示來源提供的用量窗口，包括 5 小時及 7 日額度、可用百分比、重置時間與倒數。
- **三種檢視**：詳細、Grid、簡潔，可隨時切換；簡潔檢視以圓形時計呈現重置進度。
- **帳號與 Workspace 記錄**：分開保存曾經查詢成功的帳號與工作區，可設定最多 40 字的帳號別名。
- **每日 Token**：詳細檢視顯示近 7 日的每日數值及已記錄帳號的每日合計，標示有資料的帳號數。
- **手動重置額度明細**：查看可用次數、來源、說明、狀態及到期時間。
- **到期提醒**：支援 Windows 系統匣及可選的 Telegram 通知。
- **主題與語言**：深色／白天模式；繁體中文、英文、簡體中文。
- **本機快照**：啟動時載入已保存資料，預設每 60 秒查詢一次；查詢失敗時保留快照並延長重試間隔。

本工具讀取 Codex 的目前登入身份及用量，不提供登入、登出、切換帳號或兌換手動重置額度的功能。

## 畫面預覽

以下畫面由 1.0 原始碼配合虛構帳號資料產生。

| 詳細檢視 | Grid 檢視 |
| --- | --- |
| ![詳細檢視](docs/previews/detailed.png) | ![Grid 檢視](docs/previews/grid.png) |

![簡潔檢視](docs/previews/compact.png)

[查看手動重置額度明細畫面](docs/previews/reset-credit-details.png)

## 下載與啟動

### 使用條件

- Windows x64 電腦。
- 已安裝 Codex，並在同一 Windows 使用者環境登入需要監察的帳號。
- 查詢最新資料及發送 Telegram 通知時需要網絡連線。

Windows 發佈包已包含 .NET 執行環境，一般使用者不需另外安裝 .NET SDK。

### 桌面程式

1. 在本專案的 [Releases](https://github.com/kaywkkkkk/codex-quota-hub/releases) 下載 [CodexQuotaHub-1.0-win-x64.zip](https://github.com/kaywkkkkk/codex-quota-hub/releases/download/v1.0/CodexQuotaHub-1.0-win-x64.zip)。
2. 將 ZIP 解壓縮至自己的資料夾，執行 `CodexQuotaHub.exe`。
3. 啟動後程式會讀取目前帳號並保存經確認的資料。首次尚未查詢成功時，畫面沒有歷史快照。

本機整理的發佈包位於 `release-assets/`。ZIP 內亦附上使用說明、MIT 授權及第三方元件聲明。

關閉主視窗會縮到系統匣並繼續監察。要完全停止程式，請在系統匣選單選擇「退出」。同一 Windows 使用者只能開啟一個桌面程式實例。

### 基本操作

- 「立即刷新」讀取目前帳號的最新資料。
- 「暫停監察」暫停自動查詢及 Telegram 到期通知；恢復後繼續監察。
- 「設定」可更改刷新間隔、主題、語言及 Telegram 設定。
- 詳細檢視的帳號可逐一折疊；切換檢視不會觸發重新查詢。
- 帳號旁的鉛筆按鈕可編輯別名；留空並儲存可移除別名。
- 點擊手動重置次數可查看額度明細。數字 0 表示沒有可用次數；「—／未提供」表示來源未提供數字。

當你在 Codex 中切換帳號或 Workspace，Codex Quota Hub 下次查詢只會更新目前帳號；其他帳號繼續顯示最後保存的快照。

### Telegram 到期通知

1. 在 Telegram 向 [@BotFather](https://t.me/BotFather) 使用 `/newbot` 建立 Bot，取得 Bot Token。
2. 在 Codex Quota Hub「設定」填入 Token。在 Telegram 向該 Bot 傳送設定畫面顯示的短代碼，再按「尋找對話」，或手動填入數字對話 ID。
3. 按「發送測試」確認收到訊息，勾選「啟用 Telegram 到期通知」並儲存。

通知包含帳號別名、額度窗口及預定重置時間；若最後成功查詢有提供手動重置次數，亦會附上該數字。未設定別名時，程式只附上部分遮蔽的電郵，不自行加入完整電郵或 Workspace ID。別名會按你輸入的內容傳送。

Bot Token 以目前 Windows 使用者的資料保護機制加密後存入本機資料庫。請勿分享 Token 或資料庫。「清除設定」後儲存可移除 Token 及對話 ID。已有 Webhook 的 Bot 不能使用自動尋找對話，可手動輸入 ID 或使用專用 Bot。

程式須保持運行且能連網才可發送通知。電腦休眠或程式退出期間不能準時發送；恢復後會處理已排入佇列而未送出的通知。同一重置週期只排入一次，網絡失敗時會重試；若訊息已送達但成功記錄未能保存，仍可能重發。

## 資料與顯示方式

- 預設資料庫：`%LOCALAPPDATA%\CodexQuotaHub\meter.db`。
- 若新版預設資料庫尚未存在，程式會自動沿用既有的 `%LOCALAPPDATA%\CodexMeter\meter.db`，保留帳號、別名及設定，無需搬移資料庫；`--db` 指定的路徑仍優先使用。
- 資料包含電郵、Workspace 識別資料、方案、用量、重置時間、每日 Token、帳號別名及程式設定。請勿將個人資料庫上傳 GitHub。
- 重置倒數由最後確認的時間計算。到期後進度條可顯示 100%，但這是畫面呈現，仍需刷新確認最新可用量；程式不會自行修改已保存的用量或推算下一輪重置時間。
- 每日 Token 缺少資料時顯示「—」，不當作 0。每日合計只計入有資料的帳號，來源日期按原值保留。
- 手動重置資料缺少明細時，不會推算其到期時間。
- 查詢失敗、未登入或查詢期間身份改變時，既有快照不會被覆蓋。

資料查詢透過本機 `codex.exe app-server` 進行。程式會先尋找 Codex 的本機安裝目錄；如果找不到，可指定路徑：

```powershell
.\CodexQuotaHub.exe --codex "C:\path\to\codex.exe"
.\CodexQuotaHub.exe --db "C:\path\to\meter.db"
```

## 命令列工具

下載並解壓縮 [CodexQuotaHub-Cli-1.0-win-x64.zip](https://github.com/kaywkkkkk/codex-quota-hub/releases/download/v1.0/CodexQuotaHub-Cli-1.0-win-x64.zip)，在所在資料夾執行：

```powershell
.\CodexQuotaHub.Cli.exe refresh
.\CodexQuotaHub.Cli.exe list
.\CodexQuotaHub.Cli.exe watch --interval 300
```

| 指令 | 用途 |
| --- | --- |
| `refresh` | 查詢目前身份與用量，確認一致後保存快照。 |
| `list` | 讀取本機快照並顯示倒數，不連接 App Server。 |
| `watch` | 定期查詢；預設每 60 秒一次，按 Ctrl+C 停止。 |
| `help` | 顯示參數說明。 |

可用 `--db <路徑>` 指定資料庫，或在 `refresh`／`watch` 使用 `--codex <路徑>` 指定 Codex 執行檔。`watch` 的 `--interval` 接受 15–3600 秒。命令列與桌面程式預設使用同一資料庫。

## 從原始碼建置

開發環境需要 Windows 及 [.NET 10 SDK](https://learn.microsoft.com/en-us/dotnet/core/install/windows)。在專案根目錄執行：

```powershell
dotnet build CodexQuotaHub.slnx -c Release
dotnet run --project src/CodexQuotaHub.Desktop
dotnet run --project src/CodexQuotaHub.Cli -- list
```

執行既有核心測試：

```powershell
dotnet run --project tests/CodexQuotaHub.Core.Tests -c Release
```

重新產生 Windows x64 發佈包：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

腳本會建置桌面及命令列的自含單檔執行檔，連同說明和授權打包至 `release-assets/`，並產生 ZIP 的 SHA-256 校驗值。程式與 SQLite 所需的原生程式庫會封裝在執行檔內。

`tests/CodexQuotaHub.Desktop.Visual` 提供以虛構資料驗證及擷取 WPF 畫面的工具。若要重新產生 README 預覽圖，另需 Python 及 Pillow：

```powershell
python -m pip install Pillow
python tests/render_view_previews.py
```

## 專案結構

```text
src/                  核心、桌面與命令列原始碼
tests/                核心測試及 WPF 畫面驗證工具
docs/previews/        1.0 畫面預覽
docs/third-party/     第三方授權及聲明
scripts/publish.ps1   Windows 發佈打包腳本
release-assets/       本機 Windows 發佈包（不提交至 Git）
Directory.Build.props 統一版本及作者資訊
CodexQuotaHub.slnx    建置方案
README.md             英文說明
README.zh-TW.md       繁體中文說明
README.zh-CN.md       簡體中文說明
CHANGELOG.md          英文版本記錄
LICENSE               MIT 授權
```

GitHub 原始碼及 Release 附件的整理方式見 [發佈說明（英文）](docs/RELEASE.md)。1.0 功能清單見 [版本記錄（英文）](CHANGELOG.md)。

## 1.0 使用限制

- 只提供 Windows x64 發佈包，未包含安裝程式、開機自動啟動或崩潰後自動重啟功能。
- 只會查詢目前登入的帳號，歷史帳號的快照不會在背景各自更新。
- 額度、每日 Token 及重置明細以 Codex 回傳的資料為準；未提供的欄位會顯示未知。
- Codex 更新後，App Server 的介面或資料格式變動可能影響查詢。
- 在與 Codex 不同的 Windows 使用者或隔離環境執行，可能無法讀到目前登入身份。

## 授權

Copyright (c) 2026 kaywkkkkk。Codex Quota Hub 採用 [MIT License](LICENSE)。隨 Windows 自含執行檔提供的第三方元件各自保留其原有授權，詳見 [第三方聲明（英文）](THIRD-PARTY-NOTICES.md)。
