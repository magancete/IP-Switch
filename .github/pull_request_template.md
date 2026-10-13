<!-- Thanks for contributing · ¡Gracias por contribuir! -->

## Description · Descripción

<!-- What changes and why? Link the issue if there is one: "Closes #123". -->

## Type of change · Tipo de cambio

- [ ] 🐞 Bug fix · Corrección de error
- [ ] ✨ New feature · Nueva funcionalidad
- [ ] ♻️ Refactor (no behavior change) · Refactorización
- [ ] 📝 Documentation · Documentación
- [ ] 🔧 Build / CI

## Screenshots · Capturas

<!-- Required if the UI changes (before / after, light and dark theme when relevant). -->

## Checklist

- [ ] `./build.ps1` finishes with no errors or warnings (includes `Check-Resources` and the tests).
- [ ] New UI texts are in **both** `Resources/Strings.en.xaml` and `Strings.es.xaml` (no literals in XAML or code).
- [ ] New public members have XML documentation (`///`); comments and docs are in English.
- [ ] New non-UI logic has tests in `tests/IPSwitch.Tests`.
- [ ] No NuGet packages added without justification (the goal is a ~3 MB portable `.exe` on .NET Framework 4.8).
- [ ] Tested in the real UI (and with `tests/Integration` if it touches the network configuration).
- [ ] The view adapts to narrow windows (600 px) and to the compact view.
- [ ] `CHANGELOG.md` updated under "Unreleased".
