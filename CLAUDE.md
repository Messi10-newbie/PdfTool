# PdfTool

A personal Windows desktop app for working with PDF files offline. No hosting,
no subscriptions, no cloud. Everything runs locally on the user's machine.
Shipped as a single self-contained exe plus an Inno Setup installer.

## Tech stack

- C# / .NET 8 (`net8.0-windows10.0.19041.0` - the Windows 10 TFM unlocks `Windows.Data.Pdf`)
- Windows Forms (WinForms), UI built entirely in code
- PDFsharp 6.1.1 (NuGet, MIT licence) - writes/modifies PDFs
- `Windows.Data.Pdf` (built into Windows 10/11, no NuGet) - renders pages to images for the editor
- Inno Setup 6 (free) - builds the installer; installed per-user at `%LOCALAPPDATA%\Programs\Inno Setup 6`
- Built in Visual Studio 2022

## Repository

- GitHub: https://github.com/Messi10-newbie/PdfTool  ·  local: `C:\Users\Deveswar Mohan\source\repos\PdfTool`
- Commit in small, focused steps with clear messages. New features on a branch -> PR.

## Project layout

```
PdfTool/                    # repo root
├── PdfTool.slnx            # solution (open this in Visual Studio)
├── README.md · LICENSE (MIT) · CLAUDE.md (this file) · .gitignore · .gitattributes
└── PdfTool/                # the app project
├── PdfTool.csproj    # TFM, app identity (icon/version), single-file publish settings
├── app.ico           # app icon (red "PDF" tile, 16-256 px)
├── Program.cs        # entry point; passes command-line files (Open with / drop on exe) to MainForm
├── MainForm.cs       # main window: header, file list, tool cards, status bar + all batch tools
├── EditorForm.cs     # "Edit PDF" window UI: tools, fonts, undo/redo, page viewer
├── PageEdits.cs      # editor model: PageEdit, TextLayout (measure/draw), EditHistory, PdfEditWriter
├── WindowsFontResolver.cs # lets PDFsharp embed ANY installed Windows font (incl. .ttc bundles)
├── DocConverter.cs   # .doc/.docx/.rtf/.odt -> PDF via installed office app (no UI code)
├── Theme.cs          # colours, fonts, DPI scaling S(), RoundRect, Primary/Secondary/Icon buttons
├── UiControls.cs     # ToolCard, DropZone, FileList (owner-drawn rows, drag-to-reorder)
├── Dialogs.cs        # styled option dialogs (AskRotate, AskWatermark)
├── Settings.cs       # remembers last folder in %AppData%\PdfTool\settings.txt
└── installer/
    └── PdfTool.iss   # Inno Setup script -> installer\Output\PdfTool-Setup-<ver>.exe
```

## Important conventions

- **No designer files.** The whole UI is built in code. Do NOT create `*.Designer.cs`
  or `.resx` files, and do not convert the UI to the drag-and-drop designer.
- **Look & feel comes from `Theme.cs`.** Use its colours/fonts and `Theme.PrimaryButton`,
  `SecondaryButton`, `IconButton` - never default grey WinForms buttons. Icons are
  characters from the built-in "Segoe MDL2 Assets" font (`Theme.Icon(size)`, e.g. `"\uE710"` = plus).
- **Scale every pixel size with `Theme.S(this, px)`** (the app runs at 125% DPI on this PC).
  Prefer Dock / AutoSize / TableLayoutPanel over absolute positions.
- **Adding a tool** = one `AddTool(glyph, title, description, colour, handler, needs)` line
  in `MainForm.BuildToolsPanel()` under the right `Section(...)`, plus a handler method.
  `needs` returns `null` when the tool can run, or a short hint ("Add 2 or more PDFs")
  shown on the card. Handlers can therefore assume their input exists.
- Single-PDF tools use `SinglePdf()`: the selected PDF, or the only PDF in the list.
- Tool option prompts go in `Dialogs.cs` (auto-sizing, styled). Don't use bare InputBox-style forms.
- Default output names come from the input: `OutputName(src, "rotated")` -> `report_rotated.pdf`.
  Save/Open/Folder dialogs start in `Settings.LastFolder` and call `Settings.Remember(...)`.
