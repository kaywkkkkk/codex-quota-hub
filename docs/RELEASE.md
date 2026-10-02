# Codex Quota Hub 1.0 Release Guide

This folder contains the GitHub release files for the first version of Codex Quota Hub.

Product subtitle: **Multi-account quota overview and reset alerts for Codex.**

## GitHub source repository

Place the files not excluded by `.gitignore` at the root of the GitHub repository. These include the source code, tests, README files, changelog, build script, screenshots, and license notices.

`release-assets/`, `dist/`, `bin/`, and `obj/` contain local build or packaging output and are excluded by `.gitignore`. Keep personal databases, bot tokens, sign-in data, and local development environments out of the repository.

## GitHub Release attachments

The suggested tag for the first release is `v1.0`, with the title `Codex Quota Hub 1.0`. Use the draft release notes below and attach these files from `release-assets/`:

- `CodexQuotaHub-1.0-win-x64.zip`: the desktop executable, `CodexQuotaHub.exe`, and documentation.
- `CodexQuotaHub-Cli-1.0-win-x64.zip`: the command-line executable, `CodexQuotaHub.Cli.exe`, and documentation.
- `SHA256SUMS.txt`: SHA-256 checksums for both ZIP files.

The Windows executables include the .NET runtime and are ready to run after extraction. Distribute the ZIP files as Release attachments. Each package includes English, Traditional Chinese, and Simplified Chinese README files, the English changelog, the MIT License, and third-party notices.

## Draft release notes

Codex Quota Hub 1.0 is the project's first public release, providing a Windows x64 desktop usage monitor and a command-line tool.

**Multi-account quota overview and reset alerts for Codex.**

Features include remaining Codex quota, reset countdowns, account and Workspace snapshots, daily token usage for the last 7 days, manual reset credit details, three views, theme and language options, and Windows system tray and Telegram due alerts.

Download and extract the desktop ZIP, then run `CodexQuotaHub.exe`. For the command-line package, run `CodexQuotaHub.Cli.exe help` to view usage instructions. No separate .NET installation is required. Use the apps under the same Windows user that is signed in to Codex.

Codex Quota Hub is licensed under the MIT License. Copyright (c) 2026 kaywkkkkk. See the README for detailed instructions and limitations.

## Rebuild the release packages

Install the .NET 10 SDK on Windows, then run these commands from the project root:

```powershell
dotnet build CodexQuotaHub.slnx -c Release
dotnet run --project tests/CodexQuotaHub.Core.Tests -c Release
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

The packaging script regenerates both Windows ZIP files, their unpacked app folders, and the checksums. It does not upload files or create a GitHub Release.
