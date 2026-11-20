# Contributing to reController

Thanks for helping! Bug reports, feature ideas, controller compatibility reports and code are all welcome.

## Reporting a bug

1. Run `reController.exe --selftest report.txt` and attach `report.txt`.
2. Attach the latest log from `%LocalAppData%\reController\logs` (it contains no personal data beyond device names and file paths — check before posting).
3. Describe the controller (model, USB/Bluetooth), the game, and the steps to reproduce.

## Development setup

* Windows 10/11 x64, .NET 8 SDK, any C# IDE.
* ViGEmBus installed if you want to exercise virtual-controller output.
* `dotnet test` must pass; CI builds with warnings treated as errors (`CI=true`).

```powershell
dotnet build
dotnet test
dotnet run --project src/ReController.App
```

## Architecture rules

* **`ReController.Core` has no OS dependencies.** Anything that decides *what* to output belongs there and gets unit tests. Platform code only reads devices and performs output.
* **The engine thread is sacred.** No allocations in the per-tick path, no locks shared with the UI, no blocking I/O. Talk to it through `RemapService` commands.
* **Outputs are desired state, never events.** New output types must be expressible in `OutputFrame` so they are released automatically.
* **Persisted enums are stored by name.** Never rename a member of `Control`, `ActivationMode`, etc. without adding a migration in `ProfileStore.Migrate`, and bump `GameProfile.CurrentSchemaVersion`.
* **Anything elevated is allow-listed.** The `--elevated-batch` helper only runs verbs listed in `ElevatedBatch.AllowedVerbs` and resolves the HidHide CLI path itself.

## Code style

* Follow `.editorconfig`; nullable reference types are on everywhere.
* MVVM via CommunityToolkit.Mvvm source generators (`[ObservableProperty]`, `[RelayCommand]`).
* Keep XAML styles in `Themes/`; use `DynamicResource` for accent brushes so theme changes apply live.
* Prefer small, focused pull requests with a clear description and tests for engine changes.

## Releasing (maintainers)

1. Update `CHANGELOG.md` and `VersionPrefix` in `Directory.Build.props`.
2. Tag `vX.Y.Z` and push the tag. The *Release* workflow builds the single-file exe, portable zip and installer, and drafts a GitHub release.
