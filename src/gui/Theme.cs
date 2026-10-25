using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CmdWatch.Gui
{
    /// <summary>
    /// Paleta e tipografia do painel. As cores seguem a referencia visual:
    /// fundo quase preto com leve verde, paineis grafite, destaques em ciano
    /// (titulos), verde (positivo), vermelho (alerta/erro), ambar, azul, magenta e roxo.
    /// </summary>
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(0x0B, 0x0E, 0x0B);
        public static readonly Color Panel = Color.FromArgb(0x14, 0x18, 0x14);
        public static readonly Color PanelDeep = Color.FromArgb(0x0F, 0x12, 0x0F);
        public static readonly Color Border = Color.FromArgb(0x2A, 0x31, 0x2A);
        public static readonly Color Grid = Color.FromArgb(0x1E, 0x24, 0x1E);
        public static readonly Color Text = Color.FromArgb(0xC6, 0xCF, 0xC6);
        public static readonly Color Dim = Color.FromArgb(0x76, 0x80, 0x76);

        public static readonly Color Cyan = Color.FromArgb(0x2F, 0xDF, 0xE4);
        public static readonly Color Green = Color.FromArgb(0x2A, 0xC6, 0x63);
        public static readonly Color Red = Color.FromArgb(0xFF, 0x3D, 0x3D);
        public static readonly Color Amber = Color.FromArgb(0xF0, 0xA8, 0x3C);
        public static readonly Color Blue = Color.FromArgb(0x3D, 0x9B, 0xE8);
        public static readonly Color Purple = Color.FromArgb(0xA3, 0x5B, 0xF0);
        public static readonly Color Magenta = Color.FromArgb(0xDD, 0x45, 0xC6);
        public static readonly Color Yellow = Color.FromArgb(0xE6, 0xD2, 0x4C);

        public static readonly Font Ui = new Font("Segoe UI", 8.75f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font UiBold = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font UiTitle = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font Mono = new Font("Consolas", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font MonoSmall = new Font("Consolas", 8f, FontStyle.Regular, GraphicsUnit.Point);

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (radius < 1) radius = 1;
            int d = radius * 2;
            if (d > r.Width) d = Math.Max(2, r.Width);
            if (d > r.Height) d = Math.Max(2, r.Height);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void DrawPanel(Graphics g, Rectangle r, int radius, Color fill, Color border)
        {
            using (GraphicsPath path = RoundRect(r, radius))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                using (Pen p = new Pen(border)) g.DrawPath(p, path);
            }
        }

        public static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }

    /// <summary>
    /// Fonte de matriz 5x7 desenhada pixel a pixel (estilo LED/segmentado dos titulos).
    /// Cada glifo sao 7 linhas de 5 bits; o bit mais alto e o pixel da esquerda.
    /// </summary>
    internal static class PixelFont
    {
        private static readonly Dictionary<char, byte[]> Glyphs = new Dictionary<char, byte[]>();

        private static void Add(char c, string hex)
        {
            byte[] rows = new byte[7];
            for (int i = 0; i < 7; i++) rows[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            Glyphs[c] = rows;
        }

        static PixelFont()
        {
            Add('A', "0E11111F111111");
            Add('B', "1E11111E11111E");
            Add('C', "0E11101010110E");
            Add('D', "1E11111111111E");
            Add('E', "1F10101E10101F");
            Add('F', "1F10101E101010");
            Add('G', "0E11101711110F");
            Add('H', "1111111F111111");
            Add('I', "0E04040404040E");
            Add('J', "0702020202120C");
            Add('K', "11121418141211");
            Add('L', "1010101010101F");
            Add('M', "111B1515111111");
            Add('N', "11191513111111");
            Add('O', "0E11111111110E");
            Add('P', "1E11111E101010");
            Add('Q', "0E11111115120D");
            Add('R', "1E11111E141211");
            Add('S', "0F10100E01011E");
            Add('T', "1F040404040404");
            Add('U', "1111111111110E");
            Add('V', "11111111110A04");
            Add('W', "11111115151B11");
            Add('X', "11110A040A1111");
            Add('Y', "11110A04040404");
            Add('Z', "1F01020408101F");
            Add('0', "0E11131519110E");
            Add('1', "040C040404040E");
            Add('2', "0E11010204081F");
            Add('3', "1F02040201110E");
            Add('4', "02060A121F0202");
            Add('5', "1F101E0101110E");
            Add('6', "0608101E11110E");
            Add('7', "1F010204080808");
            Add('8', "0E11110E11110E");
            Add('9', "0E11110F01020C");
            Add(' ', "00000000000000");
            Add('.', "0000000000000C0C");
            Add(',', "00000000000C0408");
            Add(':', "000C0C000C0C00");
            Add(';', "000C0C000C0408");
            Add('-', "0000001F000000");
            Add('+', "0004041F040400");
            Add('=', "00001F001F0000");
            Add('_', "0000000000001F");
            Add('/', "01020204080810");
            Add('\\', "10080804020201");
            Add('(', "02040808080402");
            Add(')', "08040202020408");
            Add('[', "0E08080808080E");
            Add(']', "0E02020202020E");
            Add('<', "02040810080402");
            Add('>', "08040201020408");
            Add('%', "191A0204080B13");
            Add('°', "0C120C00000000");
            Add('!', "04040404040004");
            Add('?', "0E110102040004");
            Add('\'', "04040000000000");
            Add('"', "0A0A0000000000");
            Add('#', "0A0A1F0A1F0A0A");
            Add('*', "00150E1F0E1500");
            Add('&', "0C12140815120D");
            Add('@', "0E11171517100E");
            Add('$', "040F140E051E04");
            Add('·', "00000C0C000000");
        }

        public static int MeasureWidth(string text, int scale)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return text.Length * 6 * scale;
        }

        public static int Height(int scale) { return 7 * scale; }

        /// <summary>Desenha o texto como matriz de pixels, com halo opcional.</summary>
        public static void Draw(Graphics g, string text, int x, int y, int scale, Color color, bool glow)
        {
            if (string.IsNullOrEmpty(text)) return;

            using (SolidBrush glowBrush = new SolidBrush(Color.FromArgb(58, color)))
            using (SolidBrush solidBrush = new SolidBrush(color))
            {
                int cx = x;
                string up = text.ToUpperInvariant();
                for (int i = 0; i < up.Length; i++)
                {
                    byte[] rows;
                    if (!Glyphs.TryGetValue(up[i], out rows)) rows = Glyphs[' '];
                    for (int r = 0; r < 7; r++)
                    {
                        byte bits = rows[r];
                        if (bits == 0) continue;
                        for (int c = 0; c < 5; c++)
                        {
                            if ((bits & (1 << (4 - c))) == 0) continue;
                            int px = cx + c * scale;
                            int py = y + r * scale;
                            if (glow)
                                g.FillRectangle(glowBrush, px - scale, py - scale, scale * 3, scale * 3);
                            g.FillRectangle(solidBrush, px, py, scale, scale);
                        }
                    }
                    cx += 6 * scale;
                }
            }
        }
    }
}
