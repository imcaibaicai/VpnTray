using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace VpnTray
{
    /// <summary>
    /// 本地前门（协议兼容层）。
    ///
    /// 背景：内核只讲 SOCKS5，而 Windows 的 `socks=` 系统代理在 Chromium 内核里会被解析成
    /// SOCKS4（实测首字节 0x04），于是内核把每个连接都判成 "unsupported socks version"。
    /// 这里让内核退到 127.0.0.1:(端口+1) 只给本机用，由本前门对外，按首字节分流：
    ///   0x05 → SOCKS5，原样透传；
    ///   0x04 → SOCKS4/4a，转成 SOCKS5；
    ///   其它（可打印 ASCII）→ HTTP 代理，CONNECT 建隧道、绝对 URI 改写成 origin-form。
    /// HTTP 路径把主机名原样交给隧道（本机不解析 DNS），内网域名也能命中路由表。
    /// </summary>
    internal sealed class FrontDoor : IDisposable
    {
        private readonly int _listenPort;
        private readonly string _upHost;
        private readonly int _upPort;
        private TcpListener _listener;
        private volatile bool _stopping;
        private int _socks4Count;
        private int _httpCount;
        private int _httpErrorCount;

        public event Action<string, LogKind> Log;

        public int ListenPort { get { return _listenPort; } }
        public int UpstreamPort { get { return _upPort; } }
        public int Socks4Used { get { return _socks4Count; } }
        public int HttpUsed { get { return _httpCount; } }

        public FrontDoor(int listenPort, string upstreamHost, int upstreamPort)
        {
            _listenPort = listenPort;
            _upHost = upstreamHost;
            _upPort = upstreamPort;
        }

        public bool Start(out string error)
        {
            error = null;
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _listenPort);
                _listener.Start(128);
            }
            catch (Exception ex)
            {
                _listener = null;
                error = ex.Message;
                return false;
            }
            Thread t = new Thread(AcceptLoop);
            t.IsBackground = true;
            t.Name = "vpn-front-door";
            t.Start();
            return true;
        }

        public void Stop()
        {
            _stopping = true;
            TcpListener l = _listener;
            _listener = null;
            if (l != null)
            {
                try { l.Stop(); } catch { }
            }
        }

        public void Dispose() { Stop(); }

        private void AcceptLoop()
        {
            while (!_stopping)
            {
                TcpListener l = _listener;
                if (l == null) return;
                TcpClient c;
                try { c = l.AcceptTcpClient(); }
                catch { if (_stopping) return; continue; }
                TcpClient client = c;
                ThreadPool.QueueUserWorkItem(delegate { Handle(client); });
            }
        }

        private void Handle(TcpClient client)
        {
            TcpClient up = null;
            try
            {
                client.NoDelay = true;
                NetworkStream cs = client.GetStream();
                cs.ReadTimeout = 20000;

                int first = cs.ReadByte();
                if (first < 0) return;

                if (first == 0x05)
                {
                    up = ConnectUpstream();
                    if (up == null) return;
                    NetworkStream us = up.GetStream();
                    us.WriteByte(0x05);
                    Pump(client, up);
                    return;
                }

                if (first == 0x04)
                {
                    CountOnce(ref _socks4Count, "检测到 SOCKS4 客户端（Windows socks= 系统代理默认走 SOCKS4），已自动转成 SOCKS5", LogKind.Route);
                    up = HandleSocks4(cs);
                    if (up != null) Pump(client, up);
                    return;
                }

                CountOnce(ref _httpCount, "检测到 HTTP 代理请求，已转成 SOCKS5 隧道", LogKind.Route);
                up = HandleHttp(cs, (byte)first);
                if (up != null) Pump(client, up);
            }
            catch { }
            finally
            {
                try { if (up != null) up.Close(); } catch { }
                try { client.Close(); } catch { }
            }
        }

        // ───────────────────────── 连接与转发

        private TcpClient ConnectUpstream()
        {
            TcpClient up = new TcpClient();
            try
            {
                IAsyncResult ar = up.BeginConnect(_upHost, _upPort, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(5000))
                {
                    up.Close();
                    return null;
                }
                up.EndConnect(ar);
                up.NoDelay = true;
                return up;
            }
            catch
            {
                try { up.Close(); } catch { }
                return null;
            }
        }

        private static void Pump(TcpClient a, TcpClient b)
        {
            NetworkStream sa = a.GetStream();
            NetworkStream sb = b.GetStream();
            try { sa.ReadTimeout = 0; sb.ReadTimeout = 0; } catch { }
            Thread back = new Thread(delegate() { Copy(sb, sa, a); });
            back.IsBackground = true;
            back.Start();
            Copy(sa, sb, b);
            try { back.Join(15000); } catch { }
        }

        private static void Copy(NetworkStream from, NetworkStream to, TcpClient toOwner)
        {
            byte[] buf = new byte[16384];
            try
            {
                while (true)
                {
                    int n = from.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    to.Write(buf, 0, n);
                    to.Flush();
                }
            }
            catch { }
            try { toOwner.Client.Shutdown(SocketShutdown.Send); } catch { }
        }

        // ───────────────────────── SOCKS4 / 4a

        private TcpClient HandleSocks4(NetworkStream cs)
        {
            byte[] head = new byte[7];
            if (!ReadExact(cs, head, 0, 7)) return null;

            int cmd = head[0];
            int port = (head[1] << 8) | head[2];
            bool useDomain = head[3] == 0 && head[4] == 0 && head[5] == 0 && head[6] != 0;
            string clientIp = head[3] + "." + head[4] + "." + head[5] + "." + head[6];

            if (!SkipNullTerminated(cs)) return null;   // userid

            string host;
            if (useDomain)
            {
                host = ReadNullTerminatedString(cs);
                if (string.IsNullOrEmpty(host)) { Reply4(cs, 91); return null; }
            }
            else host = clientIp;

            if (cmd != 1) { Reply4(cs, 91); return null; }   // 只支持 CONNECT

            TcpClient up = ConnectUpstream();
            if (up == null) { Reply4(cs, 91); return null; }

            if (!Socks5Open(up.GetStream(), host, port))
            {
                up.Close();
                Reply4(cs, 91);
                return null;
            }
            Reply4(cs, 90);
            return up;
        }

        private static void Reply4(NetworkStream cs, int code)
        {
            byte[] r = new byte[] { 0x00, (byte)code, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            try { cs.Write(r, 0, r.Length); cs.Flush(); } catch { }
        }

        // ───────────────────────── HTTP 代理

        private TcpClient HandleHttp(NetworkStream cs, byte first)
        {
            byte[] raw = ReadHeaderBlock(cs, first);
            if (raw == null || raw.Length == 0) return null;

            string text = Encoding.ASCII.GetString(raw);
            int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            string hdr = end >= 0 ? text.Substring(0, end) : text;
            byte[] leftover = end >= 0 ? Slice(raw, end + 4) : new byte[0];

            string[] lines = hdr.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0) return null;

            string[] parts = lines[0].Split(' ');
            if (parts.Length < 2)
            {
                ReplyHttp(cs, 400, "请求行无法识别");
                return null;
            }

            string method = parts[0];
            string target = parts[1];
            string version = parts.Length > 2 ? parts[2] : "HTTP/1.1";

            if (string.Equals(method, "CONNECT", StringComparison.OrdinalIgnoreCase))
            {
                string host;
                int port;
                if (!SplitHostPort(target, 443, out host, out port))
                {
                    ReplyHttp(cs, 400, "CONNECT 目标无法解析");
                    return null;
                }

                TcpClient up = ConnectUpstream();
                if (up == null)
                {
                    ReplyHttp(cs, 502, "连不上本地内核（隧道可能尚未就绪）");
                    return null;
                }
                if (!Socks5Open(up.GetStream(), host, port))
                {
                    up.Close();
                    ReplyHttp(cs, 502, "内核拒绝了到 " + host + ":" + port + " 的隧道");
                    return null;
                }
                WriteAll(cs, Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\nProxy-Agent: VpnTray\r\n\r\n"));
                if (leftover.Length > 0) up.GetStream().Write(leftover, 0, leftover.Length);
                return up;
            }

            // 绝对 URI：浏览器走 HTTP 代理的常规形态，改成 origin-form 再交给隧道
            string rest = target;
            if (rest.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) rest = rest.Substring(7);
            else if (rest.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) rest = rest.Substring(8);
            else
            {
                ReplyHttp(cs, 400, "这是一个代理端口：请把浏览器或系统的代理指向它，而不要直接访问。");
                return null;
            }

            int slash = rest.IndexOf('/');
            string authority = slash >= 0 ? rest.Substring(0, slash) : rest;
            string originPath = slash >= 0 ? rest.Substring(slash) : "/";

            string hHost;
            int hPort;
            if (!SplitHostPort(authority, 80, out hHost, out hPort))
            {
                ReplyHttp(cs, 400, "目标主机无法解析");
                return null;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(method).Append(' ').Append(originPath).Append(' ').Append(version).Append("\r\n");
            for (int i = 1; i < lines.Length; i++)
            {
                string ln = lines[i];
                if (ln.Length == 0) continue;
                string low = ln.ToLowerInvariant();
                if (low.StartsWith("proxy-connection:")) continue;
                if (low.StartsWith("connection:")) continue;
                sb.Append(ln).Append("\r\n");
            }
            // 强制一问一答：上游回完就关，本前门按 EOF 收尾，避免长连接里后续绝对 URI 失配
            sb.Append("Connection: close\r\n\r\n");

            TcpClient tu = ConnectUpstream();
            if (tu == null)
            {
                ReplyHttp(cs, 502, "连不上本地内核（隧道可能尚未就绪）");
                return null;
            }
            if (!Socks5Open(tu.GetStream(), hHost, hPort))
            {
                tu.Close();
                ReplyHttp(cs, 502, "内核拒绝了到 " + hHost + ":" + hPort + " 的隧道");
                return null;
            }
            WriteAll(tu.GetStream(), Encoding.UTF8.GetBytes(sb.ToString()));
            if (leftover.Length > 0) tu.GetStream().Write(leftover, 0, leftover.Length);
            return tu;
        }

        private byte[] ReadHeaderBlock(NetworkStream cs, byte first)
        {
            MemoryStream ms = new MemoryStream();
            ms.WriteByte(first);
            byte[] chunk = new byte[4096];
            while (ms.Length <= 131072)
            {
                byte[] cur = ms.ToArray();
                if (IndexOfDoubleCrlf(cur) >= 0) break;
                int n;
                try { n = cs.Read(chunk, 0, chunk.Length); }
                catch { break; }
                if (n <= 0) break;
                ms.Write(chunk, 0, n);
            }
            return ms.ToArray();
        }

        private static int IndexOfDoubleCrlf(byte[] b)
        {
            for (int i = 0; i + 3 < b.Length; i++)
            {
                if (b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10) return i;
            }
            return -1;
        }

        private static byte[] Slice(byte[] src, int from)
        {
            if (from >= src.Length) return new byte[0];
            byte[] r = new byte[src.Length - from];
            Array.Copy(src, from, r, 0, r.Length);
            return r;
        }

        private void ReplyHttp(NetworkStream cs, int code, string message)
        {
            if (Interlocked.Increment(ref _httpErrorCount) == 1)
            {
                Action<string, LogKind> h = Log;
                if (h != null) { try { h("HTTP 代理请求失败：" + message, LogKind.Warn); } catch { } }
            }
            byte[] body = Encoding.UTF8.GetBytes("VpnTray: " + message + "\r\n");
            string head = "HTTP/1.1 " + code + " " + (code == 400 ? "Bad Request" : "Bad Gateway") + "\r\n"
                        + "Content-Type: text/plain; charset=utf-8\r\n"
                        + "Content-Length: " + body.Length + "\r\n"
                        + "Connection: close\r\n\r\n";
            try
            {
                WriteAll(cs, Encoding.ASCII.GetBytes(head));
                WriteAll(cs, body);
            }
            catch { }
        }

        // ───────────────────────── SOCKS5 客户端侧

        private static bool Socks5Open(NetworkStream us, string host, int port)
        {
            WriteAll(us, new byte[] { 0x05, 0x01, 0x00 });

            byte[] rep = new byte[2];
            if (!ReadExact(us, rep, 0, 2)) return false;
            if (rep[0] != 0x05 || rep[1] != 0x00) return false;

            IPAddress ip;
            bool isV4 = IPAddress.TryParse(host, out ip) && ip.AddressFamily == AddressFamily.InterNetwork;

            List<byte> req = new List<byte>(32);
            req.Add(0x05);
            req.Add(0x01);
            req.Add(0x00);
            if (isV4)
            {
                req.Add(0x01);
                req.AddRange(ip.GetAddressBytes());
            }
            else
            {
                byte[] name = Encoding.UTF8.GetBytes(host);
                if (name.Length == 0 || name.Length > 255) return false;
                req.Add(0x03);
                req.Add((byte)name.Length);
                req.AddRange(name);
            }
            req.Add((byte)((port >> 8) & 0xFF));
            req.Add((byte)(port & 0xFF));
            WriteAll(us, req.ToArray());

            byte[] head = new byte[4];
            if (!ReadExact(us, head, 0, 4)) return false;
            if (head[0] != 0x05 || head[1] != 0x00) return false;

            int tail;
            if (head[3] == 0x01) tail = 4 + 2;
            else if (head[3] == 0x04) tail = 16 + 2;
            else if (head[3] == 0x03)
            {
                int l = us.ReadByte();
                if (l < 0) return false;
                tail = l + 2;
            }
            else return false;

            byte[] buf = new byte[tail];
            return ReadExact(us, buf, 0, tail);
        }

        // ───────────────────────── 小工具

        private static bool SplitHostPort(string authority, int defaultPort, out string host, out int port)
        {
            host = authority;
            port = defaultPort;
            if (string.IsNullOrEmpty(authority)) return false;

            if (authority[0] == '[')
            {
                int close = authority.IndexOf(']');
                if (close < 0) return false;
                host = authority.Substring(1, close - 1);
                if (close + 1 < authority.Length && authority[close + 1] == ':')
                {
                    string pt = authority.Substring(close + 2);
                    if (!int.TryParse(pt, out port)) return false;
                }
                return true;
            }

            int colon = authority.LastIndexOf(':');
            if (colon > 0)
            {
                string ph = authority.Substring(0, colon);
                string pt2 = authority.Substring(colon + 1);
                int parsed;
                if (int.TryParse(pt2, out parsed))
                {
                    host = ph;
                    port = parsed;
                }
            }
            return !string.IsNullOrEmpty(host) && port > 0 && port <= 65535;
        }

        private static bool ReadExact(NetworkStream s, byte[] buf, int off, int len)
        {
            while (len > 0)
            {
                int n;
                try { n = s.Read(buf, off, len); }
                catch { return false; }
                if (n <= 0) return false;
                off += n;
                len -= n;
            }
            return true;
        }

        private static bool SkipNullTerminated(NetworkStream s)
        {
            for (int i = 0; i < 512; i++)
            {
                int b;
                try { b = s.ReadByte(); }
                catch { return false; }
                if (b < 0) return false;
                if (b == 0) return true;
            }
            return false;
        }

        private static string ReadNullTerminatedString(NetworkStream s)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 255; i++)
            {
                int b;
                try { b = s.ReadByte(); }
                catch { return null; }
                if (b < 0) return null;
                if (b == 0) return sb.ToString();
                sb.Append((char)b);
            }
            return null;
        }

        private static void WriteAll(NetworkStream s, byte[] data)
        {
            s.Write(data, 0, data.Length);
            s.Flush();
        }

        private void CountOnce(ref int counter, string message, LogKind kind)
        {
            if (Interlocked.Increment(ref counter) != 1) return;
            Action<string, LogKind> h = Log;
            if (h != null)
            {
                try { h(message, kind); } catch { }
            }
        }
    }
}
