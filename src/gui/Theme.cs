using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CmdWatch.Gui
{
    /// <summary>
    /// Paleta e tipografia do painel. Visual limpo: fundo cinza muito claro,
    /// cartoes brancos de cantos arredondados, texto quase preto, um unico azul
    /// de destaque e cores de estado (verde, laranja, vermelho, cinza). Segue o
    /// O padrao e o tema escuro; CMDWATCH_TEMA=claro troca para a paleta clara.
    /// Os nomes antigos (Cyan, Panel, PanelDeep...) foram mantidos como papeis:
    /// Cyan e o destaque, Panel e o cartao, PanelDeep o fundo das listas.
    /// </summary>
    internal static class Theme
    {
        public static readonly bool Dark;

        public static readonly Color Bg, Panel, PanelDeep, Border, Grid, Text, Dim;
        public static readonly Color Cyan, Green, Red, Amber, Blue, Purple, Magenta, Yellow;
        /// <summary>Fundo da linha sob o mouse e cor dos controles "pilula" inativos.</summary>
        public static readonly Color Hover, Fill, Gray;
        /// <summary>Verde-agua de destaque: item ativo, selecao, icones das estatisticas.</summary>
        public static readonly Color Teal = Color.FromArgb(0x2D, 0xD4, 0xA0);

        public static readonly Font Ui, UiBold, UiTitle, UiHeader, UiSmall, Mono, MonoSmall;
        /// <summary>Numeros grandes das estatisticas, titulo da pagina e icones (Segoe Fluent Icons / MDL2).</summary>
        public static readonly Font UiStat, UiPage, IconFont, IconBig;

        static Theme()
        {
            string forcado = (Environment.GetEnvironmentVariable("CMDWATCH_TEMA") ?? "").Trim().ToLowerInvariant();
            if (forcado == "claro" || forcado == "light") Dark = false;
            else Dark = true;   // escuro e o visual padrao do painel; claro so por CMDWATCH_TEMA=claro

            if (Dark)
            {
                // azul-marinho quase preto, cartoes um tom acima com borda fina,
                // verde-agua como destaque (estados ativos) e azul para comandos
                Bg = Rgb(0x0B1117); Panel = Rgb(0x111820); PanelDeep = Rgb(0x111820);
                Border = Rgb(0x1E2A33); Grid = Rgb(0x1A242D); Text = Rgb(0xE6EDF3); Dim = Rgb(0x8B98A5);
                Cyan = Rgb(0x3B82F6); Green = Rgb(0x34D399); Red = Rgb(0xEF4444); Amber = Rgb(0xF5B544);
                Blue = Rgb(0x60A5FA); Purple = Rgb(0xA78BFA); Magenta = Rgb(0xF472B6); Yellow = Rgb(0xFACC15);
                Hover = Rgb(0x16202A); Fill = Rgb(0x18222C); Gray = Rgb(0x6B7785);
            }
            else
            {
                Bg = Rgb(0xF5F5F7); Panel = Rgb(0xFFFFFF); PanelDeep = Rgb(0xFFFFFF);
                Border = Rgb(0xE3E3E8); Grid = Rgb(0xEDEDF0); Text = Rgb(0x1D1D1F); Dim = Rgb(0x6E6E73);
                // tons um pouco mais escuros que os de sistema: texto colorido legivel sobre branco
                Cyan = Rgb(0x007AFF); Green = Rgb(0x1F9D4C); Red = Rgb(0xE5342A); Amber = Rgb(0xC77700);
                Blue = Rgb(0x0A6CC2); Purple = Rgb(0x9944C8); Magenta = Rgb(0xD12B5A); Yellow = Rgb(0x9A7400);
                Hover = Rgb(0xF2F2F7); Fill = Rgb(0xE9E9ED); Gray = Rgb(0xAEAEB2);
            }

            string sans = Existe("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
            string semi = Existe("Segoe UI Semibold") ? "Segoe UI Semibold" : "Segoe UI";
            string mono = Existe("Cascadia Mono") ? "Cascadia Mono" : "Consolas";
            Ui = new Font(sans, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            UiSmall = new Font(sans, 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            UiBold = new Font(semi, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            UiTitle = new Font(semi, 11.5f, FontStyle.Regular, GraphicsUnit.Point);
            UiHeader = new Font(semi, 14f, FontStyle.Regular, GraphicsUnit.Point);
            Mono = new Font(mono, 9f, FontStyle.Regular, GraphicsUnit.Point);
            MonoSmall = new Font(mono, 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            string bold = Existe("Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";
            UiStat = new Font(bold, 14f, FontStyle.Bold, GraphicsUnit.Point);
            UiPage = new Font(bold, 18f, FontStyle.Bold, GraphicsUnit.Point);
            string icones = Existe("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
            IconFont = new Font(icones, 10f, FontStyle.Regular, GraphicsUnit.Point);
            IconBig = new Font(icones, 16f, FontStyle.Regular, GraphicsUnit.Point);
        }

        /// <summary>Desenha um icone (glifo da fonte de icones do Windows) centrado em r.</summary>
        public static void DrawIcon(Graphics g, string glyph, Rectangle r, Color c, Font f = null)
        {
            TextRenderer.DrawText(g, glyph, f ?? IconFont, r, c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        /// <summary>Pilula de estado com contorno e fundo translucido na cor do estado.</summary>
        public static void DrawPill(Graphics g, Rectangle r, string text, Color c, bool filled)
        {
            DrawPanel(g, r, r.Height / 2, Color.FromArgb(filled ? 40 : 26, c), Color.FromArgb(filled ? 200 : 90, c));
            TextRenderer.DrawText(g, text, UiSmall, r, filled ? c : Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        private static Color Rgb(int v) { return Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF); }

        private static bool Existe(string familia)
        {
            try
            {
                // o construtor de Font cai silenciosamente em outra fonte:
                // so aceita quando o nome devolvido e o mesmo pedido
                using (Font f = new Font(familia, 9f))
                    return string.Equals(f.Name, familia, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>Le o tema de aplicativos do Windows (AppsUseLightTheme = 0 e escuro).</summary>
        internal static bool WindowsEscuro()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser
                    .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k == null) return false;
                    object v = k.GetValue("AppsUseLightTheme");
                    if (v == null) return false;
                    return Convert.ToInt32(v) == 0;
                }
            }
            catch { return false; }
        }

        /// <summary>Fonte dos titulos das secoes (antes Bahnschrift; agora a mesma familia da interface).</summary>
        public static Font TitleFont { get { return UiTitle; } }

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

        /// <summary>Superficie arredondada chapada com borda fina (sem gradiente).</summary>
        public static void DrawPanel(Graphics g, Rectangle r, int radius, Color fill, Color border)
        {
            SmoothingMode antes = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundRect(r, radius))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border.A > 0) using (Pen p = new Pen(border)) g.DrawPath(p, path);
            }
            g.SmoothingMode = antes;
        }

        /// <summary>Cartao: superficie branca com sombra suave por baixo (no escuro, so a borda).</summary>
        public static void DrawCard(Graphics g, Rectangle r, int radius)
        {
            SmoothingMode antes = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (!Dark)
            {
                // sombra: dois contornos translucidos deslocados para baixo
                for (int i = 2; i >= 1; i--)
                {
                    Rectangle s = new Rectangle(r.X, r.Y + i, r.Width, r.Height);
                    using (GraphicsPath sp = RoundRect(s, radius))
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(i == 1 ? 14 : 8, 0, 0, 0)))
                        g.FillPath(sb, sp);
                }
            }
            using (GraphicsPath path = RoundRect(r, radius))
            {
                using (SolidBrush b = new SolidBrush(Panel)) g.FillPath(b, path);
                using (Pen p = new Pen(Dark ? Border : Color.FromArgb(0xE6, 0xE6, 0xEA))) g.DrawPath(p, path);
            }
            g.SmoothingMode = antes;
        }

        /// <summary>Bolinha de estado (verde = ativo, laranja = escondido, cinza = encerrado).</summary>
        public static void DrawDot(Graphics g, int cx, int cy, int d, Color c)
        {
            SmoothingMode antes = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, cx - d / 2f, cy - d / 2f, d, d);
            g.SmoothingMode = antes;
        }

        public static Color Lighten(Color c, float t)
        {
            return Color.FromArgb(c.A,
                (int)Math.Min(255, c.R + (255 - c.R) * t),
                (int)Math.Min(255, c.G + (255 - c.G) * t),
                (int)Math.Min(255, c.B + (255 - c.B) * t));
        }

        public static Color Darken(Color c, float t)
        {
            return Color.FromArgb(c.A,
                (int)Math.Max(0, c.R - (255 - c.R) * t),
                (int)Math.Max(0, c.G - (255 - c.G) * t),
                (int)Math.Max(0, c.B - (255 - c.B) * t));
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

        /// <summary>Cor de cada papel de linha no fluxo de comandos.</summary>
        public static Color FlowColor(HumanRole role)
        {
            switch (role)
            {
                case HumanRole.Open: return Cyan;
                case HumanRole.Call: return Purple;
                case HumanRole.Step: return Amber;
                case HumanRole.Close: return Green;
                case HumanRole.Banner: return Cyan;
                default: return Text;
            }
        }

        /// <summary>Desenha o polegar de uma barra de rolagem (ponta arredondada).</summary>
        public static void DrawScrollThumb(Graphics g, Rectangle r, Color fill)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            int radius = r.Width / 2;
            if (radius < 2) radius = 2;
            SmoothingMode antes = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = RoundRect(r, radius))
            using (SolidBrush b = new SolidBrush(fill))
                g.FillPath(b, p);
            g.SmoothingMode = antes;
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
