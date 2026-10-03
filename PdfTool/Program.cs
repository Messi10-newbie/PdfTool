using System;
using System.Windows.Forms;

namespace PdfTool
{
    internal static class Program
    {
        // args = files dropped onto PdfTool.exe / its shortcut, or passed by "Open with".
        [STAThread]
        static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();
            WindowsFontResolver.Install(); // must happen before any PDF text is drawn
            Settings.Load(); // last-used folder etc.
            Application.Run(new MainForm(args));
        }
    }
}
