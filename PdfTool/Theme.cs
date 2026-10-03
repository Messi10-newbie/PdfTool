using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PdfTool
{
    // Colours, fonts and small drawing helpers shared by every window,
    // so the whole app looks consistent. Change a colour here and it changes everywhere.
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(245, 246, 248);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(225, 228, 233);
        public static readonly Color Text = Color.FromArgb(31, 35, 40);
        public static readonly Color Muted = Color.FromArgb(107, 114, 128);
        public static readonly Color Accent = Color.FromArgb(229, 50, 45);       // "PDF red"
        public static readonly Color AccentHover = Color.FromArgb(200, 38, 34);
        public static readonly Color Hover = Color.FromArgb(240, 242, 245);
        public static readonly Color Selected = Color.FromArgb(253, 236, 235);
        public static readonly Color Success = Color.FromArgb(22, 128, 61);
        public static readonly Color Error = Color.FromArgb(200, 30, 30);
        public static readonly Color Info = Color.FromArgb(37, 99, 235);

        // Tool icon colours
        public static readonly Color Red = Color.FromArgb(229, 50, 45);
        public static readonly Color Orange = Color.FromArgb(234, 120, 20);
        public static readonly Color Purple = Color.FromArgb(124, 58, 237);
        public static readonly Color Amber = Color.FromArgb(217, 160, 0);
        public static readonly Color Blue = Color.FromArgb(37, 99, 235);
        public static readonly Color Teal = Color.FromArgb(13, 148, 136);
        public static readonly Color Pink = Color.FromArgb(219, 39, 119);

        public static readonly Font Body = new Font("Segoe UI", 9.75f);
        public static readonly Font BodyBold = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font SmallBold = new Font("Segoe UI Semibold", 8.5f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 15f);
        public static readonly Font Section = new Font("Segoe UI Semibold", 8.25f);

        // "Segoe MDL2 Assets" is the icon font built into Windows 10 and 11.
        // Each icon is a single character, e.g. "" = plus.
        public static Font Icon(float size) => new Font("Segoe MDL2 Assets", size);

        // Scales a pixel size for the screen's DPI (e.g. 125% display -> 40 becomes 50).
        public static int S(Control c, int px) => (int)Math.Round(px * c.DeviceDpi / 96f);

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // Draws a single centred icon character.
        public static void DrawGlyph(Graphics g, string glyph, Font font, Color color, RectangleF area)
        {
            using var brush = new SolidBrush(color);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(glyph, font, brush, area, sf);
        }

        // ---------- buttons ----------

        // Red filled button for the main action.
        public static Button PrimaryButton(string text)
        {
            var b = BaseButton(text);
            b.BackColor = Accent;
            b.ForeColor = Color.White;
            b.Font = BodyBold;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = AccentHover;
            b.FlatAppearance.MouseDownBackColor = AccentHover;
            return b;
        }

        // White outlined button for everything else.
        public static Button SecondaryButton(string text)
        {
            var b = BaseButton(text);
            b.BackColor = Surface;
            b.ForeColor = Text;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.MouseOverBackColor = Hover;
            b.FlatAppearance.MouseDownBackColor = Border;
            return b;
        }

        // Square button showing only an icon; the tooltip explains it.
        public static Button IconButton(string glyph, string tooltip, ToolTip tips)
        {
            var b = SecondaryButton(glyph);
            b.Font = Icon(10f);
            b.Padding = new Padding(4, 3, 4, 3); // AutoSize stays on, so it scales with DPI
            tips.SetToolTip(b, tooltip);
            return b;
        }

        private static Button BaseButton(string text)
        {
            return new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Font = Body,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 3, 10, 3),
                Margin = new Padding(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
        }
    }
}
