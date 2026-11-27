using System;
using System.Drawing;
using System.Windows.Forms;

namespace CmdWatch.Gui
{
    /// <summary>
    /// Dialogo modal neo-brutalista (0px radius, borda 1px #2A2A2A, cabecalho
    /// com marcador quadrado amarelo acido). ESC cancela, ENTER confirma.
    /// </summary>
    internal sealed class NeonDialog : Form
    {
        private readonly string _title;
        private readonly string _message;
        private readonly string _okText;
        private readonly string _cancelText;
        private readonly Color _okAccent;

        private Rectangle _okRect, _cancelRect, _closeX;
        private int _hover = -1; // 0 = confirmar, 1 = cancelar, 2 = X
        private const int Pad = 18;
        private const int HeadH = 40;
        private const int BtnW = 124;
        private const int BtnH = 30;

        public NeonDialog(string title, string message, string okText, string cancelText, Color okAccent)
        {
            _title = title ?? "";
            _message = message ?? "";
            _okText = okText ?? "";
            _cancelText = cancelText ?? "";
            _okAccent = okAccent;

            Text = _title;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            KeyPreview = true;
            MinimizeBox = false;
            MaximizeBox = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            int w = 460;
            Size msg = TextRenderer.MeasureText(_message, Theme.MonoSmall,
                new Size(w - Pad * 2 - 16, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int bodyH = Math.Max(48, msg.Height + 24);
            int h = HeadH + 14 + bodyH + 16 + BtnH + 16;

            ClientSize = new Size(w, h);

            _closeX = new Rectangle(w - 12 - 24, (HeadH - 24) / 2, 24, 24);
            _okRect = new Rectangle(w - Pad - BtnW, h - 16 - BtnH, BtnW, BtnH);
            _cancelRect = _cancelText.Length > 0
                ? new Rectangle(_okRect.Left - 8 - BtnW, h - 16 - BtnH, BtnW, BtnH)
                : Rectangle.Empty;
        }

        public static bool Confirm(IWin32Window owner, string title, string message,
            string okText, string cancelText, Color okAccent)
        {
            using (NeonDialog dlg = new NeonDialog(title, message, okText, cancelText, okAccent))
            {
                if (owner != null && owner is Form)
                {
                    Form f = (Form)owner;
                    dlg.StartPosition = FormStartPosition.Manual;
                    dlg.Location = new Point(
                        f.Left + (f.Width - dlg.Width) / 2,
                        f.Top + (f.Height - dlg.Height) / 2);
                }
                return dlg.ShowDialog(owner) == DialogResult.OK;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);

            // Cabecalho neo-brutalista
            using (SolidBrush hb = new SolidBrush(Theme.Fill))
                g.FillRectangle(hb, 0, 0, Width, HeadH);
            using (Pen p = new Pen(Theme.Border))
            {
                g.DrawLine(p, 0, HeadH, Width, HeadH);
                g.DrawLine(p, 0, Height - 16 - BtnH - 12, Width, Height - 16 - BtnH - 12);
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }

            // Barra de destaque lateral esquerda
            using (SolidBrush ab = new SolidBrush(_okAccent))
                g.FillRectangle(ab, 0, 0, 3, HeadH);

            Theme.DrawDot(g, Pad + 3, HeadH / 2, 6, _okAccent);
            TextRenderer.DrawText(g, _title.ToUpperInvariant(), Theme.UiTitle,
                new Rectangle(Pad + 14, 0, Width - Pad * 2 - 44, HeadH), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            // Botao [x]
            Theme.DrawPanel(g, new Rectangle(_closeX.X, _closeX.Y, _closeX.Width - 1, _closeX.Height - 1),
                0, Theme.PanelDeep, _hover == 2 ? Theme.Teal : Theme.Border);
            TextRenderer.DrawText(g, "×", Theme.MonoBold, _closeX, _hover == 2 ? Theme.Teal : Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            // Caixa interna da mensagem
            Rectangle msgBox = new Rectangle(Pad, HeadH + 14, Width - Pad * 2 - 1, Height - HeadH - BtnH - 46);
            Theme.DrawPanel(g, msgBox, 0, Theme.PanelDeep, Theme.Border);
            TextRenderer.DrawText(g, _message, Theme.MonoSmall,
                new Rectangle(msgBox.X + 10, msgBox.Y + 10, msgBox.Width - 20, msgBox.Height - 16), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

            if (!_cancelRect.IsEmpty) DrawButton(g, _cancelRect, _cancelText, false, _hover == 1);
            DrawButton(g, _okRect, _okText, true, _hover == 0);
        }

        private void DrawButton(Graphics g, Rectangle r, string text, bool primary, bool hover)
        {
            bool isAcid = _okAccent == Theme.Teal;
            Color fill = primary
                ? (hover ? (isAcid ? Theme.AcidHover : Theme.Lighten(_okAccent, 0.12f)) : _okAccent)
                : (hover ? Theme.Hover : Theme.PanelDeep);
            Color fg = primary ? (isAcid ? Theme.OnAcid : Color.White) : (hover ? Theme.Text : Theme.Dim);
            Color bd = primary ? Color.Empty : (hover ? Theme.Text : Theme.Border);

            Theme.DrawPanel(g, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), 0, fill, bd);
            TextRenderer.DrawText(g, (text ?? "").ToUpperInvariant(), Theme.MonoBold, r, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private int HitTest(Point p)
        {
            if (_okRect.Contains(p)) return 0;
            if (!_cancelRect.IsEmpty && _cancelRect.Contains(p)) return 1;
            if (_closeX.Contains(p)) return 2;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            if (h != _hover)
            {
                _hover = h;
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int h = HitTest(e.Location);
            if (h == 0) { DialogResult = DialogResult.OK; Close(); }
            else if (h == 1 || h == 2) { DialogResult = DialogResult.Cancel; Close(); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DashboardForm.AplicarMoldura(Handle);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            if (keyData == Keys.Enter)
            {
                DialogResult = DialogResult.OK;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
