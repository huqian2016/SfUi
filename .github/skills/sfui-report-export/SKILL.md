---
name: sfui-report-export
description: "Add CSV/Markdown/HTML/Excel/PDF report export to a SfUi window or tab using the shared ReportModel + export dialog. Use when a view needs an Export button or an existing export must include more data."
---
# Add report export to a window or tab

All windows share the same model and dialog. Adding export = build a `ReportDocument` + expose a command + add a button in **both** shells.

## 1. ViewModel (src/SfUi.Presentation)
```csharp
private readonly IReportExportDialog _reportDialog;   // inject last, before AppLog

[RelayCommand]
private async Task ExportReportAsync()
{
    var sections = new List<ReportSection>();
    if (/* source has rows */)
    {
        var table = WindowReportBuilder.Table(
            name: UiText.T("Report_XxxSection"),
            columns: new[] { UiText.T("Report_ColA"), UiText.T("Report_ColB") },
            rows: items.Select(i => (IReadOnlyList<string?>)new[] { i.A, i.B }).ToList(),
            numericColumns: new[] { 1 });
        if (WindowReportBuilder.Section(table.Name, table) is { } section) sections.Add(section);
    }

    if (sections.Count == 0) return;         // no data → do nothing (or show Report_NoData)

    var document = WindowReportBuilder.Document(
        UiText.T("Report_XxxTitle"), subtitle: null, org: null, sections);
    await ReportExportRunner.RunAsync(_reportDialog, document, "xxx-report", null);
}
```
- `ReportTable` / `ReportSection` / `ReportDocument` are in namespace **`SfUi.Core`** and use `init` properties — build them through `WindowReportBuilder` (`src/SfUi.Presentation/UI/WindowReportBuilder.cs`).
- Multi-section documents produce one CSV and one Excel sheet per section.
- Register any new constructor dependency in `App.xaml.cs` and `App.axaml.cs` (`IReportExportDialog` is already registered).

## 2. Views (both shells)
```xml
<!-- WPF src/SfUi.App/Views/XxxWindow.xaml -->
<Button Content="{loc:Tr Report_Export}" Padding="10,2" Command="{Binding ExportReportCommand}"
        AutomationProperties.AutomationId="XxxExportReport" />
```
```xml
<!-- Avalonia src/SfUi.Avalonia/Views/XxxWindow.axaml -->
<Button Content="{loc:Tr Report_Export}" Padding="10,2" Command="{Binding ExportReportCommand}"
        AutomationProperties.AutomationId="XxxExportReport" />
```
Place it in the window's top toolbar, next to refresh.

## 3. Localization
Add the report title/columns to `UiText.{En,Ja,Zh,Ko}` (see `sfui-add-ui-text`).

## 4. Tests
- Unit test: construct the VM with `new NullReportDialog()` and assert the document sections (or add a fake that captures the `ReportDocument`).
- Existing regression: `tests/SfUi.Tests/ReportExportTests.cs`, `HistoryViewModelTests.cs`.

## 5. Verify
`dotnet build`, full tests, both `--smoke` runs, then the export E2E (`C:\huqian\report-export-e2e.ps1` pattern) if the dialog flow changed.
