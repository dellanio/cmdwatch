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
        private static readonly Color Sel = Color.FromArgb(46, Theme.Cyan);

        public override Color ToolStripDropDownBackground { get { return Theme.PanelDeep; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Color.FromArgb(150, Theme.Cyan); } }
        public override Color MenuItemSelected { get { return Sel; } }
        public override Color MenuItemSelectedGradientBegin { get { return Sel; } }
        public override Color MenuItemSelectedGradientEnd { get { return Sel; } }
        public override Color MenuItemPressedGradientBegin { get { return Sel; } }
        public override Color MenuItemPressedGradientEnd { get { return Sel; } }
        public override Color SeparatorDark { get { return Theme.Grid; } }
        public override Color SeparatorLight { get { return Theme.Grid; } }
        public override Color ImageMarginGradientBegin { get { return Theme.PanelDeep; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.PanelDeep; } }
        public override Color ImageMarginGradientEnd { get { return Theme.PanelDeep; } }
        public override Color ToolStripGradientBegin { get { return Theme.PanelDeep; } }
        public override Color ToolStripGradientMiddle { get { return Theme.PanelDeep; } }
        public override Color ToolStripGradientEnd { get { return Theme.PanelDeep; } }
    }

    /// <summary>
    /// Desenha o menu de contexto no estilo do painel: fundo escuro, item
    /// selecionado em ciano, texto na cor do tema e separadores discretos.
    /// </summary>
    internal sealed class NeonMenuRenderer : ToolStripProfessionalRenderer
    {
        public NeonMenuRenderer() : base(new NeonMenuColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.PanelDeep))
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

            Rectangle r = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(46, Theme.Cyan)))
                e.Graphics.FillRectangle(b, r);
            using (Pen p = new Pen(Color.FromArgb(150, Theme.Cyan)))
                e.Graphics.DrawRectangle(p, r);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Color c = e.Item.Enabled ? Theme.Text : Theme.Dim;
            Font f = e.TextFont ?? SystemFonts.MenuFont;
            TextRenderer.DrawText(e.Graphics, e.Text, f, e.TextRectangle, c,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(Theme.Grid))
                e.Graphics.DrawLine(p, 8, y, e.ToolStrip.Width - 8, y);
        }
    }
}
