using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace CmdWatch.Gui
{
    /// <summary>
    /// Titulos das secoes em fonte tecnica (estilo instrumento/HUD): maiuscula,
    /// espacamento aberto entre letras, gradiente vertical e halo neon.
    /// Fallback final: a matriz de pixels 5x7, caso o sistema nao tenha
    /// nenhuma das fontes tecnicas.
    /// </summary>
    internal static class TechTitle
    {
        private const float Tracking = 2.4f;
        private static readonly Dictionary<char, float> WidthCache = new Dictionary<char, float>();

        public static void Draw(Graphics g, string text, Rectangle area, Color color)
        {
            Draw(g, text, area, color, false);
        }

        public static void Draw(Graphics g, string text, Rectangle area, Color color, bool rightAlign)
        {
            if (string.IsNullOrEmpty(text)) return;

            Font f = Theme.TitleFont;
            if (f == null)
            {
                // nenhum fonte de texto disponivel: usa a matriz de pixels
                PixelFont.Draw(g, text, area.Left, area.Top, 3, color, true);
                return;
            }

            TextRenderingHint old = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            string up = text.ToUpperInvariant();
            float total = MeasureWidth(g, up, f);
            float x = rightAlign ? area.Right - total : area.Left;
            float y = area.Y + (area.Height - f.GetHeight(g)) / 2f;

            // halo: 4 passadas deslocadas em uma cor translucida
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(44, color)))
            {
                int[] dx = { -2, 2, 0, 0 };
                int[] dy = { 0, 0, -2, 2 };
                for (int i = 0; i < 4; i++)
                    DrawTracked(g, up, f, x + dx[i], y + dy[i], glow);
            }

            // sombra dura para dar profundidade
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(130, Color.Black)))
                DrawTracked(g, up, f, x + 1, y + 1, shadow);

            // corpo com gradiente (topo mais claro)
            using (LinearGradientBrush grad = new LinearGradientBrush(
                    new RectangleF(x, y, Math.Max(1f, total), f.GetHeight(g)),
                    Lighten(color, 0.5f), color, LinearGradientMode.Vertical))
            {
                DrawTracked(g, up, f, x, y, grad);
            }

            g.TextRenderingHint = old;
        }

        public static float MeasureWidth(Graphics g, string text, Font f)
        {
            if (string.IsNullOrEmpty(text) || f == null) return 0;
            string up = text.ToUpperInvariant();
            float total = 0;
            foreach (char ch in up) total += CharW(g, ch, f) + Tracking;
            return total;
        }

        private static void DrawTracked(Graphics g, string text, Font f, float x, float y, Brush brush)
        {
            float cx = x;
            foreach (char ch in text)
            {
                string s = ch.ToString();
                if (s != " ")
                    g.DrawString(s, f, brush, cx, y, StringFormat.GenericTypographic);
                cx += CharW(g, ch, f) + Tracking;
                // espacos ganham folga extra para o separador nao virar "."
                if (s == " ") cx += 2.6f;
            }
        }

        private static float CharW(Graphics g, char ch, Font f)
        {
            float w;
            if (WidthCache.TryGetValue(ch, out w)) return w;
            w = g.MeasureString(ch.ToString(), f, PointF.Empty, StringFormat.GenericTypographic).Width;
            WidthCache[ch] = w;
            return w;
        }

        private static Color Lighten(Color c, float t)
        {
            return Color.FromArgb(c.A,
                (int)Math.Min(255, c.R + (255 - c.R) * t),
                (int)Math.Min(255, c.G + (255 - c.G) * t),
                (int)Math.Min(255, c.B + (255 - c.B) * t));
        }
    }
}
