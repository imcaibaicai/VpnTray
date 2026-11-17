using System;
using System.Drawing;
using System.Windows.Forms;

namespace VpnTray
{
    internal sealed class MainForm : Form
    {
        private readonly TrayApp _app;

        private CardPanel _cardStatus;
        private Panel _dot;
        private Label _lblState;
        private Label _lblDetail;
        private Label _lblRight;

        private CardPanel _cardAccount;
        private Label _lblAccount;
        private Label _lblServer;
        private Label _lblEdit;

        private FlatButton _btnMain;
        private Label _lblProxy;

        private CardPanel _cardLog;
        private RichTextBox _log;
        private CheckBox _chkAutoScroll;
        private Button _btnClear;
        private Button _btnCopyLog;

        private CheckBox _chkAutostart;
        private FlatButton _btnSettings;
        private FlatButton _btnExit;
        private bool _built;

        public MainForm(TrayApp app)
        {
            _app = app;
            Ui.InitScale();
            BuildUi();
            ApplyTheme();
            SetState(ConnState.Idle, "未连接");
        }

        // 给布局自检（--layouttest）读几何用
        internal CardPanel CardStatus { get { return _cardStatus; } }
        internal CardPanel CardAccount { get { return _cardAccount; } }
        internal CardPanel CardLog { get { return _cardLog; } }
        internal FlatButton BtnMain { get { return _btnMain; } }
        internal FlatButton BtnSettings { get { return _btnSettings; } }
        internal FlatButton BtnExit { get { return _btnExit; } }
        internal Label LblProxy { get { return _lblProxy; } }
        internal RichTextBox LogBox { get { return _log; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Font = Ui.F(9f, FontStyle.Regular);
            // 句柄建好后才知道非客户区（边框+标题栏）尺寸，这时设最小尺寸才准
            MinimumSize = new Size(Ui.S(420) + (Width - ClientSize.Width),
                                   Ui.S(592) + (Height - ClientSize.Height));
        }

        private void BuildUi()
        {
            SuspendLayout();

            Text = "校园 VPN " + AppInfo.Version;
            // 可拖边缩放：控件位置全部由 Relayout() 按客户区重算，不靠 Anchoring
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Ui.S(420), Ui.S(592));
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;

            // ── 标题区
            Label title = new Label();
            title.Text = "校园 VPN";
            title.Font = Ui.F(15f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(Ui.S(18), Ui.S(16));
            Controls.Add(title);

            Label sub = new Label();
            sub.Text = "一键接入，断开自动还原系统代理";
            sub.Font = Ui.F(8.5f, FontStyle.Regular);
            sub.AutoSize = true;
            sub.Location = new Point(Ui.S(19), Ui.S(46));
            sub.Name = "lblSub";
            Controls.Add(sub);

            // ── 状态卡片
            _cardStatus = new CardPanel();
            _cardStatus.Bounds = Ui.R(16, 74, 388, 92);
            Controls.Add(_cardStatus);

            _dot = new Panel();
            _dot.Bounds = Ui.R(18, 24, 12, 12);
            _cardStatus.Controls.Add(_dot);

            _lblState = new Label();
            _lblState.Text = "未连接";
            _lblState.Font = Ui.F(14f, FontStyle.Bold);
            _lblState.AutoSize = true;
            _lblState.Location = new Point(Ui.S(40), Ui.S(18));
            _cardStatus.Controls.Add(_lblState);

            _lblDetail = new Label();
            _lblDetail.Text = "";
            _lblDetail.Font = Ui.F(8.5f, FontStyle.Regular);
            _lblDetail.AutoSize = false;
            _lblDetail.Bounds = Ui.R(40, 46, 250, 34);
            _cardStatus.Controls.Add(_lblDetail);

            _lblRight = new Label();
            _lblRight.Text = "";
            _lblRight.Font = Ui.F(8.5f, FontStyle.Regular);
            _lblRight.TextAlign = ContentAlignment.TopRight;
            _lblRight.AutoSize = false;
            _lblRight.Bounds = Ui.R(250, 20, 120, 20);
            _cardStatus.Controls.Add(_lblRight);

            // ── 账户卡片（点击进入设置）
            _cardAccount = new CardPanel();
            _cardAccount.Bounds = Ui.R(16, 178, 388, 66);
            _cardAccount.Cursor = Cursors.Hand;
            _cardAccount.Click += delegate { _app.ShowSettings(); };
            Controls.Add(_cardAccount);

            _lblAccount = new Label();
            _lblAccount.Text = "账号  —";
            _lblAccount.Font = Ui.F(9f, FontStyle.Regular);
            _lblAccount.AutoSize = true;
            _lblAccount.Location = new Point(Ui.S(16), Ui.S(12));
            _lblAccount.Click += delegate { _app.ShowSettings(); };
            _cardAccount.Controls.Add(_lblAccount);

            _lblServer = new Label();
            _lblServer.Text = "服务器  —";
            _lblServer.Font = Ui.F(8.5f, FontStyle.Regular);
            _lblServer.AutoSize = true;
            _lblServer.Location = new Point(Ui.S(16), Ui.S(36));
            _lblServer.Click += delegate { _app.ShowSettings(); };
            _cardAccount.Controls.Add(_lblServer);

            _lblEdit = new Label();
            _lblEdit.Text = "编辑 ›";
            _lblEdit.Font = Ui.F(8.5f, FontStyle.Regular);
            _lblEdit.AutoSize = true;
            _lblEdit.Location = new Point(Ui.S(330), Ui.S(24));
            _lblEdit.Cursor = Cursors.Hand;
            _lblEdit.Click += delegate { _app.ShowSettings(); };
            _cardAccount.Controls.Add(_lblEdit);

            // ── 主按钮
            _btnMain = new FlatButton();
            _btnMain.Bounds = Ui.R(16, 258, 388, 48);
            _btnMain.Text = "连接";
            _btnMain.Font = Ui.F(11f, FontStyle.Bold);
            _btnMain.Click += delegate { _app.ToggleConnection(); };
            Controls.Add(_btnMain);

            // ── 代理提示
            _lblProxy = new Label();
            _lblProxy.Font = Ui.F(8.5f, FontStyle.Regular);
            _lblProxy.AutoSize = false;
            _lblProxy.TextAlign = ContentAlignment.MiddleLeft;
            _lblProxy.Bounds = Ui.R(18, 312, 384, 20);
            Controls.Add(_lblProxy);

            // ── 日志卡片
            _cardLog = new CardPanel();
            _cardLog.Bounds = Ui.R(16, 340, 388, 196);
            Controls.Add(_cardLog);

            Label logTitle = new Label();
            logTitle.Text = "运行日志";
            logTitle.Font = Ui.F(8.5f, FontStyle.Bold);
            logTitle.AutoSize = true;
            logTitle.Location = new Point(Ui.S(14), Ui.S(10));
            _cardLog.Controls.Add(logTitle);

            _chkAutoScroll = new CheckBox();
            _chkAutoScroll.Text = "自动滚动";
            _chkAutoScroll.Checked = true;
            _chkAutoScroll.Font = Ui.F(8f, FontStyle.Regular);
            _chkAutoScroll.AutoSize = false;
            _chkAutoScroll.Bounds = Ui.R(214, 5, 74, 26);
            _cardLog.Controls.Add(_chkAutoScroll);

            _btnCopyLog = new Button();
            _btnCopyLog.Text = "复制";
            _btnCopyLog.Font = Ui.F(8f, FontStyle.Regular);
            _btnCopyLog.FlatStyle = FlatStyle.Flat;
            _btnCopyLog.FlatAppearance.BorderSize = 0;
            _btnCopyLog.Bounds = Ui.R(294, 4, 40, 26);
            _btnCopyLog.Cursor = Cursors.Hand;
            _btnCopyLog.Click += delegate { CopyLog(); };
            _cardLog.Controls.Add(_btnCopyLog);

            _btnClear = new Button();
            _btnClear.Text = "清空";
            _btnClear.Font = Ui.F(8f, FontStyle.Regular);
            _btnClear.FlatStyle = FlatStyle.Flat;
            _btnClear.FlatAppearance.BorderSize = 0;
            _btnClear.Bounds = Ui.R(338, 4, 40, 26);
            _btnClear.Cursor = Cursors.Hand;
            _btnClear.Click += delegate { _log.Clear(); };
            _cardLog.Controls.Add(_btnClear);

            _log = new RichTextBox();
            _log.Bounds = Ui.R(10, 28, 368, 160);
            _log.ReadOnly = true;
            _log.BorderStyle = BorderStyle.None;
            _log.WordWrap = false;
            _log.ScrollBars = RichTextBoxScrollBars.Vertical;
            _log.Font = Ui.FMono(8.5f);
            _log.DetectUrls = false;
            _cardLog.Controls.Add(_log);

            // ── 底部
            _chkAutostart = new CheckBox();
            _chkAutostart.Text = "开机自启";
            _chkAutostart.Font = Ui.F(8.5f, FontStyle.Regular);
            _chkAutostart.AutoSize = true;
            _chkAutostart.Location = new Point(Ui.S(18), Ui.S(556));
            _chkAutostart.CheckedChanged += OnAutostartToggled;
            Controls.Add(_chkAutostart);

            _btnSettings = new FlatButton();
            _btnSettings.Primary = false;
            _btnSettings.Text = "设置";
            _btnSettings.Font = Ui.F(9f, FontStyle.Regular);
            _btnSettings.Bounds = Ui.R(238, 548, 76, 32);
            _btnSettings.Click += delegate { _app.ShowSettings(); };
            Controls.Add(_btnSettings);

            _btnExit = new FlatButton();
            _btnExit.Primary = false;
            _btnExit.Text = "退出";
            _btnExit.Font = Ui.F(9f, FontStyle.Regular);
            _btnExit.Bounds = Ui.R(324, 548, 76, 32);
            _btnExit.Click += delegate { _app.ExitApp(); };
            Controls.Add(_btnExit);

            ResumeLayout(false);
            PerformLayout();

            _built = true;
            Relayout();
        }

        /// <summary>
        /// 按当前客户区重排：顶部几块保持固定高度，日志卡片吃掉多余高度，底部一行贴底。
        /// 坐标都按 96dpi 设计值再乘缩放，所以改窗口大小时只有“宽度/日志高度”在变。
        /// </summary>
        private void Relayout()
        {
            if (!_built) return;

            int w = ClientSize.Width;
            int h = ClientSize.Height;
            int margin = Ui.S(16);
            int cardW = w - margin * 2;
            if (cardW < Ui.S(200)) cardW = Ui.S(200);

            // 状态卡片
            _cardStatus.Bounds = new Rectangle(margin, Ui.S(74), cardW, Ui.S(92));
            _lblDetail.Bounds = new Rectangle(Ui.S(40), Ui.S(46), Math.Max(Ui.S(120), cardW - Ui.S(138)), Ui.S(34));
            _lblRight.Bounds = new Rectangle(cardW - Ui.S(138), Ui.S(20), Ui.S(120), Ui.S(20));

            // 账户卡片
            _cardAccount.Bounds = new Rectangle(margin, Ui.S(178), cardW, Ui.S(66));
            _lblEdit.Location = new Point(cardW - Ui.S(58), Ui.S(24));

            // 主按钮与本地入口提示
            _btnMain.Bounds = new Rectangle(margin, Ui.S(258), cardW, Ui.S(48));
            _lblProxy.Bounds = new Rectangle(margin + Ui.S(2), Ui.S(312), cardW - Ui.S(4), Ui.S(20));

            // 日志卡片：高度吃掉剩余空间
            int bottomRowTop = h - Ui.S(44);
            int logTop = Ui.S(340);
            int logH = bottomRowTop - Ui.S(12) - logTop;
            if (logH < Ui.S(120)) logH = Ui.S(120);
            _cardLog.Bounds = new Rectangle(margin, logTop, cardW, logH);

            int innerW = _cardLog.Width;
            _chkAutoScroll.Bounds = new Rectangle(innerW - Ui.S(174), Ui.S(5), Ui.S(74), Ui.S(26));
            _btnCopyLog.Bounds = new Rectangle(innerW - Ui.S(94), Ui.S(4), Ui.S(40), Ui.S(26));
            _btnClear.Bounds = new Rectangle(innerW - Ui.S(50), Ui.S(4), Ui.S(40), Ui.S(26));
            _log.Bounds = new Rectangle(Ui.S(10), Ui.S(28),
                Math.Max(Ui.S(80), innerW - Ui.S(20)),
                Math.Max(Ui.S(40), _cardLog.Height - Ui.S(38)));

            // 底部一行
            _chkAutostart.Location = new Point(Ui.S(18), h - Ui.S(36));
            _btnSettings.Bounds = new Rectangle(w - Ui.S(182), bottomRowTop, Ui.S(76), Ui.S(32));
            _btnExit.Bounds = new Rectangle(w - Ui.S(96), bottomRowTop, Ui.S(76), Ui.S(32));

            Ui.RoundControl(_btnMain, _btnMain.Radius);
            Ui.RoundControl(_btnSettings, _btnSettings.Radius);
            Ui.RoundControl(_btnExit, _btnExit.Radius);

            _cardStatus.Invalidate();
            _cardAccount.Invalidate();
            _cardLog.Invalidate();
        }

        public void ApplyTheme()
        {
            Theme t = Theme.Current;
            BackColor = t.Bg;
            ForeColor = t.Text;
            foreach (Control c in Controls) StyleRecursive(c);
            ApplyThemeToChildren(_cardStatus);
            ApplyThemeToChildren(_cardAccount);
            ApplyThemeToChildren(_cardLog);

            _lblState.ForeColor = t.StateColor(_app.CoreState);
            _lblDetail.ForeColor = t.Sub;
            _lblRight.ForeColor = t.Sub;
            _lblAccount.ForeColor = t.Text;
            _lblServer.ForeColor = t.Sub;
            _lblProxy.ForeColor = t.Sub;
            _chkAutostart.ForeColor = t.Sub;
            _chkAutoScroll.ForeColor = t.Sub;
            _btnClear.ForeColor = t.Sub;
            _btnCopyLog.ForeColor = t.Sub;
            _log.BackColor = t.LogBg;
            _log.ForeColor = t.LogText;
            _btnMain.ApplyTheme();
            _btnSettings.ApplyTheme();
            _btnExit.ApplyTheme();
            PaintDot(_app.CoreState);
            RepaintAllLog();
        }

        private void StyleRecursive(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                Label l = c as Label;
                if (l != null)
                {
                    if (l.Name == "lblSub") l.ForeColor = Theme.Current.Sub;
                    else if (l.ForeColor == Color.Empty) l.ForeColor = Theme.Current.Text;
                }
                CheckBox cb = c as CheckBox;
                if (cb != null) cb.ForeColor = Theme.Current.Sub;
                if (c.HasChildren) StyleRecursive(c);
            }
        }

