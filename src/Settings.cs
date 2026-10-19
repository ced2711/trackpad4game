using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Trackpad4Game
{
    class Settings
    {
        public Keys LeftKey = Keys.V;
        public Keys RightKey = Keys.B;
        public Keys ToggleKey = Keys.Oemtilde;
        public bool Enabled = true;
        public bool BlockTouchpadClicks = true;
        public bool KeepPointerMoving = true;
        public bool OnlyInGames = true;
        public List<string> Games = new List<string> { "javaw", "java", "Minecraft.Windows" };
        public bool Beep = true;
        public string Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en";
        public int TouchpadRestore = -1; // touchpad sensitivity to put back after a crash, or -1
        public bool FirstRun;

        // Portable when a settings.ini sits next to the exe (that is where older versions kept it),
        // otherwise in %APPDATA%, which is always writable.
        public static string DefaultPath
        {
            get
            {
                string portable = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "settings.ini");
                if (File.Exists(portable)) return portable;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "trackpad4game", "settings.ini");
            }
        }

        public static Settings Load() { return Load(DefaultPath); }

        public static Settings Load(string path)
        {
            var s = new Settings();
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception) { s.FirstRun = true; return s; }
            foreach (var raw in lines)
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0 || raw.TrimStart().StartsWith(";")) continue;
                string name = raw.Substring(0, eq).Trim();
                string value = raw.Substring(eq + 1).Trim();
                switch (name)
                {
                    case "LeftKey": s.LeftKey = ParseKey(value, s.LeftKey); break;
                    case "RightKey": s.RightKey = ParseKey(value, s.RightKey); break;
                    case "ToggleKey": s.ToggleKey = ParseKey(value, s.ToggleKey); break;
                    case "Enabled": s.Enabled = ParseBool(value, s.Enabled); break;
                    case "BlockTouchpadClicks": s.BlockTouchpadClicks = ParseBool(value, s.BlockTouchpadClicks); break;
                    case "KeepPointerMoving": s.KeepPointerMoving = ParseBool(value, s.KeepPointerMoving); break;
                    case "OnlyInGames": s.OnlyInGames = ParseBool(value, s.OnlyInGames); break;
                    case "GamePrograms": s.Games = ParseGames(value); break;
                    case "Beep": s.Beep = ParseBool(value, s.Beep); break;
                    case "Language": s.Language = value == "zh" ? "zh" : "en"; break;
                    case "TouchpadRestore":
                        int n;
                        s.TouchpadRestore = int.TryParse(value, out n) && n >= 0 && n <= 4 ? n : -1;
                        break;
                }
            }
            // Three different keys are needed; fall back to the defaults rather than run with a clash.
            if (s.LeftKey == s.RightKey || s.LeftKey == s.ToggleKey || s.RightKey == s.ToggleKey)
            {
                s.LeftKey = Keys.V; s.RightKey = Keys.B; s.ToggleKey = Keys.Oemtilde;
            }
            return s;
        }

        public bool Save() { return Save(DefaultPath); }

        public bool Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, new[]
                {
                    "; trackpad4game settings. Key names: https://learn.microsoft.com/dotnet/api/system.windows.forms.keys",
                    "LeftKey=" + LeftKey,
                    "RightKey=" + RightKey,
                    "ToggleKey=" + ToggleKey,
                    "Enabled=" + Enabled,
                    "BlockTouchpadClicks=" + BlockTouchpadClicks,
                    "KeepPointerMoving=" + KeepPointerMoving,
                    "OnlyInGames=" + OnlyInGames,
                    "GamePrograms=" + string.Join(", ", Games.ConvertAll(g => g + ".exe")),
                    "Beep=" + Beep,
                    "Language=" + Language,
                    "TouchpadRestore=" + TouchpadRestore,
                });
                return true;
            }
            catch (Exception) { return false; }
        }

        static Keys ParseKey(string value, Keys fallback)
        {
            Keys k;
            // Only plain, named keys: no numbers, no modifier combinations like "V, Shift".
            if (value.Length == 0 || char.IsDigit(value[0]) || value.Contains(",")) return fallback;
            if (!Enum.TryParse(value, true, out k) || (k & Keys.Modifiers) != 0 || k == Keys.None) return fallback;
            return k;
        }

        static bool ParseBool(string value, bool fallback)
        {
            bool b;
            if (bool.TryParse(value, out b)) return b;
            if (value == "1") return true;
            if (value == "0") return false;
            return fallback;
        }

        public static List<string> ParseGames(string value)
        {
            var list = new List<string>();
            foreach (var part in value.Split(',', ';'))
            {
                string name = NormalizeGame(part);
                if (name.Length > 0 && !list.Exists(g => g.Equals(name, StringComparison.OrdinalIgnoreCase))) list.Add(name);
            }
            return list;
        }

        public static string NormalizeGame(string name)
        {
            name = (name ?? "").Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            return name;
        }

        public HashSet<string> GameSet() { return new HashSet<string>(Games, StringComparer.OrdinalIgnoreCase); }

        // Short label for a key, as printed on a key cap.
        public static string KeyName(Keys k)
        {
            switch (k)
            {
                case Keys.Oemtilde: return "`";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: case Keys.OemBackslash: return "\\";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.Space: return "Space";
                case Keys.Return: return "Enter";
                case Keys.Back: return "Backspace";
                case Keys.Capital: return "Caps Lock";
                case Keys.Next: return "Page Down";
                case Keys.Prior: return "Page Up";
                default:
                    if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
                    if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num " + (int)(k - Keys.NumPad0);
                    return k.ToString();
            }
        }
    }

    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "trackpad4game";

        public static bool Get()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(Name) != null;
            }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(Name, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(Name, false);
                }
            }
            catch { }
        }
    }
}
