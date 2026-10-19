// End-to-end test for trackpad4game. Starts a throwaway copy configured with F13/F14/F15 (keys that type
// nothing), sends key presses and fake touchpad clicks, and records what comes out.
// Two hooks of its own surround the app's hooks:
//  - "outer" is installed after the app, so it runs first: it records and swallows the app's clicks.
//  - "inner" is installed before the app, so it runs after it: it records the fake touchpad clicks the
//    app let through, and swallows them.
// Nothing is clicked on screen. The pointer is moved for a moment and then put back.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class HookTest
{
    const int VK_F13 = 0x7C, VK_F14 = 0x7D, VK_F15 = 0x7E;
    static readonly IntPtr AppMarker = new IntPtr(0x54503447), TestMarker = new IntPtr(0x7E57);
    static readonly List<string> events = new List<string>();
    static readonly HookProc outerProc = OuterProc, innerProc = InnerProc;
    static IntPtr outer, inner;

    [STAThread]
    static int Main(string[] args)
    {
        string exe = Path.GetFullPath(args[0]);
        File.WriteAllLines(Path.Combine(Path.GetDirectoryName(exe), "settings.ini"), new[]
        {
            "LeftKey=F13", "RightKey=F14", "ToggleKey=F15", "BlockTouchpadClicks=True", "KeepPointerMoving=False",
            "OnlyInGames=False", "GamePrograms=javaw.exe", "Beep=False", "Language=en",
        });
        inner = SetWindowsHookEx(14, innerProc, GetModuleHandle(null), 0);
        var app = Process.Start(exe, "--on");
        // Keep pumping messages while waiting: Windows silently drops a hook whose thread does not answer.
        var wait = Stopwatch.StartNew();
        while (wait.ElapsedMilliseconds < 1500) { Application.DoEvents(); Thread.Sleep(5); }
        outer = SetWindowsHookEx(14, outerProc, GetModuleHandle(null), 0);

        POINT saved;
        GetCursorPos(out saved);
        var screen = Screen.PrimaryScreen;
        var middle = new POINT { X = screen.Bounds.Left + screen.Bounds.Width / 2, Y = screen.Bounds.Top + screen.Bounds.Height / 3 };
        RECT bar;
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        bool haveTaskbar = taskbar != IntPtr.Zero && GetWindowRect(taskbar, out bar) && (bar.R - bar.L) > 0;
        GetWindowRect(taskbar, out bar);
        var onTaskbar = new POINT { X = (bar.L + bar.R) / 2 - 300, Y = (bar.T + bar.B) / 2 };

        int failures = 0;
        var worker = new Thread(() =>
        {
            SetCursorPos(middle.X, middle.Y);
            failures += Check("left key click", () => { Key(VK_F13, true); Key(VK_F13, true); Key(VK_F13, false); }, "LDOWN", "LUP");
            failures += Check("right key click", () => { Key(VK_F14, true); Key(VK_F14, false); }, "RDOWN", "RUP");
            failures += Check("hold both keys", () => { Key(VK_F13, true); Key(VK_F14, true); Key(VK_F13, false); Key(VK_F14, false); },
                              "LDOWN", "RDOWN", "LUP", "RUP");
            failures += Check("touchpad click blocked while on", () => { Click(true); Click(false); });
            if (haveTaskbar)
            {
                SetCursorPos(onTaskbar.X, onTaskbar.Y);
                failures += Check("taskbar click still works while on", () => { Click(true); }, "TOUCH-LDOWN", "TOUCH-LUP");
                SetCursorPos(middle.X, middle.Y);
            }
            failures += Check("toggle off", () => { Tap(VK_F15); });
            failures += Check("keys do nothing while off", () => { Key(VK_F13, true); Key(VK_F13, false); });
            failures += Check("touchpad click passes while off", () => { Click(true); }, "TOUCH-LDOWN", "TOUCH-LUP");
            failures += Check("toggle on again", () => { Tap(VK_F15); Key(VK_F13, true); Key(VK_F13, false); }, "LDOWN", "LUP");
            failures += Check("held button released on toggle off", () => { Key(VK_F14, true); Tap(VK_F15); Key(VK_F14, false); }, "RDOWN", "RUP");
            failures += Check("blocked press keeps its release blocked after toggle", () =>
            {
                Tap(VK_F15); // on
                Button(true, true);
                Tap(VK_F15); // off
                Button(true, false);
            });
            SetCursorPos(saved.X, saved.Y);
            Application.Exit();
        });
        worker.Start();
        Application.Run();
        UnhookWindowsHookEx(outer);
        UnhookWindowsHookEx(inner);
        try { app.Kill(); } catch { }
        Console.WriteLine(failures == 0 ? "E2E: ALL PASSED" : "E2E: " + failures + " FAILED");
        return failures;
    }

    static int Check(string name, Action act, params string[] expected)
    {
        lock (events) events.Clear();
        act();
        Thread.Sleep(400);
        string got;
        lock (events) got = string.Join(" ", events);
        string want = string.Join(" ", expected);
        bool ok = got == want;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + "  expected [" + want + "] got [" + got + "]");
        return ok ? 0 : 1;
    }

    static void Tap(int vk) { Key(vk, true); Key(vk, false); }

    static void Key(int vk, bool down)
    {
        var input = new INPUT { type = 1 };
        input.u.ki.wVk = (ushort)vk;
        input.u.ki.dwFlags = down ? 0u : 2u;
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        Thread.Sleep(60);
    }

    static void Click(bool left) { Button(left, true); Button(left, false); }

    // A fake touchpad button event. It carries our own marker, which the app does not trust.
    static void Button(bool left, bool down)
    {
        var input = new INPUT { type = 0 };
        input.u.mi.dwFlags = left ? (down ? 0x2u : 0x4u) : (down ? 0x8u : 0x10u);
        input.u.mi.dwExtraInfo = TestMarker;
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        Thread.Sleep(60);
    }

    static string Name(int msg)
    {
        switch (msg)
        {
            case 0x201: return "LDOWN";
            case 0x202: return "LUP";
            case 0x204: return "RDOWN";
            case 0x205: return "RUP";
        }
        return null;
    }

    // Runs before the app: records and swallows the app's own clicks.
    static IntPtr OuterProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var m = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            string name = Name(wParam.ToInt32());
            if (name != null && m.dwExtraInfo == AppMarker)
            {
                lock (events) events.Add(name);
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(outer, nCode, wParam, lParam);
    }

    // Runs after the app: records and swallows the fake touchpad clicks it let through.
    static IntPtr InnerProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var m = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            string name = Name(wParam.ToInt32());
            if (name != null && (m.dwExtraInfo == TestMarker || m.dwExtraInfo == AppMarker))
            {
                if (m.dwExtraInfo == TestMarker) lock (events) events.Add("TOUCH-" + name);
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(inner, nCode, wParam, lParam);
    }

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int L, T, R, B; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
}
