# 7 fonts → 281: embedding any Windows font with PDFsharp

**Code:** [`WindowsFontResolver.cs`](../../PdfTool/WindowsFontResolver.cs)

## Problem

The PDF editor lets you type text in any installed font. On screen every font worked,
but saving with Calibri, Cambria, Segoe UI and most others threw an exception from PDFsharp.

Measured on the development PC (Windows 11, 349 font families installed):

| | Fonts that could be embedded in a PDF |
|---|---|
| PDFsharp 6.1 out of the box | **7** - Arial, Arial Black, Courier New, Lucida Console, Symbol, Times New Roman, Verdana |
| With `WindowsFontResolver` | **281** of the 283 families that have a regular style |

## Cause

To put text in a PDF, PDFsharp needs the **font file's bytes** so it can embed them.
On Windows it finds those files with a small built-in lookup that only knows a handful of
classic fonts. The screen preview uses GDI+, which asks Windows directly, so the editor
showed fonts that the PDF writer could not use.

PDFsharp's answer is the `IFontResolver` interface: you tell it, for a family + bold/italic,
which file to use, and then hand over the bytes.

## Fix

`WindowsFontResolver` implements `IFontResolver` and builds its own font map at startup:

1. **Read the registry.** Windows lists every installed font in
   `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts` (and the same key under `HKCU`
   for fonts installed "for me only"), for example `"Calibri Bold (TrueType)" = "calibrib.ttf"`.
2. **Parse the names.** Strip `(TrueType)`, then peel style words off the end:
   `Calibri Bold Italic` → family `Calibri`, bold, italic. Key = `calibri|bi` → file path.
3. **Fallbacks.** No bold file for a decorative font? Return the regular file with
   `bold: true` so PDFsharp simulates bold. Unknown family? Defer to PDFsharp's own resolver.
4. **Unpack `.ttc` collections.** Some fonts (Cambria, MS Gothic...) ship as `.ttc` files
   that bundle several fonts in one file. PDFsharp can only embed a single font, so
   `ExtractFromCollection` rebuilds a stand-alone `.ttf` in memory:
   - the `.ttc` header gives the offset of font *n*;
   - that font has a table directory (tag, checksum, **offset**, length per table);
   - copy the directory and each table into a new byte array (padded to 4 bytes) and
     **rewrite each offset** to the table's new position.

   The registry lists the fonts inside a `.ttc` in order (`"Cambria & Cambria Math"`),
   so the index is known; it's stored as `path#index` and unpacked on first use.

The resolver must be installed before PDFsharp draws any text (it refuses a resolver after
first use), so `Program.Main` calls `WindowsFontResolver.Install()` first thing.
For the last ~1%, `TextLayout.CanEmbed` test-renders a font once and hides it from the
font list if it fails.

## What I learned

- When a library "doesn't support" something, look for its extension point before working
  around it. Here a single interface turned a hard limit into a solved problem.
- File formats are less scary than they look: a font file is a header, a table of contents
  and blobs. Knowing that much is enough to split a `.ttc`.
- Measure the before/after. "Most fonts work now" is vague; "7 → 281" is a result.
