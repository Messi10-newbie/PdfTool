# Problems solved

Short write-ups of the hardest bugs and limitations hit while building PdfTool:
what went wrong, how the cause was found, the fix, and what it taught.

| # | Write-up | In one line |
|---|---|---|
| 1 | [7 fonts → 281](01-fonts.md) | PDFsharp could embed only 7 Windows fonts; a custom font resolver reads the registry and unpacks `.ttc` collections. |
| 2 | [Edits on rotated pages](02-rotated-pages.md) | Text added to a rotated page landed in the wrong place; fixed by drawing in the page's unrotated coordinates. |
| 3 | [WPS Office pretending to be Word](03-wps-com.md) | Word → PDF failed on every second file because WPS quits itself; detect the COM error, restart, retry. |
| 4 | [The bug the tests found](04-tests-found-a-bug.md) | Merge and Watermark reported "failed" after a successful save. The first unit tests caught it. |
