---
description: "Use when adding or fixing xUnit tests in tests/SfUi.Tests. Covers fakes, localization parity tests, language-independent assertions and flakiness rules."
applyTo: "tests/**"
---
# Test rules (`SfUi.Tests`)

- **xUnit + Microsoft.NET.Test.Sdk.** One test class per subject (`XxxViewModelTests`, `XxxStoreTests`, `ReportExportTests`). Names describe behaviour: `ExportReport_BuildsOneSectionPerLoadedTab`.
- **No real org, no network, no user data.** Use the fakes (`FakeDialogs`, `FakeFiles`, `FakeDispatcher`, `FakeProtector`, `NullReportDialog`, fake `sf` scripts) and temporary directories under `Path.GetTempPath()` that are deleted in `Dispose`.
- **Language independence**: assert with `UiText.T("Key")` instead of Japanese/English literals, so tests pass under any UI language.
- **Guarded tests**: `UiTextTests.AllLanguages_HaveSameKeys` and `LocalizationUsageTests` fail when a key is missing or only added to some languages — add keys to En/Ja/Zh/Ko in the same change.
- **Never race the filesystem/process.** When a fake CLI writes logs, poll with the existing `WaitForCalls(...)` helper (do not read once right after starting the process).
- **Report export tests** write to a temp folder, assert the expected files exist (`*.csv`, `*.md`, `*.html`, `*.xlsx`), and avoid PDF when no browser is available.
- **ViewModels** are constructed directly with fakes; do not boot the WPF/Avalonia app.
- Run the suite with:
  `dotnet test tests\SfUi.Tests\SfUi.Tests.csproj -c Debug --nologo`
  Everything must stay green (currently 1095 tests) before a commit.
