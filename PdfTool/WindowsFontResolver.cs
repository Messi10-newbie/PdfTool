using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using PdfSharp.Fonts;

namespace PdfTool
{
    // PDFsharp only knows a handful of Windows fonts by itself (Arial, Times New Roman, ...).
    // This resolver tells it where EVERY installed font file is, so any font in the editor's
    // dropdown can be embedded in the PDF.
    //
    // Windows lists installed fonts in the registry, e.g.
    //   "Calibri Bold (TrueType)" = "calibrib.ttf"
    // We turn each entry into (family "Calibri", bold, not italic) -> file path.
    internal class WindowsFontResolver : IFontResolver
    {
        // key: "calibri|b" / "calibri|i" / "calibri|bi" / "calibri|" -> full path of the font file
        private readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> cache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly SortedSet<string> families = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        private static WindowsFontResolver instance;

        // Call once at startup, before any PDF text is drawn
        // (PDFsharp doesn't allow changing the resolver after it has been used).
        public static void Install()
        {
            if (instance != null) return;
            instance = new WindowsFontResolver();
            try { GlobalFontSettings.FontResolver = instance; } catch { /* already set/used - keep default */ }
        }

        // Font families that have a regular (non-bold, non-italic) file - used for the dropdown.
        public static IEnumerable<string> Families
        {
            get { Install(); return instance.families; }
        }

        private WindowsFontResolver()
        {
            string winFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            string userFonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
            const string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";
            ReadRegistry(Registry.LocalMachine, key, winFonts);
            ReadRegistry(Registry.CurrentUser, key, userFonts); // fonts installed "for me only"
        }

        private void ReadRegistry(RegistryKey root, string keyPath, string defaultFolder)
        {
            try
            {
                using var k = root.OpenSubKey(keyPath);
                if (k == null) return;
                foreach (string name in k.GetValueNames())
                {
                    if (!(k.GetValue(name) is string file)) continue;
                    string path = Path.IsPathRooted(file) ? file : Path.Combine(defaultFolder, file);
                    string ext = Path.GetExtension(path).ToLower();
                    if (ext != ".ttf" && ext != ".otf" && ext != ".ttc") continue; // skip old bitmap fonts (.fon)
                    if (!File.Exists(path)) continue;

                    // "Cambria & Cambria Math (TrueType)" -> ["Cambria", "Cambria Math"]
                    string faces = name;
                    int paren = faces.LastIndexOf(" (", StringComparison.Ordinal);
                    if (paren > 0) faces = faces.Substring(0, paren);
                    // A .ttc file bundles several fonts, in the same order as the names listed.
                    // We remember which one with "path#index" and extract it later in GetFont.
                    string[] faceNames = faces.Split(new[] { " & " }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < faceNames.Length; i++)
                        AddFace(faceNames[i].Trim(), ext == ".ttc" ? $"{path}#{i}" : path);
                }
            }
            catch { /* registry not readable - fall back to PDFsharp's built-in fonts */ }
        }

        private void AddFace(string face, string path)
        {
            bool bold = false, italic = false;
            string family = face;
            // Peel style words off the end: "Calibri Bold Italic" -> family "Calibri", bold+italic
            foreach (var (suffix, b, i) in new[] {
                (" Bold Italic", true, true), (" Bold Oblique", true, true),
                (" Bold", true, false), (" Italic", false, true), (" Oblique", false, true), (" Regular", false, false) })
            {
                if (family.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    family = family.Substring(0, family.Length - suffix.Length);
                    bold = b; italic = i;
                    break;
                }
            }
            string k = Key(family, bold, italic);
            if (!files.ContainsKey(k)) files[k] = path;
            if (!bold && !italic) families.Add(family);
        }

        private static string Key(string family, bool bold, bool italic) =>
            family + "|" + (bold ? "b" : "") + (italic ? "i" : "");

        // ---------- IFontResolver ----------

        // PDFsharp asks: "which font file do you have for Calibri, bold, not italic?"
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic)
        {
            // Exact match first.
            if (files.TryGetValue(Key(familyName, bold, italic), out var path))
                return new FontResolverInfo(path);

            // No bold/italic file (common for decorative fonts): use the regular file and let
            // PDFsharp fake the style (simulated bold = thicker outline, italic = slanted).
            if (files.TryGetValue(Key(familyName, false, false), out path))
                return new FontResolverInfo(path, bold, italic);

            // Not a font we know - let PDFsharp's own resolver have a go (Arial etc.).
            return PlatformFontResolver.ResolveTypeface(familyName, bold, italic);
        }

        // PDFsharp asks for the bytes of the file we named above (faceName = the path).
        public byte[] GetFont(string faceName)
        {
            if (cache.TryGetValue(faceName, out var data)) return data;
            int hash = faceName.LastIndexOf('#');
            data = hash < 0
                ? File.ReadAllBytes(faceName)
                : ExtractFromCollection(File.ReadAllBytes(faceName.Substring(0, hash)), int.Parse(faceName.Substring(hash + 1)));
            cache[faceName] = data;
            return data;
        }

        // Pulls font number `index` out of a .ttc collection as a normal stand-alone .ttf,
        // because PDFsharp can only embed single fonts.
        // A font file is: a 12-byte header, a directory of tables (16 bytes each:
        // tag, checksum, offset, length), then the table data. We copy the font's directory
        // and tables into a new file and fix up the offsets.
        private static byte[] ExtractFromCollection(byte[] ttc, int index)
        {
            int numFonts = ReadInt(ttc, 8);
            if (index >= numFonts) index = 0;
            int fontStart = ReadInt(ttc, 12 + 4 * index);   // where this font's header begins
            int numTables = (ttc[fontStart + 4] << 8) | ttc[fontStart + 5];
            int headerSize = 12 + 16 * numTables;

            // Work out the new file size: header + every table, each padded to 4 bytes.
            int total = headerSize;
            for (int t = 0; t < numTables; t++)
                total += (ReadInt(ttc, fontStart + 12 + 16 * t + 12) + 3) & ~3;

            var output = new byte[total];
            Array.Copy(ttc, fontStart, output, 0, headerSize); // header + table directory
            int pos = headerSize;
            for (int t = 0; t < numTables; t++)
            {
                int rec = 12 + 16 * t;
                int offset = ReadInt(ttc, fontStart + rec + 8);
                int length = ReadInt(ttc, fontStart + rec + 12);
                Array.Copy(ttc, offset, output, pos, length);
                WriteInt(output, rec + 8, pos);                 // table now lives at `pos`
                pos += (length + 3) & ~3;
            }
            return output;
        }

        // Font files store numbers big-endian (most significant byte first).
        private static int ReadInt(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
        private static void WriteInt(byte[] b, int i, int v)
        {
            b[i] = (byte)(v >> 24); b[i + 1] = (byte)(v >> 16); b[i + 2] = (byte)(v >> 8); b[i + 3] = (byte)v;
        }
    }
}
