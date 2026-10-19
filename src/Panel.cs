using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Trackpad4Game
{
    enum Kind { Text, Switch, Row, Sep, Link, Pill, Segment, KeyCap, Chip }

    class El
    {
        public Kind Kind;
        public Rectangle R;
        public string Text, Sub, Id;
        public Font F;
        public Color C;
        public bool Accent;
        public Func<bool> GetB;
        public Action<bool> SetB;
        public Action Click;

        public bool Interactive { get { return Kind != Kind.Text && Kind != Kind.Sep && Kind != Kind.Pill; } }
    }

    // The tray popup. Everything is custom drawn so it stays crisp at any DPI.
    class Panel : Form
    {
        const int W = 328;
        readonly App app;
        float scale = 1;
        List<El> els = new List<El>();
        El hot;
        Font fTitle, fBody, fSmall, fKey, fZh, fHead;
        bool anchorBottom = true;
        DateTime hiddenAt;
        string capturing; // "left", "right" or "toggle" while waiting for a key

        public Panel(App app)
        {
            this.app = app;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Ui.Bg;
            KeyPreview = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            SetScale(1, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.Dwm(Handle, 20, 1); // dark border
            Native.Dwm(Handle, 33, 2); // Windows 11 rounded corners
        }

        int S(float v) { return (int)Math.Round(v * scale); }

        void SetScale(float s, bool force)
        {
            if (!force && s == scale) return;
            scale = s;
            if (fTitle != null) { fTitle.Dispose(); fBody.Dispose(); fSmall.Dispose(); fKey.Dispose(); fZh.Dispose(); fHead.Dispose(); }
            fTitle = new Font("Segoe UI Semibold", 16 * s, GraphicsUnit.Pixel);
            fBody = new Font(Ui.Font, 13 * s, GraphicsUnit.Pixel);
            fSmall = new Font(Ui.Font, 11.5f * s, GraphicsUnit.Pixel);
            fKey = new Font("Segoe UI Semibold", 12.5f * s, GraphicsUnit.Pixel);
            fZh = new Font("Microsoft YaHei UI", 11.5f * s, GraphicsUnit.Pixel);
            fHead = new Font(Ui.Font, 11 * s, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        public void LanguageChanged()
        {
            SetScale(scale, true);
            Rebuild();
        }

        public void Toggle()
        {
            if (Visible) Hide();
            else if ((DateTime.Now - hiddenAt).TotalMilliseconds > 300) ShowNear();
        }

        public void ShowNear()
        {
            var screen = Screen.FromPoint(Cursor.Position);
            SetScale(Native.ScaleAt(Cursor.Position), false);
            Rebuild();
            Rectangle wa = screen.WorkingArea, b = screen.Bounds;
            int m = S(12);
            int x = wa.Left > b.Left ? wa.Left + m : wa.Right - Width - m;
            anchorBottom = !(wa.Top > b.Top);
            int y = anchorBottom ? wa.Bottom - Height - m : wa.Top + m;
            Location = new Point(x, y);
            Show();
            Activate();
            Native.SetForegroundWindow(Handle);
            Native.SetTop(Handle);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            app.PanelShown(Visible);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            capturing = null;
            Hide();
            hiddenAt = DateTime.Now;
        }

        // Catches every key, including Tab, Enter and arrows, while a key cap waits for a key.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (capturing == null)
            {
                if (key == Keys.Escape) { Hide(); return true; }
                // The hooks pause while the panel is open, so handle the on/off key here.
                if (key == app.S.ToggleKey && (keyData & Keys.Modifiers) == 0)
                {
                    if ((msg.LParam.ToInt64() & 0x40000000) == 0) app.SetEnabled(!app.S.Enabled, false); // ignore auto-repeat
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            if (key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
                return true; // wait for a real key
            if (key != Keys.Escape) app.SetKey(capturing, key);
            capturing = null;
            Rebuild();
            return true;
        }

        // ---------- layout ----------

        public void Rebuild()
        {
            if (IsDisposed) return;
            int height;
            els = BuildLayout(out height);
            var size = new Size(S(W), height);
            if (ClientSize != size)
            {
                int bottom = Bottom;
                ClientSize = size;
                if (Visible && anchorBottom) Top = bottom - Height;
            }
            Invalidate();
        }

        List<El> BuildLayout(out int height)
        {
            var L = new List<El>();
            var st = app.S;
            int x0 = S(20), x1 = S(W) - S(20), w = x1 - x0, y = S(18);

            L.Add(new El { Kind = Kind.Text, R = new Rectangle(x0, y, w, S(24)), Text = "trackpad4game", F = fTitle, C = Ui.Fg });
            L.Add(new El { Kind = Kind.Switch, R = new Rectangle(x1 - S(40), y + S(2), S(40), S(22)),
                           GetB = () => st.Enabled, SetB = v => app.SetEnabled(v, false) });
            y += S(26);
            Color statusColor;
            string status = app.Status(out statusColor);
            L.Add(new El { Kind = Kind.Text, R = new Rectangle(x0, y, w, S(18)), Text = status, F = fSmall, C = statusColor });
            y += S(18) + S(12);
            AddSep(L, ref y, x0, w);

            AddHead(L, ref y, x0, w, Lang.T("KEYS", "按键"));
            AddKey(L, ref y, x0, w, "left", Lang.T("Left click", "左键"), st.LeftKey);
            AddKey(L, ref y, x0, w, "right", Lang.T("Right click", "右键"), st.RightKey);
            AddKey(L, ref y, x0, w, "toggle", Lang.T("Switch on / off", "开关"), st.ToggleKey);
            AddSep(L, ref y, x0, w);

            AddHead(L, ref y, x0, w, Lang.T("TOUCHPAD", "触控板"));
            AddRow(L, ref y, x0, w, Lang.T("Block touchpad clicks and taps", "屏蔽触控板点击和轻触"),
                   Lang.T("Moving the pointer still works", "移动指针不受影响"),
                   () => st.BlockTouchpadClicks, v => app.SetBlock(v));
            if (app.TouchpadSupported)
                AddRow(L, ref y, x0, w, Lang.T("Keep the pointer moving", "按键时指针不卡顿"),
                       Lang.T("Turns off palm rejection while on", "开启时关闭防误触（手掌检测）"),
                       () => st.KeepPointerMoving, v => app.SetKeepMoving(v));
            AddRow(L, ref y, x0, w, Lang.T("Only in these games", "只在这些游戏里生效"), null,
                   () => st.OnlyInGames, v => app.SetOnlyInGames(v));
            if (st.OnlyInGames) AddChips(L, ref y, x0, w);
            AddSep(L, ref y, x0, w);

            AddRow(L, ref y, x0, w, Lang.T("Sound when switching", "开关时提示音"), null, () => st.Beep, v => app.SetBeep(v));
            AddRow(L, ref y, x0, w, Lang.T("Start with Windows", "开机自动启动"), null, Autostart.Get, v => Autostart.Set(v));

            // Language: a two-segment pill.
            int segW = S(52), segH = S(24), rowH = S(32);
            L.Add(new El { Kind = Kind.Text, R = new Rectangle(x0, y, w, rowH), Text = Lang.T("Language", "语言"), F = fBody, C = Ui.Fg });
            var pill = new Rectangle(x1 - 2 * segW, y + (rowH - segH) / 2, 2 * segW, segH);
            L.Add(new El { Kind = Kind.Pill, R = pill });
            L.Add(new El { Kind = Kind.Segment, R = new Rectangle(pill.X, pill.Y, segW, segH), Text = "EN", F = fSmall,
                           GetB = () => !Lang.Zh, Click = () => app.SetLanguage("en") });
            L.Add(new El { Kind = Kind.Segment, R = new Rectangle(pill.X + segW, pill.Y, segW, segH), Text = "中文", F = fZh,
                           GetB = () => Lang.Zh, Click = () => app.SetLanguage("zh") });
            y += rowH + S(2);
            AddSep(L, ref y, x0, w);

            string touchpadLink = Lang.T("Windows touchpad settings", "Windows 触控板设置");
            int linkW = TextRenderer.MeasureText(touchpadLink, fSmall).Width + S(4);
            L.Add(new El { Kind = Kind.Link, R = new Rectangle(x0, y, linkW, S(18)), Text = touchpadLink, F = fSmall, C = Ui.Sub,
                           Click = () => app.OpenTouchpadSettings() });
            L.Add(new El { Kind = Kind.Link, R = new Rectangle(x1 - S(40), y, S(40), S(18)), Text = Lang.T("Quit", "退出"),
                           F = fSmall, C = Ui.Sub, Click = () => app.Exit() });
            y += S(18);
            height = y + S(14);
            return L;
        }

        void AddHead(List<El> L, ref int y, int x, int w, string text)
        {
            L.Add(new El { Kind = Kind.Text, R = new Rectangle(x, y, w, S(16)), Text = text, F = fHead, C = Ui.Sub });
            y += S(20);
        }

        void AddKey(List<El> L, ref int y, int x, int w, string id, string label, Keys key)
        {
            L.Add(new El { Kind = Kind.Text, R = new Rectangle(x, y, w, S(32)), Text = label, F = fBody, C = Ui.Fg });
            bool waiting = capturing == id;
            string text = waiting ? Lang.T("Press a key…", "请按一个键…") : key == Keys.Oemtilde ? "` ~" : Settings.KeyName(key);
            int capW = Math.Max(S(40), TextRenderer.MeasureText(text, fKey).Width + S(22));
            L.Add(new El { Kind = Kind.KeyCap, R = new Rectangle(x + w - capW, y + S(3), capW, S(26)), Text = text, F = fKey,
                           Id = id, Accent = waiting,
                           Click = () => { capturing = capturing == id ? null : id; } });
            y += S(34);
        }

        void AddRow(List<El> L, ref int y, int x, int w, string label, string sub, Func<bool> get, Action<bool> set)
        {
            int h = sub == null ? S(32) : S(42);
            L.Add(new El { Kind = Kind.Row, R = new Rectangle(x, y, w, h), Text = label, Sub = sub, F = fBody, GetB = get, SetB = set });
            y += h + S(2);
        }

        void AddChips(List<El> L, ref int y, int x, int w)
        {
            var chips = new List<El>();
            foreach (var g in app.S.Games)
            {
                string game = g;
                chips.Add(new El { Kind = Kind.Chip, Text = game + ".exe", Sub = "×", F = fSmall, Click = () => app.RemoveGame(game) });
            }
            string last = app.AddableApp;
            if (last != null)
                chips.Add(new El { Kind = Kind.Chip, Text = "+ " + last + ".exe", F = fSmall, Accent = true, Click = () => app.AddGame(last) });

            int cx = x, ch = S(24), gap = S(6);
            foreach (var c in chips)
            {
                int cw = TextRenderer.MeasureText(c.Text, c.F).Width + (c.Sub != null ? S(34) : S(20));
                if (cx > x && cx + cw > x + w) { cx = x; y += ch + gap; }
                c.R = new Rectangle(cx, y, cw, ch);
                L.Add(c);
                cx += cw + gap;
            }
            y += ch + S(8);
            string hint = last == null
                ? Lang.T("To add a game, open it, then click this icon again.", "要添加游戏：先打开游戏，再点托盘图标。")
                : Lang.T("Minecraft Java runs as javaw.exe.", "Minecraft Java 版的程序名是 javaw.exe。");
            L.Add(new El { Kind = Kind.Text, R = new Rectangle(x, y, w, S(16)), Text = hint, F = fSmall, C = Ui.Sub });
            y += S(22);
        }

        void AddSep(List<El> L, ref int y, int x, int w)
        {
            y += S(4);
            L.Add(new El { Kind = Kind.Sep, R = new Rectangle(x, y, w, 1) });
            y += S(12);
        }

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            var right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

            foreach (var el in els)
            {
                bool isHot = el == hot;
                switch (el.Kind)
                {
                    case Kind.Text:
                        TextRenderer.DrawText(g, el.Text, el.F, el.R, el.C, left);
                        break;
                    case Kind.Switch:
                        Ui.Switch(g, el.R, el.GetB(), isHot);
                        break;
                    case Kind.Row:
                    {
                        int sw = S(34), sh = S(18);
                        var textR = new Rectangle(el.R.X, el.R.Y, el.R.Width - sw - S(10), el.R.Height);
                        if (el.Sub == null)
                            TextRenderer.DrawText(g, el.Text, el.F, textR, Ui.Fg, left | TextFormatFlags.EndEllipsis);
                        else
                        {
                            TextRenderer.DrawText(g, el.Text, el.F, new Rectangle(textR.X, textR.Y + S(3), textR.Width, S(19)), Ui.Fg, left | TextFormatFlags.EndEllipsis);
                            TextRenderer.DrawText(g, el.Sub, fSmall, new Rectangle(textR.X, textR.Y + S(22), textR.Width, S(16)), Ui.Sub, left | TextFormatFlags.EndEllipsis);
                        }
                        Ui.Switch(g, new Rectangle(el.R.Right - sw, el.R.Top + (el.R.Height - sh) / 2, sw, sh), el.GetB(), isHot);
                        break;
                    }
                    case Kind.KeyCap:
                    {
                        // A key cap: raised face with a darker lip at the bottom.
                        float r = S(6);
                        Color face = el.Accent ? Ui.SurfaceHot : (isHot ? Ui.SurfaceHot : Ui.Surface);
                        Ui.FillRound(g, Color.FromArgb(18, 18, 21), new RectangleF(el.R.X, el.R.Y + S(2), el.R.Width, el.R.Height), r);
                        Ui.FillRound(g, face, el.R, r);
                        if (el.Accent) Ui.StrokeRound(g, Ui.Accent, Math.Max(1, S(1.5f)), el.R, r);
                        TextRenderer.DrawText(g, el.Text, el.F, el.R, el.Accent ? Ui.Accent : Ui.Fg, center);
                        break;
                    }
                    case Kind.Chip:
                    {
                        float r = el.R.Height / 2f;
                        if (el.Accent)
                        {
                            Ui.StrokeRound(g, isHot ? Ui.Accent : Color.FromArgb(70, 120, 92), Math.Max(1, S(1)), el.R, r);
                            TextRenderer.DrawText(g, el.Text, el.F, el.R, Ui.Accent, center);
                        }
                        else
                        {
                            Ui.FillRound(g, isHot ? Ui.SurfaceHot : Ui.Surface, el.R, r);
                            var textR = new Rectangle(el.R.X + S(10), el.R.Y, el.R.Width - S(30), el.R.Height);
                            TextRenderer.DrawText(g, el.Text, el.F, textR, Ui.Fg, left);
                            var xR = new Rectangle(el.R.Right - S(22), el.R.Y, S(14), el.R.Height);
                            TextRenderer.DrawText(g, el.Sub, el.F, xR, isHot ? Ui.Warn : Ui.Sub, center);
                        }
                        break;
                    }
                    case Kind.Pill:
                        Ui.FillRound(g, Ui.Surface, el.R, el.R.Height / 2f);
                        break;
                    case Kind.Segment:
                    {
                        bool sel = el.GetB();
                        if (sel) Ui.FillRound(g, Ui.SurfaceHot, Rectangle.Inflate(el.R, -S(2), -S(2)), el.R.Height / 2f);
                        TextRenderer.DrawText(g, el.Text, el.F, el.R, sel || isHot ? Ui.Fg : Ui.Sub, center);
                        break;
                    }
                    case Kind.Sep:
                        using (var p = new Pen(Ui.Line))
                            g.DrawLine(p, el.R.Left, el.R.Top, el.R.Right, el.R.Top);
                        break;
                    case Kind.Link:
                        TextRenderer.DrawText(g, el.Text, el.F, el.R, isHot ? Ui.Fg : el.C, el.R.X < S(40) ? left : right);
                        break;
                }
            }
        }

        // ---------- input ----------

        El Hit(Point p)
        {
            for (int i = els.Count - 1; i >= 0; i--)
            {
                var el = els[i];
                if (el.Interactive && el.R.Contains(p)) return el;
            }
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var el = Hit(e.Location);
            if (el == null || e.Button != MouseButtons.Left) return;
            if (el.Kind != Kind.KeyCap) capturing = null;
            switch (el.Kind)
            {
                case Kind.Switch:
                case Kind.Row:
                    el.SetB(!el.GetB());
                    break;
                default:
                    if (el.Click != null) el.Click();
                    break;
            }
            hot = null;
            if (!IsDisposed && Visible) Rebuild();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = Hit(e.Location);
            Cursor = h != null ? Cursors.Hand : Cursors.Default;
            if (h != hot) { hot = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hot != null) { hot = null; Invalidate(); }
        }
    }
}
