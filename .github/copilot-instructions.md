# SfUi — working agreements for AI agents

SfUi is a Windows/macOS desktop workspace for the **Salesforce CLI (`sf`)**: SOQL/Apex/command tabs, org info & compare, data import/export, backup & restore, ETL migration, source editor, org management, scratch orgs, REST explorer — plus a shared report export (CSV / Markdown / HTML / Excel / PDF) on every window.

## Solution layout (.NET 9)

| Project | Target | Role |
| --- | --- | --- |
| `src/SfUi.Core` | `net9.0` | Domain services, stores, CLI/REST clients, reports. **No UI types.** |
| `src/SfUi.Presentation` | `net9.0` | ViewModels + dialog/service abstractions shared by both UIs. **No WPF/Avalonia types.** |
| `src/SfUi.App` | `net9.0-windows` (WPF) | Views, dialogs, DI composition, `Localization.cs` (XAML markup extension). |
| `src/SfUi.Avalonia` | `net9.0` | Avalonia shell with the same ViewModels. |
| `src/SfUi.Etl` | `net9.0` | ETL engine, connectors, expressions. |
| `tests/SfUi.Tests` | `net9.0` | xUnit. Currently **1394 tests** — keep them green. |

`Nullable` and `ImplicitUsings` are enabled everywhere.

## Commands

```powershell
dotnet build SfUi.sln -c Debug --nologo -v q
dotnet test tests\SfUi.Tests\SfUi.Tests.csproj -c Debug --nologo
dotnet run --project src\SfUi.App\SfUi.App.csproj -- --smoke        # exit code must be 0
dotnet run --project src\SfUi.Avalonia\SfUi.Avalonia.csproj -- --smoke
```

Stop a running `SfUi` process before building (`Get-Process SfUi | Stop-Process -Force`), otherwise the exe is locked.

## Non-negotiables

1. **Localization.** Never hardcode user-visible text. Add every key to **all four** dictionaries — `src/SfUi.Core/Localization/UiText.{En,Ja,Zh,Ko}.cs` (identical key sets) — and consume it with `loc:Tr Key` in WPF XAML or `UiText.T("Key")` / `UiText.T("Key", args)` in C#. Tests fail if the key sets differ or a referenced key is missing.
2. **MVVM.** ViewModels are `partial` + `ObservableObject` from CommunityToolkit.Mvvm. Use `[ObservableProperty]` / `[RelayCommand]` generators (never reference the backing field directly), and constructors for DI.
3. **Both shells stay in sync.** A feature added to WPF must be mirrored in Avalonia (same ViewModel, same command names, same `AutomationProperties.AutomationId`), unless it is explicitly Windows-only.
4. **Avalonia is pinned to 11.x.** `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Controls.DataGrid`, `Avalonia.AvaloniaEdit` must stay on the same 11.x line; `.github/dependabot.yml` groups them and ignores major updates (Avalonia 12 breaks `IClipboard`/`IBinding`, and AvaloniaEdit/DataGrid have no 12 support yet).
5. **Tests are required** for new Core/Presentation logic, and they must never need a real org, network, or the user's `data/` folder. Use `NullReportDialog` and the existing fakes.
6. **Commits** are a single line in English imperative (`Add report export to org info window`), pushed to `main`; CI (10 checks) must be green.
7. **Dependabot PRs** are only merged when their own CI run is green; major bumps of Avalonia packages are intentionally rejected.

## Where to look

- Feature plans: `docs/*-window-plan.md`, `docs/report-export-plan.md`, `docs/publish-plan.md`
- User manual (4 languages): `docs/manual/`
- Packaging / Store / macOS signing: `packaging/`, `docs/publish-plan.md`, `docs/macos-signing-notarization.md`

## Skills (load on demand)

- `.github/skills/sfui-verify` — build → tests → smokes → UI E2E verification loop
- `.github/skills/sfui-add-ui-text` — add/rename localized strings safely
- `.github/skills/sfui-report-export` — add report export to a window or tab
- `.github/skills/sfui-release` — release, MSIX/Store packaging, GitHub upload
- `.github/skills/sfui-ui-e2e` — write and run UIA automation checks
