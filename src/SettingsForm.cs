using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace VpnTray
{
    internal sealed class SettingsForm : Form
    {
        private readonly TrayApp _app;

        private TabControl _tabs;

        // 常规
        private TextBox _txtServer;
        private TextBox _txtUser;
        private TextBox _txtPass;
        private CheckBox _chkShowPass;
        private NumericUpDown _numPort;
        private CheckBox _chkFallback;
        private TextBox _txtFallback;
        private CheckBox _chkReconnect;

        // 系统代理
        private CheckBox _chkTakeOver;
        private CheckBox _chkHeal;
        private CheckBox _chkStripLan;
        private Label _lblCurrentProxy;
        private Button _btnRestoreNow;
        private Button _btnClash;

        // 启动与外观
        private CheckBox _chkAutoStart;
        private ComboBox _cmbAutoStartMode;
        private CheckBox _chkAutoConnect;
        private ComboBox _cmbCloseMode;
        private ComboBox _cmbTheme;
        private CheckBox _chkNotify;

        // 高级
        private TextBox _txtPipeline;
        private CheckBox _chkLogFile;
        private TextBox _txtArgs;

        private FlatButton _btnOk;
        private FlatButton _btnCancel;

        public SettingsForm(TrayApp app)
        {
            _app = app;
            Ui.InitScale();
            BuildUi();
            LoadFrom(_app.Settings);
            ApplyTheme();
        }

        private void BuildUi()
        {
            SuspendLayout();
            Text = "设置";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Ui.S(470), Ui.S(520));
            AutoScaleMode = AutoScaleMode.None;

            _tabs = new TabControl();
            _tabs.Bounds = Ui.R(10, 10, 450, 452);
            _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            _tabs.ItemSize = new Size(Ui.S(112), Ui.S(28));
            _tabs.SizeMode = TabSizeMode.Fixed;
            _tabs.DrawItem += OnDrawTab;
            Controls.Add(_tabs);

            TabPage p1 = new TabPage("常规");
            TabPage p2 = new TabPage("系统代理");
            TabPage p3 = new TabPage("启动与外观");
            TabPage p4 = new TabPage("高级");
            _tabs.TabPages.Add(p1);
            _tabs.TabPages.Add(p2);
            _tabs.TabPages.Add(p3);
            _tabs.TabPages.Add(p4);

            // ── 常规
            int y = 18;
            AddLabel(p1, "服务器地址", 16, y);
            _txtServer = AddText(p1, 120, y - 3, 300);
            y += 38;
            AddLabel(p1, "用户名", 16, y);
            _txtUser = AddText(p1, 120, y - 3, 300);
            y += 38;
            AddLabel(p1, "密码", 16, y);
            _txtPass = AddText(p1, 120, y - 3, 300);
            _txtPass.UseSystemPasswordChar = true;
            y += 36;
            _chkShowPass = AddCheck(p1, "显示密码", 120, y);
            _chkShowPass.CheckedChanged += delegate { _txtPass.UseSystemPasswordChar = !_chkShowPass.Checked; };
            y += 32;
            AddLabel(p1, "本地端口", 16, y);
            _numPort = new NumericUpDown();
            _numPort.Minimum = 1024;
            _numPort.Maximum = 65535;
            _numPort.Bounds = Ui.R(120, y - 3, 100, 26);
            _numPort.Font = Ui.F(9f, FontStyle.Regular);
            p1.Controls.Add(_numPort);
            AddHint(p1, "默认 1080；内核会占用它的下一个端口", 230, y);
            y += 38;
            _chkFallback = AddCheck(p1, "非校园流量交上游代理（推荐）", 16, y);
            y += 30;
            _txtFallback = AddText(p1, 36, y - 2, 384);
            y += 30;
            AddHint(p1, "例：socks5h://127.0.0.1:7890（留空则非校园流量直连）", 36, y);
            y += 30;
            _chkReconnect = AddCheck(p1, "内核异常退出后自动重连（最多 3 次）", 16, y);
            y += 34;
            AddHint(p1, "校园网地址不知道的话，可以先看一眼原版 EasyConnect 客户端里填的那一行。", 16, y);
            AddHint(p1, "有的网关要求 https:// 前缀，照抄即可。", 16, y + 18);

            // ── 系统代理
            y = 18;
            _chkTakeOver = AddCheck(p2, "连接时接管 Windows 系统代理", 16, y);
            y += 26;
            AddHint(p2, "把系统代理指向本地端口，断开时精确还原原设置。", 36, y);
            y += 30;
            _chkHeal = AddCheck(p2, "发现系统代理被其他软件抢回时自动改回", 16, y);
            y += 26;
            AddHint(p2, "FlClash 等客户端会持续维护自己的系统代理开关，建议先把它的系统代理关掉。", 36, y);
            y += 30;
            _chkStripLan = AddCheck(p2, "清理内网直连例外（10.* / 172.16-31.* / 192.168.*）", 16, y);
            y += 26;
            AddHint(p2, "不清掉的话，校园内网 IP 会绕过代理直连而打不开。", 36, y);
            y += 34;

            _lblCurrentProxy = new Label();
            _lblCurrentProxy.Bounds = Ui.R(16, y, 410, 40);
            _lblCurrentProxy.Font = Ui.F(8.5f, FontStyle.Regular);
            p2.Controls.Add(_lblCurrentProxy);
            y += 46;

            _btnRestoreNow = new Button();
            _btnRestoreNow.Text = "立即还原为原始代理";
            _btnRestoreNow.Font = Ui.F(9f, FontStyle.Regular);
            _btnRestoreNow.Bounds = Ui.R(16, y, 170, 30);
            _btnRestoreNow.FlatStyle = FlatStyle.Flat;
            _btnRestoreNow.Cursor = Cursors.Hand;
            _btnRestoreNow.Click += delegate { RestoreProxyNow(); };
            p2.Controls.Add(_btnRestoreNow);

            _btnClash = new Button();
            _btnClash.Text = "生成 Clash 规则片段…";
            _btnClash.Font = Ui.F(9f, FontStyle.Regular);
            _btnClash.Bounds = Ui.R(196, y, 170, 30);
            _btnClash.FlatStyle = FlatStyle.Flat;
            _btnClash.Cursor = Cursors.Hand;
            _btnClash.Click += delegate { ShowClashSnippet(); };
            p2.Controls.Add(_btnClash);

            // ── 启动与外观
            y = 18;
            _chkAutoStart = AddCheck(p3, "开机自启动", 16, y);
            _chkAutoStart.CheckedChanged += delegate { SyncAutoStartEnabled(); };
            y += 36;
            AddLabel(p3, "自启后", 36, y);
            _cmbAutoStartMode = AddCombo(p3, 120, y - 3, 180, new string[] { "只进托盘（不弹窗）", "显示主界面" });
            y += 36;
            _chkAutoConnect = AddCheck(p3, "自启后自动连接（免手动）", 36, y);
            y += 36;
            AddLabel(p3, "关闭窗口时", 16, y);
            _cmbCloseMode = AddCombo(p3, 120, y - 3, 180, new string[] { "最小化到托盘", "退出程序" });
            y += 36;
            AddLabel(p3, "主题", 16, y);
            _cmbTheme = AddCombo(p3, 120, y - 3, 180, new string[] { "浅色", "深色", "跟随系统" });
            y += 36;
            _chkNotify = AddCheck(p3, "连接状态变化时弹气泡提醒", 16, y);
            y += 40;
            AddHint(p3, "托盘图标右键可随时连接/断开；双击图标打开主界面。", 16, y);

            // ── 高级
            y = 18;
            AddLabel(p4, "内核路径", 16, y);
            _txtPipeline = AddText(p4, 120, y - 3, 250);
            Button browse = new Button();
            browse.Text = "浏览";
            browse.Font = Ui.F(9f, FontStyle.Regular);
            browse.Bounds = Ui.R(378, y - 3, 52, 26);
            browse.FlatStyle = FlatStyle.Flat;
            browse.Click += delegate { BrowsePipeline(); };
            p4.Controls.Add(browse);
            y += 38;
            _chkLogFile = AddCheck(p4, "把日志同时写入文件", 16, y);
            y += 34;
            AddLabel(p4, "内核参数", 16, y);
            _txtArgs = new TextBox();
            _txtArgs.Bounds = Ui.R(16, y + 20, 414, 60);
            _txtArgs.Multiline = true;
            _txtArgs.ReadOnly = true;
            _txtArgs.ScrollBars = ScrollBars.Vertical;
            _txtArgs.Font = Ui.FMono(8f);
            p4.Controls.Add(_txtArgs);
            y += 92;

            Button openData = new Button();
            openData.Text = "打开数据目录";
            openData.Font = Ui.F(9f, FontStyle.Regular);
            openData.Bounds = Ui.R(16, y, 120, 30);
            openData.FlatStyle = FlatStyle.Flat;
            openData.Click += delegate { Process.Start("explorer.exe", AppSettings.DataDir); };
            p4.Controls.Add(openData);

            Button clearPass = new Button();
            clearPass.Text = "清除已保存密码";
            clearPass.Font = Ui.F(9f, FontStyle.Regular);
            clearPass.Bounds = Ui.R(146, y, 120, 30);
            clearPass.FlatStyle = FlatStyle.Flat;
            clearPass.Click += delegate { ClearPassword(); };
            p4.Controls.Add(clearPass);

            Button openLog = new Button();
            openLog.Text = "打开日志目录";
            openLog.Font = Ui.F(9f, FontStyle.Regular);
            openLog.Bounds = Ui.R(276, y, 120, 30);
            openLog.FlatStyle = FlatStyle.Flat;
            openLog.Click += delegate
            {
                Directory.CreateDirectory(AppSettings.LogDir);
                Process.Start("explorer.exe", AppSettings.LogDir);
            };
            p4.Controls.Add(openLog);

            y += 42;
            AddHint(p4, "密码用 Windows DPAPI 加密，只有当前 Windows 账户能解开；不会写进命令行。", 16, y);

            // ── 底部按钮
            _btnOk = new FlatButton();
            _btnOk.Text = "保存";
            _btnOk.Bounds = Ui.R(272, 472, 88, 34);
            _btnOk.Font = Ui.F(9.5f, FontStyle.Bold);
            _btnOk.Click += delegate { SaveAndClose(); };
            Controls.Add(_btnOk);

            _btnCancel = new FlatButton();
            _btnCancel.Primary = false;
            _btnCancel.Text = "取消";
            _btnCancel.Bounds = Ui.R(370, 472, 88, 34);
            _btnCancel.Font = Ui.F(9.5f, FontStyle.Regular);
            _btnCancel.Click += delegate { Close(); };
            Controls.Add(_btnCancel);

            // 回车不冒泡
            AcceptButton = null;

            ResumeLayout(false);
        }

        private Label AddLabel(Control parent, string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = Ui.F(9f, FontStyle.Regular);
            l.Location = new Point(Ui.S(x), Ui.S(y));
            parent.Controls.Add(l);
            return l;
        }

        private Label AddHint(Control parent, string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Ui.F(8f, FontStyle.Regular);
            l.AutoSize = true;
            l.Location = new Point(Ui.S(x), Ui.S(y));
            l.Tag = "hint";
            parent.Controls.Add(l);
            return l;
        }

        private TextBox AddText(Control parent, int x, int y, int w)
        {
            TextBox t = new TextBox();
            t.Bounds = Ui.R(x, y, w, 26);
            t.Font = Ui.F(9f, FontStyle.Regular);
            t.BorderStyle = BorderStyle.FixedSingle;
            parent.Controls.Add(t);
            return t;
        }

        private CheckBox AddCheck(Control parent, string text, int x, int y)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.Font = Ui.F(9f, FontStyle.Regular);
            c.Location = new Point(Ui.S(x), Ui.S(y));
            parent.Controls.Add(c);
            return c;
        }

        private ComboBox AddCombo(Control parent, int x, int y, int w, string[] items)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Bounds = Ui.R(x, y, w, 26);
            c.Font = Ui.F(9f, FontStyle.Regular);
            c.FlatStyle = FlatStyle.System;
            c.Items.AddRange(items);
            c.SelectedIndex = 0;
            parent.Controls.Add(c);
            return c;
        }

        private void LoadFrom(AppSettings s)
        {
            _txtServer.Text = s.Server;
            _txtUser.Text = s.Username;
            _txtPass.Text = s.Password;
            _numPort.Value = Math.Max(1024, Math.Min(65535, s.BindPort));
            _chkFallback.Checked = s.UseFallback;
            _txtFallback.Text = s.FallbackProxy;
            _chkReconnect.Checked = s.AutoReconnect;
            _chkTakeOver.Checked = s.TakeOverProxy;
            _chkHeal.Checked = s.AutoHealProxy;
            _chkStripLan.Checked = s.StripLanBypass;
            _chkAutoStart.Checked = s.AutoStart;
            _cmbAutoStartMode.SelectedIndex = s.AutoStartMode == "window" ? 1 : 0;
            _chkAutoConnect.Checked = s.AutoConnect;
            _cmbCloseMode.SelectedIndex = s.CloseToTray ? 0 : 1;
            _cmbTheme.SelectedIndex = s.ThemeName == "dark" ? 1 : (s.ThemeName == "system" ? 2 : 0);
            _chkNotify.Checked = s.NotifyState;
            _txtPipeline.Text = s.PipelinePath;
            _chkLogFile.Checked = s.LogToFile;
            SyncAutoStartEnabled();
            RefreshProxyLabel();
            RefreshArgsPreview();
        }

        private void SyncAutoStartEnabled()
        {
            bool on = _chkAutoStart.Checked;
            _cmbAutoStartMode.Enabled = on;
            _chkAutoConnect.Enabled = on;
        }

        private void RefreshProxyLabel()
        {
            ProxyManager.Snapshot s = ProxyManager.Read();
            string txt = "当前系统代理：" + (s.Enable == 0 ? "未启用" : s.Server) +
                         (string.IsNullOrEmpty(s.PacUrl) ? "" : "（另有 PAC：" + s.PacUrl + "）");
            _lblCurrentProxy.Text = txt;
        }

        private void RefreshArgsPreview()
        {
            AppSettings tmp = BuildFromUi();
            _txtArgs.Text = Path.GetFileName(tmp.PipelinePath) + " " + tmp.BuildArguments();
        }

        private void BrowsePipeline()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "可执行文件|*.exe|所有文件|*.*";
                d.Title = "选择内核程序";
                string cur = _txtPipeline.Text;
                if (!string.IsNullOrEmpty(cur))
                {
                    try { d.InitialDirectory = Path.GetDirectoryName(cur); } catch { }
                }
                if (d.ShowDialog(this) == DialogResult.OK) _txtPipeline.Text = d.FileName;
            }
        }

        private void ClearPassword()
        {
            if (!Dialogs.Ask(this, "清除已保存的密码？下次连接前需要重新输入。")) return;
            _txtPass.Text = "";
            _app.Settings.Password = "";
            _app.Settings.PasswordEnc = "";
            _app.Settings.Save();
            Dialogs.Info(this, "已清除。");
        }

        private void RestoreProxyNow()
        {
            try
            {
                ProxyManager.Restore();
                RefreshProxyLabel();
                Dialogs.Info(this, "已还原为接管前的系统代理设置。");
            }
            catch (Exception ex)
            {
                Dialogs.Warn(this, "还原失败：" + ex.Message);
            }
        }

        private void ShowClashSnippet()
        {
            AppSettings s = BuildFromUi();
            string snippet = ProxyManager.BuildClashSnippet(s.BindPort, s.Server);
            using (Form f = new Form())
            {
                f.Text = "Clash / FlClash 规则片段";
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(Ui.S(560), Ui.S(420));
                TextBox tb = new TextBox();
                tb.Multiline = true;
                tb.ScrollBars = ScrollBars.Both;
                tb.WordWrap = false;
                tb.Font = Ui.FMono(9f);
                tb.Bounds = Ui.R(10, 10, 540, 360);
                tb.Text = snippet;
                tb.SelectAll();
                f.Controls.Add(tb);
                FlatButton copy = new FlatButton();
                copy.Text = "复制到剪贴板";
                copy.Bounds = Ui.R(370, 376, 180, 34);
                copy.Click += delegate
                {
                    try { Clipboard.SetText(snippet); copy.Text = "已复制"; } catch { }
                };
                f.Controls.Add(copy);
                f.ShowDialog(this);
            }
        }

        private AppSettings BuildFromUi()
        {
            AppSettings s = new AppSettings();
            s.Server = _txtServer.Text.Trim();
            s.Username = _txtUser.Text.Trim();
            s.Password = _txtPass.Text;
            s.BindPort = (int)_numPort.Value;
            s.UseFallback = _chkFallback.Checked;
            s.FallbackProxy = _txtFallback.Text.Trim();
            s.AutoReconnect = _chkReconnect.Checked;
            s.TakeOverProxy = _chkTakeOver.Checked;
            s.AutoHealProxy = _chkHeal.Checked;
            s.StripLanBypass = _chkStripLan.Checked;
            s.AutoStart = _chkAutoStart.Checked;
            s.AutoStartMode = _cmbAutoStartMode.SelectedIndex == 1 ? "window" : "tray";
            s.AutoConnect = _chkAutoConnect.Checked;
            s.CloseToTray = _cmbCloseMode.SelectedIndex == 0;
            s.ThemeName = _cmbTheme.SelectedIndex == 1 ? "dark" : (_cmbTheme.SelectedIndex == 2 ? "system" : "light");
            s.NotifyState = _chkNotify.Checked;
            s.PipelinePath = _txtPipeline.Text.Trim();
            s.LogToFile = _chkLogFile.Checked;
            return s;
        }

        private void SaveAndClose()
        {
            AppSettings s = BuildFromUi();
            if (string.IsNullOrWhiteSpace(s.Server))
            {
                Dialogs.Warn(this, "服务器地址不能为空。");
                _tabs.SelectedIndex = 0;
                return;
            }
            if (string.IsNullOrWhiteSpace(s.Username))
            {
                Dialogs.Warn(this, "用户名不能为空。");
                _tabs.SelectedIndex = 0;
                return;
            }
            if (string.IsNullOrEmpty(s.Password))
            {
                if (!Dialogs.Ask(this, "还没有填密码。现在保存吗？（下次连接前仍要填）")) return;
            }
            if (s.BindPort < 1024 || s.BindPort > 65535)
            {
                Dialogs.Warn(this, "端口请填 1024-65535。");
                _tabs.SelectedIndex = 0;
                return;
            }
            if (!File.Exists(s.PipelinePath) && !Dialogs.Ask(this, "内核程序不存在：\n" + s.PipelinePath + "\n\n仍然保存？"))
                return;

            _app.ApplySettings(s);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnDrawTab(object sender, DrawItemEventArgs e)
        {
            Theme t = Theme.Current;
            Rectangle r = _tabs.GetTabRect(e.Index);
            bool selected = e.Index == _tabs.SelectedIndex;
            Color back = selected ? t.Card : t.Bg;
            using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, r);
            if (selected)
            {
                using (SolidBrush bar = new SolidBrush(t.Accent))
                    e.Graphics.FillRectangle(bar, r.X + Ui.S(14), r.Bottom - Ui.S(3), r.Width - Ui.S(28), Ui.S(3));
            }
            TextRenderer.DrawText(e.Graphics, _tabs.TabPages[e.Index].Text, _tabs.Font, r,
                selected ? t.Text : t.Sub,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void ApplyTheme()
        {
            Theme t = Theme.Current;
            BackColor = t.Bg;
            ForeColor = t.Text;
            _tabs.BackColor = t.Bg;
            _tabs.ForeColor = t.Text;
            foreach (TabPage p in _tabs.TabPages)
            {
                p.BackColor = t.Card;
                p.ForeColor = t.Text;
                StylePage(p);
            }
            _btnOk.ApplyTheme();
            _btnCancel.ApplyTheme();
            _lblCurrentProxy.ForeColor = t.Sub;

            _txtServer.BackColor = t.Ctrl;
            _txtServer.ForeColor = t.Text;
            _txtUser.BackColor = t.Ctrl;
            _txtUser.ForeColor = t.Text;
            _txtPass.BackColor = t.Ctrl;
            _txtPass.ForeColor = t.Text;
            _txtFallback.BackColor = t.Ctrl;
            _txtFallback.ForeColor = t.Text;
            _txtPipeline.BackColor = t.Ctrl;
            _txtPipeline.ForeColor = t.Text;
            _txtArgs.BackColor = t.LogBg;
            _txtArgs.ForeColor = t.LogText;
            Ui.RoundControl(_btnOk, _btnOk.Radius);
            Ui.RoundControl(_btnCancel, _btnCancel.Radius);
        }

        private void StylePage(Control page)
        {
            Theme t = Theme.Current;
            foreach (Control c in page.Controls)
            {
                Label l = c as Label;
                if (l != null) l.ForeColor = (l.Tag as string) == "hint" ? t.Sub : t.Text;
                CheckBox cb = c as CheckBox;
                if (cb != null) cb.ForeColor = t.Text;
                Button b = c as Button;
                if (b != null)
                {
                    b.BackColor = t.Ctrl;
                    b.ForeColor = t.CtrlText;
                    b.FlatAppearance.BorderColor = t.CtrlBorder;
                    b.FlatAppearance.BorderSize = 1;
                }
                ComboBox cm = c as ComboBox;
                if (cm != null) { cm.BackColor = t.Ctrl; cm.ForeColor = t.Text; }
                TextBox tb = c as TextBox;
                if (tb != null && tb != _txtArgs) { tb.BackColor = t.Ctrl; tb.ForeColor = t.Text; }
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshProxyLabel();
            RefreshArgsPreview();
        }
    }
}
