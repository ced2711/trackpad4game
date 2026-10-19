// Unit tests for the key/click rules (Mapper) and the settings file. Compiled together with
// src\Mapper.cs and src\Settings.cs by run-tests.cmd; needs no hooks and touches nothing on screen.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Trackpad4Game;

static class UnitTests
{
    static int failures;

    static int Main()
    {
        MapperTests();
        SettingsTests();
        Console.WriteLine(failures == 0 ? "UNIT: ALL PASSED" : "UNIT: " + failures + " FAILED");
        return failures;
    }

    static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : "  " + detail));
        if (!ok) failures++;
    }

    static Mapper NewMapper(List<string> log)
    {
        var m = new Mapper();
        m.Send = (left, down) => log.Add((left ? "L" : "R") + (down ? "DOWN" : "UP"));
        m.Toggle = () => log.Add("TOGGLE");
        return m;
    }

    static void Expect(string name, List<string> log, params string[] want)
    {
        string got = string.Join(" ", log), w = string.Join(" ", want);
        Check(name, got == w, "expected [" + w + "] got [" + got + "]");
        log.Clear();
    }

    static void MapperTests()
    {
        var log = new List<string>();
        var m = NewMapper(log);

        bool s1 = m.OnKey(Keys.V, true, true, true), s2 = m.OnKey(Keys.V, true, true, true), s3 = m.OnKey(Keys.V, false, true, true);
        Check("click key is swallowed (down, repeat, up)", s1 && s2 && s3);
        Expect("hold V = one press, auto-repeat ignored", log, "LDOWN", "LUP");

        m.OnKey(Keys.B, true, true, true); m.OnKey(Keys.B, false, true, true);
        Expect("B = right click", log, "RDOWN", "RUP");

        m.OnKey(Keys.V, true, true, true); m.OnKey(Keys.B, true, true, true);
        m.OnKey(Keys.V, false, true, true); m.OnKey(Keys.B, false, true, true);
        Expect("both buttons held together", log, "LDOWN", "RDOWN", "LUP", "RUP");

        Check("keys pass while inactive", !m.OnKey(Keys.V, true, false, true) && !m.OnKey(Keys.V, false, false, true));
        Expect("no clicks while inactive", log);

        m.OnKey(Keys.V, true, true, true);
        Check("release swallowed after becoming inactive", m.OnKey(Keys.V, false, false, false));
        Expect("a started click is always finished", log, "LDOWN", "LUP");

        Check("other keys pass", !m.OnKey(Keys.W, true, true, true) && !m.OnKey(Keys.W, false, true, true));

        bool t1 = m.OnKey(Keys.Oemtilde, true, false, true), t2 = m.OnKey(Keys.Oemtilde, true, false, true), t3 = m.OnKey(Keys.Oemtilde, false, false, true);
        Check("toggle key swallowed (down, repeat, up)", t1 && t2 && t3);
        Expect("toggle fires once per press", log, "TOGGLE");

        Check("toggle key passes where not allowed", !m.OnKey(Keys.Oemtilde, true, false, false) && !m.OnKey(Keys.Oemtilde, false, false, false));
        Expect("no toggle where not allowed", log);

        m.OnKey(Keys.Oemtilde, true, false, true);
        Check("toggle up swallowed even if no longer allowed", m.OnKey(Keys.Oemtilde, false, false, false));
        log.Clear();

        m.OnKey(Keys.B, true, true, true); m.ReleaseHeld(); m.ReleaseHeld();
        Check("key up after ReleaseHeld passes", !m.OnKey(Keys.B, false, false, true));
        Expect("ReleaseHeld releases once", log, "RDOWN", "RUP");

        m.OnKey(Keys.V, true, true, true);
        m.ReleaseHeld();
        m.LeftKey = Keys.F;
        Check("new key works after rebinding", m.OnKey(Keys.F, true, true, true) && m.OnKey(Keys.F, false, true, true));
        Check("old key passes after rebinding", !m.OnKey(Keys.V, true, true, true));
        log.Clear();
        m.LeftKey = Keys.V;

        // Touchpad buttons
        Check("touchpad press blocked", m.OnButton(true, true, true));
        Check("its release blocked", m.OnButton(true, false, false));
        Check("touchpad press passes when not blocking", !m.OnButton(true, true, false));
        Check("its release passes", !m.OnButton(true, false, true));
        m.OnButton(false, true, true);
        Check("left release passes while right press is blocked", !m.OnButton(true, false, false));
        Check("right release blocked", m.OnButton(false, false, false));
        Check("second release passes (no stuck state)", !m.OnButton(false, false, false));
    }

    static void SettingsTests()
    {
        string dir = Path.Combine(Path.GetTempPath(), "t4g-unit-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "sub", "settings.ini");
        try
        {
            var missing = Settings.Load(path);
            Check("missing file gives defaults", missing.FirstRun && missing.LeftKey == Keys.V && missing.RightKey == Keys.B
                  && missing.ToggleKey == Keys.Oemtilde && missing.OnlyInGames && missing.Games.Contains("javaw"));

            var s = new Settings { LeftKey = Keys.F, RightKey = Keys.G, ToggleKey = Keys.F8, Enabled = false, BlockTouchpadClicks = false,
                                   KeepPointerMoving = false, OnlyInGames = false, Beep = false, Language = "zh", TouchpadRestore = 3 };
            s.Games = Settings.ParseGames("javaw.exe, Game.EXE; game ,  ,other");
            Check("game list normalised", string.Join("|", s.Games) == "javaw|Game|other", string.Join("|", s.Games));
            Check("save creates folders", s.Save(path));
            var r = Settings.Load(path);
            Check("round trip", !r.FirstRun && r.LeftKey == Keys.F && r.RightKey == Keys.G && r.ToggleKey == Keys.F8 && !r.Enabled
                  && !r.BlockTouchpadClicks && !r.KeepPointerMoving && !r.OnlyInGames && !r.Beep && r.Language == "zh"
                  && r.TouchpadRestore == 3 && string.Join("|", r.Games) == "javaw|Game|other");
            Check("game set ignores case", r.GameSet().Contains("GAME") && r.GameSet().Contains("Javaw"));

            // An old (v1) file: True/False values, no newer keys.
            File.WriteAllLines(path, new[] { "LeftKey=Z", "RightKey=X", "ToggleKey=Oemtilde", "BlockTouchpadClicks=False",
                                             "OnlyInGames=True", "GamePrograms=javaw.exe, java.exe", "Beep=True" });
            var old = Settings.Load(path);
            Check("old file still reads", old.LeftKey == Keys.Z && old.RightKey == Keys.X && !old.BlockTouchpadClicks
                  && old.KeepPointerMoving && old.TouchpadRestore == -1);

            File.WriteAllLines(path, new[] { "LeftKey=12", "RightKey=V, Shift", "ToggleKey=Nonsense", "Beep=maybe", "TouchpadRestore=9", "garbage", "=x" });
            var bad = Settings.Load(path);
            Check("malformed values fall back", bad.LeftKey == Keys.V && bad.RightKey == Keys.B && bad.ToggleKey == Keys.Oemtilde
                  && bad.Beep && bad.TouchpadRestore == -1);

            File.WriteAllLines(path, new[] { "LeftKey=G", "RightKey=G" });
            var clash = Settings.Load(path);
            Check("clashing keys fall back to defaults", clash.LeftKey == Keys.V && clash.RightKey == Keys.B);

            Check("key names", Settings.KeyName(Keys.Oemtilde) == "`" && Settings.KeyName(Keys.D5) == "5"
                  && Settings.KeyName(Keys.NumPad3) == "Num 3" && Settings.KeyName(Keys.V) == "V");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
