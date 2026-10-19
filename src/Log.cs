using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Trackpad4Game
{
    // Optional debug log (Debug=True in settings.ini, or --debug). Lines are queued and written by a
    // background thread, so logging never slows down the input hooks.
    static class Log
    {
        static BlockingCollection<string> queue;

        public static bool On { get { return queue != null; } }

        public static void Start(string path)
        {
            if (queue != null) return;
            queue = new BlockingCollection<string>();
            var t = new Thread(() =>
            {
                foreach (var line in queue.GetConsumingEnumerable())
                    try { File.AppendAllText(path, line + Environment.NewLine); } catch { }
            }) { IsBackground = true, Name = "log" };
            t.Start();
            Write("---- start, log at " + path);
        }

        public static void Write(string text)
        {
            var q = queue;
            if (q != null) q.Add(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text);
        }
    }
}
