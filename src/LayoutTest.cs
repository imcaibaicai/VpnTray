using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace VpnTray
{
    /// <summary>
    /// 布局自检（--layouttest）：不联网、不碰系统代理，只把主界面按几个尺寸摆一遍，
    /// 校验控件不越界、不重叠，并各存一张 PNG 供肉眼确认，然后自己退出。
    /// </summary>
    internal static class LayoutTest
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
            Say("==== VpnTray " + AppInfo.Version + " 布局自检 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====");
            string dir = AppSettings.AppDir;

            TrayApp app = null;
            try
            {
                app = new TrayApp(false, false);       // 不触发自动连接
                MainForm f = app.Main;
                f.Show();
                Application.DoEvents();

                Check("窗口可缩放（FormBorderStyle=Sizable）", f.FormBorderStyle == FormBorderStyle.Sizable,
                    "FormBorderStyle=" + f.FormBorderStyle);
                Check("最大化按钮可用", f.MaximizeBox, "MaximizeBox=" + f.MaximizeBox);

                int[][] sizes = new int[][] {
                    new int[] { 420, 592 },   // 设计尺寸
                    new int[] { 560, 720 },   // 拉宽
                    new int[] { 900, 980 },   // 大幅放大
                    new int[] { 700, 640 }    // 宽而矮
                };

                foreach (int[] sz in sizes)
                {
                    int cw = Ui.S(sz[0]);
                    int ch = Ui.S(sz[1]);
                    f.ClientSize = new Size(cw, ch);
                    Application.DoEvents();
                    Say("---- 客户区 " + f.ClientSize.Width + " x " + f.ClientSize.Height + " ----");
                    Check("尺寸生效 " + sz[0] + "x" + sz[1],
                        f.ClientSize.Width == cw && f.ClientSize.Height == ch,
                        "实际 " + f.ClientSize.Width + " x " + f.ClientSize.Height);
                    Verify(f);
                    Shot(f, Path.Combine(dir, "layout-" + sz[0] + "x" + sz[1] + ".png"));
                }

                // 最小尺寸：MinimumSize 应当不小于设计尺寸，且把窗口程序化缩到更小时应被夹住
                Size minBefore = f.MinimumSize;
                bool minOk = minBefore.Width >= Ui.S(420) && minBefore.Height >= Ui.S(592);
                f.Size = new Size(200, 200);
                Application.DoEvents();
                Size afterSize = f.Size;
                Size afterClient = f.ClientSize;
                bool clamped = afterSize.Width >= minBefore.Width - 2 && afterSize.Height >= minBefore.Height - 2;
                Check("MinimumSize 不小于设计尺寸", minOk, "MinimumSize=" + minBefore + " 设计=" + Ui.S(420) + "x" + Ui.S(592));
                Check("缩到更小时被夹住", clamped,
                    "请求窗口 200x200，实际 Size=" + afterSize + " ClientSize=" + afterClient);
            }
            catch (Exception ex)
            {
                Check("布局自检执行", false, ex.GetType().Name + " " + ex.Message);
            }
            finally
            {
                Say("==== 合计失败 " + _fail + " 项 ====");
                try
                {
                    File.WriteAllLines(Path.Combine(AppSettings.AppDir, "layouttest.log"), Lines.ToArray(), new UTF8Encoding(false));
                }
                catch { }
                try
                {
                    if (app != null) app.Shutdown();
                }
                catch { }
            }
            return _fail == 0 ? 0 : 1;
        }

        /// <summary>按当前尺寸核对基本面：每块都在客户区内、纵向互不重叠、日志面板吃满卡片。</summary>
        private static void Verify(MainForm f)
        {
            int cw = f.ClientSize.Width;
            int ch = f.ClientSize.Height;

            Control[] blocks = new Control[] { f.CardStatus, f.CardAccount, f.BtnMain, f.LblProxy, f.CardLog, f.BtnSettings, f.BtnExit };
            bool inside = true;
            string bad = "";
            foreach (Control c in blocks)
            {
                if (c.Left < 0 || c.Top < 0 || c.Right > cw || c.Bottom > ch)
                {
                    inside = false;
                    bad += c.Name + "(" + c.Bounds + ") ";
                }
            }
            Check("所有控件都在客户区内", inside, bad.Length > 0 ? bad : cw + "x" + ch);

            int[] tops = new int[] { f.CardStatus.Top, f.CardAccount.Top, f.BtnMain.Top, f.LblProxy.Top, f.CardLog.Top };
            int[] bots = new int[] { f.CardStatus.Bottom, f.CardAccount.Bottom, f.BtnMain.Bottom, f.LblProxy.Bottom, f.CardLog.Bottom };
            bool ordered = true;
            for (int i = 1; i < tops.Length; i++)
            {
                if (tops[i] < bots[i - 1]) ordered = false;
            }
            Check("纵向块互不重叠", ordered, "top=" + string.Join(",", Array.ConvertAll(tops, delegate(int v) { return v.ToString(); })));

            Check("日志卡片与底部按钮不重叠", f.CardLog.Bottom <= f.BtnSettings.Top, "logBottom=" + f.CardLog.Bottom + " btnTop=" + f.BtnSettings.Top);
            Check("日志面板吃满卡片宽度", f.LogBox.Width >= f.CardLog.Width - Ui.S(24), "logW=" + f.LogBox.Width + " cardW=" + f.CardLog.Width);
            Check("日志面板高度随窗口增长", f.LogBox.Height >= Ui.S(60), "logH=" + f.LogBox.Height);
            Check("三块卡片等宽", f.CardStatus.Width == f.CardLog.Width && f.CardAccount.Width == f.CardLog.Width,
                "status=" + f.CardStatus.Width + " account=" + f.CardAccount.Width + " log=" + f.CardLog.Width);
            Check("主按钮与卡片等宽", f.BtnMain.Width == f.CardLog.Width, "btn=" + f.BtnMain.Width);
        }

        private static void Shot(Form f, string path)
        {
            try
            {
                using (Bitmap bmp = new Bitmap(Math.Max(1, f.Width), Math.Max(1, f.Height)))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        bool ok = NativeMethods.PrintWindow(f.Handle, hdc, 2);   // PW_RENDERFULLCONTENT
                        g.ReleaseHdc(hdc);
                        if (!ok) return;
                    }
                    bmp.Save(path, ImageFormat.Png);
                    Say("[INFO] 截图 " + path + " (" + bmp.Width + "x" + bmp.Height + ")");
                }
            }
            catch (Exception ex)
            {
                Say("[WARN] 截图失败 " + ex.Message);
            }
        }
    }
}
