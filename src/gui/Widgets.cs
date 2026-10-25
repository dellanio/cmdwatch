using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace CmdWatch.Gui
{
    /// <summary>Linha da tabela de sessoes exibida no painel.</summary>
    internal sealed class SessionRow
    {
        public int Pid;
        public string Script = "";
        public string Caller = "";
        public string Origin = "";
        public int Steps;
        public int LastLine;
        public DateTime StartedAt = DateTime.Now;
        public DateTime ClosedAt = DateTime.Now;
        public bool Closed;

        public double Elapsed
        {
            get { return (Closed ? ClosedAt : DateTime.Now).Subtract(StartedAt).TotalSeconds; }
        }
    }

    /// <summary>Paineis com cabecalho em matriz de pixels e moldura arredondada.</summary>
    internal sealed class NeonPanel : Control
    {
        public string Title = "";
        public string TitleValue = "";
        public Color Accent = Theme.Cyan;
        public int TitleScale = 3;
        public int HeaderHeight = 26;

        public NeonPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
        }

        public Rectangle PlotArea
        {
            get
            {
                return new Rectangle(10, HeaderHeight + 6, Math.Max(10, Width - 20), Math.Max(10, Height - HeaderHeight - 14));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.DrawPanel(g, r, 8, Theme.Panel, Theme.Border);

            if (Title.Length > 0)
            {
                PixelFont.Draw(g, Title, 12, 8, TitleScale, Accent, true);
            }

            if (TitleValue.Length > 0)
            {
                int vw = PixelFont.MeasureWidth(TitleValue, TitleScale);
                int tw = PixelFont.MeasureWidth(Title, TitleScale);
                int x = Width - 12 - vw;
                if (x > 12 + tw + 12)
                    PixelFont.Draw(g, TitleValue, x, 8, TitleScale, Accent, true);
            }

            using (Pen p = new Pen(Theme.Grid))
                g.DrawLine(p, 10, HeaderHeight, Math.Max(10, Width - 11), HeaderHeight);
        }
    }

    /// <summary>Medidor vertical segmentado (estilo das colunas da referencia).</summary>
    internal sealed class MeterColumn : Control
    {
        public string Label = "CPU";
        public string ValueText = "0";
        public double Ratio;
        public Color[] Ramp;
        public int SegmentCount = 24;

        public MeterColumn()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        private Color At(double pos)
        {
            if (Ramp == null || Ramp.Length == 0) return Theme.Green;
            if (Ramp.Length == 1) return Ramp[0];
            double t = pos * (Ramp.Length - 1);
            int i = (int)t;
            if (i >= Ramp.Length - 1) return Ramp[Ramp.Length - 1];
            return Theme.Mix(Ramp[i], Ramp[i + 1], t - i);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;

            Color accent = At(0.5);
            TextRenderer.DrawText(g, Label, Theme.UiBold,
                new Rectangle(0, 0, Width, 16), accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            int top = 20;
            int bottom = Height - 22;
            int avail = Math.Max(10, bottom - top);
            int segH = Math.Max(2, avail / SegmentCount - 2);
            int barW = Math.Min(18, Math.Max(8, Width - 14));
            int x = (Width - barW) / 2;

            int lit = (int)Math.Round(Ratio * SegmentCount);
            if (lit < 0) lit = 0;
            if (lit > SegmentCount) lit = SegmentCount;

            for (int i = 0; i < SegmentCount; i++)
            {
                // i = 0 no topo
                double pos = SegmentCount <= 1 ? 0 : (double)(SegmentCount - 1 - i) / (SegmentCount - 1);
                Color c = At(pos);
                bool on = (SegmentCount - i) <= lit;
                int y = top + i * (segH + 2);
                if (y + segH > bottom) break;

                Color fill = on ? c : Color.FromArgb(42, c);
                using (SolidBrush b = new SolidBrush(fill))
                    g.FillRectangle(b, x, y, barW, segH);
                if (on)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(60, c)))
                        g.FillRectangle(b, x - 1, y - 1, barW + 2, segH + 2);
                }
            }

            TextRenderer.DrawText(g, ValueText, Theme.UiBold,
                new Rectangle(0, bottom + 2, Width, 16), accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Grafico de linhas com duas series (verde e vermelho) sobre grade.</summary>
    internal sealed class LineChart : Control
    {
        public List<double> SeriesA = new List<double>();
        public List<double> SeriesB = new List<double>();
        public string LabelA = "A";
        public string LabelB = "B";
        public int Capacity = 60;

        public LineChart()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;

            int left = 46, right = 46, top = 8, bottom = 18;
            Rectangle plot = new Rectangle(left, top, Math.Max(10, Width - left - right), Math.Max(10, Height - top - bottom));

            double max = 1;
            foreach (double v in SeriesA) if (v > max) max = v;
            foreach (double v in SeriesB) if (v > max) max = v;

            using (Pen gp = new Pen(Theme.Grid))
            {
                for (int i = 0; i <= 4; i++)
                {
                    int y = plot.Bottom - (int)(plot.Height * i / 4.0);
                    g.DrawLine(gp, plot.Left, y, plot.Right, y);
                }
                int cols = Math.Max(2, Capacity / 10);
                for (int i = 0; i <= cols; i++)
                {
                    int x = plot.Left + (int)(plot.Width * i / (double)cols);
                    g.DrawLine(gp, x, plot.Top, x, plot.Bottom);
                }
            }

            for (int i = 0; i <= 4; i++)
            {
                double frac = i / 4.0;
                string txt = (max * frac).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
                TextRenderer.DrawText(g, txt, Theme.MonoSmall,
                    new Rectangle(0, plot.Bottom - (int)(plot.Height * i / 4.0) - 8, left - 4, 16), Theme.Green,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, txt, Theme.MonoSmall,
                    new Rectangle(plot.Right + 4, plot.Bottom - (int)(plot.Height * i / 4.0) - 8, right - 6, 16), Theme.Red,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            DrawSeries(g, SeriesA, plot, max, Theme.Green);
            DrawSeries(g, SeriesB, plot, max, Theme.Red);

            if (SeriesA.Count > 0)
            {
                TextRenderer.DrawText(g, LabelA, Theme.UiBold, new Rectangle(plot.Left + 4, plot.Top + 2, 160, 16),
                    Theme.Green, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, LabelB, Theme.UiBold, new Rectangle(plot.Right - 164, plot.Top + 2, 160, 16),
                    Theme.Red, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        private static void DrawSeries(Graphics g, List<double> data, Rectangle plot, double max, Color color)
        {
            if (data.Count < 2) return;
            int n = data.Count;
            PointF[] pts = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                float x = plot.Left + (float)(plot.Width * i / (double)(n - 1));
                float y = plot.Bottom - (float)(plot.Height * Math.Min(1.0, data[i] / max));
                pts[i] = new PointF(x, y);
            }
            using (Pen glow = new Pen(Color.FromArgb(70, color), 5f))
            using (Pen core = new Pen(color, 1.6f))
            {
                glow.LineJoin = LineJoin.Round;
                core.LineJoin = LineJoin.Round;
                g.DrawLines(glow, pts);
                g.DrawLines(core, pts);
            }
        }
    }

    /// <summary>Grafico de area (roxo) usado na linha do tempo.</summary>
    internal sealed class AreaChart : Control
    {
        public List<double> Values = new List<double>();
        public int Capacity = 120;
        public string TopLabel = "";
        public string BottomLabel = "";

        public AreaChart()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;

            int left = 54, right = 10, top = 10, bottom = 16;
            Rectangle plot = new Rectangle(left, top, Math.Max(10, Width - left - right), Math.Max(10, Height - top - bottom));

            double max = 1, min = 0;
            foreach (double v in Values) { if (v > max) max = v; if (v < min) min = v; }
            double span = Math.Max(1e-6, max - min);

            using (Pen gp = new Pen(Theme.Grid))
            {
                for (int i = 0; i <= 4; i++)
                {
                    int y = plot.Bottom - (int)(plot.Height * i / 4.0);
                    g.DrawLine(gp, plot.Left, y, plot.Right, y);
                }
                int cols = Math.Max(2, Capacity / 20);
                for (int i = 0; i <= cols; i++)
                {
                    int x = plot.Left + (int)(plot.Width * i / (double)cols);
                    g.DrawLine(gp, x, plot.Top, x, plot.Bottom);
                }
            }

            if (Values.Count >= 2)
            {
                int n = Values.Count;
                PointF[] pts = new PointF[n + 2];
                for (int i = 0; i < n; i++)
                {
                    float x = plot.Left + (float)(plot.Width * i / (double)(n - 1));
                    float y = plot.Bottom - (float)(plot.Height * ((Values[i] - min) / span));
                    pts[i] = new PointF(x, y);
                }
                pts[n] = new PointF(plot.Right, plot.Bottom);
                pts[n + 1] = new PointF(plot.Left, plot.Bottom);

                using (LinearGradientBrush fill = new LinearGradientBrush(
                        new Rectangle(plot.Left, plot.Top, plot.Width, plot.Height),
                        Color.FromArgb(120, Theme.Purple), Color.FromArgb(8, Theme.Purple), 90f))
                {
                    g.FillPolygon(fill, pts);
                }

                PointF[] edge = new PointF[n];
                Array.Copy(pts, edge, n);
                using (Pen glow = new Pen(Color.FromArgb(80, Theme.Purple), 5f))
                using (Pen core = new Pen(Theme.Purple, 1.7f))
                {
                    glow.LineJoin = LineJoin.Round;
                    core.LineJoin = LineJoin.Round;
                    g.DrawLines(glow, edge);
                    g.DrawLines(core, edge);
                }
            }

            TextRenderer.DrawText(g, TopLabel, Theme.MonoSmall,
                new Rectangle(2, plot.Top - 2, left - 6, 16), Theme.Purple,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, BottomLabel, Theme.MonoSmall,
                new Rectangle(2, plot.Bottom - 14, left - 6, 16), Theme.Purple,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Painel compacto da barra inferior (titulo, valor, subtitulo e barra segmentada).</summary>
    internal sealed class MiniStat : Control
    {
        public string Title = "";
        public string Value = "";
        public string Subtitle = "";
        public double Ratio;
        public Color Accent = Theme.Blue;

        public MiniStat()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.DrawPanel(g, r, 6, Theme.PanelDeep, Color.FromArgb(120, Accent));

            TextRenderer.DrawText(g, Title, Theme.UiTitle, new Rectangle(10, 5, Width - 20, 20),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Value, Theme.UiBold, new Rectangle(10, 5, Width - 20, 20),
                Accent, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(g, Subtitle, Theme.Ui, new Rectangle(10, 25, Width - 20, 17),
                Theme.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            int barH = 10;
            int barY = Height - barH - 7;
            int segW = 6, gap = 2;
            int total = Math.Max(1, (Width - 20) / (segW + gap));
            int lit = (int)Math.Round(Ratio * total);
            if (lit < 0) lit = 0;
            if (lit > total) lit = total;

            for (int i = 0; i < total; i++)
            {
                int x = 10 + i * (segW + gap);
                if (x + segW > Width - 10) break;
                Color c = i < lit ? Accent : Color.FromArgb(38, Accent);
                using (SolidBrush b = new SolidBrush(c))
                    g.FillRectangle(b, x, barY, segW, barH);
            }
        }
    }

    /// <summary>
    /// Tabela de sessoes desenhada a mao (sem scrollbar do sistema), com cabecalho
    /// fixo, linhas alternadas, barra de estado a esquerda e rolagem pela roda do mouse.
    /// </summary>
    internal sealed class SessionTable : Control
    {
        public List<SessionRow> Rows = new List<SessionRow>();
        public int RowHeight = 22;
        private int _scroll;

        private static readonly string[] Headers = { "PID", "SCRIPT", "CHAMADOR", "COMANDOS", "LINHA", "TEMPO" };

        public SessionTable()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.PanelDeep;
        }

        private int[] Columns(int total)
        {
            int fixedW = 62 + 92 + 62 + 70;   // pid + comandos + linha + tempo
            int rest = Math.Max(120, total - fixedW - 12);
            int script = (int)(rest * 0.56);
            int caller = rest - script;
            return new int[] { 62, script, caller, 92, 62, 70 };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;

            int headerH = 20;
            using (SolidBrush b = new SolidBrush(Theme.Panel))
                g.FillRectangle(b, new Rectangle(0, 0, Width, headerH));
            using (Pen p = new Pen(Theme.Grid))
                g.DrawLine(p, 0, headerH - 1, Width, headerH - 1);

            int[] w = Columns(Width);
            int x = 10;
            for (int i = 0; i < Headers.Length; i++)
            {
                TextRenderer.DrawText(g, Headers[i], Theme.UiBold,
                    new Rectangle(x, 0, w[i] - 6, headerH), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                x += w[i];
            }

            if (Rows.Count == 0)
            {
                TextRenderer.DrawText(g, "nenhuma sessao .cmd observada ainda", Theme.Ui,
                    new Rectangle(10, headerH + 10, Width - 20, 20), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }

            int visible = Math.Max(1, (Height - headerH) / RowHeight);
            if (_scroll > Rows.Count - visible) _scroll = Math.Max(0, Rows.Count - visible);
            if (_scroll < 0) _scroll = 0;

            for (int i = 0; i < visible && _scroll + i < Rows.Count; i++)
            {
                SessionRow r = Rows[_scroll + i];
                int y = headerH + i * RowHeight;
                Rectangle b = new Rectangle(0, y, Width, RowHeight);

                using (SolidBrush br = new SolidBrush(i % 2 == 0 ? Theme.PanelDeep : Theme.Panel))
                    g.FillRectangle(br, b);

                Color state = r.Closed ? Theme.Red : Theme.Green;
                using (SolidBrush br = new SolidBrush(Color.FromArgb(180, state)))
                    g.FillRectangle(br, new Rectangle(2, y + 3, 3, RowHeight - 6));
                using (SolidBrush br = new SolidBrush(Color.FromArgb(55, state)))
                    g.FillRectangle(br, new Rectangle(0, y + RowHeight - 2, Width, 2));

                int cx = 10;
                string[] cells =
                {
                    r.Pid.ToString(), r.Script, r.Caller,
                    r.Steps.ToString(), r.LastLine > 0 ? r.LastLine.ToString() : "-",
                    r.Elapsed.ToString("0.0") + "s"
                };
                for (int c = 0; c < cells.Length; c++)
                {
                    Color color = Theme.Text;
                    if (c == 0) color = Theme.Dim;
                    else if (c == 3) color = r.Steps > 0 ? Theme.Green : Theme.Dim;
                    else if (c == 4) color = r.LastLine > 0 ? Theme.Cyan : Theme.Dim;
                    else if (c == 5) color = r.Closed ? Theme.Dim : Theme.Amber;

                    TextRenderer.DrawText(g, cells[c], Theme.MonoSmall,
                        new Rectangle(cx, y, Math.Max(10, w[c] - 6), RowHeight), color,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    cx += w[c];
                }
            }

            // indicador de rolagem
            if (Rows.Count > visible)
            {
                int trackH = visible * RowHeight;
                int barH = Math.Max(24, trackH * visible / Rows.Count);
                int barY = headerH + (trackH - barH) * _scroll / Math.Max(1, Rows.Count - visible);
                using (SolidBrush br = new SolidBrush(Color.FromArgb(90, Theme.Cyan)))
                    g.FillRectangle(br, new Rectangle(Width - 4, barY, 3, barH));
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int visible = Math.Max(1, (Height - 20) / RowHeight);
            int max = Math.Max(0, Rows.Count - visible);
            _scroll -= (e.Delta / 120) * 3;
            if (_scroll < 0) _scroll = 0;
            if (_scroll > max) _scroll = max;
            Invalidate();
        }
    }
}
