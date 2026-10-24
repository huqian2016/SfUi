---
description: "Use when editing Avalonia views, styles or startup wiring in src/SfUi.Avalonia. Covers Avalonia 11.x pinning, missing DataTrigger, automation ids and parity with the WPF shell."
applyTo: "src/SfUi.Avalonia/**"
---
# Avalonia shell rules (`SfUi.Avalonia`)

- **Stay on Avalonia 11.x.** All five Avalonia packages move together (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Controls.DataGrid`, `Avalonia.AvaloniaEdit`). Major bumps are blocked by `.github/dependabot.yml` — Avalonia 12 renames `IClipboard.SetTextAsync` and moves `IBinding`, which breaks the build.
- **No `DataTrigger`.** Use classes + selectors:
  ```xml
  <Button Classes.selected="{Binding IsGetMethod}" ... />
  <Style Selector="Button.method.selected"><Setter Property="Background" Value="#DDEBFF" /></Style>
  ```
- **Tooltips** use `ToolTip.Tip="{loc:Tr Key}"` (not `ToolTip=`).
- **Text** uses `{loc:Tr Key}` (`xmlns:loc="clr-namespace:SfUi.Avalonia"`).
- **Automation**: `AutomationProperties.AutomationId="..."` matches the WPF id exactly; add `x:Name` for elements the E2E scripts query.
- **Parity**: every change here must also exist in `src/SfUi.App` (same ViewModel, same command, same ids) unless documented as platform-specific.
- **Startup/DI** is in `App.axaml.cs` — keep the service registrations aligned with the WPF `App.xaml.cs`.
- Build/verify on Windows even when only editing Avalonia files:
  `dotnet build SfUi.sln -c Debug --nologo -v q` then `dotnet run --project src\SfUi.Avalonia\SfUi.Avalonia.csproj -- --smoke` (exit code 0).