        private void ApplyThemeToChildren(Control parent)
        {
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                if (c is Label) c.ForeColor = (c.Text.EndsWith("›")) ? Theme.Current.Sub : Theme.Current.Text;
                if (c is Label && ((Label)c).Text.StartsWith("服务器")) c.ForeColor = Theme.Current.Sub;
                if (c is Label && ((Label)c).Text.StartsWith("账号")) c.ForeColor = Theme.Current.Text;
            }
            parent.Invalidate();
        }

        private void OnAutostartToggled(object sender, EventArgs e)
        {
            _app.SetAutostart(_chkAutostart.Checked);
        }

        public void SyncAutostart(bool on)
        {
            _chkAutostart.CheckedChanged -= OnAutostartToggled;
            _chkAutostart.Checked = on;
            _chkAutostart.CheckedChanged += OnAutostartToggled;
        }

        public void UpdateAccount(AppSettings s)
        {
            _lblAccount.Text = "账号  " + (string.IsNullOrEmpty(s.Username) ? "—" : s.Username);
            _lblServer.Text = "服务器  " + (string.IsNullOrEmpty(s.Server) ? "—" : s.Server);
            _lblProxy.Text = "本地入口  127.0.0.1:" + s.BindPort + "（HTTP / SOCKS5）" +
                (s.TakeOverProxy ? "　·　接管系统代理" : "　·　未接管系统代理");
        }

