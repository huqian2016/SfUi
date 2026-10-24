---
name: sfui-release
description: "Cut a SfUi release: bump versions, tag, wait for CI, build Windows/MSIX/macOS assets, publish the GitHub Release and prepare the Microsoft Store package. Use when asked to release, publish, package for the Store, or upload assets to GitHub."
---
# SfUi release procedure

## 0. Preconditions
- `main` is green (10 CI checks) and contains the changes to ship.
- Local build + tests were run (`sfui-verify`).

## 1. Bump the version
| File | Change |
| --- | --- |
| `src/SfUi.App/SfUi.App.csproj` | `<Version>x.y.z</Version>` |
| `src/SfUi.Avalonia/SfUi.Avalonia.csproj` | same |
| `packaging/AppxManifest.xml` | `Version="x.y.z.0"` |
| `README.md` | download file names + Japanese changelog line |

Commit `Bump version to x.y.z`, then:
```powershell
git tag vX.Y.Z; git push; git push origin vX.Y.Z
```

## 2. Release notes
Write `dist/release-notes-X.Y.Z.md` (English + 日本語). Keep the Windows/macOS download sections accurate.

## 3. Wait for CI
```powershell
$r = Invoke-RestMethod -Headers $h -Uri "https://api.github.com/repos/huqian2016/SfUi/commits/<sha>/check-runs?per_page=100"
```
- Get the token with `git credential fill` (`protocol=https`/`host=github.com`), never print it.
- Find the **CI** run (not the Dependabot runs) via `/actions/runs?head_sha=<sha>` and its artifact `SfUi-macos-app`.
- Download artifacts with `curl.exe -L --retry 3 -C -` (plain `-sL` can stall part-way); the zip contains `SfUi-X.Y.Z-osx-arm64.zip` directly.

## 4. Windows assets
```powershell
Get-Process SfUi -ErrorAction SilentlyContinue | Stop-Process -Force
powershell -NoProfile -ExecutionPolicy Bypass -File packaging\Make-Msix.ps1
```
Produces `dist\SfUi.exe`, `dist\SfUi_X.Y.Z.0_x64.msix`, `dist\SfUi_X.Y.Z.0_x64.cer`; copy them to `SfUi-X.Y.Z-x64.msix/.cer`.

**Portable zip**: stage **outside the repo** (e.g. `C:\huqian\sfui-portable-X.Y.Z`), copy `dist\SfUi.exe`, run `.\SfUi.exe --seed-samples` then `.\SfUi.exe --smoke` (exit 0), then `Compress-Archive` to `dist\SfUi-vX.Y.Z-portable.zip`. Inside the repo `AppPaths` resolves to the repo `data/`, so seeding writes nothing.

## 5. GitHub Release
Copy the previous `C:\huqian\sfui-release-0XYZ.ps1` and change: tag `vX.Y.Z`, `target_commitish`, title, notes path, asset names. Run it; expect 5 assets:
`SfUi.exe`, `SfUi-vX.Y.Z-portable.zip`, `SfUi-X.Y.Z-x64.msix`, `SfUi-X.Y.Z-x64.cer`, `SfUi-X.Y.Z-osx-arm64.zip`.

## 6. Microsoft Store package
- MSIX: `dist\SfUi-X.Y.Z-x64.msix` (Identity `HKS.SfUi`, `x.y.z.0`, x64). The Store re-signs.
- Screenshots: copy `C:\huqian\sfui-store-pack-0XYZ.ps1`, update the version and README text (`sfui-store-readme-XYZ.txt`), run it → `dist\SfUi-Store-Screenshots-X.Y.Z.zip` (en/ja, 01..10, each ≥ 1366×768).
- Listing text: copy `dist\store-listing-0.20.0.md` → new version, update ①description ②説明 ③What's new ④What's new.
- **Partner Center submission is manual** — report the artifact paths and the 5-step checklist.

## Traps
- PowerShell 5.1 reads BOM-less UTF-8 `.ps1` as ANSI → keep release scripts **ASCII-only**, put Japanese in separate `.txt`/`.md` read with `[IO.File]::ReadAllText(path, [Text.Encoding]::UTF8)`.
- `Start-Sleep`/poll loops belong in scripts; do not poll long-running tool calls.
- Tag must be pushed separately (`git push origin vX.Y.Z`); a release created without the tag fails.
