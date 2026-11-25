# SV Time Tracker

[![Build & Release Windows x64](https://github.com/softwarevantage/Sv.TmeTracker/actions/workflows/release.yml/badge.svg)](https://github.com/softwarevantage/Sv.TmeTracker/actions/workflows/release.yml)

A sleek, lightweight Windows 11 desktop time tracker with a frosted-glass dark interface, always-on-top floating timer, pause/resume support, system tray controls, global keyboard shortcuts, gentle hourly chimes, and local CSV reporting.

---

## For Everyday Users

### What is SV Time Tracker?
SV Time Tracker helps you stay focused and see where your time goes throughout the day—without accounts, subscriptions, or cloud tracking. Everything is stored privately right on your computer.

### Key Features
- **One-Click Time Tracking**: Pick a task (or type a new one right from the timer) and click **Start Timer**.
- **Pause & Resume**: Need a quick break? Click **Pause** to freeze the clock without ending your session, then **Resume** when you're back. Paused time is never counted toward your session total.
- **Compact Mini-Timer Mode**: Click the **◰** button in the top-right title bar to shrink the window into a tiny floating bar that stays out of your way while you work.
- **Always on Top**: Click the **📌** pin button to keep the timer floating above your other windows.
- **Customizable Frosted Transparency**: Adjust how transparent or solid the frosted-glass background looks using the **Backdrop Opacity** slider in **Settings**.
- **System Tray Control**: Minimizing the window tucks it neatly into your Windows system tray (near the clock). Right-click the tray stopwatch icon anytime to Start, Pause/Resume, Stop, Show/Hide, or Quit.
- **Gentle Chime Reminders**: Optionally plays a soft chime at the top (`:00`) and half-hour (`:30`) mark while a timer is running, with volume control, custom `.wav`/`.mp3` sound support, and **Quiet Hours** (e.g., `22:00` to `07:00`).
- **History & Reports**: View your **Sessions**, **By Task** breakdown with color-coded progress bars, and **By Day** totals, edit session notes, or export everything to a **CSV** spreadsheet for Excel.

### How to Install & Run
1. Download the latest Windows x64 zip (`SV-Time-Tracker-v*-win-x64.zip`) from the **[Latest Release](https://github.com/softwarevantage/Sv.TmeTracker/releases/latest)** page, browse **[All Versions](https://github.com/softwarevantage/Sv.TmeTracker/releases)**, or grab the latest CI build from **[GitHub Actions](https://github.com/softwarevantage/Sv.TmeTracker/actions/workflows/release.yml)**.
2. Extract the zip and double-click **`SV Time Tracker.exe`** to launch.
   - *Note*: The release zip is **self-contained**, so no extra .NET installation is required. (If you build a framework-dependent version yourself, it requires the [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0).)
3. Three starter tasks (*Deep Work*, *Meetings & Syncs*, *Code Review & Planning*) are created automatically so you can start tracking immediately.

### Default Keyboard Shortcuts
These work even when the app is minimized to the system tray (and can be customized in **Settings**):
- **`Ctrl + Shift + T`** — Start / Stop timer
- **`Ctrl + Shift + H`** — Show / Hide window
- **`Ctrl + Shift + W`** — Open Work Items (Tasks)

### Privacy & Where Your Data Lives
- **100% Local & Offline**: Your tasks, time entries, and settings are stored locally in a single SQLite file at:
  `%APPDATA%\Sv.TimeTracker\timetracker.db`
- Back up or move your history anytime by copying that file.

---

## For Developers

### Tech Stack & Architecture
- **Framework**: `.NET 10` (`net10.0-windows`), WPF + WinForms `NotifyIcon` interop
- **UI & Styling**: Native Windows 11 DWM Acrylic/Mica backdrop (`dwmapi.dll`), `.NET 10` `ThemeMode="Dark"` Fluent controls, custom `WindowChrome` title bar, tabular numerals
- **Pattern**: MVVM (`CommunityToolkit.Mvvm`) + Dependency Injection (`Microsoft.Extensions.DependencyInjection`)
- **Database**: Local SQLite (`Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlite3`) in WAL mode with foreign keys and cascading deletes
- **Testing**: `xUnit` + `Moq` with isolated temporary SQLite databases and `AdjustableTimeProvider`

### Project Structure
```text
Sv.TimeTracker.sln
├── Sv.TimeTracker/
│   ├── Assets/app.ico                  # Multi-resolution app & taskbar icon
│   ├── Controls/MainTimerControl.*     # Standard & Compact Mini-Timer UI
│   ├── Data/
│   │   ├── DatabaseHelper.cs           # ISqliteConnectionFactory, WAL, schema & seeding
│   │   └── Repositories/               # WorkItem, TimeEntry, Settings repositories
│   ├── Models/                         # Domain models, SettingKeys, DurationFormatter
│   ├── Services/                       # Timer, Notification, Hotkey, Export, TrayIcon, Backdrop
│   ├── ViewModels/                     # Main, Timer, WorkItems, History, Settings ViewModels
│   ├── Views/                          # WorkItemsView, HistoryView, SettingsView
│   ├── App.xaml(.cs)                   # Theme resources & DI container startup
│   └── MainWindow.xaml(.cs)            # Frosted single-window shell & tray/hotkey host
└── Sv.TimeTracker.Tests/               # Automated xUnit test suite (25 tests)
```

### Prerequisites
- Windows 10 (build 19041+) or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build, Test & Publish Commands

#### Run in Development
```powershell
dotnet run --project Sv.TimeTracker
```

#### Run Automated Tests
```powershell
dotnet test Sv.TimeTracker.sln -c Release
```

#### Publish for Windows x64 (`SV Time Tracker\SV Time Tracker.exe`)
Framework-dependent (requires .NET 10 Desktop Runtime on target PC):
```powershell
dotnet publish Sv.TimeTracker\Sv.TimeTracker.csproj -c Release -r win-x64 --self-contained false
```

Self-contained single-file (portable, no .NET runtime required):
```powershell
dotnet publish Sv.TimeTracker\Sv.TimeTracker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output path:
`Sv.TimeTracker\bin\Release\net10.0-windows\win-x64\SV Time Tracker\SV Time Tracker.exe`

### Cutting a New Release
1. Bump `<Version>`, `<AssemblyVersion>`, `<FileVersion>`, and `<InformationalVersion>` in `Sv.TimeTracker\Sv.TimeTracker.csproj`.
2. Commit your changes, then create and push a matching tag:
   ```powershell
   git tag v1.0.1
   git push origin main --tags
   ```
3. GitHub Actions will run all tests and automatically publish `SV-Time-Tracker-v1.0.1-win-x64.zip` under **Releases**.

### Automation / Smoke Testing Flags
- `--smoke-test`: Launches the app, initializes DI/SQLite/window chrome, and exits cleanly.
- `SV_TIMETRACKER_DB_PATH`: Environment variable to override the SQLite database path for isolated testing.
