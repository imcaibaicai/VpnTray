using System;
using System.IO;
using System.Text;

namespace VpnTray
{
    /// <summary>启动轨迹与崩溃记录，写到程序目录（一定可写），用于排查启动阶段问题。</summary>
    internal static class Diag
    {
        private static readonly object Gate = new object();
        private static string _path;

        public static string Path0
        {
            get
            {
                if (_path != null) return _path;
                try
                {
                    string dir = System.IO.Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                    _path = System.IO.Path.Combine(dir, "trace.log");
                }
                catch { _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VpnTray-trace.log"); }
                return _path;
            }
        }

        public static void T(string message)
        {
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(Path0,
                        DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch { }
        }

        public static void Crash(Exception ex, string where)
        {
            T("!! 异常@" + where + ": " + ex);
        }
    }
}
