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
                // Same drawing as the website icon (forthgaze apps/trackpad4game/icon.svg, a 108-unit grid):
                // a white touchpad with a green left and a white right button. Off: everything grey.
                float u = size / 108f;
                Color white = on ? Color.FromArgb(245, 245, 245) : Color.FromArgb(140, 140, 150);
                Color green = on ? Color.FromArgb(74, 222, 128) : Color.FromArgb(90, 90, 100);
                Ui.FillRound(g, Color.FromArgb(17, 18, 20), new RectangleF(0, 0, size, size), size * 15f / 64f);

                // Keep strokes and gaps at least about a pixel wide, so the 16 px tray icon stays readable.
                float stroke = Math.Max(1.2f, 5 * u);
                Ui.StrokeRound(g, white, stroke, new RectangleF(22 * u, 26 * u, 64 * u, 40 * u), 7 * u);
                float top = Math.Max(71 * u, 66 * u + stroke / 2 + 1), h = Math.Max(1.5f, 84 * u - top);
                Ui.FillRound(g, green, new RectangleF(22 * u, top, 30 * u, h), 4 * u);
                Ui.FillRound(g, white, new RectangleF(56 * u, top, 30 * u, h), 4 * u);
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
