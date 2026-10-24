---
description: "Use when adding ViewModels, commands, dialog abstractions, filtering or report export wiring in src/SfUi.Presentation. Covers MVVM conventions, constructor injection and shared dialog patterns."
applyTo: "src/SfUi.Presentation/**"
---
# SfUi.Presentation rules

- **No WPF/Avalonia types.** Presentation is `net9.0`; UI-specific behaviour is expressed through interfaces in `UI/` (`IDialogService`, `IFilePickerService`, `IUiDispatcher`, `IClipboardService`, `IAppWindowService`, `IReportExportDialog`).
- **ViewModels**: `public sealed partial class XxxViewModel : ObservableObject, IDisposable`. Fields are injected in the constructor and stored in `private readonly` fields; assign services before any work.
- **Generated members**: use `[ObservableProperty] private string _title;` and consume the generated `Title`; use `[RelayCommand] private async Task DoAsync()` and bind `DoCommand` / `DoAsyncCommand` accordingly. Never read or write the underscore field outside the initializer.
- **Constructor parameter order** used across windows: existing services …, optional UI services …, `IReportExportDialog reportDialog`, then `AppLog log`. Register new dependencies in `src/SfUi.App/App.xaml.cs` and `src/SfUi.Avalonia/App.axaml.cs`.
- **Report export pattern** (one method per window/tab):
  ```csharp
  [RelayCommand]
  private async Task ExportReportAsync()
  {
      var sections = new List<ReportSection>();          // skip empty sources
      sections.Add(...);                                  // WindowReportBuilder.Section(heading, table)
      if (sections.Count == 0) return;
      var document = WindowReportBuilder.Document(title, subtitle, org, sections);
      await ReportExportRunner.RunAsync(_reportDialog, document, "suggested-base-name", null);
  }
  ```
  Build tables with `WindowReportBuilder.Table(name, columns, rows, numericColumnIndexes)` — never construct `ReportTable` by mutating collections (it is `init`-only).
- **Filtering/sorting** lives in the ViewModel (collection views or LINQ over the source list); the view binds only.
- **Localization**: `UiText.T("Key")` for text, `UiText.T("Key", arg0)` for formats; titles come from `UiText`, never literals.
- **Long operations**: accept/observe `CancellationToken`, guard re-entrancy (`CanRun`), wrap in try/catch and surface `StatusMessage = UiText.T("Common_FailedFmt", ex.Message)`.
- **Tests**: any new rule that can be tested without UI belongs here (not in the view) so `tests/SfUi.Tests` can cover it.
