# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[semantic versioning](https://semver.org/).

## [Unreleased]

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

[Unreleased]: https://github.com/magancete/IP-Switch/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/magancete/IP-Switch/releases/tag/v1.0.0
