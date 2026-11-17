using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace VpnTray
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        private static void Main(string[] args)
        {
            bool autostart = false;
            bool openSettings = false;
            bool selftest = false;
            bool layouttest = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--autostart", StringComparison.OrdinalIgnoreCase)) autostart = true;
                if (string.Equals(args[i], "--settings", StringComparison.OrdinalIgnoreCase)) openSettings = true;
                if (string.Equals(args[i], "--selftest", StringComparison.OrdinalIgnoreCase)) selftest = true;
                if (string.Equals(args[i], "--layouttest", StringComparison.OrdinalIgnoreCase)) layouttest = true;
            }

            if (selftest)
            {
                Environment.ExitCode = SelfTest.Run();
                return;
            }

            if (layouttest)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                int code = LayoutTest.Run();
                Environment.Exit(code);
                return;
            }

            bool createdNew;
            _mutex = new Mutex(true, "VpnTray.SingleInstance", out createdNew);
            if (!createdNew)
            {
                try
                {
                    using (EventWaitHandle ev = EventWaitHandle.OpenExisting("VpnTray.Show"))
                        ev.Set();
                }
                catch { }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;

            TrayApp app = null;
            try
            {
                Diag.T("main: 创建 TrayApp");
                app = new TrayApp(autostart, openSettings);                Diag.T("main: TrayApp 就绪，进入消息循环");
                Application.Run(app);
                Diag.T("main: 消息循环结束");
            }
            catch (Exception ex)
            {
                Diag.Crash(ex, "Program.Main");
                try { ProxyManager.Restore(); } catch { }
                MessageBox.Show("启动失败：\n" + ex.Message + "\n\n详情见程序目录下 trace.log",
                    "校园 VPN", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (app != null)
                {
                    try { app.Shutdown(); } catch { }
                }
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            try { LogFile.Append("[GUI] 未处理异常: " + e.Exception, true); } catch { }
            try { ProxyManager.Restore(); } catch { }
            MessageBox.Show("程序出现异常，已尝试还原系统代理：\n" + e.Exception.Message,
                "校园 VPN", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            try { LogFile.Append("[GUI] 致命异常: " + e.ExceptionObject, true); } catch { }
            try { ProxyManager.Restore(); } catch { }
        }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private readonly AppSettings _s;
        private readonly CoreRunner _core;
        private NotifyIcon _tray;
        private MainForm _main;
        private SettingsForm _settings;
        private readonly Dictionary<ConnState, Icon> _icons = new Dictionary<ConnState, Icon>();
        private readonly WinFormsTimer _watchdog;
        private readonly WinFormsTimer _reconnect;
        private readonly WinFormsTimer _uptimeTick;
        private EventWaitHandle _showEvent;
        private int _reconnectAttempt;
        private bool _proxyApplied;
        private bool _healWarned;
        private bool _shuttingDown;

        public AppSettings Settings { get { return _s; } }
        public MainForm Main { get { return _main; } }
        public ConnState CoreState { get { return _core.State; } }
        public TimeSpan CoreUptime { get { return _core.Uptime; } }
        public bool CloseToTray { get { return _s.CloseToTray; } }

        public TrayApp(bool autostart, bool openSettings)
        {
            Diag.T("ctor: 开始 数据目录=" + AppSettings.DataDir);
            _s = AppSettings.Load();
            Diag.T("ctor: 配置已载入 server=" + _s.Server + " user=" + _s.Username + " port=" + _s.BindPort);
            _s.TryLoadPassword();
            Diag.T("ctor: 密码载入 has=" + (_s.Password.Length > 0));

            // 上次异常退出留下的代理备份先还原，避免用户网络被留在接管状态
            bool recovered = false;
            try { recovered = ProxyManager.RecoverIfDirty(); } catch { }

            Theme.Current = Theme.Resolve(_s.ThemeName);
            Diag.T("ctor: 主题已定");

            // 首次运行：从已装的官方客户端痕迹里嗅服务器地址
            if (string.IsNullOrEmpty(_s.Server))
            {
                string detected = ServerDetector.Detect();
                Diag.T("ctor: 服务器嗅探结果=" + detected);
                if (!string.IsNullOrEmpty(detected))
                {
                    _s.Server = detected;
                    _s.Save();
                }
            }

            BuildIcons();
            Diag.T("ctor: 图标已生成");

            _core = new CoreRunner();
            _core.StateChanged += OnCoreState;
            _core.LogLine += OnCoreLog;

            _main = new MainForm(this);
            Diag.T("ctor: 主界面已构建");
            _main.Icon = GetIcon(ConnState.Idle);
            _main.UpdateAccount(_s);
            _main.SyncAutostart(Autostart.IsEnabled());
            _main.SetState(ConnState.Idle, recovered ? "已还原上次异常退出的系统代理" : "未连接");

            _tray = new NotifyIcon();
            _tray.Icon = GetIcon(ConnState.Idle);
            _tray.Text = "校园 VPN · 未连接";
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ShowMain(); };
            _tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ShowMain();
            };
            RebuildTrayMenu();

            _watchdog = new WinFormsTimer();
            _watchdog.Interval = 3000;
            _watchdog.Tick += OnWatchdog;

            _reconnect = new WinFormsTimer();
            _reconnect.Interval = 5000;
            _reconnect.Tick += OnReconnectTick;

            _uptimeTick = new WinFormsTimer();
            _uptimeTick.Interval = 1000;
            _uptimeTick.Tick += delegate { if (_main != null && _main.Visible) _main.TickUptime(); };
            _uptimeTick.Start();

            StartShowListener();

            if (recovered) _main.AppendLog("[GUI] 检测到上次异常退出，已把系统代理还原为原设置", LogKind.Warn);

            bool showWindow = true;
            if (autostart)
            {
                showWindow = _s.AutoStartMode == "window";
                if (_s.AutoConnect && _s.Complete) BeginInvokeLater(delegate { Connect(); });
                else if (_s.AutoConnect && !_s.Complete) BeginInvokeLater(delegate { ShowMain(); ShowSettings(); });
            }

            if (showWindow) _main.Show(); else _main.Hide();
            if (openSettings) BeginInvokeLater(delegate { ShowMain(); ShowSettings(); });
            Diag.T("ctor: 完成 visible=" + _main.Visible + " autostart=" + autostart);
        }

        private void BeginInvokeLater(Action a)
        {
            WinFormsTimer t = new WinFormsTimer();
            t.Interval = 800;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                try { a(); } catch { }
            };
            t.Start();
        }

        private void StartShowListener()
        {
            try
            {
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "VpnTray.Show");
                Thread th = new Thread(delegate()
                {
                    while (true)
                    {
                        try
                        {
                            if (!_showEvent.WaitOne()) break;
                            if (_shuttingDown) break;
                            try
                            {
                                if (_main != null) _main.BeginInvoke(new Action(ShowMain));
                            }
                            catch { }
                        }
                        catch { break; }
                    }
                });
                th.IsBackground = true;
                th.Start();
            }
            catch { }
        }

        // ───────────────────────── 托盘图标
        private void BuildIcons()
        {
            _icons[ConnState.Idle] = MakeIcon(Color.FromArgb(0x8C, 0x96, 0xA3));
            _icons[ConnState.Connecting] = MakeIcon(Color.FromArgb(0xE0, 0x9A, 0x1E));
            _icons[ConnState.Connected] = MakeIcon(Color.FromArgb(0x1F, 0xA5, 0x5A));
            _icons[ConnState.Error] = MakeIcon(Color.FromArgb(0xD9, 0x3A, 0x3A));
        }

        private static Icon MakeIcon(Color color)
        {
            int size = 32;
            using (Bitmap bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (SolidBrush b = new SolidBrush(color))
                        g.FillEllipse(b, 2, 2, size - 5, size - 5);
                    using (Pen p = new Pen(Color.FromArgb(235, 255, 255, 255), 2.4f))
                        g.DrawEllipse(p, 8.5f, 8.5f, size - 18, size - 18);
                    using (SolidBrush b2 = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
                        g.FillEllipse(b2, size / 2 - 3, size / 2 - 3, 6, 6);
                }
                IntPtr h = bmp.GetHicon();
                return Icon.FromHandle(h);
            }
        }

        private Icon GetIcon(ConnState s)
        {
            Icon ic;
            if (_icons.TryGetValue(s, out ic)) return ic;
            return _icons[ConnState.Idle];
        }

        private void RebuildTrayMenu()
        {
            if (_tray == null) return;
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = Ui.F(9f, FontStyle.Regular);

            bool busy = _core.State == ConnState.Connected || _core.State == ConnState.Connecting;
            ToolStripMenuItem conn = new ToolStripMenuItem(busy ? "断开连接" : "连接");
            conn.Font = Ui.F(9f, FontStyle.Bold);
            conn.Click += delegate { ToggleConnection(); };
            menu.Items.Add(conn);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem show = new ToolStripMenuItem("打开主界面");
            show.Click += delegate { ShowMain(); };
            menu.Items.Add(show);

            ToolStripMenuItem set = new ToolStripMenuItem("设置…");
            set.Click += delegate { ShowSettings(); };
            menu.Items.Add(set);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem autos = new ToolStripMenuItem("开机自启");
            autos.Checked = Autostart.IsEnabled();
            autos.Click += delegate { SetAutostart(!Autostart.IsEnabled()); };
            menu.Items.Add(autos);

            ToolStripMenuItem proxy = new ToolStripMenuItem("接管系统代理");
            proxy.Checked = _s.TakeOverProxy;
            proxy.Click += delegate
            {
                _s.TakeOverProxy = !_s.TakeOverProxy;
                _s.Save();
                if (_s.TakeOverProxy && _core.State == ConnState.Connected) ApplyProxy();
                if (!_s.TakeOverProxy && _proxyApplied) RestoreProxy();
                _main.UpdateAccount(_s);
                RebuildTrayMenu();
            };
            menu.Items.Add(proxy);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { ExitApp(); };
            menu.Items.Add(exit);

            _tray.ContextMenuStrip = menu;
            UpdateTrayText();
        }

        private void UpdateTrayText()
        {
            if (_tray == null) return;
            string st;
            switch (_core.State)
            {
                case ConnState.Connecting: st = "连接中…"; break;
                case ConnState.Connected: st = "已连接"; break;
                case ConnState.Error: st = "连接失败"; break;
                default: st = "未连接"; break;
            }
            string text = "校园 VPN · " + st;
            if (text.Length > 63) text = text.Substring(0, 63);
            _tray.Text = text;
            _tray.Icon = GetIcon(_core.State);
            if (_main != null) _main.Icon = GetIcon(_core.State);
        }

        // ───────────────────────── 连接控制
        public void ToggleConnection()
        {
            if (_core.State == ConnState.Connected || _core.State == ConnState.Connecting) Disconnect("手动断开");
            else Connect();
        }

        public void Connect()
        {
            if (!_s.Complete)
            {
                ShowMain();
                Dialogs.Info(_main, string.IsNullOrEmpty(_s.Server)
                    ? "还没有配置服务器地址，先填一下设置。"
                    : "还没有填账号或密码，先在设置里补上。");
                ShowSettings();
                return;
            }
            _reconnectAttempt = 0;
            _reconnect.Stop();
            _main.SetState(ConnState.Connecting, "正在登录网关…");
            _core.Start(_s);
            RebuildTrayMenu();
        }

        public void Disconnect(string reason)
        {
            _reconnect.Stop();
            _core.Stop(true);
            RestoreProxy();
            _main.SetState(ConnState.Idle, string.IsNullOrEmpty(reason) ? "未连接" : reason);
            RebuildTrayMenu();
            UpdateTrayText();
        }

        private void OnCoreState(ConnState state, string detail)
        {
            if (_main != null)
            {
                try
                {
                    _main.BeginInvoke(new Action(delegate
                    {
                        _main.SetState(state, detail);
                        RebuildTrayMenu();
                        UpdateTrayText();
                    }));
                }
                catch { }
            }
            else
            {
                RebuildTrayMenu();
                UpdateTrayText();
            }

            if (state == ConnState.Connected)
            {
                _reconnectAttempt = 0;
                if (_s.TakeOverProxy) ApplyProxy();
                Notify("已连接", "校园网已接入，本地入口 127.0.0.1:" + _s.BindPort, ToolTipIcon.Info);
            }
            else if (state == ConnState.Error)
            {
                RestoreProxy();
                Notify("连接失败", detail, ToolTipIcon.Error);
                ScheduleReconnect();
            }
            else if (state == ConnState.Idle)
            {
                RestoreProxy();
                _healWarned = false;
            }
        }

        private void OnCoreLog(string line, LogKind kind)
        {
            LogFile.Append(line, _s.LogToFile);
            if (_main == null) return;
            try
            {
                _main.BeginInvoke(new Action(delegate { _main.AppendLog(line, kind); }));
            }
            catch { }
        }

        private void ScheduleReconnect()
        {
            if (!_s.AutoReconnect || _core.UserStopped) return;
            if (_reconnectAttempt >= 3) return;
            _reconnectAttempt++;
            _reconnect.Interval = 5000 * _reconnectAttempt;
            _main.AppendLog("[GUI] " + (_reconnect.Interval / 1000) + " 秒后尝试第 " + _reconnectAttempt + " 次重连", LogKind.Warn);
            _reconnect.Start();
        }

        private void OnReconnectTick(object sender, EventArgs e)
        {
            _reconnect.Stop();
            if (_core.State == ConnState.Connected || _core.State == ConnState.Connecting) return;
            if (_core.UserStopped) return;
            _core.Start(_s);
        }

        // ───────────────────────── 系统代理
        private void ApplyProxy()
        {
            try
            {
                ProxyManager.Apply(_s.BindPort, _s.StripLanBypass);
                _proxyApplied = true;
                _healWarned = false;
                _main.AppendLog("[GUI] 系统代理已指向 " + ProxyManager.ExpectedServer(_s.BindPort), LogKind.Info);
            }
            catch (Exception ex)
            {
                _main.AppendLog("[GUI] 系统代理设置失败：" + ex.Message, LogKind.Err);
            }
        }

        private void RestoreProxy()
        {
            if (!_proxyApplied) return;
            try
            {
                ProxyManager.Restore();
                _proxyApplied = false;
                _main.AppendLog("[GUI] 系统代理已还原", LogKind.Info);
            }
            catch (Exception ex)
            {
                _main.AppendLog("[GUI] 系统代理还原失败：" + ex.Message, LogKind.Err);
            }
        }

        private void OnWatchdog(object sender, EventArgs e)
        {
            if (_core.State != ConnState.Connected) return;
            if (!_s.TakeOverProxy || !_proxyApplied) return;

            if (ProxyManager.HeldByUs(_s.BindPort)) { _healWarned = false; return; }

            if (_s.AutoHealProxy)
            {
                try
                {
                    ProxyManager.Apply(_s.BindPort, _s.StripLanBypass);
                    _main.AppendLog("[GUI] 系统代理被其他软件改回去了，已自动改回", LogKind.Warn);
                }
                catch { }
            }
            else if (!_healWarned)
            {
                _healWarned = true;
                _main.AppendLog("[GUI] 注意：系统代理已被其他软件（如 FlClash）改回，校园流量可能不走 VPN", LogKind.Warn);
                Notify("系统代理被抢占", "另一个代理软件把系统代理改回去了，可到设置里开启自动修复。", ToolTipIcon.Warning);
            }
        }

        // ───────────────────────── 界面
        public void ShowMain()
        {
            if (_shuttingDown) return;
            if (_main == null || _main.IsDisposed) return;
            if (!_main.Visible) _main.Show();
            if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
            _main.ShowInTaskbar = true;
            _main.Activate();
            _main.BringToFront();
        }

        public void NotifyHidden()
        {
            Notify("仍在后台运行", "双击托盘图标可以再打开主界面。", ToolTipIcon.Info);
        }

        public void ShowSettings()
        {
            if (_settings != null && !_settings.IsDisposed)
            {
                _settings.Activate();
                return;
            }
            _settings = new SettingsForm(this);
            try
            {
                if (_main != null && _main.Visible) _settings.ShowDialog(_main);
                else _settings.ShowDialog();
            }
            finally
            {
                if (_settings != null && !_settings.IsDisposed) _settings.Dispose();
                _settings = null;
            }
        }

        public void ApplySettings(AppSettings s)
        {
            bool wasConnected = _core.State == ConnState.Connected || _core.State == ConnState.Connecting;
            bool serverChanged = s.Server != _s.Server || s.Username != _s.Username || s.BindPort != _s.BindPort;

            _s.Server = s.Server;
            _s.Username = s.Username;
            _s.Password = s.Password;
            _s.BindPort = s.BindPort;
            _s.UseFallback = s.UseFallback;
            _s.FallbackProxy = s.FallbackProxy;
            _s.AutoReconnect = s.AutoReconnect;
            _s.TakeOverProxy = s.TakeOverProxy;
            _s.AutoHealProxy = s.AutoHealProxy;
            _s.StripLanBypass = s.StripLanBypass;
            _s.AutoStart = s.AutoStart;
            _s.AutoStartMode = s.AutoStartMode;
            _s.AutoConnect = s.AutoConnect;
            _s.CloseToTray = s.CloseToTray;
            _s.ThemeName = s.ThemeName;
            _s.NotifyState = s.NotifyState;
            _s.PipelinePath = s.PipelinePath;
            _s.LogToFile = s.LogToFile;
            _s.Save();

            try { Autostart.Set(_s.AutoStart); } catch (Exception ex) { Dialogs.Warn(_main, "开机自启设置失败：" + ex.Message); }

            Theme.Current = Theme.Resolve(_s.ThemeName);
            _main.ApplyTheme();
            _main.UpdateAccount(_s);
            _main.SyncAutostart(Autostart.IsEnabled());
            RebuildTrayMenu();

            if (wasConnected && serverChanged)
            {
                Disconnect("配置已修改，已断开");
                _main.AppendLog("[GUI] 账号/服务器/端口有变动，已断开连接，请重新连接", LogKind.Warn);
            }
            else if (wasConnected && _s.TakeOverProxy && !_proxyApplied)
            {
                ApplyProxy();
            }
            else if (wasConnected && !_s.TakeOverProxy && _proxyApplied)
            {
                RestoreProxy();
            }
        }

        public void SetAutostart(bool on)
        {
            try
            {
                Autostart.Set(on);
                _s.AutoStart = on;
                _s.Save();
                _main.SyncAutostart(Autostart.IsEnabled());
                RebuildTrayMenu();
                _main.AppendLog("[GUI] 开机自启：" + (on ? "已开启" : "已关闭"), LogKind.Info);
            }
            catch (Exception ex)
            {
                Dialogs.Warn(_main, "开机自启设置失败：" + ex.Message);
                _main.SyncAutostart(Autostart.IsEnabled());
            }
        }

        private void Notify(string title, string body, ToolTipIcon icon)
        {
            if (!_s.NotifyState) return;
            try { _tray.ShowBalloonTip(2600, title, body, icon); } catch { }
        }

        public void ExitApp()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            try { _core.Stop(true); } catch { }
            RestoreProxy();
            try { if (_tray != null) _tray.Visible = false; } catch { }
            try { _s.Save(); } catch { }
            try
            {
                if (_main != null)
                {
                    _main.AllowClose();
                    _main.Close();
                }
            }
            catch { }
            ExitThread();
        }

        public void Shutdown()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            try { _core.Stop(true); } catch { }
            RestoreProxy();
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
            try { _s.Save(); } catch { }
        }
    }
}
