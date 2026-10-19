using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Trackpad4Game
{
    // Owns the keyboard and mouse hooks. They run on their own thread, which does nothing else:
    // Windows silently removes a low-level hook whose thread is too slow to answer, and on the
    // UI thread a dialog, a redraw or a slow process lookup could cause exactly that.
    class Engine
    {
        // Marks the clicks we inject, so they are never mistaken for touchpad clicks.
        public static readonly IntPtr Marker = new IntPtr(0x54503447);
        const int WM_REHOOK = Native.WM_APP + 1;

        public volatile bool Enabled, Suspended, BlockClicks = true, OnlyInGames = true;
        volatile HashSet<string> games = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        volatile bool gameInFront;
        volatile string frontApp, lastApp;

        public event Action ToggleRequested;  // raised on the UI thread
        public event Action FrontChanged;     // raised on the UI thread

        readonly Mapper mapper = new Mapper();
        readonly SynchronizationContext ui;
        readonly Native.HookProc keyboardProc, mouseProc;     // kept as fields so the GC never collects them
        readonly Native.WinEventProc foregroundProc;
        readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
        IntPtr keyboardHook, mouseHook, foregroundHook;
        Thread thread;
        uint threadId;

        public Engine(SynchronizationContext ui)
        {
            this.ui = ui;
            keyboardProc = KeyboardProc;
            mouseProc = MouseProc;
            foregroundProc = delegate(IntPtr h, uint ev, IntPtr hwnd, int o, int c, uint t, uint time) { UpdateFront(hwnd); };
            mapper.Send = SendButton;
            mapper.Toggle = delegate { Post(ToggleRequested); };
        }

        // True while the click keys act: switched on, not paused, and a game (if required) in front.
        public bool Active { get { return Enabled && !Suspended && (!OnlyInGames || gameInFront); } }
        public bool GameInFront { get { return gameInFront; } }
        public string FrontApp { get { return frontApp; } }
        public string LastApp { get { return lastApp; } }  // the last app used, other than the shell and us

        public void SetKeys(Keys left, Keys right, Keys toggle)
        {
            lock (mapper)
            {
                mapper.ReleaseHeld();
                mapper.LeftKey = left; mapper.RightKey = right; mapper.ToggleKey = toggle;
            }
        }

        public void SetGames(IEnumerable<string> list)
        {
            games = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            string app = frontApp;
            gameInFront = app != null && games.Contains(app);
        }

        public void ReleaseHeld() { lock (mapper) mapper.ReleaseHeld(); }

        // Installs the hooks again: a cheap repair in case Windows dropped them.
        public void Rehook() { if (threadId != 0) Native.PostThreadMessage(threadId, WM_REHOOK, IntPtr.Zero, IntPtr.Zero); }

        public bool Start()
        {
            using (var ready = new ManualResetEvent(false))
            {
                thread = new Thread(() => Run(ready)) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "hooks" };
                thread.Start();
                ready.WaitOne(5000);
            }
            // Foreground tracking looks up process names, which can be slow, so it stays off the hook thread.
            foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                                                    IntPtr.Zero, foregroundProc, 0, 0, 0); // WINEVENT_OUTOFCONTEXT
            UpdateFront(Native.GetForegroundWindow());
            return hooked;
        }

        volatile bool hooked;

        public void Stop()
        {
            ReleaseHeld();
            if (foregroundHook != IntPtr.Zero) { Native.UnhookWinEvent(foregroundHook); foregroundHook = IntPtr.Zero; }
            if (threadId != 0) Native.PostThreadMessage(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            if (thread != null) thread.Join(1000);
        }

        // The hook thread. Windows removes a low-level hook for good, without telling anyone, when one call
        // takes too long (a few hundred ms). So this thread runs nothing but the hooks, the hook code is
        // warmed up before the first real event, and the hooks are renewed every few seconds in case
        // Windows dropped one anyway (for example while the PC was very busy).
        void Run(ManualResetEvent ready)
        {
            threadId = Native.GetCurrentThreadId();
            Native.MSG msg;
            Native.PeekMessage(out msg, IntPtr.Zero, 0, 0, 0); // creates this thread's message queue
            WarmUp();
            hooked = Hook();
            Native.SetTimer(IntPtr.Zero, IntPtr.Zero, 2000, IntPtr.Zero);
            ready.Set();
            while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WM_REHOOK || msg.message == Native.WM_TIMER)
                {
                    Hook();
                    if (Log.On && msg.message == Native.WM_TIMER && keyEvents + buttonEvents > 0)
                    {
                        Log.Write("hooks alive: " + keyEvents + " key events, " + buttonEvents + " button events in the last 2 s");
                        keyEvents = buttonEvents = 0;
                    }
                }
            }
            Unhook(keyboardHook, mouseHook);
        }

        int keyEvents, buttonEvents; // for the debug log only

        // Installs fresh hooks before removing the old ones, so no key or button event is missed in between.
        // An event both see is harmless: the newer hook runs first, and anything it acts on, it swallows.
        bool Hook()
        {
            IntPtr oldKeyboard = keyboardHook, oldMouse = mouseHook;
            IntPtr module = Native.GetModuleHandle(null);
            IntPtr kb = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyboardProc, module, 0);
            int kbErr = Marshal.GetLastWin32Error();
            IntPtr ms = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, module, 0);
            int msErr = Marshal.GetLastWin32Error();
            if (kb != IntPtr.Zero) keyboardHook = kb;
            if (ms != IntPtr.Zero) mouseHook = ms;
            Unhook(kb != IntPtr.Zero ? oldKeyboard : IntPtr.Zero, ms != IntPtr.Zero ? oldMouse : IntPtr.Zero);
            if (kb == IntPtr.Zero || ms == IntPtr.Zero || oldKeyboard == IntPtr.Zero)
                Log.Write("hooks installed: keyboard=" + (kb != IntPtr.Zero) + " (" + kbErr + ") mouse=" + (ms != IntPtr.Zero) + " (" + msErr + ")");
            return keyboardHook != IntPtr.Zero && mouseHook != IntPtr.Zero;
        }

        static void Unhook(IntPtr kb, IntPtr ms)
        {
            if (kb != IntPtr.Zero) Native.UnhookWindowsHookEx(kb);
            if (ms != IntPtr.Zero) Native.UnhookWindowsHookEx(ms);
        }

        // Runs every code path the hooks use once, so the first real event does not wait for the JIT compiler.
        void WarmUp()
        {
            var probe = new Mapper();
            probe.OnKey(Keys.V, true, true, true); probe.OnKey(Keys.V, false, true, true);
            probe.OnButton(true, true, true); probe.OnButton(true, false, false);
            IsExempt(new Native.POINT(0, 0));
            IntPtr buf = Marshal.AllocHGlobal(64);
            try
            {
                for (int i = 0; i < 64; i += 4) Marshal.WriteInt32(buf, i, 0);
                KeyboardProc(-1, IntPtr.Zero, buf);
                MouseProc(-1, IntPtr.Zero, buf);
                ReadKey(buf); ReadButton(buf);
            }
            finally { Marshal.FreeHGlobal(buf); }
            Marshal.SizeOf(typeof(Native.INPUT));
            foreach (var m in new[] { "KeyboardProc", "MouseProc", "IsExempt", "SendButton", "StateText" })
            {
                var mi = typeof(Engine).GetMethod(m, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                                                      | System.Reflection.BindingFlags.NonPublic);
                if (mi != null) System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(mi.MethodHandle);
            }
        }

        // Field offsets of the hook structs, read directly: much faster than Marshal.PtrToStructure.
        static readonly int KeyFlagsOffset = (int)Marshal.OffsetOf(typeof(Native.KBDLLHOOKSTRUCT), "flags");
        static readonly int MouseFlagsOffset = (int)Marshal.OffsetOf(typeof(Native.MSLLHOOKSTRUCT), "flags");
        static readonly int MouseExtraOffset = (int)Marshal.OffsetOf(typeof(Native.MSLLHOOKSTRUCT), "dwExtraInfo");

        static Keys ReadKey(IntPtr p) { return (Keys)Marshal.ReadInt32(p, 0); }

        static Native.POINT ReadButton(IntPtr p) { return new Native.POINT(Marshal.ReadInt32(p, 0), Marshal.ReadInt32(p, 4)); }

        static readonly HashSet<string> ShellApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "LockApp", "ApplicationFrameHost" };

        void UpdateFront(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            string name = Native.ProcessName(pid);
            // Store apps (such as older Minecraft Bedrock) live inside an ApplicationFrameHost frame.
            if (name != null && name.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            {
                uint inner = Native.ChildProcess(hwnd, pid);
                if (inner != 0) { pid = inner; name = Native.ProcessName(pid); }
            }
            frontApp = name;
            Log.Write("foreground: " + name);
            gameInFront = name != null && games.Contains(name);
            if (name != null && pid != ownPid && !ShellApps.Contains(name)) lastApp = name;
            Post(FrontChanged);
        }

        void Post(Action a)
        {
            if (a != null) ui.Post(delegate { a(); }, null);
        }

        IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                bool up = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
                if (down || up)
                {
                    bool toggleAllowed = !Suspended && (!OnlyInGames || gameInFront);
                    bool swallow;
                    Keys key = ReadKey(lParam);
                    keyEvents++;
                    lock (mapper)
                    {
                        swallow = mapper.OnKey(key, down, Active, toggleAllowed);
                        if (Log.On && (key == mapper.LeftKey || key == mapper.RightKey || key == mapper.ToggleKey))
                            Log.Write("key " + key + (down ? " down" : " up") + " flags=" + Marshal.ReadInt32(lParam, KeyFlagsOffset)
                                      + " " + StateText() + " swallow=" + swallow);
                    }
                    if (swallow) return (IntPtr)1;
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
                    IntPtr extra = Marshal.ReadIntPtr(lParam, MouseExtraOffset);
                    // Only our own clicks pass by marker. Some touchpad drivers inject their taps,
                    // so the "injected" flag alone does not mean the click came from us.
                    if (extra != Marker)
                    {
                        var pt = ReadButton(lParam);
                        bool isDown = msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN;
                        bool block = isDown && BlockClicks && Active && !IsExempt(pt);
                        bool swallow;
                        buttonEvents++;
                        lock (mapper) swallow = mapper.OnButton(isLeft, isDown, block);
                        if (Log.On)
                        {
                            uint pid;
                            string cls = Native.RootClassName(Native.WindowFromPoint(pt), out pid);
                            Log.Write("button " + (isLeft ? "L" : "R") + (isDown ? " down" : " up") + " at " + pt.X + "," + pt.Y
                                      + " over " + cls + " flags=" + Marshal.ReadInt32(lParam, MouseFlagsOffset) + " extra=0x" + extra.ToInt64().ToString("X")
                                      + " " + StateText() + " block=" + block + " swallow=" + swallow);
                        }
                        if (swallow) return (IntPtr)1;
                    }
                }
            }
            return Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        string StateText()
        {
            return "[enabled=" + Enabled + " suspended=" + Suspended + " onlyGames=" + OnlyInGames + " gameFront=" + gameInFront
                + " front=" + frontApp + " active=" + Active + " blockClicks=" + BlockClicks + "]";
        }

        // Clicks on the taskbar, the tray and our own windows always work, so the touchpad
        // can still switch the app off or reach other windows.
        static readonly HashSet<string> ExemptClasses = new HashSet<string>
        {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
            "XamlExplorerHostIslandWindow",
        };

        bool IsExempt(Native.POINT pt)
        {
            IntPtr hwnd = Native.WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return false;
            uint pid;
            string cls = Native.RootClassName(hwnd, out pid);
            return pid == ownPid || ExemptClasses.Contains(cls);
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
    }
}
