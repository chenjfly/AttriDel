using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AttriDel
{
    /// <summary>Win11 Fluent 风格配色。</summary>
    internal static class UiColors
    {
        public static readonly Color WindowBg = Color.FromArgb(243, 244, 246);
        public static readonly Color Card = Color.White;
        public static readonly Color CardBorder = Color.FromArgb(225, 228, 232);
        public static readonly Color DropBorder = Color.FromArgb(176, 196, 222);
        public static readonly Color Text = Color.FromArgb(27, 31, 38);
        public static readonly Color TextSecondary = Color.FromArgb(95, 101, 112);
        public static readonly Color TextMuted = Color.FromArgb(148, 153, 162);
        public static readonly Color Accent = Color.FromArgb(0, 103, 192);
        public static readonly Color AccentHover = Color.FromArgb(0, 90, 168);
        public static readonly Color AccentSoft = Color.FromArgb(232, 243, 252);
        public static readonly Color DropActive = Color.FromArgb(236, 246, 255);
        public static readonly Color Success = Color.FromArgb(15, 123, 30);
        public static readonly Color Warning = Color.FromArgb(196, 106, 0);
        public static readonly Color Danger = Color.FromArgb(196, 32, 32);
    }

    /// <summary>圆角几何辅助：生成圆角路径与裁剪区域。</summary>
    internal static class RoundRect
    {
        public static GraphicsPath Path(Rectangle rect, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            if (d <= 0)
            {
                p.AddRectangle(rect);
                return p;
            }
            p.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            p.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            p.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Region RegionOf(Rectangle rect, int radius)
        {
            using (GraphicsPath p = Path(rect, radius))
                return new Region(p);
        }
    }

    /// <summary>自绘圆角面板：圆角背景 + 圆角边框，可用作卡片容器。</summary>
    internal class RoundedPanel : Panel
    {
        private int _radius = 8;
        private Color _borderColor = UiColors.CardBorder;
        private bool _dashed;

        public RoundedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = UiColors.Card;
        }

        public int Radius
        {
            get { return _radius; }
            set
            {
                _radius = value;
                UpdateRegion();
                Invalidate();
            }
        }

        public Color BorderColor
        {
            get { return _borderColor; }
            set { _borderColor = value; Invalidate(); }
        }

        public bool Dashed
        {
            get { return _dashed; }
            set { _dashed = value; Invalidate(); }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            this.Region = RoundRect.RegionOf(new Rectangle(0, 0, Width, Height), _radius);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundRect.Path(rect, _radius))
            {
                using (SolidBrush br = new SolidBrush(BackColor))
                    g.FillPath(br, path);
                using (Pen pen = new Pen(_borderColor))
                {
                    if (_dashed) pen.DashStyle = DashStyle.Dash;
                    g.DrawPath(pen, path);
                }
            }
        }
    }

    /// <summary>Win11 风格自绘圆角按钮，Primary 为品牌蓝主按钮。</summary>
    internal class RoundedButton : Button
    {
        private int _radius = 6;
        private bool _primary;
        private bool _hover;
        private bool _pressed;

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
        }

        public int Radius
        {
            get { return _radius; }
            set
            {
                _radius = value;
                UpdateRegion();
                Invalidate();
            }
        }

        public bool Primary
        {
            get { return _primary; }
            set { _primary = value; Invalidate(); }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            this.Region = RoundRect.RegionOf(new Rectangle(0, 0, Width, Height), _radius);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true; Invalidate(); base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _pressed = true; Invalidate(); base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false; Invalidate(); base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill, border, text;
            if (Primary)
            {
                fill = !Enabled ? Color.FromArgb(170, 182, 198)
                     : _pressed ? UiColors.AccentHover
                     : _hover ? Color.FromArgb(23, 121, 201)
                     : UiColors.Accent;
                border = fill;
                text = Color.White;
            }
            else
            {
                fill = _pressed ? Color.FromArgb(232, 236, 241)
                     : _hover ? Color.FromArgb(244, 247, 250)
                     : Color.White;
                border = _hover ? Color.FromArgb(170, 182, 198) : UiColors.CardBorder;
                text = Enabled ? UiColors.Text : UiColors.TextMuted;
            }

            // 用与按钮一致的背景色填充，Region 已把绘制区裁剪成圆角。
            using (SolidBrush br = new SolidBrush(fill))
                g.FillRectangle(br, rect);

            // 文字用 GDI+ DrawString 的灰度抗锯齿，不用 TextRenderer。
            // TextRenderer 走 GDI ClearType（LCD 亚像素），在自绘按钮上会
            // 因拿不到正确背景混合而在边缘渗出橙/青/紫彩色杂点。
            // AntiAliasGridFit 是纯灰度抗锯齿，任何环境都不产生彩色边。
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (SolidBrush textBrush = new SolidBrush(text))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(Text, Font, textBrush, rect, sf);
            }

            // 边框最后画，避免被上面的背景填充覆盖。
            using (GraphicsPath path = RoundRect.Path(rect, _radius))
            using (Pen pen = new Pen(border))
                g.DrawPath(pen, path);
        }
    }

    /// <summary>
    /// 自绘加号徽章：浅蓝圆形底 + 居中蓝色圆头加号。
    /// 圆底与加号在同一 OnPaint 内基于控件中心绘制，避免字符字形留白不对称
    /// （全角"＋"在微软雅黑里不居中）或父子图层叠加造成的偏移。
    /// 加号臂长/线宽按控件尺寸比例计算，随 DPI 自动缩放。
    /// </summary>
    internal class PlusBadge : Control
    {
        public PlusBadge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = ClientRectangle;

            // 圆形底（留 1 像素避免边缘被裁）。
            using (SolidBrush br = new SolidBrush(UiColors.AccentSoft))
                g.FillEllipse(br, r.Left, r.Top, r.Width - 1, r.Height - 1);

            // 圆心即加号中心：圆底用 Width-1/Height-1 绘制，其中心在
            // (Width-1)/2，加号用同一中心才能严格同心（否则偏 0.5px）。
            float cx = r.Left + (r.Width - 1) / 2f;
            float cy = r.Top + (r.Height - 1) / 2f;
            float side = Math.Min(r.Width, r.Height);
            float arm = side * 0.17f;                          // 半臂长
            float thickness = Math.Max(1f, side * 0.085f);     // 线宽

            using (Pen pen = new Pen(UiColors.Accent, thickness))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, cx - arm, cy, cx + arm, cy);   // 横
                g.DrawLine(pen, cx, cy - arm, cx, cy + arm);   // 竖
            }
        }
    }
}
