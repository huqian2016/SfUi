---
description: "Use when editing WPF views, XAML, dialogs or DI wiring in src/SfUi.App. Covers localization markup, AutomationId conventions, binding pitfalls and dialog service implementations."
applyTo: "src/SfUi.App/**"
---
# WPF shell rules (`SfUi.App`)

- **Text**: `Content="{loc:Tr Some_Key}"`, `Text="{loc:Tr Some_Key}"` (markup extension in `Localization.cs`). No literal UI strings in XAML.
- **Automation ids** for anything an E2E script must click or read:
  `AutomationProperties.AutomationId="OrgInfoExportReport"` on buttons, `StatusText` on status TextBlocks. Names follow `<Window><Purpose>` (e.g. `SoqlExportReport`, `BackupExportReport`).
- **Read-only bindings need `Mode=OneWay`** — `TextBox.Text` defaults to TwoWay and throws at startup when the source property has no setter.
- **Styles**: never set both `Style="..."` and `<Button.Style>` (MC3024). Merge with `BasedOn`.
- **Dialogs** live in `Views/` and are wrapped by a service in `Services/` (`WpfReportExportDialog`, etc.) implementing the Presentation interface; register them in `App.xaml.cs` (`services.AddSingleton<IReportExportDialog, WpfReportExportDialog>()`).
- **Markup extension availability**: `loc:Tr` requires `xmlns:loc="clr-namespace:SfUi.App"` on the root element.
- **Keep parity with Avalonia**: the same feature must exist in `src/SfUi.Avalonia` with the same command names and automation ids.
- **`LocalizationUsageTests` scans this XAML** for `loc:Tr` keys — every key must exist in `UiText` (all four languages).
- Window layout conventions: a top `Border`/`StackPanel` toolbar holds the window-level actions (refresh, export, help); tabs host the per-feature views.
