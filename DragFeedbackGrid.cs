using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FileNameNormalizer
{
    // Paint feedback in the existing table, so no child window intercepts OLE drops.
    internal sealed class DragFeedbackGrid : DataGridView
    {
        private readonly Font feedbackTitleFont = new Font("맑은 고딕", 11F, FontStyle.Bold);
        private readonly Font feedbackDetailFont = new Font("맑은 고딕", 9F);
        private bool feedbackActive;
        private int folderCount;
        private double pulse;
        private Rectangle lastBadgeBounds;
        private int feedbackMargin = 12;

        public DragFeedbackGrid()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.ResizeRedraw, true);
        }

        public bool FeedbackActive { get { return feedbackActive; } }

        public void ShowDropFeedback(int count)
        {
            if (count < 1) { HideDropFeedback(); return; }
            if (feedbackActive && folderCount == count) return;
            folderCount = count;
            if (!feedbackActive) pulse = 0;
            feedbackActive = true;
            Invalidate();
        }

        public void HideDropFeedback()
        {
            if (!feedbackActive) return;
            feedbackActive = false;
            folderCount = 0;
            pulse = 0;
            lastBadgeBounds = Rectangle.Empty;
            Invalidate();
        }

        public void AdvanceDropFeedback(double elapsedSeconds)
        {
            if (!feedbackActive || Double.IsNaN(elapsedSeconds) || Double.IsInfinity(elapsedSeconds)) return;
            double cycle = (Math.Max(0, elapsedSeconds) % 1.4) / 1.4;
            pulse = (1 - Math.Cos(cycle * Math.PI * 2)) / 2;
            // The interior tint stays constant. Repaint only moving/emphasized parts.
            Rectangle bounds = FeedbackBounds();
            int margin = Math.Min(feedbackMargin, Math.Max(1, Math.Min(bounds.Width, bounds.Height)));
            Invalidate(new Rectangle(bounds.Left, bounds.Top, bounds.Width, margin));
            Invalidate(new Rectangle(bounds.Left, Math.Max(bounds.Top, bounds.Bottom - margin), bounds.Width, margin));
            Invalidate(new Rectangle(bounds.Left, bounds.Top, margin, bounds.Height));
            Invalidate(new Rectangle(Math.Max(bounds.Left, bounds.Right - margin), bounds.Top, margin, bounds.Height));
            if (!lastBadgeBounds.IsEmpty)
                Invalidate(Rectangle.Inflate(lastBadgeBounds, feedbackMargin, feedbackMargin));
        }

        protected override void OnResize(EventArgs e)
        {
            lastBadgeBounds = Rectangle.Empty;
            base.OnResize(e);
            if (feedbackActive) Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!feedbackActive) return;
            Rectangle bounds = FeedbackBounds();
            if (bounds.Width < 2 || bounds.Height < 2) return;
            float scale = e.Graphics.DpiX / 96F;
            feedbackMargin = Math.Max(4, (int)Math.Ceiling(10 * scale));
            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush tint = new SolidBrush(Color.FromArgb(14, Theme.Accent)))
                    e.Graphics.FillRectangle(tint, bounds);

                float frameInset = 3 * scale;
                RectangleF frame = new RectangleF(bounds.Left + frameInset, bounds.Top + frameInset,
                    bounds.Width - frameInset * 2 - 1, bounds.Height - frameInset * 2 - 1);
                if (frame.Width > 0 && frame.Height > 0)
                {
                    using (Pen glow = new Pen(Color.FromArgb(25 + (int)(25 * pulse), Theme.Accent), 6 * scale))
                        e.Graphics.DrawRectangle(glow, frame.X, frame.Y, frame.Width, frame.Height);
                    using (Pen border = new Pen(Color.FromArgb(165 + (int)(80 * pulse), Theme.Accent), 2 * scale))
                        e.Graphics.DrawRectangle(border, frame.X, frame.Y, frame.Width, frame.Height);
                }
                DrawBadge(e.Graphics, bounds, scale);
            }
            finally { e.Graphics.Restore(state); }
        }

        private Rectangle FeedbackBounds()
        {
            Rectangle bounds = ClientRectangle;
            // Native scroll bars are child windows; keep the frame beside them.
            foreach (Control child in Controls)
            {
                if (!child.Visible || !(child is ScrollBar)) continue;
                if (child.Height > child.Width) bounds.Width = Math.Max(0, Math.Min(bounds.Right, child.Left) - bounds.Left);
                else bounds.Height = Math.Max(0, Math.Min(bounds.Bottom, child.Top) - bounds.Top);
            }
            return bounds;
        }

        private void DrawBadge(Graphics graphics, Rectangle bounds, float scale)
        {
            const TextFormatFlags measureFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix;
            const TextFormatFlags drawFlags = measureFlags | TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;
            string title = "놓으면 추가됩니다";
            string detail = String.Format("{0:N0}개 폴더 · 하위 폴더 파일 포함", folderCount);
            Size titleSize = TextRenderer.MeasureText(graphics, title, feedbackTitleFont, Size.Empty, measureFlags);
            Size detailSize = TextRenderer.MeasureText(graphics, detail, feedbackDetailFont, Size.Empty, measureFlags);
            int paddingX = Math.Max(4, (int)Math.Ceiling(20 * scale));
            int paddingY = Math.Max(3, (int)Math.Ceiling(14 * scale));
            int gap = Math.Max(2, (int)Math.Ceiling(4 * scale));
            int inset = Math.Max(4, (int)Math.Ceiling(24 * scale));
            int header = ColumnHeadersVisible ? ColumnHeadersHeight : 0;
            Rectangle body = new Rectangle(bounds.Left, Math.Min(bounds.Bottom, bounds.Top + header), bounds.Width,
                Math.Max(0, bounds.Height - header));
            inset = Math.Min(inset, Math.Max(0, Math.Min(body.Width, body.Height) / 4));
            body.Inflate(-inset, -inset);
            if (body.Width < 24 * scale || body.Height < 18 * scale)
            {
                lastBadgeBounds = Rectangle.Empty;
                return;
            }

            int desiredWidth = Math.Max(titleSize.Width, detailSize.Width) + paddingX * 2;
            int desiredHeight = titleSize.Height + detailSize.Height + gap + paddingY * 2;
            int width = Math.Min(desiredWidth, body.Width);
            int height = Math.Min(desiredHeight, body.Height);
            Rectangle badge = new Rectangle(body.Left + (body.Width - width) / 2,
                body.Top + (body.Height - height) / 2, width, height);
            lastBadgeBounds = badge;
            using (GraphicsPath path = RoundedRectangle(badge, 8 * scale))
            {
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(246, Theme.Background)))
                    graphics.FillPath(fill, path);
                using (Pen border = new Pen(Color.FromArgb(90 + (int)(35 * pulse), Theme.Accent), Math.Max(1, scale)))
                    graphics.DrawPath(border, path);
            }

            paddingX = Math.Min(paddingX, Math.Max(1, badge.Width / 8));
            paddingY = Math.Min(paddingY, Math.Max(1, badge.Height / 5));
            Rectangle textArea = Rectangle.Inflate(badge, -paddingX, -paddingY);
            if (textArea.Width < 1 || textArea.Height < 1) return;
            bool showDetail = textArea.Height >= titleSize.Height + gap + detailSize.Height;
            if (showDetail)
            {
                int y = textArea.Top + (textArea.Height - titleSize.Height - gap - detailSize.Height) / 2;
                TextRenderer.DrawText(graphics, title, feedbackTitleFont,
                    new Rectangle(textArea.Left, y, textArea.Width, titleSize.Height), Theme.Accent, drawFlags);
                TextRenderer.DrawText(graphics, detail, feedbackDetailFont,
                    new Rectangle(textArea.Left, y + titleSize.Height + gap, textArea.Width, detailSize.Height),
                    Theme.Text, drawFlags);
            }
            else TextRenderer.DrawText(graphics, title, feedbackTitleFont, textArea, Theme.Accent, drawFlags);
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter < 2) { path.AddRectangle(bounds); return path; }
            float right = bounds.Right - 1, bottom = bounds.Bottom - 1;
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(right - diameter, bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                feedbackTitleFont.Dispose();
                feedbackDetailFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