- Status messages: `Done(msg, outputPath)` (green, shows "Show in folder"), `Warn(msg)` (red),
  `Info(msg)` (neutral). Do not use `MessageBox.Show` for normal feedback.
- Long-running work (e.g. Word -> PDF) runs off the UI thread; call `SetBusy(true/false)`
  around it and report progress via `BeginInvoke`.
- Every file operation is wrapped in try/catch, with the error surfaced through
  `Warn(...)`. Never let an exception crash the app.
- Page ranges from the user go through `ParsePages(text, pageCount)` (1-based, "all" supported). Reuse it.
- Use `XUnit.FromPoint(...)` and `.Point` when setting page sizes — PDFsharp 6
  does not accept raw doubles for `page.Width` / `page.Height`.
- Keep each file under ~600 lines; split UI pieces into `UiControls.cs` / `Dialogs.cs`.

## WinForms gotchas (already hit these)

- Avoid naming members `Move`, `Scale` or `Select` on a Form — they collide with `Control.Move` / `Scale` / `Select`.
- A **docked** child inside an **AutoSize** form makes the form collapse to a sliver
  (each waits for the other's size). In auto-sizing dialogs, don't dock the content panel.
- Section headings inside a single wrapping `FlowLayoutPanel` get the height of a whole
  card row. Tools panel is therefore a TopDown stack of [heading, wrapping row] pairs.
- Docked controls lay out in reverse add order: add the `Fill` control first, edges after
  (or call `BringToFront()` on the Fill control).
- Event handlers that fire during construction (e.g. `RadioButton.Checked = true`)
  can run before later controls exist — null-check (EditorForm's `canvas` bit us).

## PDFsharp 6.1 gotchas (already hit these)

- `GlobalFontSettings.UseWindowsFontsUnderWindows` does NOT exist in 6.1.
  Windows fonts resolve automatically. Do not add it back.
- Font styles use `XFontStyleEx`, not `XFontStyle`.
- `PdfDocumentOpenMode.Import` for reading pages into a new doc;
  `PdfDocumentOpenMode.Modify` when drawing onto an existing doc.
- Overlay drawing needs `XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append)`
  or the existing page content gets wiped.
- **Rotated pages (Rotate 90/270):** `XGraphics.FromPdfPage` flips the y-axis using the
  swapped `page.Height`, so drawings land offset/off-page. `PdfEditWriter.ApplyEdits` handles
  it: set `Rotate = 0`, `TranslateTransform(0, page.Height.Point - MediaBox.Height)`, apply
  the visual->unrotated rotation, draw, restore `Rotate`. Do NOT set `page.Orientation`
  to work around it — that corrupts the page on save.
- Both PDFsharp and Windows.Data.Pdf have a `PdfDocument` class — use the `SharpDoc` /
  `WinPdfDoc` aliases.
- **Fonts:** out of the box PDFsharp 6.1 finds only ~7 Windows fonts (no Calibri, Cambria,
  Segoe UI...). `WindowsFontResolver` maps every font from the registry
  (`HKLM/HKCU\...\CurrentVersion\Fonts`) to its file, and extracts single fonts out of
  `.ttc` collections. It MUST be installed (`WindowsFontResolver.Install()`, done in
  `Program.Main`) before any XFont is used - PDFsharp won't accept a resolver after first use.
  281/283 fonts work on this PC; `TextLayout.CanEmbed` rejects the rest at pick time.

## Current features

Main window: file panel on the left (drop zone when empty, drag rows to reorder,
double-click opens a file, Del removes, Ctrl+O adds), tool cards on the right.

| Section | Tool | What it does |
|---|---|---|
| Organize | Merge PDFs | combines 2+ PDFs in list order |
| Organize | Split PDF | one PDF -> one file per page into a chosen folder |
| Organize | Rotate pages | 90 right / 180 / 90 left, all or chosen pages (`1,3-5`) |
| Convert | Images to PDF | JPG/PNG -> one page each, page sized to the image |
| Convert | Word to PDF | .doc/.docx/.rtf/.odt -> one PDF each (`report (2).pdf` if name taken) |
| Edit | Edit PDF | opens `EditorForm`: add text, **replace text** (drag box -> whiteout + new text, size guessed from box height), whiteout, select/move/delete; any installed font, size, B/I/U, colour; undo/redo/revert all; save as new file |
| Edit | Watermark | diagonal text, auto-sized to the page, chosen opacity and colour |

Also: remembers last folder; files passed on the command line (drop onto the exe,
"Open with PdfTool") are pre-loaded into the list.

### Editor notes

- Edit positions are stored in **points, from the top-left of the page as displayed**
  (after rotation). Screen px = points x `PxPerPt` (zoom x 96/72).
- Screen preview and saved PDF use the same font family/style, line spacing
  (`TextLayout.LineSpacing`) and top-left anchoring, so they match to ~1px. Keep
  `TextLayout.DrawOnScreen` and `PdfEditWriter.DrawEdits` in sync if you change either.
- Edits are drawn in list order on screen AND on save (later items on top).
- **Every change goes through `Change(() => ...)`**, which snapshots the edit list into
  `EditHistory` first - that's what makes undo/redo work. Never modify `edits` directly
  outside it (exceptions: drags push the snapshot taken at mouse-down; style changes
  record once per selected item via `styleRecordedFor`).
- When copying a selection's style INTO the toolbar, set `syncingToolbar = true` so the
  control events don't count as a user change.
- Shortcuts: Del, Ctrl+Z / Ctrl+Y, Ctrl+B/I/U, PgUp/PgDn. Save button lives in the bottom bar.

### Word -> PDF notes

- Uses whatever is registered as COM `Word.Application` via `dynamic` (no interop NuGet).
  On this PC that is **WPS Office**, not MS Word (WPS hijacked the registration;
  `WINWORD.EXE /r` would restore Word). Falls back to LibreOffice `soffice --headless`.
- WPS quits itself after each document closes -> next call throws RPC_E_SERVER_UNAVAILABLE
  (0x800706BA). `DocConverter` restarts the app and retries once. Keep that logic.
- Pass a dummy `PasswordDocument` to `Documents.Open` so protected files fail instead of
  hanging on an invisible password prompt. Keep `DisplayAlerts = 0` for the same reason.
- Runs on a dedicated STA thread (COM office apps expect STA).
- ~4 s per file with WPS (it restarts each time).

## Planned / not built yet

- Editor: add image / signature, highlight, freehand pen, shapes, fill form fields
- Editor: detect the original text's font automatically (needs text extraction - not available in PDFsharp)
- Delete selected pages
- Reorder pages inside a single PDF (thumbnail view)
- Compress (re-encode embedded images)
- Password protect / remove password
- Extract text
- Web version as a PWA (separate project; pdf-lib + pdf.js, see conversation notes)

## How I want you to help

- Give full working code directly, minimal explanation.
- Step-by-step when it involves clicking around Visual Studio.
- Keep everything free and offline — no paid libraries, no API calls, no
  hosting. Check the licence before suggesting any new NuGet package
  (MIT/Apache fine, AGPL like iText is not).
- This is a learning project. Prefer clear, readable code over clever code,
  and say briefly what a non-obvious line does.

## Build, run, release

From the repo root: `dotnet build PdfTool.slnx`. To run: `cd PdfTool` then `dotnet run`.

Release - from the `PdfTool\` project folder (bump `<Version>` in PdfTool.csproj AND
`AppVersion` in installer\PdfTool.iss first):

```
dotnet publish -c Release -o publish
"%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\PdfTool.iss
```

- `publish\PdfTool.exe` — single ~79 MB self-contained exe, runs without .NET installed.
- `installer\Output\PdfTool-Setup-<ver>.exe` — all-users installer into `C:\Program Files\PdfTool` (UAC prompt), Start Menu
  + optional desktop shortcut, "Open with" entry, clean uninstall. Never change its `AppId`.
