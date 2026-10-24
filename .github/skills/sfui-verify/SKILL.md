---
name: sfui-verify
description: "Build, test and smoke-verify the SfUi solution after a change (WPF + Avalonia), including the gotchas that make local verification fail for the wrong reasons. Use when asked to verify, check, test or validate changes in this repository."
---
# Verify SfUi changes

Run the loop in order and **stop at the first failure**.

## 1. Unlock and build
```powershell
Get-Process SfUi -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build SfUi.sln -c Debug --nologo -v q 2>&1 | Select-String -Pattern ': error ' | Select-Object -Unique -First 15
```
- A locked exe shows as `MSB3027 ... being used by another process`; kill `SfUi` and rebuild.
- NuGet lock errors after a package change: `dotnet nuget locals http-cache --clear` then rebuild.

## 2. Tests
```powershell
dotnet test tests\SfUi.Tests\SfUi.Tests.csproj -c Debug --nologo 2>&1 | Select-Object -Last 3
```
Expect `Failed: 0` and 1095+ passing (the count grows with new tests).

## 3. Smokes (both shells, exit code must be 0)
```powershell
dotnet run --project src\SfUi.App\SfUi.App.csproj -- --smoke; "EXIT=$LASTEXITCODE"
dotnet run --project src\SfUi.Avalonia\SfUi.Avalonia.csproj -- --smoke; "EXIT=$LASTEXITCODE"
```

## 4. UI/E2E (only when the change is visible)
Follow `.github/skills/sfui-ui-e2e` (WPF or Avalonia scripts under `C:\huqian\*.ps1`).

## 5. Report
State: build result, test count, smoke exit codes, E2E pass count, and anything not covered.
Never claim a UI change works without step 3 (and step 4 for new controls).
