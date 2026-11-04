using System;
using System.Drawing;
using System.Windows.Forms;

namespace CmdWatch.Gui
{
    /// <summary>
    /// Titulos das secoes: texto semibold na cor do texto, sem halo nem
    /// maiusculas forcadas. O valor (contador) a direita sai em cinza.
    /// TextRenderer usa o cache de glifos do GDI - barato de repintar.
    /// </summary>
    internal static class TechTitle
    {
        private const TextFormatFlags Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                                              TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        public static void Draw(Graphics g, string text, Rectangle area, Color color)
        {
            Draw(g, text, area, color, false);
        }

        public static void Draw(Graphics g, string text, Rectangle area, Color color, bool rightAlign)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (rightAlign)
                TextRenderer.DrawText(g, text, Theme.Ui, area, Theme.Dim, Flags | TextFormatFlags.Right);
            else
                TextRenderer.DrawText(g, text, Theme.UiTitle, area, Theme.Text, Flags | TextFormatFlags.Left);
        }

        public static float MeasureWidth(Graphics g, string text, Font f)
        {
            if (string.IsNullOrEmpty(text) || f == null) return 0;
            return TextRenderer.MeasureText(g, text, f, Size.Empty, Flags).Width;
        }
    }
}
