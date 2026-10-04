// trackpad4game: tray app that turns two keyboard keys into left/right mouse buttons
// and optionally blocks the touchpad's own clicks, so a laptop touchpad can be used for games.
// Built with the .NET Framework compiler (C# 5): see build.cmd.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Trackpad4Game
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "Trackpad4Game.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("trackpad4game is already running. Look for its icon in the system tray.", "trackpad4game");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (sender, e) => ReportError(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (sender, e) => ReportError(e.ExceptionObject as Exception);
                bool startOn = Array.IndexOf(args, "--on") >= 0;
                var app = new TrayApp(Settings.Load(), startOn);
                if (Array.IndexOf(args, "--settings") >= 0) app.OpenSettingsWhenReady();
                Application.Run(app);
            }
        }

        static void ReportError(Exception ex)
        {
            string log = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "error.log");
            try { File.AppendAllText(log, DateTime.Now + "  " + ex + Environment.NewLine + Environment.NewLine); } catch (IOException) { }
            MessageBox.Show("Something went wrong:\n\n" + (ex == null ? "unknown error" : ex.Message) + "\n\nDetails were written to " + log, "trackpad4game");
        }
    }

    class Settings
    {
        public Keys LeftKey = Keys.V;
        public Keys RightKey = Keys.B;
        public Keys ToggleKey = Keys.Oemtilde;
        public bool BlockTouchpadClicks = true;
        public bool OnlyInGames = true;
        public string GamePrograms = "javaw.exe, java.exe";
        public bool Beep = true;

        public static string FilePath
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "settings.ini"); }
        }

        public static Settings Load()
        {
            var s = new Settings();
            if (!File.Exists(FilePath)) { s.Save(); return s; }
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0 || raw.TrimStart().StartsWith(";")) continue;
                string name = raw.Substring(0, eq).Trim();
                string value = raw.Substring(eq + 1).Trim();
                try
                {
                    switch (name)
                    {
                        case "LeftKey": s.LeftKey = ParseKey(value, s.LeftKey); break;
                        case "RightKey": s.RightKey = ParseKey(value, s.RightKey); break;
                        case "ToggleKey": s.ToggleKey = ParseKey(value, s.ToggleKey); break;
                        case "BlockTouchpadClicks": s.BlockTouchpadClicks = bool.Parse(value); break;
                        case "OnlyInGames": s.OnlyInGames = bool.Parse(value); break;
                        case "GamePrograms": s.GamePrograms = value; break;
                        case "Beep": s.Beep = bool.Parse(value); break;
                    }
                }
                catch (FormatException) { /* keep the default for a malformed line */ }
            }
            return s;
        }

        static Keys ParseKey(string value, Keys fallback)
        {
            Keys k;
            return Enum.TryParse(value, true, out k) ? k : fallback;
        }

        public void Save()
        {
            File.WriteAllLines(FilePath, new[]
            {
                "; trackpad4game settings. Key names: https://learn.microsoft.com/dotnet/api/system.windows.forms.keys",
                "LeftKey=" + LeftKey,
                "RightKey=" + RightKey,
                "ToggleKey=" + ToggleKey,
                "BlockTouchpadClicks=" + BlockTouchpadClicks,
                "OnlyInGames=" + OnlyInGames,
                "GamePrograms=" + GamePrograms,
                "Beep=" + Beep,
            });
        }

        public Settings Clone() { return (Settings)MemberwiseClone(); }

        public HashSet<string> GameProgramSet()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in GamePrograms.Split(',', ';'))
            {
                string name = part.Trim();
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                if (name.Length > 0) set.Add(name);
            }
            return set;
        }

        public static string KeyName(Keys k)
        {
            switch (k)
            {
                case Keys.Oemtilde: return "` (backtick)";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                default:
                    if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
                    return k.ToString();
            }
        }
    }

    class TrayApp : ApplicationContext
    {
        // Marks the clicks we inject, so they are never mistaken for touchpad clicks.
        static readonly IntPtr Marker = new IntPtr(0x54503447);

        Settings settings;
        HashSet<string> games;
        bool enabled;
        bool suspended;           // true while the settings window is open
        bool leftHeld, rightHeld; // mouse buttons we pressed on behalf of a key
        bool toggleHeld;          // the toggle key's down was swallowed, so swallow its up too
        bool blockedLeftDown, blockedRightDown; // touchpad presses we swallowed; swallow their releases too

        readonly NotifyIcon tray;
        readonly ToolStripMenuItem enabledItem, blockItem, startupItem;
        readonly Icon iconOn, iconOff;
        readonly SynchronizationContext ui;
        readonly Native.HookProc keyboardProc, mouseProc; // kept as fields so the GC never collects them
        IntPtr keyboardHook, mouseHook;

        IntPtr lastForeground = IntPtr.Zero;
        bool lastForegroundIsGame;

        public TrayApp(Settings s, bool startOn)
        {
            settings = s;
            games = s.GameProgramSet();
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            iconOn = MakeIcon(Color.FromArgb(46, 160, 67));
            iconOff = MakeIcon(Color.FromArgb(120, 120, 120));

            var menu = new ContextMenuStrip();
            enabledItem = new ToolStripMenuItem("Enabled", null, delegate { SetEnabled(!enabled); });
            blockItem = new ToolStripMenuItem("Block touchpad clicks while enabled", null, delegate
            {
                settings.BlockTouchpadClicks = !settings.BlockTouchpadClicks;
                settings.Save();
                RefreshUi();
            });
            startupItem = new ToolStripMenuItem("Start with Windows", null, delegate { SetStartup(!IsStartup()); RefreshUi(); });
            menu.Items.Add(enabledItem);
            menu.Items.Add(blockItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Settings...", null, delegate { ShowSettings(); });
            menu.Items.Add("Windows touchpad settings...", null, delegate { Process.Start("ms-settings:devices-touchpad"); });
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });

            tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += (sender, e) => { if (e.Button == MouseButtons.Left) SetEnabled(!enabled); };

            keyboardProc = KeyboardProc;
            mouseProc = MouseProc;
            IntPtr module = Native.GetModuleHandle(null);
            keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyboardProc, module, 0);
            mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, module, 0);
            if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
                MessageBox.Show("Could not install the keyboard/mouse hooks (error " + Marshal.GetLastWin32Error() + ").", "trackpad4game");

            enabled = startOn;
            RefreshUi();
        }

        void SetEnabled(bool on)
        {
            enabled = on;
            if (!on) ReleaseHeldButtons();
            RefreshUi();
            if (settings.Beep)
            {
                int freq = on ? 1200 : 600;
                ThreadPool.QueueUserWorkItem(delegate { try { Console.Beep(freq, 90); } catch { } });
            }
        }

        void RefreshUi()
        {
            tray.Icon = enabled ? iconOn : iconOff;
            string text = "trackpad4game: " + (enabled ? "ON" : "OFF") + " (" + Settings.KeyName(settings.LeftKey) + "=left, "
                + Settings.KeyName(settings.RightKey) + "=right)";
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            enabledItem.Checked = enabled;
            enabledItem.Text = "Enabled  (toggle: " + Settings.KeyName(settings.ToggleKey) + " or click the icon)";
            blockItem.Checked = settings.BlockTouchpadClicks;
            startupItem.Checked = IsStartup();
        }

        public void OpenSettingsWhenReady()
        {
            // A timer fires only once the message loop runs; a Post made before Application.Run can be lost.
            var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += delegate { timer.Dispose(); ShowSettings(); };
            timer.Start();
        }

        void ShowSettings()
        {
            suspended = true;
            ReleaseHeldButtons();
            try
            {
                using (var form = new SettingsForm(settings.Clone()))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        settings = form.Result;
                        games = settings.GameProgramSet();
                        lastForeground = IntPtr.Zero;
                        settings.Save();
                    }
                }
            }
            finally
            {
                suspended = false;
                RefreshUi();
            }
        }

        bool GameInFront()
        {
            if (!settings.OnlyInGames) return true;
            IntPtr hwnd = Native.GetForegroundWindow();
            if (hwnd == lastForeground) return lastForegroundIsGame;
            lastForeground = hwnd;
            lastForegroundIsGame = false;
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            try
            {
                using (var p = Process.GetProcessById((int)pid))
                    lastForegroundIsGame = games.Contains(p.ProcessName);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return lastForegroundIsGame;
        }

        IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                int msg = wParam.ToInt32();
                bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                bool up = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
                Keys key = (Keys)k.vkCode;

                // Always finish a click we started, even if the mode or the focused window changed meanwhile.
                if (up && key == settings.LeftKey && leftHeld) { SendButton(true, false); leftHeld = false; return (IntPtr)1; }
                if (up && key == settings.RightKey && rightHeld) { SendButton(false, false); rightHeld = false; return (IntPtr)1; }
                if (up && key == settings.ToggleKey && toggleHeld) { toggleHeld = false; return (IntPtr)1; }

                if (!suspended && GameInFront())
                {
                    if (key == settings.ToggleKey && down)
                    {
                        if (!toggleHeld)
                        {
                            toggleHeld = true;
                            ui.Post(delegate { SetEnabled(!enabled); }, null);
                        }
                        return (IntPtr)1;
                    }
                    if (enabled && (down || up))
                    {
                        if (key == settings.LeftKey)
                        {
                            if (down && !leftHeld) { SendButton(true, true); leftHeld = true; } // ignore key auto-repeat
                            return (IntPtr)1;
                        }
                        if (key == settings.RightKey)
                        {
                            if (down && !rightHeld) { SendButton(false, true); rightHeld = true; }
                            return (IntPtr)1;
                        }
                    }
                }
            }
            return Native.CallNextHookEx(keyboardHook, nCode, wParam, lParam);
        }

        IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                bool isLeft = msg == Native.WM_LBUTTONDOWN || msg == Native.WM_LBUTTONUP;
                bool isRight = msg == Native.WM_RBUTTONDOWN || msg == Native.WM_RBUTTONUP;
                if (isLeft || isRight)
                {
                    var m = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    bool injected = (m.flags & Native.LLMHF_INJECTED) != 0;
                    bool isDown = msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN;
                    if (!injected)
                    {
                        if (isDown)
                        {
                            if (enabled && !suspended && settings.BlockTouchpadClicks && GameInFront())
                            {
                                if (isLeft) blockedLeftDown = true; else blockedRightDown = true;
                                return (IntPtr)1;
                            }
                        }
                        else
                        {
                            // Swallow only releases whose press we swallowed; any other release must pass,
                            // or the button would stay stuck down.
                            bool pressBlocked = isLeft ? blockedLeftDown : blockedRightDown;
                            if (isLeft) blockedLeftDown = false; else blockedRightDown = false;
                            if (pressBlocked) return (IntPtr)1;
                        }
                    }
                }
            }
            return Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        static void SendButton(bool left, bool down)
        {
            var input = new Native.INPUT();
            input.type = Native.INPUT_MOUSE;
            input.mi.dwFlags = left ? (down ? Native.MOUSEEVENTF_LEFTDOWN : Native.MOUSEEVENTF_LEFTUP)
                                    : (down ? Native.MOUSEEVENTF_RIGHTDOWN : Native.MOUSEEVENTF_RIGHTUP);
            input.mi.dwExtraInfo = Marker;
            Native.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        void ReleaseHeldButtons()
        {
            if (leftHeld) { SendButton(true, false); leftHeld = false; }
            if (rightHeld) { SendButton(false, false); rightHeld = false; }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static bool IsStartup()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue("trackpad4game") != null;
        }

        static void SetStartup(bool on)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key == null) return;
                if (on) key.SetValue("trackpad4game", "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue("trackpad4game", false);
            }
        }

        static Icon MakeIcon(Color fill)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var brush = new SolidBrush(fill))
                using (var path = new GraphicsPath())
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    // A rounded touchpad with two buttons along the bottom.
                    path.AddArc(2, 2, 10, 10, 180, 90);
                    path.AddArc(20, 2, 10, 10, 270, 90);
                    path.AddArc(20, 20, 10, 10, 0, 90);
                    path.AddArc(2, 20, 10, 10, 90, 90);
                    path.CloseFigure();
                    g.FillPath(brush, path);
                    using (var pen = new Pen(Color.White, 2))
                    {
                        g.DrawLine(pen, 4, 21, 28, 21);
                        g.DrawLine(pen, 16, 21, 16, 29);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        protected override void ExitThreadCore()
        {
            ReleaseHeldButtons();
            if (keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboardHook);
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }

    class SettingsForm : Form
    {
        public Settings Result { get; private set; }
        readonly Settings s;
        Button capturing;
        Keys leftKey, rightKey, toggleKey;
        readonly Button leftButton, rightButton, toggleButton;
        readonly CheckBox blockBox, onlyGamesBox, beepBox;
        readonly TextBox gamesBox;

        public SettingsForm(Settings settings)
        {
            s = settings;
            leftKey = s.LeftKey; rightKey = s.RightKey; toggleKey = s.ToggleKey;

            Text = "trackpad4game settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9f);
            TopMost = true;

            var table = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, Padding = new Padding(12), AutoSize = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            leftButton = KeyButton(); rightButton = KeyButton(); toggleButton = KeyButton();
            AddRow(table, "Left click key:", leftButton);
            AddRow(table, "Right click key:", rightButton);
            AddRow(table, "On/off toggle key:", toggleButton);

            blockBox = new CheckBox { Text = "Block touchpad clicks and taps while enabled", Checked = s.BlockTouchpadClicks, AutoSize = true };
            onlyGamesBox = new CheckBox { Text = "Only work while one of these programs is in front:", Checked = s.OnlyInGames, AutoSize = true };
            gamesBox = new TextBox { Text = s.GamePrograms, Dock = DockStyle.Fill };
            beepBox = new CheckBox { Text = "Beep when switching on/off", Checked = s.Beep, AutoSize = true };
            onlyGamesBox.CheckedChanged += delegate { gamesBox.Enabled = onlyGamesBox.Checked; };
            gamesBox.Enabled = onlyGamesBox.Checked;
            AddWide(table, blockBox);
            AddWide(table, onlyGamesBox);
            AddWide(table, gamesBox);
            AddWide(table, beepBox);
            AddWide(table, new Label
            {
                Text = "Minecraft runs as javaw.exe. Click a key button, then press the new key (Esc cancels).",
                AutoSize = true, MaximumSize = new Size(360, 0), ForeColor = SystemColors.GrayText
            });

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var ok = new Button { Text = "OK", AutoSize = true };
            ok.Click += delegate { Accept(); };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            AddWide(table, buttons);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(table);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            UpdateKeyButtons();
        }

        Button KeyButton()
        {
            var b = new Button { Width = 160, Height = 28 };
            b.Click += (sender, e) => { capturing = (Button)sender; UpdateKeyButtons(); };
            return b;
        }

        static void AddRow(TableLayoutPanel t, string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 8, 0) });
            t.Controls.Add(c);
        }

        static void AddWide(TableLayoutPanel t, Control c)
        {
            t.Controls.Add(c);
            t.SetColumnSpan(c, 2);
        }

        void UpdateKeyButtons()
        {
            leftButton.Text = capturing == leftButton ? "Press a key..." : Settings.KeyName(leftKey);
            rightButton.Text = capturing == rightButton ? "Press a key..." : Settings.KeyName(rightKey);
            toggleButton.Text = capturing == toggleButton ? "Press a key..." : Settings.KeyName(toggleKey);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (capturing == null) return base.ProcessCmdKey(ref msg, keyData);
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu) return true; // wait for a real key
            if (key != Keys.Escape)
            {
                if (capturing == leftButton) leftKey = key;
                else if (capturing == rightButton) rightKey = key;
                else toggleKey = key;
            }
            capturing = null;
            UpdateKeyButtons();
            return true;
        }

        void Accept()
        {
            if (leftKey == rightKey || leftKey == toggleKey || rightKey == toggleKey)
            {
                MessageBox.Show(this, "The left, right and toggle keys must all be different.", "trackpad4game");
                return;
            }
            var r = s.Clone();
            r.LeftKey = leftKey; r.RightKey = rightKey; r.ToggleKey = toggleKey;
            r.BlockTouchpadClicks = blockBox.Checked;
            r.OnlyInGames = onlyGamesBox.Checked;
            r.GamePrograms = gamesBox.Text.Trim();
            r.Beep = beepBox.Checked;
            Result = r;
            DialogResult = DialogResult.OK;
        }
    }

    static class Native
    {
        public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
        public const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
        public const int WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205;
        public const uint LLMHF_INJECTED = 0x1;
        public const uint INPUT_MOUSE = 0;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4, MOUSEEVENTF_RIGHTDOWN = 0x8, MOUSEEVENTF_RIGHTUP = 0x10;

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT { public int x, y; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        // MOUSEINPUT is the largest member of the native INPUT union, so it alone gives the right size.
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public MOUSEINPUT mi; }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
