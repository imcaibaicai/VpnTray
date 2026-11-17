using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace VpnTray
{
    internal enum LogKind { Info, Route, Warn, Err }

    /// <summary>内核进程（pipeline.exe）的生命周期、日志与状态机。</summary>
    internal sealed class CoreRunner : IDisposable
    {
        public event Action<ConnState, string> StateChanged;
        public event Action<string, LogKind> LogLine;

        private Process _proc;
        private JobObject _job;
        private Timer _poll;
        private readonly Stopwatch _uptime = new Stopwatch();
        private bool _connectedRaised;
        private bool _userStopped = true;
        private int _port = 1080;
        private int _corePort = 1081;
        private FrontDoor _front;

        private string _lastError = "";
        private int _tooManyErrors;

        private static readonly Regex Ansi = new Regex(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);
        private static readonly Regex TagRx = new Regex(@"\[(APP|LOGIN|AGENT|REQ|UPSTREAM|VPN|NETSTACK|CLI)\]", RegexOptions.Compiled);

        public ConnState State { get; private set; }
        public string LastError { get { return _lastError; } }
        public TimeSpan Uptime { get { return _uptime.Elapsed; } }
        public int Port { get { return _port; } }
        /// <summary>内核真正监听的内部端口（只给前门用，不对外）。</summary>
        public int CorePort { get { return _corePort; } }
        public bool Running { get { return _proc != null && !_proc.HasExited; } }
        public bool UserStopped { get { return _userStopped; } }

        public CoreRunner()
        {
            State = ConnState.Idle;
            _poll = new Timer();
            _poll.Interval = 400;
            _poll.Tick += OnPoll;
        }

        public void Start(AppSettings s)
        {
            Stop(resetState: false);
            _userStopped = false;
            _connectedRaised = false;
            _lastError = "";
            _port = s.BindPort;
            _corePort = FindFreePort(_port + 1);

            if (!File.Exists(s.PipelinePath))
            {
                Fail("找不到内核程序：" + s.PipelinePath);
                return;
            }

            // 先起前门：端口冲突要在这里就失败，不能等内核跑起来才发现
            FrontDoor door = new FrontDoor(_port, "127.0.0.1", _corePort);
            door.Log += delegate(string line, LogKind kind) { Emit("[GUI] " + line, kind); };
            string doorError;
            if (!door.Start(out doorError))
            {
                door.Dispose();
                Fail("本地端口 " + _port + " 被占用，请在设置里换一个端口（" + doorError + "）");
                return;
            }
            _front = door;

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = s.PipelinePath;
            psi.Arguments = s.BuildArguments(_corePort);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.StandardErrorEncoding = new UTF8Encoding(false);
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.WorkingDirectory = Path.GetDirectoryName(s.PipelinePath);
            // 密码走环境变量，不进命令行（命令行会被其他进程看到）
            psi.EnvironmentVariables["SHIEP_PIPELINE_PASSWORD"] = s.Password;

            try
            {
                _proc = new Process();
                _proc.StartInfo = psi;
                _proc.EnableRaisingEvents = true;
                _proc.ErrorDataReceived += OnData;
                _proc.OutputDataReceived += OnData;
                _proc.Exited += OnExited;
                _proc.Start();
                _job = new JobObject();
                _job.Assign(_proc);
                _proc.BeginErrorReadLine();
                _proc.BeginOutputReadLine();
            }
            catch (Exception ex)
            {
                if (_front != null) { _front.Dispose(); _front = null; }
                Fail("启动内核失败：" + ex.Message);
                return;
            }

            _uptime.Reset();
            _uptime.Start();
            SetState(ConnState.Connecting, "正在登录网关…");
            _poll.Start();
            Emit("[GUI] 内核已启动 pid=" + _proc.Id + " 内部端口=" + _corePort, LogKind.Info);
            Emit("[GUI] 前门就绪 127.0.0.1:" + _port + " → 内核 127.0.0.1:" + _corePort + "（HTTP / SOCKS5 均可用）", LogKind.Info);
        }

        public void Stop(bool resetState)
        {
            _userStopped = true;
            _poll.Stop();
            _uptime.Stop();
            if (_front != null) { try { _front.Dispose(); } catch { } _front = null; }
            Process p = _proc;
            _proc = null;
            if (p != null)
            {
                try { if (!p.HasExited) p.Kill(); } catch { }
                try { p.WaitForExit(2500); } catch { }
                try { p.Dispose(); } catch { }
            }
            if (_job != null) { _job.Dispose(); _job = null; }
            if (resetState) SetState(ConnState.Idle, "未连接");
        }

        public void Dispose() { Stop(false); }

        private void Fail(string message)
        {
            _lastError = message;
            _poll.Stop();
            Emit("[GUI] " + message, LogKind.Err);
            SetState(ConnState.Error, message);
        }

        private void OnPoll(object sender, EventArgs e)
        {
            Process p = _proc;
            if (p == null) return;

            if (p.HasExited)
            {
                _poll.Stop();
                _uptime.Stop();
                if (_front != null) { try { _front.Dispose(); } catch { } _front = null; }
                string reason = BuildExitReason();
                if (!_userStopped) SetState(ConnState.Error, reason);
                else SetState(ConnState.Idle, "已断开");
                return;
            }

            if (!_connectedRaised && IsPortListening(_corePort))
            {
                _connectedRaised = true;
                SetState(ConnState.Connected, "代理就绪 127.0.0.1:" + _port);
            }
        }

        private static int FindFreePort(int start)
        {
            for (int i = 0; i < 60; i++)
            {
                int port = start + i;
                if (port > 65535) break;
                if (PortFree(port)) return port;
            }
            // 端口号已经顶到 65535 时向下找，别把 65536 交给内核
            for (int i = 1; i < 60; i++)
            {
                int port = start - i;
                if (port < 1024) break;
                if (PortFree(port)) return port;
            }
            return start;
        }

        private static bool PortFree(int port)
        {
            TcpListener probe = null;
            try
            {
                probe = new TcpListener(System.Net.IPAddress.Loopback, port);
                probe.Start();
                return true;
            }
            catch { return false; }
            finally { try { if (probe != null) probe.Stop(); } catch { } }
        }

        private string BuildExitReason()
        {
            if (!string.IsNullOrEmpty(_lastError)) return _lastError;
            try
            {
                if (_proc != null && _proc.ExitCode == 0) return "内核已退出（正常结束）";
            }
            catch { }
            return "内核异常退出，详情见日志";
        }

        private static bool IsPortListening(int port)
        {
            TcpClient c = null;
            try
            {
                c = new TcpClient();
                IAsyncResult ar = c.BeginConnect("127.0.0.1", port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(150)) return false;
                c.EndConnect(ar);
                return true;
            }
            catch { return false; }
            finally { try { if (c != null) c.Close(); } catch { } }
        }

        private void OnExited(object sender, EventArgs e)
        {
            // 真正的状态迁移交给轮询，避免线程竞争
        }

        private void OnData(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            string line = Ansi.Replace(e.Data, "").TrimEnd();
            if (line.Length == 0) return;

            // 就绪探测（IsPortListening）连一下就关，内核据此报 "socks hello read failed"。
            // 这条没有诊断价值，却会在每次连接时把主界面染红，直接丢掉。
            if (line.IndexOf("socks hello read failed", StringComparison.Ordinal) >= 0) return;

            LogKind kind = LogKind.Info;
            if (line.IndexOf("ERROR", StringComparison.Ordinal) >= 0) kind = LogKind.Err;
            else if (line.IndexOf("WARN", StringComparison.Ordinal) >= 0) kind = LogKind.Warn;
            else if (line.IndexOf("[REQ]", StringComparison.Ordinal) >= 0) kind = LogKind.Route;

            if (kind == LogKind.Err)
            {
                int idx = line.IndexOf("ERROR", StringComparison.Ordinal);
                _lastError = line.Substring(idx).Trim();
                _tooManyErrors++;
            }
            else _tooManyErrors = 0;

            Emit(line, kind);

            if (kind == LogKind.Err && line.IndexOf("[CLI]", StringComparison.Ordinal) >= 0 && _proc != null)
            {
                // CLI 级错误通常意味着内核马上退出，提前把原因映射成人话
                _lastError = HumanizeError(line);
            }
        }

        private static string HumanizeError(string line)
        {
            string l = line.ToLowerInvariant();
            if (l.IndexOf("login failed") >= 0) return "登录失败：账号或密码不对（或网关拒绝）";
            if (l.IndexOf("password is required") >= 0) return "没有拿到密码";
            if (l.IndexOf("address already in use") >= 0) return "端口已被占用，请在设置里换一个端口";
            if (l.IndexOf("certificate") >= 0) return "证书校验出现问题";
            if (l.IndexOf("timed out") >= 0 || l.IndexOf("timeout") >= 0) return "连接网关超时，检查网络";
            return "内核报错：" + line;
        }

        private void Emit(string line, LogKind kind)
        {
            Action<string, LogKind> h = LogLine;
            if (h != null)
            {
                try { h(line, kind); } catch { }
            }
        }

        private void SetState(ConnState s, string detail)
        {
            State = s;
            Action<ConnState, string> h = StateChanged;
            if (h != null)
            {
                try { h(s, detail); } catch { }
            }
        }
    }
}
