using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VpnTray
{
    internal enum ConnState { Idle, Connecting, Connected, Error }

    /// <summary>简单的 key=value 配置文件（不做 INI 分段，一坨平铺更省事）。</summary>
    internal sealed class KvFile
    {
        private readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Path { get; private set; }

        public KvFile(string path) { Path = path; }

        public void Load()
        {
            _map.Clear();
            if (!File.Exists(Path)) return;
            foreach (string raw in File.ReadAllLines(Path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                _map[k] = v;
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            List<string> lines = new List<string>();
            lines.Add("# 自动生成，可手工编辑；密码字段为 Windows DPAPI 密文，勿手改");
            foreach (KeyValuePair<string, string> kv in _map)
                lines.Add(kv.Key + " = " + kv.Value);
            File.WriteAllLines(Path, lines.ToArray(), new UTF8Encoding(false));
        }

        public string Get(string key, string def)
        {
            string v;
            return _map.TryGetValue(key, out v) ? v : def;
        }
        public void Set(string key, string value) { _map[key] = value == null ? "" : value; }
        public bool GetBool(string key, bool def)
        {
            string v = Get(key, null);
            if (v == null) return def;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        public void SetBool(string key, bool value) { Set(key, value ? "1" : "0"); }
        public int GetInt(string key, int def)
        {
            int r;
            return int.TryParse(Get(key, null), out r) ? r : def;
        }
        public void SetInt(string key, int v) { Set(key, v.ToString()); }
    }

    internal sealed class AppSettings
    {
        public string Server = "";
        public string Username = "";
        public string PasswordEnc = "";
        public int BindPort = 1080;
        public bool UseFallback = true;
        public string FallbackProxy = "socks5h://127.0.0.1:7890";
        public bool TakeOverProxy = true;
        public bool AutoHealProxy = true;
        public bool StripLanBypass = true;
        public bool AutoStart = false;
        public string AutoStartMode = "tray";     // tray | window
        public bool AutoConnect = false;          // 自启后自动连接
        public bool AutoReconnect = true;
        public bool CloseToTray = true;
        public string ThemeName = "light";        // light | dark | system
        public string PipelinePath = "";
        public bool NotifyState = true;
        public bool LogToFile = true;

        /// <summary>运行时凭据（明文只存在内存，绝不落盘）。</summary>
        public string Password = "";

        private static string _dataDir;

        /// <summary>数据目录：优先 %APPDATA%，不可写时逐级回退（受控环境/权限异常时仍能工作）。</summary>
        public static string DataDir
        {
            get
            {
                if (_dataDir != null) return _dataDir;
                string[] candidates = new string[]
                {
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VpnTray"),
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnTray"),
                    System.IO.Path.Combine(AppDir, "data")
                };
                foreach (string c in candidates)
                {
                    try
                    {
                        Directory.CreateDirectory(c);
                        string probe = System.IO.Path.Combine(c, ".write-probe");
                        File.WriteAllText(probe, "ok", new UTF8Encoding(false));
                        File.Delete(probe);
                        _dataDir = c;
                        return c;
                    }
                    catch { }
                }
                _dataDir = System.IO.Path.Combine(AppDir, "data");
                try { Directory.CreateDirectory(_dataDir); } catch { }
                return _dataDir;
            }
        }

        public static string SettingsPath { get { return System.IO.Path.Combine(DataDir, "settings.ini"); } }
        public static string LogDir { get { return System.IO.Path.Combine(DataDir, "logs"); } }

        public static string AppDir
        {
            get { return System.IO.Path.GetDirectoryName(Application.ExecutablePath); }
        }

        public static AppSettings Load()
        {
            KvFile f = new KvFile(SettingsPath);
            f.Load();
            AppSettings s = new AppSettings();
            s.Server = f.Get("Server", "");
            s.Username = f.Get("Username", "");
            s.PasswordEnc = f.Get("PasswordEnc", "");
            s.BindPort = f.GetInt("BindPort", 1080);
            s.UseFallback = f.GetBool("UseFallback", true);
            s.FallbackProxy = f.Get("FallbackProxy", "socks5h://127.0.0.1:7890");
            s.TakeOverProxy = f.GetBool("TakeOverProxy", true);
            s.AutoHealProxy = f.GetBool("AutoHealProxy", true);
            s.StripLanBypass = f.GetBool("StripLanBypass", true);
            s.AutoStart = f.GetBool("AutoStart", false);
            s.AutoStartMode = f.Get("AutoStartMode", "tray");
            s.AutoConnect = f.GetBool("AutoConnect", false);
            s.AutoReconnect = f.GetBool("AutoReconnect", true);
            s.CloseToTray = f.GetBool("CloseToTray", true);
            s.ThemeName = f.Get("ThemeName", "light");
            s.PipelinePath = f.Get("PipelinePath", "");
            s.NotifyState = f.GetBool("NotifyState", true);
            s.LogToFile = f.GetBool("LogToFile", true);
            if (string.IsNullOrEmpty(s.PipelinePath))
                s.PipelinePath = System.IO.Path.Combine(AppDir, "pipeline.exe");
            return s;
        }

        public void Save()
        {
            KvFile f = new KvFile(SettingsPath);
            f.Set("Server", Server);
            f.Set("Username", Username);
            f.Set("PasswordEnc", PasswordEnc);
            f.SetInt("BindPort", BindPort);
            f.SetBool("UseFallback", UseFallback);
            f.Set("FallbackProxy", FallbackProxy);
            f.SetBool("TakeOverProxy", TakeOverProxy);
            f.SetBool("AutoHealProxy", AutoHealProxy);
            f.SetBool("StripLanBypass", StripLanBypass);
            f.SetBool("AutoStart", AutoStart);
            f.Set("AutoStartMode", AutoStartMode);
            f.SetBool("AutoConnect", AutoConnect);
            f.SetBool("AutoReconnect", AutoReconnect);
            f.SetBool("CloseToTray", CloseToTray);
            f.Set("ThemeName", ThemeName);
            f.Set("PipelinePath", PipelinePath);
            f.SetBool("NotifyState", NotifyState);
            f.SetBool("LogToFile", LogToFile);
            f.Save();
            TryPersistPassword();
        }

        private void TryPersistPassword()
        {
            if (Password == null) return;
            try
            {
                PasswordEnc = string.IsNullOrEmpty(Password) ? "" : SecurePassword.Protect(Password);
                KvFile f = new KvFile(SettingsPath);
                f.Load();
                f.Set("PasswordEnc", PasswordEnc);
                f.Save();
            }
            catch { }
        }

        public bool TryLoadPassword()
        {
            try
            {
                if (string.IsNullOrEmpty(PasswordEnc)) { Password = ""; return false; }
                Password = SecurePassword.Unprotect(PasswordEnc);
                return !string.IsNullOrEmpty(Password);
            }
            catch { Password = ""; return false; }
        }

        public bool Complete
        {
            get
            {
                return !string.IsNullOrWhiteSpace(Server)
                    && !string.IsNullOrWhiteSpace(Username)
                    && !string.IsNullOrEmpty(Password);
            }
        }

        /// <summary>内核参数（密码走环境变量，不进命令行）。bindPort 是内核内部端口，不是对外端口。</summary>
        public string BuildArguments(int bindPort)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("--server ").Append(Quote(Server));
            sb.Append(" --username ").Append(Quote(Username));
            sb.Append(" --bind 127.0.0.1:").Append(bindPort);
            if (UseFallback && !string.IsNullOrWhiteSpace(FallbackProxy))
                sb.Append(" --fallback ").Append(Quote(FallbackProxy.Trim()));
            return sb.ToString();
        }

        public string BuildArguments()
        {
            return BuildArguments(BindPort);
        }

        private static string Quote(string s)
        {
            if (s == null) return "\"\"";
            s = s.Trim();
            if (s.IndexOf(' ') >= 0 || s.IndexOf('"') >= 0)
                return "\"" + s.Replace("\"", "\\\"") + "\"";
            return s;
        }
    }

    internal static class SecurePassword
    {
        public static string Protect(string plain)
        {
            byte[] raw = Encoding.UTF8.GetBytes(plain);
            byte[] enc = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(enc);
        }

        public static string Unprotect(string b64)
        {
            byte[] enc = Convert.FromBase64String(b64);
            byte[] raw = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(raw);
        }
    }

    /// <summary>从本机已装的官方 EasyConnect 痕迹里嗅出 VPN 服务器地址，避免把学校域名硬编码进源码。</summary>
    internal static class ServerDetector
    {
        private static readonly Regex UrlRx = new Regex(@"https?://[A-Za-z0-9][A-Za-z0-9\.\-]{2,80}", RegexOptions.Compiled);

        public static string Detect()
        {
            Dictionary<string, int> hits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // 1) 客户端日志（官方客户端会把登录目标写进日志）
            try
            {
                string logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sangfor", "SSL", "Log");
                if (Directory.Exists(logDir))
                {
                    foreach (string file in Directory.GetFiles(logDir, "*.log"))
                    {
                        try
                        {
                            using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                            {
                                for (int i = 0; i < 4000 && !sr.EndOfStream; i++)
                                {
                                    string line = sr.ReadLine();
                                    if (line == null) break;
                                    foreach (Match m in UrlRx.Matches(line))
                                    {
                                        string u = m.Value.TrimEnd('.', '/', '"', '\'');
                                        if (u.IndexOf("sangfor.com", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                        if (u.IndexOf("microsoft.com", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                        int c;
                                        hits.TryGetValue(u, out c);
                                        hits[u] = c + 1;
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // 2) 注册表里官方客户端留下的字符串值
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(@"Software\Sangfor"))
                {
                    CollectRegistry(root, hits, 0);
                }
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(@"Software\Sangfor\SSL"))
                {
                    CollectRegistry(root, hits, 0);
                }
            }
            catch { }

            string best = null;
            int bestCount = 0;
            foreach (KeyValuePair<string, int> kv in hits)
            {
                if (kv.Value > bestCount) { best = kv.Key; bestCount = kv.Value; }
            }
            return best == null ? "" : best;
        }

        private static void CollectRegistry(RegistryKey key, Dictionary<string, int> hits, int depth)
        {
            if (key == null || depth > 3) return;
            try
            {
                foreach (string name in key.GetValueNames())
                {
                    string v = null;
                    try { v = key.GetValue(name) as string; } catch { }
                    if (string.IsNullOrEmpty(v) || v.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    foreach (Match m in UrlRx.Matches(v))
                    {
                        string u = m.Value.TrimEnd('.', '/', '"', '\'');
                        if (u.IndexOf("sangfor.com", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        int c;
                        hits.TryGetValue(u, out c);
                        hits[u] = c + 1;
                    }
                }
                foreach (string sub in key.GetSubKeyNames())
                {
                    using (RegistryKey sk = key.OpenSubKey(sub))
                        CollectRegistry(sk, hits, depth + 1);
                }
            }
            catch { }
        }
    }
}
