// End-to-end test for trackpad4game. Starts a copy configured with F13/F14/F15 (keys that type nothing),
// sends key presses, and records the mouse clicks it injects. Its own mouse hook is installed after the app's,
// so it runs first and swallows those clicks: nothing is clicked on screen.
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
    static readonly IntPtr Marker = new IntPtr(0x54503447);
    static readonly List<string> events = new List<string>();
    static HookProc proc = MouseProc;
    static IntPtr hook;

    [STAThread]
    static int Main(string[] args)
    {
        string exe = Path.GetFullPath(args[0]);
        File.WriteAllLines(Path.Combine(Path.GetDirectoryName(exe), "settings.ini"), new[]
        {
            "LeftKey=F13", "RightKey=F14", "ToggleKey=F15",
            "BlockTouchpadClicks=False", "OnlyInGames=False", "GamePrograms=javaw.exe", "Beep=False",
        });
        var app = Process.Start(exe, "--on");
        Thread.Sleep(1500);
        hook = SetWindowsHookEx(14, proc, GetModuleHandle(null), 0);

        int failures = 0;
        var worker = new Thread(() =>
        {
            failures += Check("left key click", () => { Key(VK_F13, true); Key(VK_F13, true); Key(VK_F13, false); }, "LDOWN", "LUP");
            failures += Check("right key click", () => { Key(VK_F14, true); Key(VK_F14, false); }, "RDOWN", "RUP");
            failures += Check("toggle off", () => { Tap(VK_F15); }); // no clicks expected
            failures += Check("keys do nothing while off", () => { Key(VK_F13, true); Key(VK_F13, false); });
            failures += Check("toggle on again", () => { Tap(VK_F15); Key(VK_F13, true); Key(VK_F13, false); }, "LDOWN", "LUP");
            failures += Check("held button released on toggle off", () => { Key(VK_F14, true); Tap(VK_F15); Key(VK_F14, false); }, "RDOWN", "RUP");
            Application.Exit();
        });
        worker.Start();
        Application.Run();
        UnhookWindowsHookEx(hook);
        try { app.Kill(); } catch { }
        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
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
        input.ki.wVk = (ushort)vk;
        input.ki.dwFlags = down ? 0u : 2u;
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        Thread.Sleep(60);
    }

    static IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var m = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            if (m.dwExtraInfo == Marker)
            {
                string name = null;
                switch (wParam.ToInt32())
                {
                    case 0x201: name = "LDOWN"; break;
                    case 0x202: name = "LUP"; break;
                    case 0x204: name = "RDOWN"; break;
                    case 0x205: name = "RUP"; break;
                }
                if (name != null) lock (events) events.Add(name);
                return (IntPtr)1; // swallow the app's click so nothing on screen is clicked
            }
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT { public int x, y; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    // Padded to the size of the native INPUT union (MOUSEINPUT is its largest member).
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public KEYBDINPUT ki; public long pad; }

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
}
