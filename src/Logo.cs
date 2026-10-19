using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Trackpad4Game
{
    // A touchpad with two buttons; the left button lights up while the app is on.
    static class Logo
    {
        public static Bitmap Render(int size, bool on)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // Same drawing as the website icon (forthgaze apps/trackpad4game/icon.svg, a 108-unit grid).
                Color ink = on ? Ui.Accent : Color.FromArgb(132, 132, 142);
                float u = size / 108f;
                Ui.FillRound(g, Color.FromArgb(17, 18, 20), new RectangleF(0, 0, size, size), size * 15f / 64f);

                float stroke = Math.Max(1.3f, 7 * u);
                var pad = new RectangleF(23 * u, 20 * u, 62 * u, 68 * u);
                float split = 63 * u;
                using (var path = Ui.Round(pad, 11 * u))
                {
                    if (on)
                    {
                        g.SetClip(path);
                        using (var b = new SolidBrush(ink))
                            g.FillRectangle(b, pad.Left, split, pad.Width / 2, pad.Bottom - split);
                        g.ResetClip();
                    }
                    using (var pen = new Pen(ink, stroke))
                    {
                        g.DrawPath(pen, path);
                        g.DrawLine(pen, pad.Left, split, pad.Right, split);
                        g.DrawLine(pen, pad.Left + pad.Width / 2, split, pad.Left + pad.Width / 2, pad.Bottom);
                    }
                }
            }
            return bmp;
        }

        public static Icon MakeIcon(int size, bool on, out IntPtr handle)
        {
            using (var bmp = Render(size, on))
            {
                handle = bmp.GetHicon();
                return Icon.FromHandle(handle);
            }
        }

        // Writes a multi-resolution .ico made of PNG frames.
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var frames = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = Render(s, true))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    frames.Add(ms.ToArray());
                }
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim); w.Write(dim);
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(frames[i].Length); w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (var f in frames) w.Write(f);
            }
        }
    }
}
