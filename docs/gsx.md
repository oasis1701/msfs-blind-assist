# Access GSX

Access GSX is the screen-reader-friendly interface for GSX Ground Services Pro. It mirrors the GSX in-sim menu and tooltip stream in standard Windows controls, so NVDA, JAWS, and other screen readers can read GSX status without relying on the simulator overlay.

## Opening And Reading GSX

- Open the Access GSX window with Input mode > `Alt+G`.
- Press `F5` inside the Access GSX window to open or reopen the GSX menu.
- Choose GSX menu options with `1` through `9`, `0`, then `A` through `E` for options above ten.
- Press `Esc` to hide the Access GSX window. The GSX service keeps running in the background.
- Press Output mode > `Ctrl+G` to read the latest cached GSX tooltip without opening the Access GSX window.

The Access GSX window has separate read-only fields for connection status, menu options, and the latest tooltip. When the GSX menu is hidden or times out, the menu field shows a prompt telling the user to press `F5` again.

## Settings

Press `C` inside the Access GSX window while a GSX menu is available to open the accessible GSX Settings window. The settings window reads GSX's published settings page, groups controls by GSX section, and exposes toggles, choices, ranges, text fields, action buttons, and read-only information with standard Windows controls.

Setting changes are sent to GSX and persisted to the GSX configuration file. Numeric and text settings are committed as they change and again when the settings window closes, with duplicate writes suppressed.

If GSX has not published its settings page yet, open the GSX menu with `F5`, then press `C` again.

## Active Services

When GSX has two or more services running at the same time, the Access GSX window shows an Active services combo box above the tooltip. Selecting a service chooses which active row drives the tooltip and automatic announcement stream. The selector is hidden when zero or one service is active so it does not clutter the tab order during normal use.

## Automatic Announcements

Access GSX speaks meaningful changes from the tooltip/status feed instead of every small text refresh. It handles:

- Boarding and deboarding progress, with repeated progress suppressed.
- Refuel, baggage, catering, and pushback status.
- Persistent ground connections such as GPU, PCA, and chocks, including periodic timer-only status announcements.
- Completed-service totals.
- Invoices and detailed receipts when GSX exposes receipt data.

The Access GSX window speaks announcements while it is visible. When the window is hidden, background tooltip announcements follow the user's Announcement Settings option for GSX background monitoring.

## Implementation Notes

- `Forms/AccessGSXForm.cs` owns the accessible window, menu shortcuts, active-services selector, and settings-window launch.
- `Forms/GsxSettingsForm.cs` renders editable GSX settings from parsed GSX HTML metadata.
- `Services/GsxService.cs` owns GSX menu/tooltip communication, active service selection, announcement throttling, invoice/receipt parsing, and settings persistence.
- `Settings/UserSettings.cs` stores the app-level `GsxBackgroundMonitoring` option; GSX's own settings are persisted to GSX configuration.
