using System;
using System.Drawing;
using System.Windows.Forms;

namespace CmdWatch.Gui
{
    /// <summary>
    /// Paleta do menu de contexto no tema do painel. O menu nativo do Windows
    /// apareceria claro sobre a interface escura - aqui as cores sao as do
    /// painel (fundo grafite, selecao ciano).
    /// </summary>
    internal sealed class NeonMenuColors : ProfessionalColorTable
    {
        private static readonly Color Sel = Theme.Hover;

        public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Teal; } }
        public override Color MenuItemSelected { get { return Sel; } }
        public override Color MenuItemSelectedGradientBegin { get { return Sel; } }
        public override Color MenuItemSelectedGradientEnd { get { return Sel; } }
        public override Color MenuItemPressedGradientBegin { get { return Sel; } }
        public override Color MenuItemPressedGradientEnd { get { return Sel; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
        public override Color ToolStripGradientBegin { get { return Theme.Surface; } }
        public override Color ToolStripGradientMiddle { get { return Theme.Surface; } }
        public override Color ToolStripGradientEnd { get { return Theme.Surface; } }
    }

    /// <summary>
    /// Desenha o menu de contexto no estilo Hoody: cantos retos, fundo escuro,
    /// barra lateral amarelo acido no item selecionado e separadores de 1px.
    /// </summary>
    internal sealed class NeonMenuRenderer : ToolStripProfessionalRenderer
    {
        public NeonMenuRenderer() : base(new NeonMenuColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.Surface))
                e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.ToolStrip.Size));
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen p = new Pen(Theme.Border))
            {
                Rectangle r = e.ToolStrip.ClientRectangle;
                e.Graphics.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled || !e.Item.Selected) return;
            Rectangle r = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            using (SolidBrush b = new SolidBrush(Theme.Hover))
                e.Graphics.FillRectangle(b, r);
            using (SolidBrush acc = new SolidBrush(Theme.Teal))
                e.Graphics.FillRectangle(acc, 2, 1, 3, e.Item.Height - 2);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Color c = !e.Item.Enabled ? Theme.Dim : (e.Item.Selected ? Theme.Teal : Theme.Text);
            Font f = !e.Item.Enabled ? Theme.MonoBold : Theme.MonoSmall;
            TextRenderer.DrawText(e.Graphics, e.Text, f, e.TextRectangle, c,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(Theme.Border))
                e.Graphics.DrawLine(p, 6, y, e.ToolStrip.Width - 6, y);
        }
    }
}
