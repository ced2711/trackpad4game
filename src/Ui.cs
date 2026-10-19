using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Trackpad4Game
{
    // English by default; Chinese on request (or when Windows is in Chinese).
    static class Lang
    {
        public static bool Zh;

        public static string T(string en, string zh) { return Zh ? zh : en; }
    }

    static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(23, 23, 27);
        public static readonly Color Surface = Color.FromArgb(38, 38, 44);
        public static readonly Color SurfaceHot = Color.FromArgb(52, 52, 60);
        public static readonly Color Line = Color.FromArgb(44, 44, 50);
        public static readonly Color Fg = Color.FromArgb(236, 236, 240);
        public static readonly Color Sub = Color.FromArgb(136, 136, 148);
        public static readonly Color Off = Color.FromArgb(62, 62, 70);
        public static readonly Color Accent = Color.FromArgb(124, 214, 160);
        public static readonly Color Warn = Color.FromArgb(240, 140, 110);

        public static string Font { get { return Lang.Zh ? "Microsoft YaHei UI" : "Segoe UI"; } }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            using (var p = Round(r, radius))
            using (var b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, Color c, float width, RectangleF r, float radius)
        {
            using (var p = Round(r, radius))
            using (var pen = new Pen(c, width))
                g.DrawPath(pen, p);
        }

        public static void Switch(Graphics g, Rectangle r, bool on, bool hot)
        {
            Color track = on ? Accent : (hot ? SurfaceHot : Off);
            FillRound(g, track, r, r.Height / 2f);
            float pad = r.Height * 0.16f, k = r.Height - 2 * pad;
            float x = on ? r.Right - pad - k : r.X + pad;
            using (var b = new SolidBrush(on ? Bg : Fg))
                g.FillEllipse(b, x, r.Y + pad, k, k);
        }
    }
}
