using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace VpnTray
{
    /// <summary>无界面自检：验证配置/凭据/系统代理/开机自启这些容易静默出错的部分。</summary>
    internal static class SelfTest
    {
        private static readonly List<string> Lines = new List<string>();
        private static int _fail;

        private static void Say(string s) { Lines.Add(s); }
        private static void Check(string name, bool ok, string detail)
        {
            if (!ok) _fail++;
            Say((ok ? "[PASS] " : "[FAIL] ") + name + (string.IsNullOrEmpty(detail) ? "" : "  → " + detail));
        }

        public static int Run()
        {
            Diag.T("st: 开始自检");
            Say("==== VpnTray " + AppInfo.Version + " 自检 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====");

            // 保护用户真实配置：自检会写测试值，跑完还原
            byte[] cfgBackup = null;
            try { if (File.Exists(AppSettings.SettingsPath)) cfgBackup = File.ReadAllBytes(AppSettings.SettingsPath); }
            catch { }

            // 0. 进程身份与环境（排查权限问题用）
            try
            {
                System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent();
                Say("[INFO] 身份=" + id.Name + " 已认证=" + id.IsAuthenticated +
                    " System=" + id.IsSystem + " 令牌类型=" + id.Token + " 模拟级别=" + id.ImpersonationLevel);
                StringBuilder sids = new StringBuilder();
                int n = 0;
                foreach (System.Security.Principal.IdentityReference r in id.Groups)
                {
                    if (n++ < 12) sids.Append(r.Value).Append(' ');
                }
                Say("[INFO] 组=" + sids.ToString());
            }
            catch (Exception ex) { Say("[INFO] 身份读取失败 " + ex.Message); }
            Say("[INFO] APPDATA=" + Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            Say("[INFO] 进程=" + System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
            Say("[INFO] 64位进程=" + Environment.Is64BitProcess);

            // 0b. 直接试探 %APPDATA% 写入与注册表写入，报出异常类型
            try
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VpnTray");
                Directory.CreateDirectory(d);
                string f = Path.Combine(d, "probe-01.txt");
                File.WriteAllText(f, "ok");
                File.Delete(f);
                Say("[INFO] 直接写 APPDATA\\VpnTray = OK");
            }
            catch (Exception ex)
            {
                Say("[INFO] 直接写 APPDATA\\VpnTray 失败: " + ex.GetType().FullName + " :: " + ex.Message);
            }
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                {
                    if (k == null) Say("[INFO] 注册表写入: OpenSubKey(writable) 返回 null");
                    else
                    {
                        object cur = k.GetValue("ProxyEnable");
                        k.SetValue("ProxyEnable", cur == null ? 0 : cur, Microsoft.Win32.RegistryValueKind.DWord);
                        Say("[INFO] 注册表 Internet Settings 可写 = OK（读值 " + cur + "）");
                    }
                }
            }
            catch (Exception ex)
            {
                Say("[INFO] 注册表 Internet Settings 写入失败: " + ex.GetType().FullName + " :: " + ex.Message);
            }

            // 1. 数据目录
            string dir = "";
            try
            {
                dir = AppSettings.DataDir;
                string probe = Path.Combine(dir, "st-probe.txt");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                Check("数据目录可写", true, dir);
            }
            catch (Exception ex) { Check("数据目录可写", false, ex.Message); }

            // 2. 配置读写
            try
            {
                AppSettings s = new AppSettings();
                s.Server = "https://example.invalid";
                s.Username = "u1";
                s.BindPort = 1088;
                s.UseFallback = false;
                s.TakeOverProxy = false;
                s.AutoStartMode = "window";
                s.ThemeName = "dark";
                s.PipelinePath = AppSettings.AppDir + "\\pipeline.exe";
                s.Save();
                AppSettings back = AppSettings.Load();
                bool ok = back.Server == s.Server && back.Username == s.Username &&
                          back.BindPort == 1088 && back.UseFallback == false &&
                          back.TakeOverProxy == false && back.AutoStartMode == "window" &&
                          back.ThemeName == "dark";
                Check("配置写入后读回一致", ok, "server=" + back.Server + " port=" + back.BindPort + " theme=" + back.ThemeName);
            }
            catch (Exception ex) { Check("配置写入后读回一致", false, ex.Message); }

            // 3. DPAPI 加解密
            try
            {
                string secret = "P@ssw0rd-测试-123";
                string enc = SecurePassword.Protect(secret);
                string dec = SecurePassword.Unprotect(enc);
                Check("DPAPI 加解密往返", dec == secret, "密文长度=" + enc.Length);
                Check("密文不是明文", enc.IndexOf("P@ssw0rd", StringComparison.Ordinal) < 0, "");
            }
            catch (Exception ex) { Check("DPAPI 加解密往返", false, ex.Message); }

            // 4. 启动参数（密码不得出现在命令行）
            try
            {
                AppSettings s = new AppSettings();
                s.Server = "https://vpn.example.edu.cn";
                s.Username = "user01";
                s.Password = "SECRET";
                s.BindPort = 1088;
                s.UseFallback = true;
                s.FallbackProxy = "socks5h://127.0.0.1:7890";
                string a = s.BuildArguments(1089);
                bool ok = a.IndexOf("--server https://vpn.example.edu.cn", StringComparison.Ordinal) >= 0
                       && a.IndexOf("--username user01", StringComparison.Ordinal) >= 0
                       && a.IndexOf("--bind 127.0.0.1:1089", StringComparison.Ordinal) >= 0
                       && a.IndexOf("--fallback socks5h://127.0.0.1:7890", StringComparison.Ordinal) >= 0
                       && a.IndexOf("SECRET", StringComparison.Ordinal) < 0;
                Check("内核参数拼装正确且不含密码", ok, a);
            }
            catch (Exception ex) { Check("内核参数拼装正确", false, ex.Message); }

            // 5. 系统代理：备份 → 接管 → 校验 → 还原 → 校验完全复原
            Diag.T("st: 5 系统代理");
            try
            {
                ProxyManager.Snapshot before = ProxyManager.Read();
                ProxyManager.Apply(1088, true);
                ProxyManager.Snapshot during = ProxyManager.Read();
                bool took = during.Enable == 1 &&
                            string.Equals(during.Server, "http=127.0.0.1:1088;https=127.0.0.1:1088", StringComparison.OrdinalIgnoreCase);
                Check("接管系统代理写入 http=/https=", took, "ProxyServer=" + during.Server);
                Check("HeldByUs 判定", ProxyManager.HeldByUs(1088), "");
                bool stripped = during.Override.IndexOf("10.*", StringComparison.Ordinal) < 0;
                Check("内网直连例外已清理", stripped, "ProxyOverride=" + during.Override);

                ProxyManager.Restore();
                ProxyManager.Snapshot after = ProxyManager.Read();
                bool same = after.Enable == before.Enable &&
                            after.Server == before.Server &&
                            after.Override == before.Override &&
                            after.PacUrl == before.PacUrl;
                Check("还原后与原设置完全一致", same,
                    "before=" + before.Enable + "/" + before.Server + " after=" + after.Enable + "/" + after.Server);
                Check("备份文件已清理", !ProxyManager.HasBackup, "");
            }
            catch (Exception ex) { Check("系统代理接管与还原", false, ex.Message); }

            // 6. 开机自启
            try
            {
                bool was = Autostart.IsEnabled();
                Autostart.Set(true);
                bool on = Autostart.IsEnabled();
                Autostart.Set(false);
                bool off = Autostart.IsEnabled();
                if (was) Autostart.Set(true);
                Check("开机自启写入/删除", on && !off, "写入=" + on + " 删除后=" + off);
            }
            catch (Exception ex) { Check("开机自启写入/删除", false, ex.Message); }

            // 7. 内核文件与版本
            try
            {
                string p = Path.Combine(AppSettings.AppDir, "pipeline.exe");
                Check("内核文件存在", File.Exists(p), p + " " + (File.Exists(p) ? new FileInfo(p).Length + " 字节" : ""));
            }
            catch (Exception ex) { Check("内核文件存在", false, ex.Message); }

            // 8. Clash 片段生成
            try
            {
                string snip = ProxyManager.BuildClashSnippet(1088, "https://vpn.example.edu.cn");
                bool ok = snip.IndexOf("type: socks5", StringComparison.Ordinal) >= 0 &&
                          snip.IndexOf("port: 1088", StringComparison.Ordinal) >= 0 &&
                          snip.IndexOf("DOMAIN-SUFFIX,vpn.example.edu.cn", StringComparison.Ordinal) >= 0;
                Check("Clash 片段生成", ok, "");
            }
            catch (Exception ex) { Check("Clash 片段生成", false, ex.Message); }

            // 9. 前门协议转换（内核只认 SOCKS5，SOCKS4 与 HTTP 都要在这里被翻译掉）
            Diag.T("st: 9 前门");
            FakeSocks5 fake = null;
            FrontDoor door = null;
            try
            {
                fake = new FakeSocks5();
                if (!fake.Start()) throw new Exception("假上游启动失败");
                int doorPort = FreePort();
                door = new FrontDoor(doorPort, "127.0.0.1", fake.Port);
                string doorError;
                if (!door.Start(out doorError)) throw new Exception("前门启动失败：" + doorError);
                Say("[INFO] 前门测试端口=" + doorPort + " 假上游端口=" + fake.Port);
                Diag.T("st: 9 前门已起 door=" + doorPort + " fake=" + fake.Port);

                // 9a SOCKS4 CONNECT —— Windows socks= 系统代理的真实形态（实测首字节 0x04）
                Diag.T("st: 9a SOCKS4");
                fake.Clear();
                byte[] s4 = new byte[] { 0x04, 0x01, 0x00, 0x50, 0x0A, 0x00, 0x00, 0x01, 0x00 };
                byte[] r4 = Talk(doorPort, s4, 3000);
                bool granted = r4.Length >= 2 && r4[0] == 0x00 && r4[1] == 0x5A;
                string t4 = Encoding.ASCII.GetString(r4);
                Check("前门：SOCKS4 转 SOCKS5 并被目标接受", granted && t4.IndexOf(fake.Banner, StringComparison.Ordinal) >= 0,
                    "回执=" + Hex2(r4) + " 收到=" + Safe(t4));
                Check("前门：SOCKS4 目标地址正确传进隧道", HasSeen(fake, "target=10.0.0.1:80"), Join(fake.Seen));

                // 9b SOCKS5 原样透传
                Diag.T("st: 9b SOCKS5 透传");
                fake.Clear();
                byte[] r5 = Talk(doorPort, new byte[] { 0x05, 0x01, 0x00 }, 2500);
                Check("前门：SOCKS5 原样透传", r5.Length >= 2 && r5[0] == 0x05 && HasSeen(fake, "ver=0x05"),
                    "回执=" + Hex2(r5) + " 上游看到=" + Join(fake.Seen));

                // 9c HTTP CONNECT（HTTPS 的常规形态）
                Diag.T("st: 9c HTTP CONNECT");
                fake.Clear();
                byte[] connect = Encoding.ASCII.GetBytes(
                    "CONNECT campus.example.edu.cn:443 HTTP/1.1\r\nHost: campus.example.edu.cn:443\r\n\r\n");
                byte[] rc = Talk(doorPort, connect, 3000);
                string tc = Encoding.ASCII.GetString(rc);
                Check("前门：HTTP CONNECT 建隧道并回 200",
                    tc.IndexOf("200 Connection Established", StringComparison.Ordinal) >= 0 &&
                    tc.IndexOf(fake.Banner, StringComparison.Ordinal) >= 0, Safe(tc));
                Check("前门：HTTP CONNECT 把域名交给内核（本机不解析 DNS）",
                    HasSeen(fake, "target=campus.example.edu.cn:443"), Join(fake.Seen));

                // 9d HTTP 绝对 URI（明文 HTTP 的常规形态）
                Diag.T("st: 9d HTTP 绝对 URI");
                fake.Clear();
                byte[] get = Encoding.ASCII.GetBytes(
                    "GET http://campus.example.edu.cn/a/b?x=1 HTTP/1.1\r\nHost: campus.example.edu.cn\r\nProxy-Connection: keep-alive\r\n\r\n");
                byte[] rg = Talk(doorPort, get, 3000);
                string tg = Encoding.ASCII.GetString(rg);
                Check("前门：HTTP 绝对 URI 改写成 origin-form",
                    HasSeen(fake, "payload=GET /a/b?x=1 HTTP/1.1"), Join(fake.Seen));
                Check("前门：HTTP 响应原样回传", tg.IndexOf(fake.Banner, StringComparison.Ordinal) >= 0, Safe(tg));
            }
            catch (Exception ex) { Check("前门协议转换", false, ex.Message); }
            finally
            {
                if (door != null) { try { door.Dispose(); } catch { } }
                if (fake != null) { try { fake.Dispose(); } catch { } }
            }

            Say("==== 合计失败 " + _fail + " 项 ====");

            try
            {
                if (cfgBackup != null) File.WriteAllBytes(AppSettings.SettingsPath, cfgBackup);
                else if (File.Exists(AppSettings.SettingsPath)) File.Delete(AppSettings.SettingsPath);
            }
            catch { }

            string outFile = Path.Combine(AppSettings.AppDir, "selftest.log");
            try { File.WriteAllLines(outFile, Lines.ToArray(), new UTF8Encoding(false)); } catch { }
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (string l in Lines) sb.AppendLine(l);
                Console.Out.Write(sb.ToString());
                Console.Out.Flush();
            }
            catch { }
            return _fail == 0 ? 0 : 1;
        }

        // ───────────────────────── 前门测试小工具

        private static int FreePort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        /// <summary>把一段字节发给前门，尽量读完回包（可被 EOF 或读超时结束）。</summary>
        private static byte[] Talk(int port, byte[] send, int waitMs)
        {
            TcpClient c = null;
            try
            {
                c = new TcpClient();
                IAsyncResult ar = c.BeginConnect("127.0.0.1", port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(2000)) return new byte[0];
                c.EndConnect(ar);
                c.NoDelay = true;
                NetworkStream s = c.GetStream();
                s.Write(send, 0, send.Length);
                s.Flush();
                s.ReadTimeout = waitMs;
                MemoryStream ms = new MemoryStream();
                byte[] buf = new byte[2048];
                while (true)
                {
                    int n;
                    try { n = s.Read(buf, 0, buf.Length); }
                    catch { break; }
                    if (n <= 0) break;
                    ms.Write(buf, 0, n);
                }
                return ms.ToArray();
            }
            catch { return new byte[0]; }
            finally { try { if (c != null) c.Close(); } catch { } }
        }

        private static string Hex2(byte[] b)
        {
            if (b == null || b.Length == 0) return "无";
            StringBuilder sb = new StringBuilder();
            int n = Math.Min(b.Length, 4);
            for (int i = 0; i < n; i++) sb.Append("0x").Append(b[i].ToString("X2")).Append(' ');
            return sb.ToString().Trim();
        }

        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "（空）";
            StringBuilder sb = new StringBuilder();
            foreach (char ch in s)
            {
                if (ch == '\r') continue;
                if (ch == '\n') { sb.Append(" | "); continue; }
                if (ch < 32 || ch > 126) continue;
                sb.Append(ch);
                if (sb.Length >= 160) break;
            }
            return sb.ToString();
        }

        private static bool HasSeen(FakeSocks5 fake, string needle)
        {
            foreach (string s in fake.Seen)
            {
                if (s.IndexOf(needle, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        private static string Join(string[] items)
        {
            if (items == null || items.Length == 0) return "（上游没收到东西）";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(" ; ");
                sb.Append(items[i]);
            }
            string s = sb.ToString();
            return s.Length > 220 ? s.Substring(0, 220) + "…" : s;
        }
    }

    /// <summary>自检用的假 SOCKS5 上游：只完成握手并记录它看到的目标与载荷。</summary>
    internal sealed class FakeSocks5 : IDisposable
    {
        private TcpListener _listener;
        private int _port;
        private readonly List<string> _seen = new List<string>();

        public string Banner = "VPNTRAY-TUNNEL-OK";
        public int Port { get { return _port; } }

        public string[] Seen
        {
            get { lock (_seen) { return _seen.ToArray(); } }
        }

        public void Clear() { lock (_seen) { _seen.Clear(); } }

        private void Add(string s) { lock (_seen) { _seen.Add(s); } }

        public bool Start()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start(16);
                _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            }
            catch { return false; }
            Thread t = new Thread(Loop);
            t.IsBackground = true;
            t.Name = "fake-socks5";
            t.Start();
            return true;
        }

        public void Dispose()
        {
            try { if (_listener != null) _listener.Stop(); } catch { }
            _listener = null;
        }

        private void Loop()
        {
            while (true)
            {
                TcpClient c;
                try { c = _listener.AcceptTcpClient(); }
                catch { return; }
                TcpClient cc = c;
                ThreadPool.QueueUserWorkItem(delegate { Serve(cc); });
            }
        }

        private void Serve(TcpClient client)
        {
            try
            {
                client.NoDelay = true;
                NetworkStream s = client.GetStream();
                s.ReadTimeout = 1500;

                int v = s.ReadByte();
                if (v < 0) return;
                Add("ver=0x" + v.ToString("X2"));
                if (v != 0x05) return;

                byte[] nm = new byte[2];
                if (!ReadN(s, nm, 2)) return;
                if (nm[1] > 0)
                {
                    byte[] methods = new byte[nm[1]];
                    if (!ReadN(s, methods, nm[1])) return;
                }
                s.Write(new byte[] { 0x05, 0x00 }, 0, 2);

                byte[] h = new byte[4];
                if (!ReadN(s, h, 4)) return;
                string host;
                if (h[3] == 0x01)
                {
                    byte[] ip = new byte[4];
                    if (!ReadN(s, ip, 4)) return;
                    host = new IPAddress(ip).ToString();
                }
                else if (h[3] == 0x03)
                {
                    int len = s.ReadByte();
                    if (len <= 0) return;
                    byte[] d = new byte[len];
                    if (!ReadN(s, d, len)) return;
                    host = Encoding.ASCII.GetString(d);
                }
                else if (h[3] == 0x04)
                {
                    byte[] ip6 = new byte[16];
                    if (!ReadN(s, ip6, 16)) return;
                    host = new IPAddress(ip6).ToString();
                }
                else return;

                byte[] pb = new byte[2];
                if (!ReadN(s, pb, 2)) return;
                int port = (pb[0] << 8) | pb[1];
                Add("target=" + host + ":" + port);

                s.Write(new byte[] { 0x05, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 0, 10);

                byte[] buf = new byte[1024];
                int n;
                try { n = s.Read(buf, 0, buf.Length); }
                catch { n = 0; }
                if (n > 0)
                {
                    string txt = Encoding.ASCII.GetString(buf, 0, n);
                    int nl = txt.IndexOf("\r\n", StringComparison.Ordinal);
                    Add("payload=" + (nl > 0 ? txt.Substring(0, nl) : txt));
                }
                else Add("payload=<空>");

                byte[] banner = Encoding.ASCII.GetBytes(Banner);
                s.Write(banner, 0, banner.Length);
                s.Flush();
                Thread.Sleep(150);
            }
            catch { }
            finally { try { client.Close(); } catch { } }
        }

        private static bool ReadN(NetworkStream s, byte[] buf, int len)
        {
            int off = 0;
            while (off < len)
            {
                int n;
                try { n = s.Read(buf, off, len - off); }
                catch { return false; }
                if (n <= 0) return false;
                off += n;
            }
            return true;
        }
    }
}
