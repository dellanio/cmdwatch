using System;
using System.Drawing;
using System.Windows.Forms;

namespace CmdWatch.Gui
{
    /// <summary>
    /// Dialogo modal de confirmacao desenhado no tema do painel (o MessageBox
    /// do Windows apareceria claro sobre a interface escura).
    ///
    /// ESC cancela, ENTER confirma.
    /// </summary>
    internal sealed class NeonDialog : Form
    {
        private readonly string _title;
        private readonly string _message;
        private readonly string _okText;
        private readonly string _cancelText;
        private readonly Color _okAccent;

        private Rectangle _okRect, _cancelRect;
        private int _hover = -1;          // 0 = confirmar, 1 = cancelar
        private const int Pad = 20;
        private const int BtnW = 112;
        private const int BtnH = 30;

        public NeonDialog(string title, string message, string okText, string cancelText, Color okAccent)
        {
            _title = title ?? "";
            _message = message ?? "";
            _okText = okText ?? "";
            _cancelText = cancelText ?? "";
            _okAccent = okAccent;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            KeyPreview = true;
            MinimizeBox = false;
            MaximizeBox = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            int w = 430;
            Size msg = TextRenderer.MeasureText(_message, Theme.Ui,
                new Size(w - Pad * 2, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int h = Pad + 26 + 10 + Math.Max(20, msg.Height) + 20 + BtnH + Pad;

            ClientSize = new Size(w, h);

            // [Cancelar] [Fechar] - o cancelador fica a esquerda da acao
            _okRect = new Rectangle(w - Pad - BtnW, h - Pad - BtnH, BtnW, BtnH);
            _cancelRect = new Rectangle(_okRect.Left - 10 - BtnW, h - Pad - BtnH, BtnW, BtnH);
        }

        /// <summary>Mostra o dialogo modal, centrado na janela proprietaria.</summary>
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
            g.Clear(Theme.Bg);
            Theme.DrawPanel(g, new Rectangle(0, 0, Width - 1, Height - 1), 8, Theme.Panel, Theme.Border);

            TextRenderer.DrawText(g, _title, Theme.UiTitle,
                new Rectangle(Pad, Pad, Width - Pad * 2, 24), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            int msgTop = Pad + 26 + 10;
            TextRenderer.DrawText(g, _message, Theme.Ui,
                new Rectangle(Pad, msgTop, Width - Pad * 2, Height - msgTop - BtnH - Pad - 10), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

            DrawButton(g, _cancelRect, _cancelText, false, _hover == 1);
            DrawButton(g, _okRect, _okText, true, _hover == 0);
        }

        private void DrawButton(Graphics g, Rectangle r, string text, bool primary, bool hover)
        {
            Color accent = primary ? _okAccent : Theme.Border;
            Color fill = primary
                ? (hover ? Color.FromArgb(70, _okAccent) : Color.FromArgb(38, _okAccent))
                : (hover ? Theme.Panel : Theme.PanelDeep);
            Color cor = primary ? _okAccent : (hover ? Theme.Text : Theme.Dim);

            Theme.DrawPanel(g, r, 4, fill, hover ? accent : Theme.Border);
            TextRenderer.DrawText(g, text, primary ? Theme.UiBold : Theme.Ui, r, cor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private int HitTest(Point p)
        {
            if (_okRect.Contains(p)) return 0;
            if (_cancelRect.Contains(p)) return 1;
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
            else if (h == 1) { DialogResult = DialogResult.Cancel; Close(); }
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
