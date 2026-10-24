---
description: "Use when adding or changing code in src/SfUi.Core (services, stores, CLI/REST clients, reports, ETL interop). Covers layering, atomic JSON stores, error handling and localization rules."
applyTo: "src/SfUi.Core/**"
---
# SfUi.Core rules

- **No UI dependencies.** Core is `net9.0` and is referenced by both shells; never add WPF/Avalonia types or `MessageBox`-style APIs.
- **Paths come from `AppPaths`** (portable `<exe>/data`, repo `data/` during development, `%APPDATA%\SfUi` on Windows, `~/Library/Application Support/SfUi` on macOS). Never hardcode paths or `Environment.SpecialFolder` usage outside `AppPaths`.
- **Persistence uses JSON stores** built on the atomic-write helper (temp file + replace) with `.bak` fallback. Follow the existing store pattern (`*Store` + `Load()` / `Save()`), keep schemas backward compatible, and treat missing/corrupt files as empty state (log a warning, never throw at startup).
- **Services take `AppLog` and a `CancellationToken`** where work can be long. Catch exceptions at the boundary, log with context (`_log.Warn("...: {message}")`), and return a result object instead of throwing into the UI.
- **Salesforce access** goes through the existing clients (`SfCliRunner`, `SalesforceRestClient`, `SalesforceSoapClient`, metadata caches). Do not spawn `sf` or `HttpClient` directly from a store or ViewModel helper.
- **Reports model is immutable.** `ReportTable` / `ReportSection` / `ReportDocument` use `init` properties (`NumericColumns` is a set) — build them with object initializers; `ReportDocument.WithRowLimit` clones. Writers: `ReportMarkdownWriter`, `ReportHtmlWriter`, `ReportPdfWriter`, orchestrated by `ReportExportService` (CSV/XLSX use `CsvExporter` / `ExcelExporter`).
- **User-visible strings** come from `UiText` (`src/SfUi.Core/Localization`). When you add a key, add it to En/Ja/Zh/Ko in the same edit.
- **Culture-safe formatting**: use `CultureInfo.InvariantCulture` for machine-readable values and the current culture only for display.
- **Windows/macOS parity**: guard OS-specific code with `PlatformInfo` / `OperatingSystem.Is*`; the macOS CI builds and runs the same Core tests.
