using System;
using System.IO;

namespace PdfTool
{
    // Remembers small things between runs (currently: the last folder you used),
    // saved as a tiny text file in %AppData%\PdfTool\settings.txt.
    internal static class Settings
    {
        private static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfTool", "settings.txt");

        public static string LastFolder { get; private set; } =
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        public static void Load()
        {
            try
            {
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                    if (key == "LastFolder" && Directory.Exists(value)) LastFolder = value;
                }
            }
            catch { /* first run, or unreadable file - just use defaults */ }
        }

        // Pass a file OR folder path; we store the folder.
        public static void Remember(string path)
        {
            try
            {
                string folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(folder)) return;
                LastFolder = folder;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, "LastFolder=" + folder);
            }
            catch { /* not worth bothering the user about */ }
        }
    }
}
