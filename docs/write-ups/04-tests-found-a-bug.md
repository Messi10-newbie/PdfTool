# The bug the first unit tests found

**Code:** [`PdfOps.cs`](../../PdfTool/PdfOps.cs) · **Tests:** [`PdfOpsTests.cs`](../../PdfTool.Tests/PdfOpsTests.cs) ·
**Fix:** commit `6e02b1c`, PR #1

## Problem

PdfTool v1.1.0 shipped without automated tests. All the PDF logic lived inside the main
window's button handlers, mixed with dialogs and status messages, so there was nothing
a test could call.

## What was done

1. **Separate logic from UI.** The work behind Merge, Split, Rotate, Images to PDF and
   Watermark moved into a static `PdfOps` class. Each method takes file paths and options,
   writes the output, and throws on failure. The button handlers now only ask for input,
   call `PdfOps`, and show the result.
2. **Add an xUnit project.** Tests create small PDFs in a temp folder, so the repo holds
   no sample files. Each test page gets a different width, which acts as its ID: after a
   merge, reading the widths back proves the page order.

The very first run: **44 passed, 2 failed** - Merge and Watermark.

## Cause

```
System.InvalidOperationException: The document was already saved and cannot be
modified anymore.
   at PdfSharp.Pdf.PdfDocument.get_PageCount()
```

Both tools read the page count for the success message *after* saving:

```csharp
output.Save(outPath);
Done($"Merged ... ({output.PageCount} pages) ...");   // throws in PDFsharp 6
```

PDFsharp 6 locks a document once it's saved, and even reading `PageCount` throws. The
`try/catch` around the handler turned that into **"Merge failed"** in the status bar, even
though the file had been written correctly. Users saw an error for a job that worked.

## Fix

Read the count before saving:

```csharp
int pageCount = output.PageCount; // read before Save - PDFsharp locks the document once saved
output.Save(outPath);
return pageCount;
```

The commits were kept in story order: refactor → tests (failing) → fix (passing). That makes
the PR easy to review. Since PR #2, GitHub Actions runs all 46 tests on every push and pull
request.

## What I learned

- Code that's hard to test is usually code with mixed responsibilities. Pulling the UI out
  was the real improvement; the tests followed easily.
- A broad `catch` keeps the app from crashing, but it can also hide bugs. Here it made a
  success look like a failure, and manual testing didn't notice.
- Read the library's rules about object lifetime. "Saved means locked" is now documented
  in [DEVELOPMENT.md](../DEVELOPMENT.md) so it doesn't happen again.
