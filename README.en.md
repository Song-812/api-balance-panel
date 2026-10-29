# API Balance Panel

English | [简体中文](README.md)

A tiny 300×250 always-on Windows panel that shows your account balances for DeepSeek, OpenAI, Gemini, xAI, MiMo, Qwen, GLM, Kimi and MiniMax at a glance.

| Light | Dark |
|---|---|
| ![Light](docs/api.png) | ![Dark](docs/api-dark.png) |

## Why

Checking balances means opening nine dashboards a day. This panel puts them on one 300×250 card board: double-click to open, refresh every minute, see what is running low at a glance.

![Drag to reorder](docs/drag.gif)

## Features

- **Nine providers on one screen**: DeepSeek, OpenAI, Gemini, xAI, MiMo, Qwen, GLM, Kimi, MiniMax.
  Six of them use an API key; three (OpenAI / Gemini / MiMo) use an embedded web login. xAI needs a Team ID, Qwen needs an Aliyun AccessKey pair, Kimi and MiniMax have a China/Global region switch.
- **Drag to reorder**: press and hold ~0.3 s, cards make room live; release to save, Esc to cancel.
- **Follows the system light/dark theme**, 300×250 by default with system rounded corners.
- **F5 to refresh**, plus automatic refresh every minute.
- **Secrets stay local**: encrypted with Windows DPAPI, never uploaded, never logged, never exported.
- **Web login through WebView2**: no cookie export, no password autofill, no DevTools.

## Quick start

1. Download `api-balance-panel-v1.0.1.zip` from [Releases](https://github.com/Song-812/api-balance-panel/releases/latest)
2. Extract anywhere — **do not move the exe alone**, it needs the dll and js files next to it
3. Run `API余额面板.exe`
4. Click the gear icon → enter keys or click the login button → Save

![Settings](docs/settings.png)

### Requirements

| Need | Notes |
|---|---|
| Windows x64 | only tested on Win11 26200 |
| .NET Framework 4.8 | included in Win10 1903+ and Win11 |
| WebView2 Runtime | included in Win11; otherwise install the Evergreen Runtime from [Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/) |

## First-run notes

- Keys are encrypted for the current Windows user — **moving to another PC or user account means re-entering them**
- Four cards are visible by default; scroll for the rest. Order is adjustable by dragging and is persisted
- Turning off "show on panel" for a provider stops its refresh and releases the web session; the saved settings are kept
- The app follows the Windows system proxy; **with no system proxy it connects directly**

## Build from source

Requires .NET Framework 4.8 (bundled with Windows) and PowerShell. Visual Studio is not needed.

```powershell
cd src
.\Build.ps1
```

The script runs the full test suite first, then builds into `build\API余额面板\` at the repository root.
Use `-OutputRoot` / `-TestRoot` to redirect build and temporary output.

## Layout

    src/        source (C# / WPF plus a little JS)
    src/lib/    WebView2 runtime DLLs and their licenses
    docs/       screenshots
    build/      build output (gitignored)

## Tests

328 automated tests: Core 156, ViewState 10, SafeDiagnostics 3, UI 104, Browser DOM 55.
All of them use synthetic fixtures, in-memory HTTP handlers and local test pages — no real keys, no user configuration.

## Known limitations

- The UI is Chinese only
- Only tested on Win11 26200 at 100% scaling; other Windows versions, DPI settings and multi-monitor setups are **untested**
- Balances come from provider APIs or web pages and may break when a provider changes
- A personal tool, not an official product; no guarantee on data accuracy

## License

MIT, see [LICENSE](LICENSE).
WebView2 files under `src/lib/` are copyright Microsoft, see `WebView2-LICENSE.txt`.