        public void SetState(ConnState state, string detail)
        {
            Theme t = Theme.Current;
            // 未连接时大字用正文色（用状态点的灰在深色下几乎看不清）
            _lblState.ForeColor = (state == ConnState.Idle) ? t.Text : t.StateColor(state);
            switch (state)
            {
                case ConnState.Connecting: _lblState.Text = "连接中…"; break;
                case ConnState.Connected: _lblState.Text = "已连接"; break;
                case ConnState.Error: _lblState.Text = "连接失败"; break;
                default: _lblState.Text = "未连接"; break;
            }
            if (state == ConnState.Idle && (detail == null || detail == "未连接"))
                detail = "点下面的「连接」开始";

            _lblDetail.Text = detail ?? "";
            _btnMain.Text = (state == ConnState.Connected || state == ConnState.Connecting) ? "断开" : "连接";
            PaintDot(state);
            _lblRight.Text = state == ConnState.Connected ? FormatUptime() : "";
        }

        private string FormatUptime()
        {
            TimeSpan d = _app.CoreUptime;
            if (d.TotalSeconds < 1) return "";
            if (d.TotalHours >= 1) return ((int)d.TotalHours) + "h " + d.Minutes + "m";
            if (d.TotalMinutes >= 1) return ((int)d.TotalMinutes) + "m " + d.Seconds + "s";
            return ((int)d.TotalSeconds) + "s";
        }

