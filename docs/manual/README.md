# SfUi Manual (manuscript set)

This folder contains the **user manual manuscripts** for SfUi. They are the single source used for both the GitHub-hosted manual and (planned) the in-app manual viewer.

## Files

| File | Language | Screenshots |
|---|---|---|
| `ja.md` | 日本語 | `images/ja/` (Japanese UI) |
| `en.md` | English | `images/en/` (English UI) |
| `zh.md` | 简体中文 | `images/en/` (English UI)* |
| `ko.md` | 한국어 | `images/en/` (English UI)* |

\* The zh/ko manuscripts reference the English-UI screenshots and state that the screenshots show the English UI; the feature set and layout are identical.

## Structure of each manuscript

- 15 chapters: intro → install/setup → UI basics → SOQL/Apex/logs/deploy/command/REST → history → org management → Org Info → data I/O → backup & restore → compare → AI → quick panel → settings reference → troubleshooting → appendix
- **Mermaid diagrams** for architecture and key flows (GitHub renders them natively; the future in-app viewer will render them via Markdig-based conversion — WebView2 is intentionally not used)
- Image references are relative to the manuscript file (`images/<lang>/<file>.png`)

## Screenshots

`images/ja/` and `images/en/` each contain 23 PNGs (1920x1080-class captures).

Sources:

- Captured live on Windows with the automation script (`C:\huqian\sfui-manual-shots.ps1 -Lang ja|en` on the dev machine), using a prepared data folder with sample history/backups.
- Store screenshots from `dist\SfUi-Store-Screenshots-v0.14.0\{lang}\` (results grid, log analyzer, org management, health, inventory, org info, backup, backup compare, org compare).
- `docs/screenshots/orginfo-export-{lang}.png` for the document export shot.

To re-capture, run the script for each language after building `SfUi.exe` (Debug) and pass a valid `-Exe` path / data folder as configured inside the script.

## Publishing roadmap

1. **GitHub (now)**: browse `docs/manual/*.md` directly; Mermaid diagrams render on GitHub.
2. **GitHub Pages (planned)**: publish a small static site generated from these Markdown files (same images).
3. **In-app viewer (planned)**: a later phase — render these same Markdown files inside SfUi (Markdig → native panels / FlowDocument), with the language following the UI language.

Keeping everything in Markdown + PNG means one source of truth for all three targets.
