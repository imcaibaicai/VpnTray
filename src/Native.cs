using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using System.Diagnostics;

namespace VpnTray
{
    internal static class NativeMethods
    {
        [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        public const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        public const int INTERNET_OPTION_REFRESH = 37;

        /// <summary>把一个窗口画到指定 DC（不受遮挡影响），用于布局自检截图。</summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        public const int JobObjectExtendedLimitInformation = 9;
        public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        public static void EnableKillOnClose(IntPtr job)
        {
            JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            int len = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr ptr = Marshal.AllocHGlobal(len);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)len);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
    }

    /// <summary>把内核进程挂进 Job 对象：GUI 一旦退出（含崩溃），内核随之被杀，不留孤儿。</summary>
    internal sealed class JobObject : IDisposable
    {
        private IntPtr _handle;

        public JobObject()
        {
            _handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
            if (_handle != IntPtr.Zero) NativeMethods.EnableKillOnClose(_handle);
        }

        public void Assign(Process p)
        {
            if (_handle == IntPtr.Zero || p == null) return;
            try { NativeMethods.AssignProcessToJobObject(_handle, p.Handle); } catch { }
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                try { NativeMethods.CloseHandle(_handle); } catch { }
                _handle = IntPtr.Zero;
            }
        }
    }

    /// <summary>系统代理（WinINET）接管、还原与崩溃恢复。</summary>
    internal static class ProxyManager
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        public sealed class Snapshot
        {
            public int Enable;
            public string Server = "";
            public string Override = "";
            public string PacUrl = "";
        }

        private static string BackupPath
        {
            get { return Path.Combine(AppSettings.DataDir, "proxy-backup.ini"); }
        }

        public static Snapshot Read()
        {
            Snapshot s = new Snapshot();
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath, false))
                {
                    if (k == null) return s;
                    object en = k.GetValue("ProxyEnable");
                    s.Enable = (en is int) ? (int)en : 0;
                    s.Server = (k.GetValue("ProxyServer") as string) ?? "";
                    s.Override = (k.GetValue("ProxyOverride") as string) ?? "";
                    s.PacUrl = (k.GetValue("AutoConfigURL") as string) ?? "";
                }
            }
            catch { }
            return s;
        }

        private static void Write(Snapshot s, bool writePac)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath, true))
            {
                if (k == null) throw new InvalidOperationException("无法打开 Internet Settings 注册表键");
                k.SetValue("ProxyEnable", s.Enable, RegistryValueKind.DWord);
                if (string.IsNullOrEmpty(s.Server)) k.DeleteValue("ProxyServer", false);
                else k.SetValue("ProxyServer", s.Server, RegistryValueKind.String);

                if (string.IsNullOrEmpty(s.Override)) k.DeleteValue("ProxyOverride", false);
                else k.SetValue("ProxyOverride", s.Override, RegistryValueKind.String);

                if (writePac)
                {
                    if (string.IsNullOrEmpty(s.PacUrl)) k.DeleteValue("AutoConfigURL", false);
                    else k.SetValue("AutoConfigURL", s.PacUrl, RegistryValueKind.String);
                }
            }
            Notify();
        }

        public static void Notify()
        {
            try
            {
                NativeMethods.InternetSetOption(IntPtr.Zero, NativeMethods.INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                NativeMethods.InternetSetOption(IntPtr.Zero, NativeMethods.INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            }
            catch { }
        }

        public static bool HasBackup { get { return File.Exists(BackupPath); } }

        private static void SaveBackup(Snapshot s)
        {
            KvFile f = new KvFile(BackupPath);
            f.SetInt("Enable", s.Enable);
            f.Set("Server", s.Server);
            f.Set("Override", s.Override);
            f.Set("PacUrl", s.PacUrl);
            f.SetBool("Dirty", true);
            f.Save();
        }

        private static Snapshot LoadBackup()
        {
            KvFile f = new KvFile(BackupPath);
            f.Load();
            Snapshot s = new Snapshot();
            s.Enable = f.GetInt("Enable", 1);
            s.Server = f.Get("Server", "");
            s.Override = f.Get("Override", "");
            s.PacUrl = f.Get("PacUrl", "");
            return s;
        }

        /// <summary>
        /// 系统代理值。必须用 http=/https=，不能用 socks=：
        /// Windows 的 `socks=` 在 Chromium 与 WinINET 下会被解析成 SOCKS4（实测首字节 0x04），
        /// 而内核只讲 SOCKS5，会逐帧报 "unsupported socks version"。
        /// 走 HTTP 代理还有个额外好处：主机名原样进隧道，由内核按路由表解析 DNS，内网域名才不会漏解析。
        /// </summary>
        public static string ExpectedServer(int port)
        {
            string hostPort = "127.0.0.1:" + port;
            return "http=" + hostPort + ";https=" + hostPort;
        }

        /// <summary>把系统代理指向本地前门。会先备份原始设置。</summary>
        public static void Apply(int port, bool stripLanBypass)
        {
            Snapshot cur = Read();
            if (!HasBackup) SaveBackup(cur);

            Snapshot target = new Snapshot();
            target.Enable = 1;
            target.Server = ExpectedServer(port);
            target.Override = stripLanBypass ? StripLan(cur.Override) : cur.Override;
            target.PacUrl = "";
            Write(target, true);
        }

        public static void Restore()
        {
            if (!HasBackup) return;
            Snapshot s = LoadBackup();
            Write(s, true);
            TryDeleteBackup();
        }

        /// <summary>进程上次异常退出后，下次启动时把代理还原回去。</summary>
        public static bool RecoverIfDirty()
        {
            if (!HasBackup) return false;
            KvFile f = new KvFile(BackupPath);
            f.Load();
            if (!f.GetBool("Dirty", true)) return false;
            Restore();
            return true;
        }

        public static bool TryDeleteBackup()
        {
            try { if (File.Exists(BackupPath)) File.Delete(BackupPath); return true; }
            catch { return false; }
        }

        /// <summary>去掉「内网直连例外」，否则校园内网 IP 会绕过代理直连而失败。</summary>
        private static string StripLan(string overrides)
        {
            if (string.IsNullOrEmpty(overrides)) return "";
            List<string> keep = new List<string>();
            foreach (string raw in overrides.Split(';'))
            {
                string tok = raw.Trim();
                if (tok.Length == 0) continue;
                string t = tok.ToLowerInvariant();
                if (IsLanPattern(t)) continue;
                keep.Add(tok);
            }
            return string.Join(";", keep.ToArray());
        }

        private static bool IsLanPattern(string t)
        {
            // 10.* 段
            if (t.StartsWith("10.") || t.StartsWith("10*")) return true;
            // 192.168.* 段
            if (t.StartsWith("192.168.") || t.StartsWith("192.168*")) return true;
            // 172.16 - 172.31 段，注意通配符写法（如 172.2* 覆盖 172.20-172.29）
            if (t.StartsWith("172."))
            {
                string rest = t.Substring(4);
                int i = 0;
                while (i < rest.Length && rest[i] >= '0' && rest[i] <= '9') i++;
                string digits = rest.Substring(0, i);
                if (digits.Length == 0) return true;          // 172.* 全覆盖
                if (digits.Length == 1) return true;           // 172.1* / 172.2* / 172.3* 都会与 16-31 重叠
                int n;
                if (int.TryParse(digits.Substring(0, 2), out n) && n >= 16 && n <= 31) return true;
            }
            return false;
        }

        /// <summary>当前系统代理是否仍由我们持有（用来发现 FlClash 等把开关抢回去）。</summary>
        public static bool HeldByUs(int port)
        {
            Snapshot cur = Read();
            return cur.Enable == 1 &&
                   string.Equals(cur.Server, ExpectedServer(port), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>备用模式：给用户一段可贴进 Clash 的配置片段。</summary>
        public static string BuildClashSnippet(int port, string server)
        {
            string host = server;
            try
            {
                Uri u = new Uri(server);
                host = u.Host;
            }
            catch { }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# 粘到 Clash / FlClash 配置里：把校园流量转给本地 " + port);
            sb.AppendLine("proxies:");
            sb.AppendLine("  - name: campus-vpn");
            sb.AppendLine("    type: socks5");
            sb.AppendLine("    server: 127.0.0.1");
            sb.AppendLine("    port: " + port);
            sb.AppendLine("    udp: false");
            sb.AppendLine();
            sb.AppendLine("proxy-groups:   # 已有 proxy-groups 时只拷 rules 即可");
            sb.AppendLine("  - name: campus");
            sb.AppendLine("    type: select");
            sb.AppendLine("    proxies: [campus-vpn]");
            sb.AppendLine();
            sb.AppendLine("rules:");
            if (host.Length > 0)
            {
                sb.AppendLine("  - DOMAIN-SUFFIX," + host + ",campus");
                string root = host;
                int dot = root.IndexOf('.');
                if (dot > 0) root = root.Substring(dot + 1);
                if (root.Length > 0 && root != host)
                    sb.AppendLine("  - DOMAIN-SUFFIX," + root + ",campus");
            }
            sb.AppendLine("  # 校内资源若走 IP，按需补充这些网段（示例，按你学校实际情况改）");
            sb.AppendLine("  - IP-CIDR,10.0.0.0/8,campus,no-resolve");
            sb.AppendLine("  - IP-CIDR,172.16.0.0/12,campus,no-resolve");
            sb.AppendLine("  - IP-CIDR,192.168.0.0/16,campus,no-resolve");
            return sb.ToString();
        }
    }

    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "VpnTray";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    return k.GetValue(ValueName) != null;
                }
            }
            catch { return false; }
        }

        public static void Set(bool enable)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null) throw new InvalidOperationException("无法写入开机自启注册表项");
                if (enable)
                    k.SetValue(ValueName, "\"" + AppSettings.AppDir.TrimEnd('\\') + "\\" + Path.GetFileName(System.Windows.Forms.Application.ExecutablePath) + "\" --autostart", RegistryValueKind.String);
                else
                    k.DeleteValue(ValueName, false);
            }
        }
    }

    internal static class LogFile
    {
        private static readonly object Gate = new object();

        public static void Append(string line, bool enabled)
        {
            if (!enabled) return;
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(AppSettings.LogDir);
                    string path = Path.Combine(AppSettings.LogDir, "app-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                    File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch { }
        }
    }
}
