using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Trackpad4Game
{
    class App : ApplicationContext
    {
        public static App Current;
        public readonly Settings S;
        public readonly bool TouchpadSupported;

        readonly Engine engine;
        readonly bool hooked;
        readonly MsgWindow msg;
        readonly NotifyIcon tray;
        readonly MenuItem miToggle, miBlock, miSettings, miQuit;
        Panel panel;
        Osd osd;
        IntPtr iconHandle;
        bool? iconOn;

        public App(bool startOn, bool openPanel)
        {
            Current = this;
            S = Settings.Load();
            if (S.Debug || Array.IndexOf(Environment.GetCommandLineArgs(), "--debug") >= 0)
                Log.Start(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Settings.DefaultPath), "debug.log"));
            Lang.Zh = S.Language == "zh";
            if (Autostart.Get()) Autostart.Set(true); // keep the entry pointing at this exe if it moved
            // Switched-on state is remembered only in game-only mode: in every-app mode, starting
            // with V and B already turned into clicks would be a surprise.
            if (!S.OnlyInGames) S.Enabled = false;
            if (startOn) S.Enabled = true;

            TouchpadSupported = Touchpad.Supported;
            if (S.TouchpadRestore >= 0) RestoreTouchpad(); // the last run ended without putting it back

            engine = new Engine(new WindowsFormsSynchronizationContext());
            engine.SetKeys(S.LeftKey, S.RightKey, S.ToggleKey);
            engine.SetGames(S.Games);
            engine.Enabled = S.Enabled;
            engine.BlockClicks = S.BlockTouchpadClicks;
            engine.OnlyInGames = S.OnlyInGames;
            engine.ToggleRequested += delegate { SetEnabled(!S.Enabled, true); };
            engine.FrontChanged += delegate { UpdateTouchpad(); if (PanelVisible) panel.Rebuild(); };
            hooked = engine.Start();
            Log.Write("started: hooked=" + hooked + " left=" + S.LeftKey + " right=" + S.RightKey + " toggle=" + S.ToggleKey + " enabled=" + S.Enabled
                      + " block=" + S.BlockTouchpadClicks + " onlyGames=" + S.OnlyInGames + " keepMoving=" + S.KeepPointerMoving + " touchpad=" + TouchpadSupported);

            msg = new MsgWindow(this);
            SystemEvents.SessionEnding += OnSessionEnding;

            miToggle = new MenuItem("", delegate { SetEnabled(!S.Enabled, false); });
            miBlock = new MenuItem("", delegate { SetBlock(!S.BlockTouchpadClicks); });
            miSettings = new MenuItem("", delegate { ShowPanel(); });
            miQuit = new MenuItem("", delegate { Exit(); });
            tray = new NotifyIcon
            {
                ContextMenu = new ContextMenu(new[] { miToggle, miBlock, new MenuItem("-"), miSettings, miQuit })
            };
            tray.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) Panel.Toggle();
                else if (e.Button == MouseButtons.Middle) SetEnabled(!S.Enabled, false);
            };

            Refresh();
            tray.Visible = true;
            if (!hooked)
                tray.ShowBalloonTip(6000, "trackpad4game",
                    Lang.T("Could not watch the keyboard and mouse. Restart the app.", "无法监听键盘和鼠标，请重新启动程序。"), ToolTipIcon.Warning);
            else if (S.FirstRun)
            {
                tray.ShowBalloonTip(5000, "trackpad4game",
                    Lang.T("Running in the tray. In your game, " + Settings.KeyName(S.LeftKey) + " and " + Settings.KeyName(S.RightKey)
                           + " click. Click the touchpad icon for settings.",
                           "已在托盘运行。在游戏中按 " + Settings.KeyName(S.LeftKey) + " / " + Settings.KeyName(S.RightKey)
                           + " 即可点击。点击托盘里的触控板图标打开设置。"), ToolTipIcon.None);
                S.Save();
            }
            UpdateTouchpad();
            if (openPanel)
            {
                // A timer fires only once the message loop runs.
                var t = new System.Windows.Forms.Timer { Interval = 100 };
                t.Tick += delegate { t.Dispose(); ShowPanel(); };
                t.Start();
            }
        }

        Panel Panel
        {
            get
            {
                if (panel == null) { panel = new Panel(this); panel.CreateControl(); }
                return panel;
            }
        }

        bool PanelVisible { get { return panel != null && panel.Visible; } }

        public void ShowPanel() { Panel.ShowNear(); }

        public void PanelShown(bool visible)
        {
            // The keys stay normal keys while the panel is open, so a new key can be picked.
            engine.Suspended = visible;
            Log.Write("panel " + (visible ? "shown" : "hidden"));
            if (visible) engine.ReleaseHeld();
        }

        // ---------- state ----------

        public string Status(out Color color)
        {
            color = Ui.Sub;
            if (!hooked) { color = Ui.Warn; return Lang.T("Not working: restart the app", "无法工作：请重新启动程序"); }
            string key = Settings.KeyName(S.ToggleKey);
            if (!S.Enabled)
                return S.OnlyInGames
                    ? Lang.T("Off · press " + key + " in a game to switch on", "已关闭 · 在游戏中按 " + key + " 开启")
                    : Lang.T("Off · press " + key + " to switch on", "已关闭 · 按 " + key + " 开启");
            color = Ui.Accent;
            return S.OnlyInGames
                ? Lang.T("On · works while a game below is in front", "已开启 · 下方游戏在前台时生效")
                : Lang.T("On · works in every app", "已开启 · 在所有程序中生效");
        }

        // The app used just before the panel opened, if it is not on the game list yet.
        public string AddableApp
        {
            get
            {
                string a = engine.LastApp;
                if (a == null || a.Equals("trackpad4game", StringComparison.OrdinalIgnoreCase)) return null;
                return S.Games.Exists(g => g.Equals(a, StringComparison.OrdinalIgnoreCase)) ? null : a;
            }
        }

        public void SetEnabled(bool on, bool fromKey)
        {
            Log.Write("SetEnabled " + on + (fromKey ? " (key)" : " (ui)"));
            S.Enabled = on;
            engine.Enabled = on;
            if (on) engine.Rehook(); else engine.ReleaseHeld();
            if (S.Beep)
            {
                int freq = on ? 1100 : 550;
                ThreadPool.QueueUserWorkItem(delegate { try { Console.Beep(freq, 70); } catch { } });
            }
            if (fromKey) ShowOsd();
            Changed();
        }

        void ShowOsd()
        {
            if (osd == null) { osd = new Osd(); osd.CreateControl(); }
            if (S.Enabled)
                osd.Flash(true, Lang.T("trackpad4game on", "trackpad4game 已开启"),
                          Settings.KeyName(S.LeftKey) + Lang.T(" left click · ", " 左键 · ") + Settings.KeyName(S.RightKey) + Lang.T(" right click", " 右键"));
            else
                osd.Flash(false, Lang.T("trackpad4game off", "trackpad4game 已关闭"),
                          Lang.T("Keys and touchpad are back to normal", "按键和触控板已恢复正常"));
        }

        public void SetBlock(bool on) { S.BlockTouchpadClicks = on; engine.BlockClicks = on; Changed(); }

        public void SetKeepMoving(bool on) { S.KeepPointerMoving = on; Changed(); }

        public void SetOnlyInGames(bool on) { S.OnlyInGames = on; engine.OnlyInGames = on; Changed(); }

        public void SetBeep(bool on) { S.Beep = on; Changed(); }

        public void SetLanguage(string lang)
        {
            S.Language = lang;
            Lang.Zh = lang == "zh";
            if (panel != null) panel.LanguageChanged();
            Changed();
        }

        // which: "left", "right" or "toggle". A key already used by another action swaps places with it.
        public void SetKey(string which, Keys key)
        {
            Keys old = which == "left" ? S.LeftKey : which == "right" ? S.RightKey : S.ToggleKey;
            if (S.LeftKey == key) S.LeftKey = old;
            if (S.RightKey == key) S.RightKey = old;
            if (S.ToggleKey == key) S.ToggleKey = old;
            if (which == "left") S.LeftKey = key;
            else if (which == "right") S.RightKey = key;
            else S.ToggleKey = key;
            engine.SetKeys(S.LeftKey, S.RightKey, S.ToggleKey);
            Changed();
        }

        public void AddGame(string name)
        {
            name = Settings.NormalizeGame(name);
            if (name.Length > 0 && !S.Games.Exists(g => g.Equals(name, StringComparison.OrdinalIgnoreCase))) S.Games.Add(name);
            engine.SetGames(S.Games);
            Changed();
        }

        public void RemoveGame(string name)
        {
            S.Games.RemoveAll(g => g.Equals(name, StringComparison.OrdinalIgnoreCase));
            engine.SetGames(S.Games);
            Changed();
        }

        public void OpenTouchpadSettings()
        {
            try { Process.Start("ms-settings:devices-touchpad"); } catch { }
        }

        void Changed()
        {
            UpdateTouchpad();
            S.Save();
            Refresh();
            if (PanelVisible) panel.Rebuild();
        }

        // ---------- touchpad sensitivity (palm rejection) ----------

        // Palm rejection is turned off only while the click keys can act, and the user's own
        // level is written to the settings file first, so it is put back even after a crash.
        void UpdateTouchpad()
        {
            if (!TouchpadSupported) return;
            bool want = S.KeepPointerMoving && S.Enabled && (!S.OnlyInGames || engine.GameInFront);
            if (want && S.TouchpadRestore < 0)
            {
                int current = Touchpad.Sensitivity;
                if (current <= Touchpad.MostSensitive) return;
                S.TouchpadRestore = current;
                S.Save();
                Log.Write("touchpad sensitivity " + current + " -> 0: " + Touchpad.SetSensitivity(Touchpad.MostSensitive));
            }
            else if (!want && S.TouchpadRestore >= 0) RestoreTouchpad();
        }

        public void RestoreTouchpad()
        {
            if (S.TouchpadRestore < 0) return;
            Log.Write("touchpad sensitivity restored to " + S.TouchpadRestore + ": " + Touchpad.SetSensitivity(S.TouchpadRestore));
            S.TouchpadRestore = -1;
            S.Save();
        }

        // ---------- tray ----------

        void Refresh()
        {
            if (iconOn != S.Enabled)
            {
                iconOn = S.Enabled;
                var old = tray.Icon;
                IntPtr oldHandle = iconHandle;
                tray.Icon = Logo.MakeIcon(SystemInformation.SmallIconSize.Width, S.Enabled, out iconHandle);
                if (old != null) old.Dispose();
                if (oldHandle != IntPtr.Zero) Native.DestroyIcon(oldHandle);
            }
            string text = "trackpad4game · " + (S.Enabled ? Lang.T("On", "已开启") : Lang.T("Off", "已关闭"))
                + " · " + Settings.KeyName(S.LeftKey) + " / " + Settings.KeyName(S.RightKey);
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            miToggle.Text = S.Enabled ? Lang.T("Turn off", "关闭") : Lang.T("Turn on", "开启");
            miBlock.Text = Lang.T("Block touchpad clicks", "屏蔽触控板点击");
            miBlock.Checked = S.BlockTouchpadClicks;
            miSettings.Text = Lang.T("Settings…", "设置…");
            miQuit.Text = Lang.T("Quit", "退出");
        }

        void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            engine.ReleaseHeld();
            RestoreTouchpad();
        }

        // Called on a crash: never leave a button held down or the touchpad changed.
        public void Emergency()
        {
            try { engine.Stop(); } catch { }
            try { RestoreTouchpad(); } catch { }
        }

        public void Exit()
        {
            SystemEvents.SessionEnding -= OnSessionEnding;
            engine.Stop();
            RestoreTouchpad();
            S.Save();
            tray.Visible = false;
            tray.Dispose();
            msg.DestroyHandle();
            if (iconHandle != IntPtr.Zero) Native.DestroyIcon(iconHandle);
            ExitThread();
        }
    }

    // Invisible window that receives the "show panel" ping from a second launch.
    class MsgWindow : NativeWindow
    {
        public static readonly int WM_SHOWPANEL = Native.RegisterWindowMessage("Trackpad4Game.ShowPanel");
        readonly App app;

        public MsgWindow(App app)
        {
            this.app = app;
            CreateHandle(new CreateParams { Caption = "Trackpad4Game" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SHOWPANEL) { app.ShowPanel(); return; }
            base.WndProc(ref m);
        }
    }
}
