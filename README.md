# Codex Quota Hub 1.0

**Multi-account quota overview and reset alerts for Codex.**

**English** | [Traditional Chinese](README.zh-TW.md) | [Simplified Chinese](README.zh-CN.md)

Codex Quota Hub is a Windows desktop tool for monitoring Codex usage. View your accounts' remaining quota, reset countdowns, and daily token usage in a separate window. Verified readings are stored locally, so you can still view the last confirmed results while offline.

**1.0 is the project's first public release.** It includes a Windows x64 desktop app, a command-line tool, and the complete source code under the MIT License.

## Features

- **Quota and countdowns**: View usage windows reported by Codex, including 5-hour and 7-day limits, remaining percentages, reset times, and countdowns.
- **Three views**: Switch between Detailed, Grid, and Compact views. Compact view uses circular clocks to show reset progress.
- **Account and Workspace history**: Store verified snapshots separately for each account and Workspace, with optional account aliases of up to 40 characters.
- **Daily tokens**: Detailed view shows the last 7 days of token usage and daily totals across saved accounts, with the number of accounts that have data.
- **Manual reset credit details**: View available counts, sources, descriptions, statuses, and expiration times.
- **Due alerts**: Receive Windows system tray alerts and optional Telegram notifications.
- **Themes and languages**: Dark and light themes; Traditional Chinese, English, and Simplified Chinese.
- **Local snapshots**: Load saved data on startup and refresh every 60 seconds by default. Failed queries retain existing snapshots and use longer retry intervals.

The tool reads Codex's current signed-in identity and usage. It does not sign in, sign out, switch accounts, or redeem manual reset credits.

## Screenshots

These screenshots were generated from the 1.0 source code using fictional account data. They show the Traditional Chinese interface.

| Detailed view | Grid view |
| --- | --- |
| ![Detailed view](docs/previews/detailed.png) | ![Grid view](docs/previews/grid.png) |

![Compact view](docs/previews/compact.png)

[View the manual reset credit details screenshot](docs/previews/reset-credit-details.png)

## Download and launch

### Requirements

- A Windows x64 computer.
- Codex installed, with the account you want to monitor signed in under the same Windows user.
- A network connection to query current usage and send Telegram notifications.

The Windows packages include the .NET runtime. You do not need to install the .NET SDK to use the prebuilt apps.

### Desktop app

