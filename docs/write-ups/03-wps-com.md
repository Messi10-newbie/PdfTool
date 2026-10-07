# WPS Office pretending to be Word: Word → PDF over COM

**Code:** [`DocConverter.cs`](../../PdfTool/DocConverter.cs)

## Problem

Word → PDF uses whatever office app is installed, through COM (`Word.Application`), so the
PDF looks exactly like "Save as PDF" in that app. On the development PC the first document
converted fine, then the **next one failed** with:

```
The RPC server is unavailable. (0x800706BA)
```

Two other cases could freeze the app completely: a password-protected document, and any
file that made the office app show a dialog.

## Cause

`Type.GetTypeFromProgID("Word.Application")` returns whichever program registered that
name, and on this PC that was **WPS Office**, not Microsoft Word. WPS implements Word's
COM interface closely enough to work, but behaves differently in one key way: **it quits
by itself when its last document is closed.** The code reused one app instance for all
files, so file 2 was sent to a process that no longer existed.

The freezes had a simpler cause: the app runs invisibly, so a password prompt or warning
dialog waits forever for a click that can never happen.

## Fix

1. **Detect "app gone" and retry once.** The two COM error codes that mean the server
   process died (`RPC_E_SERVER_UNAVAILABLE`, `RPC_E_CALL_FAILED`) are caught specifically.
   The converter then releases the dead object, starts a fresh app and converts the same
   file again. A second failure is reported as an error instead of looping.
2. **Never let a dialog appear.** `DisplayAlerts = 0`, and `Documents.Open` gets a dummy
   password. Normal files ignore it; protected files fail immediately with an error
   instead of hanging on an invisible prompt.
3. **No interop package.** COM is driven with `dynamic` (late binding), so the same code
   works with Word or WPS without referencing either one.
4. **Threading.** COM office apps expect an STA thread, so conversion runs on a dedicated
   STA background thread and reports progress to the UI via `BeginInvoke`.
5. **Fallback.** If no COM app is registered, or it fails, LibreOffice
   (`soffice --headless --convert-to pdf`) is tried.

## What I learned

- An interface name is not an implementation. "Word.Application" can be any program; code
  against the contract, but test against what users actually have installed.
- Catch the specific errors you understand and can recover from; let the rest surface
  as readable messages. A blanket retry would have hidden real failures.
- Background automation must never wait for a human. Every possible prompt needs a way
  to fail fast.
- Trade-off accepted: WPS restarts for each file, about 4 s per document.
