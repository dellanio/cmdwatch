using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CmdWatch.Gui
{
    /// <summary>
    /// Paleta e tipografia do painel no sistema visual Hoody / Neo-Brutalista:
    /// cantos 100% retos (0px radius), bordas estruturais de 1px, fundo grafite
    /// arquitetonico (#111111) e amarelo acido (#E2FF26) como destaque primario.
    /// CMDWATCH_TEMA=claro mantem uma variante clara de alto contraste.
    /// </summary>
    internal static class Theme
    {
        public static readonly bool Dark;

        public static readonly Color Bg, Panel, PanelDeep, Border, Grid, Text, Dim;
        public static readonly Color Surface, Sidebar, TopBar, HeaderBg, LineSubtle, OnAcid, AcidHover;
        public static readonly Color Cyan, Green, Red, Amber, Blue, Purple, Magenta, Yellow;
        public static readonly Color Hover, Fill, Gray;
        /// <summary>Destaque primario (Acid Yellow #E2FF26 no escuro).</summary>
        public static readonly Color Teal;

        public static readonly Font Ui, UiBold, UiTitle, UiHeader, UiSmall, Mono, MonoSmall, MonoBold, MonoTiny;
        public static readonly Font UiStat, UiPage, IconFont, IconBig;

        static Theme()
        {
            string forcado = (Environment.GetEnvironmentVariable("CMDWATCH_TEMA") ?? "").Trim().ToLowerInvariant();
            if (forcado == "claro" || forcado == "light") Dark = false;
            else Dark = true;

            if (Dark)
            {
                Bg = Rgb(0x111111);          // charcoal
                Surface = Rgb(0x141414);     // surface / KPIs / rail
                Sidebar = Rgb(0x161616);     // left sidebar
                TopBar = Rgb(0x181818);      // topbar
                HeaderBg = Rgb(0x141414);    // card header
                Panel = Rgb(0x171717);       // panel
                PanelDeep = Rgb(0x0E0E0E);   // terminal
                Border = Rgb(0x2A2A2A);      // structural 1px line
                LineSubtle = Rgb(0x222222);  // table row separator
                Grid = Rgb(0x242424);        // subtle grid / dashed line
                Text = Rgb(0xFFFFFF);        // crisp white
                Dim = Rgb(0x8A8A8A);         // muted
                Gray = Rgb(0x666666);        // dim

                Teal = Rgb(0xE2FF26);        // acid yellow
                AcidHover = Rgb(0xEEFF66);
                OnAcid = Rgb(0x0D0D0D);

                Cyan = Rgb(0xE2FF26);        // primario unificado no acido
                Green = Rgb(0xE2FF26);
                Red = Rgb(0xFF4D4D);
                Amber = Rgb(0xFFB020);
                Blue = Rgb(0xE2FF26);
                Purple = Rgb(0xCCCCCC);
                Magenta = Rgb(0xFF6B9D);
                Yellow = Rgb(0xE2FF26);

                Hover = Rgb(0x1D1D1D);
                Fill = Rgb(0x121212);
            }
            else
            {
                Bg = Rgb(0xF2F2EE);
                Surface = Rgb(0xFAFAF7);
                Sidebar = Rgb(0xEAEAE4);
                TopBar = Rgb(0xFFFFFF);
                HeaderBg = Rgb(0xF4F4F0);
                Panel = Rgb(0xFFFFFF);
                PanelDeep = Rgb(0xF7F7F3);
                Border = Rgb(0xCCCCCC);
                LineSubtle = Rgb(0xE2E2DC);
                Grid = Rgb(0xDDDDD6);
                Text = Rgb(0x111111);
                Dim = Rgb(0x5A5A5A);
                Gray = Rgb(0x888888);

                Teal = Rgb(0x111111);
                AcidHover = Rgb(0x2B2B2B);
                OnAcid = Rgb(0xE2FF26);

                Cyan = Rgb(0x111111);
                Green = Rgb(0x15803D);
                Red = Rgb(0xDC2626);
                Amber = Rgb(0xB45309);
                Blue = Rgb(0x1D4ED8);
                Purple = Rgb(0x6D28D9);
                Magenta = Rgb(0xBE185D);
                Yellow = Rgb(0x854D0E);

                Hover = Rgb(0xEDEDEA);
                Fill = Rgb(0xEAEAE5);
            }

            string sans = PickFont("Space Grotesk", "Bahnschrift", "Segoe UI Variable Text", "Segoe UI");
            string semi = PickFont("Space Grotesk", "Bahnschrift SemiBold", "Segoe UI Semibold", "Segoe UI");
            string disp = PickFont("Space Grotesk", "Bahnschrift", "Segoe UI Variable Display", "Segoe UI");
            string mono = PickFont("JetBrains Mono", "Cascadia Mono", "Consolas");
            string icones = Existe("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

            Ui = new Font(sans, 9.25f, FontStyle.Regular, GraphicsUnit.Point);
            UiSmall = new Font(mono, 8f, FontStyle.Regular, GraphicsUnit.Point);
            UiBold = new Font(semi, 9.25f, FontStyle.Bold, GraphicsUnit.Point);
            UiTitle = new Font(mono, 8.75f, FontStyle.Bold, GraphicsUnit.Point);
            UiHeader = new Font(disp, 13f, FontStyle.Bold, GraphicsUnit.Point);

            Mono = new Font(mono, 9f, FontStyle.Regular, GraphicsUnit.Point);
            MonoSmall = new Font(mono, 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            MonoBold = new Font(mono, 8.25f, FontStyle.Bold, GraphicsUnit.Point);
            MonoTiny = new Font(mono, 7.5f, FontStyle.Regular, GraphicsUnit.Point);

            UiStat = new Font(disp, 19f, FontStyle.Bold, GraphicsUnit.Point);
            UiPage = new Font(disp, 15f, FontStyle.Bold, GraphicsUnit.Point);
            IconFont = new Font(icones, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            IconBig = new Font(icones, 11.5f, FontStyle.Regular, GraphicsUnit.Point);
        }

        private static string PickFont(params string[] candidatas)
        {
            foreach (string c in candidatas)
                if (Existe(c)) return c;
            return "Segoe UI";
        }

        public static void DrawIcon(Graphics g, string glyph, Rectangle r, Color c, Font f = null)
        {
            TextRenderer.DrawText(g, glyph, f ?? IconFont, r, c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        /// <summary>Badge neo-brutalista: retangulo vivo com quadrado indicador de 5px e rotulo em caixa alta.</summary>
        public static void DrawPill(Graphics g, Rectangle r, string text, Color c, bool filled)
        {
            Color bg = filled ? Color.FromArgb(Dark ? 28 : 18, c) : PanelDeep;
            Color bd = filled ? Color.FromArgb(Dark ? 170 : 140, c) : Border;
            Color fg = filled ? c : Dim;
            DrawPanel(g, r, 0, bg, bd);

            string up = (text ?? "").ToUpperInvariant();
            int dot = 5;
            int dx = r.X + 7;
            int dy = r.Y + (r.Height - dot) / 2;
            using (SolidBrush b = new SolidBrush(filled ? c : Gray))
                g.FillRectangle(b, dx, dy, dot, dot);

            Rectangle tr = new Rectangle(dx + dot + 5, r.Y, Math.Max(4, r.Right - (dx + dot + 8)), r.Height);
            TextRenderer.DrawText(g, up, MonoTiny, tr, fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        private static Color Rgb(int v) { return Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF); }

        private static bool Existe(string familia)
        {
            try
            {
                using (Font f = new Font(familia, 9f))
                    return string.Equals(f.Name, familia, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

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

        public static Font TitleFont { get { return UiTitle; } }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddRectangle(r);
            return p;
        }

        /// <summary>Superficie estritamente retangular (0px border-radius) com borda de 1px.</summary>
        public static void DrawPanel(Graphics g, Rectangle r, int radius, Color fill, Color border)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            if (fill.A > 0)
            {
                using (SolidBrush b = new SolidBrush(fill))
                    g.FillRectangle(b, r.X, r.Y, r.Width + (border.A == 0 ? 1 : 0), r.Height + (border.A == 0 ? 1 : 0));
            }
            if (border.A > 0)
            {
                using (Pen p = new Pen(border, 1f))
                    g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            }
        }

        public static void DrawCard(Graphics g, Rectangle r, int radius)
        {
            DrawPanel(g, r, 0, Panel, Border);
        }

        /// <summary>Marcador geometrico quadrado (substitui as bolinhas arredondadas no tema Hoody).</summary>
        public static void DrawDot(Graphics g, int cx, int cy, int d, Color c)
        {
            int s = Math.Max(4, d - 1);
            using (SolidBrush b = new SolidBrush(c))
                g.FillRectangle(b, cx - s / 2, cy - s / 2, s, s);
        }

        /// <summary>Grade arquitetonica de 40x40px com mira discreta nos cruzamentos.</summary>
        public static void DrawArchGrid(Graphics g, Rectangle area)
        {
            if (!Dark || area.Width <= 0 || area.Height <= 0) return;
            const int step = 40;
            using (Pen linePen = new Pen(Color.FromArgb(9, 255, 255, 255), 1f))
            using (SolidBrush crossBrush = new SolidBrush(Color.FromArgb(32, 255, 255, 255)))
            {
                int xStart = (area.Left / step) * step;
                int yStart = (area.Top / step) * step;
                for (int x = xStart; x <= area.Right; x += step)
                    g.DrawLine(linePen, x, area.Top, x, area.Bottom);
                for (int y = yStart; y <= area.Bottom; y += step)
                    g.DrawLine(linePen, area.Left, y, area.Right, y);
                for (int x = xStart; x <= area.Right; x += step)
                    for (int y = yStart; y <= area.Bottom; y += step)
                        g.FillRectangle(crossBrush, x, y, 1, 1);
            }
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
                (int)Math.Max(0, c.R - c.R * t),
                (int)Math.Max(0, c.G - c.G * t),
                (int)Math.Max(0, c.B - c.B * t));
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

        public static Color FlowColor(HumanRole role)
        {
            switch (role)
            {
                case HumanRole.Open: return Teal;
                case HumanRole.Call: return Teal;
                case HumanRole.Step: return Amber;
                case HumanRole.Close: return Color.FromArgb(0xCC, 0xCC, 0xCC);
                case HumanRole.Banner: return Teal;
                default: return Text;
            }
        }

        /// <summary>Polegar retangular da barra de rolagem (6px, hover em amarelo acido).</summary>
        public static void DrawScrollThumb(Graphics g, Rectangle r, Color fill)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (SolidBrush b = new SolidBrush(fill))
                g.FillRectangle(b, r);
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