1. Download [CodexQuotaHub-1.0-win-x64.zip](https://github.com/kaywkkkkk/codex-quota-hub/releases/download/v1.0/CodexQuotaHub-1.0-win-x64.zip) from [Releases](https://github.com/kaywkkkkk/codex-quota-hub/releases).
2. Extract the ZIP to a folder of your choice and run `CodexQuotaHub.exe`.
3. The app reads the current account and saves verified results. On first launch, no historical snapshots are shown until a query succeeds.

Locally prepared packages are in `release-assets/`. Each ZIP also includes documentation, the MIT License, and third-party notices.

Closing the main window minimizes the app to the system tray and keeps monitoring active. Select **Exit** from the tray menu to stop it completely. Only one desktop app instance can run per Windows user.

### Basic controls

- **Refresh now** queries the current account's latest usage.
- **Pause monitoring** pauses automatic queries and Telegram due alerts. Resume monitoring to continue.
- **Settings** lets you change the refresh interval, theme, language, and Telegram configuration.
- Accounts in Detailed view can be collapsed individually. Switching views does not trigger a new query.
- Use the pencil button beside an account to edit its alias. Save an empty alias to remove it.
- Click a manual reset count to view credit details. A count of 0 means no credits are available; “— / Unavailable” means the source did not provide a count.

After you switch accounts or Workspaces in Codex, the next query updates only the current account. Other accounts continue to show their last saved snapshots.

### Telegram due alerts

1. Send `/newbot` to [@BotFather](https://t.me/BotFather) in Telegram to create a bot and obtain its bot token.
2. Enter the token in Codex Quota Hub's **Settings**. Send the short code shown in Settings to your bot in Telegram, then select **Find chat**, or enter a numeric chat ID manually.
3. Select **Send test** and confirm that you receive the message. Check **Enable Telegram due alerts** and save.

Notifications include the account alias, quota window, and scheduled reset time. If the last successful query reported available manual reset credits, that count is also included. Without an alias, the app sends a partially masked email address and does not automatically include the full email address or Workspace ID. Aliases are sent as entered.

The bot token is encrypted using the current Windows user's data protection mechanism before it is stored in the local database. Do not share your token or database. Select **Clear setup** and save to remove the token and chat ID. Bots with an existing webhook cannot use automatic chat lookup; enter a chat ID manually or use a dedicated bot.

The app must remain running and online to send notifications. Alerts cannot be sent on time while the computer is asleep or the app is closed. Previously queued, unsent alerts are processed after it resumes. Each reset cycle is queued only once, with retries for network failures. A message can still be sent again if it was delivered but the success record could not be saved.

## Data and display behavior

- Default database: `%LOCALAPPDATA%\CodexQuotaHub\meter.db`.
- Existing installations automatically keep using `%LOCALAPPDATA%\CodexMeter\meter.db` when the new default database does not exist. Saved accounts, aliases, and settings are preserved without moving the database. `--db` still takes priority.
- Stored data includes email addresses, Workspace identifiers, plans, usage, reset times, daily tokens, account aliases, and app settings. Do not upload your personal database to GitHub.
- Reset countdowns use the last confirmed reset time. After that time passes, a progress bar may show 100%, but this is a display state: refresh to confirm the latest available quota. The app does not change saved usage or infer the next reset cycle.
- Missing daily token data is shown as “—”, not 0. Daily totals include only accounts with data, and source dates are preserved as reported.
- Expiration times are not inferred when individual reset credit details are missing.
- Failed queries, signed-out states, and identity changes during a query do not overwrite existing snapshots.

Usage is queried through the local `codex.exe app-server` process. The app looks for Codex in its local installation directory. If discovery fails, specify a path:

```powershell
.\CodexQuotaHub.exe --codex "C:\path\to\codex.exe"
.\CodexQuotaHub.exe --db "C:\path\to\meter.db"
```

## Command-line tool

Download and extract [CodexQuotaHub-Cli-1.0-win-x64.zip](https://github.com/kaywkkkkk/codex-quota-hub/releases/download/v1.0/CodexQuotaHub-Cli-1.0-win-x64.zip), then run these commands from its folder:

```powershell
.\CodexQuotaHub.Cli.exe refresh
.\CodexQuotaHub.Cli.exe list
.\CodexQuotaHub.Cli.exe watch --interval 300
```

| Command | Purpose |
| --- | --- |
| `refresh` | Query the current identity and usage, then save a snapshot after confirming consistency. |
| `list` | Read local snapshots and display countdowns without connecting to App Server. |
| `watch` | Query periodically, every 60 seconds by default. Press Ctrl+C to stop. |
| `help` | Show available arguments. |

Use `--db <path>` to select a database, or `--codex <path>` with `refresh` or `watch` to select a Codex executable. The `watch` option `--interval` accepts 15–3600 seconds. The CLI and desktop app use the same database by default.

## Build from source

Development requires Windows and the [.NET 10 SDK](https://learn.microsoft.com/en-us/dotnet/core/install/windows). Run these commands from the project root:

```powershell
dotnet build CodexQuotaHub.slnx -c Release
dotnet run --project src/CodexQuotaHub.Desktop
dotnet run --project src/CodexQuotaHub.Cli -- list
```

Run the existing core tests:

```powershell
dotnet run --project tests/CodexQuotaHub.Core.Tests -c Release
```

Rebuild the Windows x64 release packages:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

The script publishes self-contained, single-file desktop and CLI executables, packages them with documentation and licenses in `release-assets/`, and creates SHA-256 checksums for the ZIP files. Native libraries required by the apps and SQLite are bundled in the executables.

`tests/CodexQuotaHub.Desktop.Visual` provides tools to verify and capture WPF views using fictional data. Regenerating the README screenshots also requires Python and Pillow:

```powershell
python -m pip install Pillow
python tests/render_view_previews.py
```

## Project structure

```text
src/                  Core, desktop, and CLI source code
tests/                Core tests and WPF visual verification tools
docs/previews/        Version 1.0 screenshots
docs/third-party/     Third-party licenses and notices
scripts/publish.ps1   Windows release packaging script
release-assets/       Local release packages (excluded from Git)
Directory.Build.props Shared version and author information
CodexQuotaHub.slnx    Build solution
README.md             English documentation
README.zh-TW.md       Traditional Chinese documentation
README.zh-CN.md       Simplified Chinese documentation
CHANGELOG.md          English changelog
LICENSE               MIT License
```

See the [release guide](docs/RELEASE.md) for preparing GitHub source files and Release attachments. See the [changelog](CHANGELOG.md) for the 1.0 feature list.

## Version 1.0 limitations

- Release packages are available for Windows x64 only. There is no installer, launch-at-startup option, or automatic restart after a crash.
- Only the currently signed-in account is queried. Historical account snapshots are not refreshed independently in the background.
- Quotas, daily tokens, and reset credit details depend on the data returned by Codex. Missing fields are shown as unknown.
- Changes to App Server interfaces or data formats after a Codex update may affect queries.
- Running under a different Windows user or in an isolated environment may prevent access to the current signed-in identity.

## License

Copyright (c) 2026 kaywkkkkk. Codex Quota Hub is licensed under the [MIT License](LICENSE). Third-party components bundled in the self-contained Windows executables retain their own licenses. See [third-party notices](THIRD-PARTY-NOTICES.md) for details.
