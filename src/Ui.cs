using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VpnTray
{
    /// <summary>DPI 与绘制小工具。坐标按 96dpi 设计，运行时统一缩放。</summary>
    internal static class Ui
    {
        public static float Scale = 1f;
        public const string FontName = "Microsoft YaHei UI";

        /// <summary>窗体还没建句柄时也能拿到正确 DPI，所以直接问屏幕。</summary>
        public static void InitScale()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    Scale = g.DpiX / 96f;
                }
            }
            catch { Scale = 1f; }
            if (Scale <= 0.5f || Scale > 4f) Scale = 1f;
        }

        public static int S(int v) { return (int)Math.Round(v * Scale); }

        public static Font F(float points, FontStyle style)
        {
            return new Font(FontName, points, style, GraphicsUnit.Point);
        }

        public static Font FMono(float points)
        {
            return new Font("Consolas", points, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Rectangle R(int x, int y, int w, int h)
        {
            return new Rectangle(S(x), S(y), S(w), S(h));
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = Math.Max(1, radius * 2);
            GraphicsPath p = new GraphicsPath();
            if (r.Width <= d || r.Height <= d)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void RoundControl(Control c, int radius)
        {
            if (c.Width <= 0 || c.Height <= 0) return;
            using (GraphicsPath p = RoundRect(new Rectangle(0, 0, c.Width, c.Height), radius))
            {
                Region old = c.Region;
                c.Region = new Region(p);
                if (old != null) old.Dispose();
            }
        }
    }

    /// <summary>圆角卡片，带 1px 边框。</summary>
    internal sealed class CardPanel : Panel
    {
        public int Radius = 10;

        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Theme.Current;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.RoundRect(r, Radius))
            {
                using (SolidBrush b = new SolidBrush(t.Card)) e.Graphics.FillPath(b, path);
                using (Pen p = new Pen(t.Border)) e.Graphics.DrawPath(p, path);
            }
            base.OnPaint(e);
        }
    }

    /// <summary>扁平按钮：主题色实心（主按钮）或描边（次按钮）。</summary>
    internal sealed class FlatButton : Button
    {
        public bool Primary = true;
        public int Radius = 8;

        public FlatButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            Font = Ui.F(10f, FontStyle.Regular);
            Height = Ui.S(44);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void ApplyTheme()
        {
            Theme t = Theme.Current;
            if (Primary)
            {
                BackColor = t.Accent;
                ForeColor = t.OnAccent;
                FlatAppearance.MouseOverBackColor = t.AccentHover;
                FlatAppearance.MouseDownBackColor = t.AccentPress;
            }
            else
            {
                BackColor = t.Ctrl;
                ForeColor = t.CtrlText;
                FlatAppearance.MouseOverBackColor = t.Dark ? Color.FromArgb(0x2B, 0x31, 0x3A) : Color.FromArgb(0xEC, 0xEF, 0xF3);
                FlatAppearance.MouseDownBackColor = t.Dark ? Color.FromArgb(0x33, 0x3A, 0x45) : Color.FromArgb(0xE0, 0xE4, 0xEA);
            }
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Ui.RoundControl(this, Radius);
        }
    }

    internal static class Dialogs
    {
        public static void Info(IWin32Window owner, string text)
        {
            MessageBox.Show(owner, text, "校园 VPN", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void Warn(IWin32Window owner, string text)
        {
            MessageBox.Show(owner, text, "校园 VPN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static bool Ask(IWin32Window owner, string text)
        {
            return MessageBox.Show(owner, text, "校园 VPN", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
        }
    }
}