        public void TickUptime()
        {
            if (_app.CoreState == ConnState.Connected) _lblRight.Text = FormatUptime();
        }

        private void PaintDot(ConnState state)
        {
            Color c = Theme.Current.StateColor(state);
            Bitmap bmp = new Bitmap(Math.Max(1, _dot.Width), Math.Max(1, _dot.Height));
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(_cardStatus != null ? Theme.Current.Card : Theme.Current.Bg);
                using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, 0, 0, bmp.Width - 1, bmp.Height - 1);
            }
            Image old = _dot.BackgroundImage;
            _dot.BackgroundImage = bmp;
            if (old != null) old.Dispose();
        }

        public void AppendLog(string line, LogKind kind)
        {
            if (_log.TextLength > 200000) _log.Clear();
            Theme t = Theme.Current;
            Color c = t.LogText;
            if (kind == LogKind.Warn) c = t.Warn;
            else if (kind == LogKind.Err) c = t.Err;
            else if (kind == LogKind.Route) c = t.Dark ? Color.FromArgb(0x8C, 0xC2, 0x6A) : Color.FromArgb(0x1B, 0x6B, 0x33);

            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = c;
            _log.AppendText(line + Environment.NewLine);
            if (_chkAutoScroll.Checked)
            {
                _log.SelectionStart = _log.TextLength;
                _log.ScrollToCaret();
            }
        }

        private void RepaintAllLog() { }

        private void CopyLog()
        {
            try
            {
                if (_log.TextLength > 0) Clipboard.SetText(_log.Text);
            }
            catch { }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        private bool _reallyClose;

        public void AllowClose() { _reallyClose = true; }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_reallyClose && _app.CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                _app.NotifyHidden();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
