---
name: sfui-ui-e2e
description: "Write and run UI automation (UIA) checks against the SfUi WPF and Avalonia windows to prove a feature works end to end. Use when a change touches a window, button, grid or dialog and needs runtime verification."
---
# SfUi UI automation (UIA) checks

Scripts live outside the repo (`C:\huqian\<feature>-check.ps1`), are executed with `powershell -NoProfile -ExecutionPolicy Bypass -File ... -App wpf|av`, and print `PASS/FAIL` lines plus a final `n/n` score.

## Script skeleton
```powershell
param([ValidateSet('wpf','av')][string]$App = 'wpf')
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$exe = if ($App -eq 'wpf') { 'C:\huqian\vscode\SfUi\src\SfUi.App\bin\Debug\net9.0-windows\SfUi.exe' }
       else { 'C:\huqian\vscode\SfUi\src\SfUi.Avalonia\bin\Debug\net9.0\SfUi.Avalonia.exe' }
$p = Start-Process $exe -PassThru
$root = [System.Windows.Automation.AutomationElement]::RootElement
$win  = $root.FindFirst([System.Windows.Automation.TreeScope]::Children,
          [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id))
# click: $el.GetCurrentPattern([InvokePattern]::Pattern).Invoke()
Stop-Process -Id $p.Id -Force
```

## Rules that make the checks reliable
- **Find by `AutomationId`** for buttons/inputs; by `Name` for text. Use exact strings or regex — "contains" matches the wrong element (e.g. a history row).
- **Modal dialogs** owned by a window do **not** appear under `RootElement` children: search `RootElement.FindFirst(TreeScope.Descendants, PropertyCondition(AutomationIdProperty, '...'))` instead.
- **Editor focus**: `SetFocus()` on a Document element throws — click inside its `BoundingRectangle` (`SetCursorPos` + `mouse_event`).
- **Signal readiness** with a stable property (status bar text, `IsEnabled` of the org combo), then poll with a deadline; never `Start-Sleep` a fixed long time.
- **Language independence**: assert against `UiText`-derived text or use language-agnostic ids; JA button names differ (e.g. `▶ 実行 (Ctrl+Enter)`).
- **Avalonia**: `AutomationProperties.AutomationId` maps to UIA id, but `TextPattern` is unavailable in AvaloniaEdit — restrict text checks to WPF.
- Save the script as **UTF-8 with BOM** if it contains Japanese; otherwise keep it ASCII-only.
- Always `Stop-Process -Id $p.Id -Force` in `finally`, and kill leftovers (`Get-Process SfUi | Stop-Process -Force`) before the next run.

## Reporting
Report the script path, the `n/n` result, and a screenshot path if the script captures one (`Copy-FromScreen` / `Graphics.CopyFromScreen`).
