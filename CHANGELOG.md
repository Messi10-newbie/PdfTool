# Changelog

All notable changes to PdfTool are listed here.
Format based on [Keep a Changelog](https://keepachangelog.com/); versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed
- Merge and Watermark showed "failed" even though the output file was written correctly.

### Changed
- PDF operations moved out of the main window into `PdfOps`, covered by an xUnit test project (`PdfTool.Tests`).

## [1.1.0] - 2026-10-04

### Added
- **Fonts in the editor:** pick any installed Windows font, with bold, italic and underline (Ctrl+B / I / U).
- **Replace text tool:** drag a box over existing text and type the replacement — the old text is
  covered and the new text placed in one step, with the font size guessed from the box.
- **Full undo / redo** (Ctrl+Z / Ctrl+Y) for every editor action, plus **Revert all** to return to the original PDF.
- Custom font resolver so PDFs can embed ~99% of Windows fonts (previously only 7), including fonts
  stored in `.ttc` collections such as Cambria.

### Changed
- Editor toolbar split into two rows: tools / history / pages, and text formatting.

## [1.0.0] - 2026-10-04

First public release.

### Added
- Merge PDFs, split PDF, rotate pages (all or a range), images to PDF, Word (DOCX/DOC/RTF/ODT) to PDF, watermark.
- PDF editor: add text, whiteout, move/delete, zoom, page navigation; correct output on rotated pages.
- Tool-card interface with drag-and-drop, drag-to-reorder file list and "Show in folder".
- Remembers the last folder; "Open with PdfTool" from Explorer.
- Installer (Inno Setup) and portable single-file exe.

[1.1.0]: https://github.com/Messi10-newbie/PdfTool/releases/tag/v1.1.0
[1.0.0]: https://github.com/Messi10-newbie/PdfTool/commits/main
