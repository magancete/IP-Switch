# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[semantic versioning](https://semver.org/).

## [Unreleased]

## [1.0.2] - 2026-10-22

### Fixed

- Desktop shortcuts of profiles (*Create desktop shortcut*) stopped working after moving the executable. When IP Switch starts from a new location, it now points the shortcuts whose executable no longer exists to the new one (shortcuts to another copy that still exists are left alone) and says how many it updated, just as it already did with the *Start with Windows* task.

## [1.0.1] - 2026-10-22

### Added

- **One-click updates**: *Settings → About → Check now → Update now* downloads the new release, verifies its SHA-256 against the published `IPSwitch.exe.sha256`, swaps the executable in place and restarts. If anything fails, the running version is left untouched.
- Additional default gateways per profile, each with an optional metric, as in the Windows advanced TCP/IP settings (*Advanced* section of the editor). "Save as profile" and the rollback snapshot keep them.

### Changed

- The profile editor is laid out horizontally (two columns) and never exceeds the screen work area.
- The main window opens at a size that fits the screen (at most 90 % of the work area, 960 × 640 by default), can be made smaller (minimum 540 × 420) and remembers its last size, position and maximized state.
- The active profile is marked with an "Active" badge (and a bold name) next to its name instead of a green check that overlapped the label color; the invalid-profile warning moved next to the name too.
- On narrow windows the profiles table also hides the adapter and IP columns, and *Add profile* shows only its icon, so the action buttons always fit.

### Fixed

- Opening the `.exe` while the app was running in the tray asked for administrator rights again instead of bringing it to the front. The executable now starts unelevated: it hands the request to the running instance (no UAC prompt) and only restarts elevated when no instance is running. Desktop shortcuts (`--apply`) no longer prompt either while the app is running.
- *Start with Windows* did not always start the app: the scheduled task now has no 72-hour limit, also starts on battery power, waits 10 seconds after sign-in, and is repaired automatically at every start if it is missing or points to an old copy of the executable. Errors when changing the option now show the Windows message.
- The tray icon's right-click menu used the classic WinForms look: it is now a Fluent (Windows 11) menu with icons that follows the app theme.

## [1.0.0] - 2026-10-14

Initial release.

- Portable single executable (~3 MB) on .NET Framework 4.8 for Windows 10 (1903+) and Windows 11.
- IPv4 profiles per adapter: static IP or DHCP, mask, gateway with metric, DNS, additional IP addresses, color and notes.
- Safe apply: connectivity check with an automatic rollback countdown.
- Home page with the state of every adapter and actions to renew DHCP, restart, enable/disable, hide and flush DNS.
- Compact view, tray quick panel and tray menu.
- Global hotkeys, automatic apply per WiFi network, command line (`--apply`, `--minimized`, `--compact`) and desktop shortcuts.
- Import and export of profiles.
- English and Spanish UI; light, dark and system themes.
- Single instance, optional start with Windows, optional update check.
- Data stored in `%AppData%\IPSwitch`.

[Unreleased]: https://github.com/magancete/IP-Switch/compare/v1.0.2...HEAD
[1.0.2]: https://github.com/magancete/IP-Switch/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/magancete/IP-Switch/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/magancete/IP-Switch/releases/tag/v1.0.0
