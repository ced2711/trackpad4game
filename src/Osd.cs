using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Trackpad4Game
{
    // Small on-screen badge shown when the toggle key is pressed. It never takes focus and
    // ignores the mouse, so it cannot get in the way of the game.
    class Osd : Form
    {
        readonly Timer hold = new Timer { Interval = 1300 }, fade = new Timer { Interval = 15 };
        string title = "", detail = "";
        bool on;
        int alpha;
        float scale = 1;
        Font fTitle, fSmall;

        public Osd()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(28, 28, 33);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            hold.Tick += delegate { hold.Stop(); fade.Start(); };
            fade.Tick += delegate
            {
                alpha -= 18;
                if (alpha <= 0) { fade.Stop(); Hide(); return; }
                Native.SetLayeredWindowAttributes(Handle, 0, (byte)alpha, 2);
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                            | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetLayeredWindowAttributes(Handle, 0, 0, 2);
        }

        int S(float v) { return (int)Math.Round(v * scale); }

        public void Flash(bool isOn, string titleText, string detailText)
        {
            on = isOn; title = titleText; detail = detailText;
            var screen = Screen.FromPoint(Cursor.Position);
            var wa = screen.WorkingArea;
            float s = Native.ScaleAt(new Point(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2));
            if (fTitle == null || s != scale || fTitle.Name != Ui.Font)
            {
                scale = s;
                if (fTitle != null) { fTitle.Dispose(); fSmall.Dispose(); }
                fTitle = new Font(Ui.Font, 14 * s, FontStyle.Bold, GraphicsUnit.Pixel);
                fSmall = new Font(Ui.Font, 12 * s, GraphicsUnit.Pixel);
            }
            int textW = Math.Max(TextRenderer.MeasureText(title, fTitle).Width, TextRenderer.MeasureText(detail, fSmall).Width);
            var size = new Size(Math.Max(S(200), textW + S(64)), S(58));
            Bounds = new Rectangle(wa.Left + (wa.Width - size.Width) / 2, wa.Bottom - size.Height - S(80), size.Width, size.Height);
            using (var p = Ui.Round(new RectangleF(0, 0, size.Width, size.Height), S(14)))
            {
                var old = Region;
                Region = new Region(p);
                if (old != null) old.Dispose();
            }
            fade.Stop();
            alpha = 240;
            Native.SetLayeredWindowAttributes(Handle, 0, (byte)alpha, 2);
            if (!Visible) Show();
            Native.SetTop(Handle);
            Invalidate();
            hold.Stop();
            hold.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = S(10);
            using (var b = new SolidBrush(on ? Ui.Accent : Ui.Off))
                g.FillEllipse(b, S(20), (Height - d) / 2f, d, d);
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, title, fTitle, new Rectangle(S(42), S(9), Width - S(52), S(22)), Ui.Fg, flags);
            TextRenderer.DrawText(g, detail, fSmall, new Rectangle(S(42), S(31), Width - S(52), S(18)), Ui.Sub, flags);
        }
    }
}
