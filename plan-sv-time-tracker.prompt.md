# Plan: Time Tracker Windows App with Frosted Glass UI

## Architecture Decisions
- **Framework:** WinUI 3 (.NET 8) — native Acrylic material, topmost window support
- **Database:** SQLite via `Microsoft.Data.Sqlite.Core` + simple repository pattern
- **Storage:** Local SQLite file in `%APPDATA%\Sv.TimeTracker\timetracker.db`
- **UI Style:** Acrylic (frosted glass) background with Mica title bar

## Database Schema

### Tables
1. **WorkItems** — id, title, description, createdAt, color
2. **TimeEntries** — id, workItemId, startTime, endTime, durationMs, notes, createdAt
3. **Settings** — key, value (for app preferences like lastWorkItemId, topmostEnabled)

## Implementation Steps

### Phase 1: Project Setup
1. Create WinUI 3 Desktop project (`.NET 8` on Windows)
2. Add NuGet packages: `Microsoft.Data.Sqlite.Core`, `Microsoft.UI.Xaml`
3. Configure app icon and basic manifest
4. Set up Acrylic/Mica material resources in `App.xaml`

### Phase 2: Data Layer
5. Create SQLite database initialization (tables + migrations)
6. Implement repository classes:
   - `WorkItemRepository` — CRUD operations
   - `TimeEntryRepository` — CRUD + time range queries
   - `SettingsRepository` — key-value store
7. Add sample data seeding

### Phase 3: Work Item Management UI
8. Create Work Items page/view:
   - List of work items with color badges
   - Add/Edit work item dialog
   - Delete confirmation
   - "Set as Active" button per work item
9. Bind to SQLite repositories

### Phase 4: Timer Core
10. Create `TimerService` class:
    - Start timer (sets startTime, marks work item active)
    - Stop timer (calculates duration, saves entry)
    - Running state tracking
11. Handle timer persistence across app restarts (resume running timer)

### Phase 5: Main Timer UI
12. Create main window with frosted glass:
    - Large elapsed time display (HH:MM:SS)
    - Active work item indicator
    - Start/Stop button (prominent, with state change)
    - "Always on Top" toggle button
13. Implement window topmost behavior using `AppWindow` or `Window.Activate()`

### Phase 6: History & Reports
14. History page:
    - List of past time entries with filters (date range, work item)
    - Duration display
    - Notes preview
15. Daily totals view:
    - Group entries by day
    - Total hours per day
16. Work item totals:
    - Aggregate time per work item
    - Visual progress bars or charts

### Phase 7: Export
17. Export functionality:
    - CSV export (date range, work item filters)
    - Option to export all data
    - File save dialog

### Phase 8: Polish
18. Settings page:
    - Default work item
    - Export format preferences
    - Window size/position persistence
19. App icon and taskbar integration
20. Tray icon option (minimize to system tray)

## Relevant Files
- `Sv.TimeTracker/Sv.TimeTracker.csproj` — Project file
- `Sv.TimeTracker/App.xaml` / `App.xaml.cs` — App resources, Acrylic materials
- `Sv.TimeTracker/MainPage.xaml` — Main timer UI
- `Sv.TimeTracker/Data/` — Database models and repositories
- `Sv.TimeTracker/Services/TimerService.cs` — Timer logic
- `Sv.TimeTracker/Views/WorkItemsView.xaml` — Work item management
- `Sv.TimeTracker/Views/HistoryView.xaml` — History and totals
- `Sv.TimeTracker/Models/` — Domain models

## Verification
1. Build succeeds with 0 errors
2. App launches with frosted glass UI
3. Timer starts/stops correctly, elapsed time updates every second
4. Work items can be created, edited, deleted, and set as active
5. Time entries persist in SQLite (verify with SQLite browser)
6. History shows past entries with correct totals
7. Export produces valid CSV
8. "Always on Top" toggle works (window stays above others)
9. App resumes running timer after restart

## Decisions
- **Single-user, local storage only** — No cloud sync
- **Plain custom work items** — No external API integration
- **WinUI 3** — Best native support for Acrylic/Mica materials and topmost windows
- **SQLite** — Lightweight, no server required, easy to backup
- **Export to CSV** — Simple format, works with any spreadsheet/time tracking tool

## Further Considerations (Confirmed Decisions)

### System Tray
- App should minimize to system tray instead of taskbar (recommended for topmost floating behavior)
- Tray icon shows running state (stopwatch icon when active, idle icon when stopped)
- Right-click tray menu: Start/Stop, Show/Hide, Quit
- Configurable in Settings screen

### Hourly Ding Notification
- **Default:** Ding sound at top of every hour and half-hour mark
- **Configurable in Settings:**
  - Enable/disable hourly ding
  - Choose sound file (built-in ding, or custom WAV/MP3)
  - Set volume level
  - Quiet hours (disable during specific time range, e.g., 22:00–07:00)
- Only plays when timer is running

### Keyboard Shortcuts
- **Default hotkey:** `Ctrl+Shift+T` — toggle timer start/stop
- **Configurable in Settings:**
  - Enable/disable global hotkeys
  - Custom hotkey combinations (any valid combination)
  - Hotkey to show/hide window (`Ctrl+Shift+H` by default)
  - Hotkey to open Work Items management (`Ctrl+Shift+W` by default)
- Global hotkeys work even when app window is hidden or minimized

### Settings Screen
All configurable options consolidated in a single Settings screen:
- **General:**
  - Default work item (auto-select on app start)
  - Minimize to system tray on close
  - Window size/position persistence
- **Timer:**
  - Hourly ding enable/disable
  - Sound file selection
  - Volume level
  - Quiet hours start/end
- **Shortcuts:**
  - Toggle timer hotkey
  - Show/hide window hotkey
  - Open Work Items hotkey
  - Enable/disable global hotkeys
