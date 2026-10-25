using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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

    /// <summary>Evento formatado do fluxo de comandos (guardado para filtrar por sessao).</summary>
    internal sealed class StreamEntry
    {
        public int Pid;
        public string Line = "";
        public Color Color;
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
                PixelFont.Draw(g, Title, 12, 8, TitleScale, Accent, true);

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

    /// <summary>Caixa de selecao desenhada no tema do painel (o controle nativo ficaria claro).</summary>
    internal sealed class NeonCheckBox : Control
    {
        public bool Checked;
        public string Label = "";
        public Color Accent = Theme.Green;
        public event Action<bool> CheckedChanged;

        public NeonCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;

            int box = 16;
            int y = (Height - box) / 2;
            Rectangle r = new Rectangle(0, y, box, box);
            Theme.DrawPanel(g, r, 3,
                Checked ? Color.FromArgb(45, Accent) : Theme.PanelDeep,
                Checked ? Accent : Theme.Border);

            if (Checked)
            {
                using (Pen p = new Pen(Accent, 2f))
                    g.DrawLines(p, new Point[] { new Point(4, y + 8), new Point(7, y + 12), new Point(12, y + 4) });
            }

            TextRenderer.DrawText(g, Label, Theme.Ui, new Rectangle(box + 9, 0, Width - box - 9, Height),
                Checked ? Theme.Text : Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            SetChecked(!Checked);
        }

        public void SetChecked(bool value)
        {
            if (Checked == value) return;
            Checked = value;
            Invalidate();
            if (CheckedChanged != null) CheckedChanged(Checked);
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

    /// <summary>
    /// Tabela de sessoes desenhada a mao (sem scrollbar do sistema), com cabecalho
    /// fixo, linhas alternadas, data de inicio/fim e selecao por clique.
    /// </summary>
    internal sealed class SessionTable : Control
    {
        public List<SessionRow> Rows = new List<SessionRow>();
        public int RowHeight = 22;
        public int SelectedPid;
        public event Action SelectionChanged;

        private int _scroll;

        private static readonly string[] Headers =
            { "PID", "SCRIPT", "CHAMADOR", "COMANDOS", "LINHA", "INICIO", "FIM" };

        public SessionTable()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.PanelDeep;
        }

        private const int HeaderH = 20;

        private int[] Columns(int total)
        {
            int fixedW = 62 + 92 + 58 + 138 + 138;
            int rest = Math.Max(160, total - fixedW - 12);
            int script = (int)(rest * 0.48);
            int caller = rest - script;
            return new int[] { 62, script, caller, 92, 58, 138, 138 };
        }

        private static string Stamp(DateTime t)
        {
            return t.ToString("dd/MM HH:mm:ss");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;

            using (SolidBrush b = new SolidBrush(Theme.Panel))
                g.FillRectangle(b, new Rectangle(0, 0, Width, HeaderH));
            using (Pen p = new Pen(Theme.Grid))
                g.DrawLine(p, 0, HeaderH - 1, Width, HeaderH - 1);

            int[] w = Columns(Width);
            int x = 10;
            for (int i = 0; i < Headers.Length; i++)
            {
                TextRenderer.DrawText(g, Headers[i], Theme.UiBold,
                    new Rectangle(x, 0, w[i] - 6, HeaderH), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                x += w[i];
            }

            if (Rows.Count == 0)
            {
                TextRenderer.DrawText(g, "nenhuma sessao .cmd observada ainda", Theme.Ui,
                    new Rectangle(10, HeaderH + 10, Width - 20, 20), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }

            int visible = Math.Max(1, (Height - HeaderH) / RowHeight);
            if (_scroll > Rows.Count - visible) _scroll = Math.Max(0, Rows.Count - visible);
            if (_scroll < 0) _scroll = 0;

            for (int i = 0; i < visible && _scroll + i < Rows.Count; i++)
            {
                SessionRow r = Rows[_scroll + i];
                int y = HeaderH + i * RowHeight;
                Rectangle b = new Rectangle(0, y, Width, RowHeight);

                bool focused = r.Pid == SelectedPid;
                using (SolidBrush br = new SolidBrush(focused ? Color.FromArgb(28, Theme.Cyan)
                                                              : (i % 2 == 0 ? Theme.PanelDeep : Theme.Panel)))
                    g.FillRectangle(br, b);

                Color state = r.Closed ? Theme.Red : Theme.Green;
                using (SolidBrush br = new SolidBrush(focused ? Theme.Cyan : Color.FromArgb(180, state)))
                    g.FillRectangle(br, new Rectangle(2, y + 3, 3, RowHeight - 6));
                using (SolidBrush br = new SolidBrush(focused ? Color.FromArgb(120, Theme.Cyan) : Color.FromArgb(55, state)))
                    g.FillRectangle(br, new Rectangle(0, y + RowHeight - 2, Width, 2));

                string[] cells =
                {
                    r.Pid.ToString(), r.Script, r.Caller,
                    r.Steps.ToString(), r.LastLine > 0 ? r.LastLine.ToString() : "-",
                    Stamp(r.StartedAt),
                    r.Closed ? Stamp(r.ClosedAt) : "em curso"
                };

                int cx = 10;
                for (int c = 0; c < cells.Length; c++)
                {
                    Color color = Theme.Text;
                    if (c == 0) color = Theme.Dim;
                    else if (c == 3) color = r.Steps > 0 ? Theme.Green : Theme.Dim;
                    else if (c == 4) color = r.LastLine > 0 ? Theme.Cyan : Theme.Dim;
                    else if (c == 5) color = Theme.Dim;
                    else if (c == 6) color = r.Closed ? Theme.Dim : Theme.Amber;

                    TextRenderer.DrawText(g, cells[c], Theme.MonoSmall,
                        new Rectangle(cx, y, Math.Max(10, w[c] - 6), RowHeight), color,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    cx += w[c];
                }

                if (focused)
                    TextRenderer.DrawText(g, "FOCO", Theme.UiBold,
                        new Rectangle(Width - 56, y, 50, RowHeight), Theme.Cyan,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            if (Rows.Count > visible)
            {
                int trackH = visible * RowHeight;
                int barH = Math.Max(24, trackH * visible / Rows.Count);
                int barY = HeaderH + (trackH - barH) * _scroll / Math.Max(1, Rows.Count - visible);
                using (SolidBrush br = new SolidBrush(Color.FromArgb(90, Theme.Cyan)))
                    g.FillRectangle(br, new Rectangle(Width - 4, barY, 3, barH));
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int idx = (e.Y - HeaderH) / RowHeight;
            if (e.Y < HeaderH || idx < 0 || _scroll + idx >= Rows.Count)
            {
                SetSelection(0);
                return;
            }
            int pid = Rows[_scroll + idx].Pid;
            SetSelection(pid == SelectedPid ? 0 : pid);
        }

        private void SetSelection(int pid)
        {
            if (SelectedPid == pid) return;
            SelectedPid = pid;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged();
        }
    }
}
