---
name: sfui-add-ui-text
description: "Add, rename or remove localized UI strings in SfUi so all four languages stay in sync and the localization tests pass. Use when adding text, labels, tooltips, dialog titles, format strings or error messages to the UI."
---
# Add localized UI text

Every user-visible string lives in `src/SfUi.Core/Localization/UiText.{En,Ja,Zh,Ko}.cs`.

## Procedure
1. **Pick a key** following the existing prefixes: `Common_*`, `Soql_*`, `OrgInfo_*`, `Compare_*`, `DataIo_*`, `Backup_*`, `Etl_*`, `SourceEditor_*`, `OrgManage_*`, `Scratch_*`, `Report_*`, `Msg_*`.
2. **Add the key to all four files in the same edit.** Insert next to related keys inside the dictionary block (before its closing `};`), keeping the same alphabetical/grouping order as neighbours.
3. English first, then Japanese, Simplified Chinese, Korean — same meaning, UI-length appropriate.
4. Use `{0}`, `{1}` placeholders for formats; consume with `UiText.T("Key", arg0, arg1)`.
5. Consume:
   - WPF XAML → `Content="{loc:Tr Key}"` / `Text="{loc:Tr Key}"`
   - Avalonia XAML → `{loc:Tr Key}`
   - C# → `UiText.T("Key")`
6. Run the localization tests:
   ```powershell
   dotnet test tests\SfUi.Tests\SfUi.Tests.csproj --nologo --filter "FullyQualifiedName~UiText|FullyQualifiedName~Localization"
   ```

## Rules and traps
- Key sets must be **identical** in all four dictionaries (`UiTextTests.AllLanguages_HaveSameKeys`).
- `LocalizationUsageTests` scans WPF XAML (`loc:Tr`) and `SfUi.App`/`SfUi.Core` C# (`UiText.T("...")`) — a key used but not defined fails the build tests.
- Do **not** edit files with PowerShell string surgery: the files contain Japanese text and PS 5.1 reads BOM-less UTF-8 as ANSI. Use the editor tools.
- Renaming a key requires updating every usage (XAML + C#) and all four dictionaries in one commit.
- Non-ASCII literals in new files are fine, but never place them outside the dictionary block.
