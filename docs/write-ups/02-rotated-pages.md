# Edits on rotated pages landed in the wrong place

**Code:** `PdfEditWriter.ApplyEdits` in [`PageEdits.cs`](../../PdfTool/PageEdits.cs) ·
**Test:** `Rotated_pages_keep_their_rotation_and_size` in [`PdfEditWriterTests.cs`](../../PdfTool.Tests/PdfEditWriterTests.cs)

## Problem

In the editor, text and whiteout boxes were placed exactly right on normal pages. On a page
rotated 90° or 270° (common in scanned documents and landscape pages), the saved PDF had the
text shifted, turned sideways or completely off the page.

## Cause

A PDF page doesn't store rotated content. It stores the content **unrotated** plus a
`/Rotate` value (0, 90, 180 or 270), and the viewer turns the page when showing it.

The editor stores positions as the user **sees** the page: points from the top-left
corner *after* rotation. So the save step has to map "what the user sees" back to the
page's unrotated layout. Two things got in the way:

1. Drawing straight onto the page used the visual coordinates on the unrotated content,
   so everything was in the wrong place.
2. A PDFsharp quirk: for a 90/270 page, `page.Height` reports the **swapped** (rotated)
   height, and `XGraphics.FromPdfPage` uses that value to flip the y-axis. Even after
   turning rotation off, everything was shifted by `width - height`.

A tempting "fix" was setting `page.Orientation`. It looked right on screen but corrupted
the page when saved, so it was dropped.

## Fix

For each page that has edits:

```csharp
int rotate = ((page.Rotate % 360) + 360) % 360;
page.Rotate = 0;                                  // draw on the unrotated page
double w = page.MediaBox.Width, h = page.MediaBox.Height;
double yFix = page.Height.Point - h;              // undo PDFsharp's swapped-height flip

using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
{
    gfx.TranslateTransform(0, yFix);
    // move the origin to the corner that the user sees as top-left, then turn
    if (rotate == 90)       { gfx.TranslateTransform(0, h); gfx.RotateTransform(-90); }
    else if (rotate == 180) { gfx.TranslateTransform(w, h); gfx.RotateTransform(180); }
    else if (rotate == 270) { gfx.TranslateTransform(w, 0); gfx.RotateTransform(90); }
    DrawEdits(gfx, pageEdits);                    // same coordinates as the screen
}
page.Rotate = rotate;                             // put the rotation back
```

The edits are drawn with the user's coordinates unchanged. The two transforms map them
onto the unrotated page, and when the viewer rotates it back, they land where the user put them.

## What I learned

- Know the data model of the format you're editing. The bug only made sense once it was
  clear that PDFs store rotation as metadata, not as rotated content.
- Transforms compose. "Translate to the corner, then rotate" is easier to reason about than
  rewriting every x/y formula by hand.
- A fix that looks right on screen isn't done until the saved file is reopened (the
  `Orientation` approach failed only on save). The regression test now checks that
  rotation and page size survive saving for 90/180/270.
