# Contributing to IP Switch

Thanks for your interest! Bug reports, ideas and pull requests are welcome. *¿Prefieres escribir en español? Adelante: issues y PRs en español también son bienvenidos.*

## Reporting bugs and suggesting improvements

Open the [issue chooser](https://github.com/magancete/IP-Switch/issues/new/choose) (also available in the app under **Settings → About**) and pick a category:

| Form | Use it for | Label |
|---|---|---|
| 🐞 Bug report | Something does not work as expected | `bug` |
| ✨ Feature request | A new feature or an improvement | `enhancement` |
| 📝 Documentation | Wrong, missing or unclear docs | `documentation` |
| ❓ Question | How to do something | `question` |

Do not include sensitive network data (public IPs, company network names). Security vulnerabilities go through [SECURITY.md](SECURITY.md), never as public issues.

## Development setup

- Windows 10/11, [.NET SDK](https://dotnet.microsoft.com/download) 8+ and the .NET Framework 4.8 targeting pack.
- Visual Studio 2022 17.13+ (`IPSwitch.slnx`) or any editor plus the CLI.

```powershell
./build.ps1                     # resource checks + Release build (0 warnings) + tests → artifacts\IPSwitch.exe
dotnet test tests/IPSwitch.Tests/IPSwitch.Tests.csproj -c Release
./tools/Check-Resources.ps1     # localization keys and icons
```

Try the UI without UAC and with throwaway data (network changes will fail):

```powershell
$env:IPSWITCH_NO_ELEVATION = '1'; $env:IPSWITCH_DATA_DIR = "$env:TEMP\ipswitch-dev"; .\artifacts\IPSwitch.exe
```

## Guidelines

- **Keep it portable and small.** The target is a single ~3 MB `.exe` on .NET Framework 4.8. Do not add NuGet packages without a strong reason; prefer what the framework ships.
- **net48 BCL.** The syntax is modern C#, but APIs such as `string.Contains(char)` or `Environment.ProcessPath` do not exist. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#net48-limitations).
- **UI texts** go in both `src/IPSwitch/Resources/Strings.en.xaml` and `Strings.es.xaml` (never literals in XAML or code).
- **XML documentation** is mandatory for public members; the build treats warnings as errors. Code comments and docs are written in English.
- **Tests:** put non-UI logic in pure functions under `src/IPSwitch/Services` and cover it in `tests/IPSwitch.Tests`.
- **Never block the UI thread** with adapter enumeration, WMI, `netsh` or `schtasks`; use `Task.Run`.
- **Icon:** edit the vector `src/IPSwitch/Resources/AppIcon.xaml` and run `./tools/Export-Icon.ps1` to regenerate `favicon.ico` (keep `docs/images/icon.svg` in sync).
- **Responsive layout:** check the windows at 600 px width and the compact view resized.
- Changes that touch the network configuration should be verified with `tests/Integration/Invoke-AdminIntegrationTests.ps1` on a test adapter.

## Pull requests

1. Fork the repository and create a branch from `master`.
2. Make your change and run `./build.ps1`.
3. Update `CHANGELOG.md` under **Unreleased**.
4. Open the pull request and fill in the [template](.github/pull_request_template.md).

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE).
