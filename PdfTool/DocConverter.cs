using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PdfTool
{
    // Converts Word-type documents (.doc/.docx/.rtf/.odt) to PDF using an office app
    // that's already installed - so the PDF looks exactly like "Save as PDF" in that app.
    //   1st choice: whatever is registered as "Word.Application" (Microsoft Word or WPS Office)
    //   fallback:   LibreOffice (soffice.exe), if installed
    // No NuGet packages needed: Word is driven through COM using `dynamic` (late binding).
    public static class DocConverter
    {
        public static readonly string[] Extensions = { ".doc", ".docx", ".rtf", ".odt" };

        private const int wdExportFormatPDF = 17;     // Word constant for ExportAsFixedFormat
        private const int wdDoNotSaveChanges = 0;     // Word constant for Document.Close

        // COM error codes meaning "the office app process has gone away".
        private const int RPC_E_SERVER_UNAVAILABLE = unchecked((int)0x800706BA);
        private const int RPC_E_CALL_FAILED = unchecked((int)0x800706BE);
        private const string AppGone = "__app_gone__";

        // Converts each file into outDir. Calls progress(message) as it goes.
        // Returns a list of "file: error" strings for files that failed (empty = all good).
        // Runs on a separate STA thread: COM office apps expect STA, and it keeps the UI responsive.
        public static Task<List<string>> ConvertAsync(List<string> files, string outDir, Action<string> progress)
        {
            var tcs = new TaskCompletionSource<List<string>>();
            var thread = new Thread(() =>
            {
                try { tcs.SetResult(ConvertAll(files, outDir, progress)); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true; // don't keep the app alive if the window is closed
            thread.Start();
            return tcs.Task;
        }

        private static List<string> ConvertAll(List<string> files, string outDir, Action<string> progress)
        {
            var errors = new List<string>();

            // Is any app registered as "Word.Application"? (null if not)
            Type wordType = Type.GetTypeFromProgID("Word.Application");
            string soffice = FindLibreOffice();

            if (wordType == null && soffice == null)
            {
                errors.Add("No office app found. Install Microsoft Word, WPS Office or LibreOffice (free).");
                return errors;
            }

            dynamic word = null;
            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    string src = Path.GetFullPath(files[i]);
                    string dst = UniqueOutputPath(outDir, Path.GetFileNameWithoutExtension(src));
                    progress($"Converting {i + 1}/{files.Count}: {Path.GetFileName(src)}...");

                    string error = null;
                    if (wordType != null)
                    {
                        // Start the office app once, on first use, and reuse it for every file.
                        if (word == null) word = StartWord(wordType);
                        error = ConvertWithWord(word, src, dst);

                        // WPS quits by itself after its last document closes, so the next
                        // file finds it gone. Start a fresh copy and retry this file once.
                        if (error == AppGone)
                        {
                            QuitWord(word);
                            word = StartWord(wordType);
                            error = ConvertWithWord(word, src, dst);
                            if (error == AppGone) error = "office app stopped responding";
                        }
                    }
                    // Word route not available or failed -> try LibreOffice.
                    if ((wordType == null || error != null) && soffice != null)
                        error = ConvertWithLibreOffice(soffice, src, dst);

                    if (error != null) errors.Add($"{Path.GetFileName(src)}: {error}");
                }
            }
            finally
            {
                QuitWord(word);
            }
            return errors;
        }

        // ---------- Word / WPS via COM ----------

        private static dynamic StartWord(Type wordType)
        {
            dynamic word = Activator.CreateInstance(wordType);
            word.Visible = false;
            word.DisplayAlerts = 0; // wdAlertsNone: never pop up a dialog that would block us
            return word;
        }

        private static string ConvertWithWord(dynamic word, string src, string dst)
        {
            dynamic doc = null;
            try
            {
                // Open(FileName, ConfirmConversions, ReadOnly, AddToRecentFiles, PasswordDocument)
                // A dummy password makes password-protected files FAIL instead of showing an
                // invisible password prompt that would hang forever. Normal files ignore it.
                doc = word.Documents.Open(src, false, true, false, "__no_password__");
                doc.ExportAsFixedFormat(dst, wdExportFormatPDF);
                return File.Exists(dst) ? null : "office app reported success but no PDF was written";
            }
            catch (COMException ex) when (ex.HResult == RPC_E_SERVER_UNAVAILABLE || ex.HResult == RPC_E_CALL_FAILED)
            {
                return AppGone;
            }
            catch (Exception ex) { return ex.Message; }
            finally
            {
                if (doc != null)
                {
                    try { doc.Close(wdDoNotSaveChanges); } catch { /* already closed */ }
                    Marshal.ReleaseComObject(doc);
                }
            }
        }

        private static void QuitWord(dynamic word)
        {
            if (word == null) return;
            // WPS sometimes exits on its own once its last document closes, so Quit can
            // throw "RPC server is unavailable". That's harmless - the app is gone either way.
            try { word.Quit(wdDoNotSaveChanges); } catch { }
            try { Marshal.FinalReleaseComObject(word); } catch { }
        }

        // ---------- LibreOffice fallback ----------

        private static string FindLibreOffice()
        {
            string[] candidates =
            {
                @"C:\Program Files\LibreOffice\program\soffice.exe",
                @"C:\Program Files (x86)\LibreOffice\program\soffice.exe"
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            return null;
        }

        private static string ConvertWithLibreOffice(string soffice, string src, string dst)
        {
            // LibreOffice always names the output "<input name>.pdf" in --outdir, so convert
            // into a temp folder first, then move it to the name we actually want.
            string tempDir = Path.Combine(Path.GetTempPath(), "PdfTool_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = soffice,
                    Arguments = $"--headless --convert-to pdf --outdir \"{tempDir}\" \"{src}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (!p.WaitForExit(120_000)) { try { p.Kill(); } catch { } return "LibreOffice timed out"; }

                string produced = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(src) + ".pdf");
                if (!File.Exists(produced)) return "LibreOffice could not convert this file";
                File.Move(produced, dst);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        // ---------- helpers ----------

        // report.pdf, or report (2).pdf, report (3).pdf ... if that name is taken.
        private static string UniqueOutputPath(string dir, string baseName)
        {
            string path = Path.Combine(dir, baseName + ".pdf");
            for (int n = 2; File.Exists(path); n++)
                path = Path.Combine(dir, $"{baseName} ({n}).pdf");
            return path;
        }
    }
}
