// trackpad4game: tray app that turns two keyboard keys into left/right mouse buttons
// and blocks the touchpad's own clicks, so a laptop touchpad can be used for games.
// Built with the .NET Framework compiler that ships with Windows: see build.cmd.
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Trackpad4Game
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--write-icon")
            {
                Logo.WriteIco(args[1]);
                return;
            }

            bool first;
            using (var mutex = new Mutex(true, @"Local\Trackpad4Game.SingleInstance", out first))
            {
                if (!first)
                {
                    // Already running: just pop its panel open.
                    Native.PostMessage(new IntPtr(0xffff), MsgWindow.WM_SHOWPANEL, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => ReportError(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportError(e.ExceptionObject as Exception);
                bool startOn = Array.IndexOf(args, "--on") >= 0;
                bool openPanel = Array.IndexOf(args, "--settings") >= 0;
                Application.Run(new App(startOn, openPanel));
                GC.KeepAlive(mutex);
            }
        }

        static void ReportError(Exception ex)
        {
            if (App.Current != null) App.Current.Emergency();
            string log = Path.Combine(Path.GetDirectoryName(Settings.DefaultPath), "error.log");
            try { File.AppendAllText(log, DateTime.Now + "  " + ex + Environment.NewLine + Environment.NewLine); } catch { }
            MessageBox.Show("trackpad4game stopped because of an error:\n\n" + (ex == null ? "unknown error" : ex.Message)
                + "\n\nDetails were written to " + log, "trackpad4game");
            Environment.Exit(1);
        }
    }
}
