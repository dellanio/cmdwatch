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

    /// <summary>
    /// Entrada do fluxo de comandos. Guarda os dados do evento (nao o texto
    /// ja formatado) para que a mensagem possa ser redesenhada quando o
    /// usuario troca o idioma da interface.
    /// </summary>
    internal sealed class StreamEntry
    {
        public int Pid;
        public WatchEvent Event;      // evento monitorado (null no cabecalho do log)
        public RunInfo Banner;       // cabecalho do log (null em eventos)
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

            Rectangle head = new Rectangle(12, 2, Math.Max(10, Width - 24), HeaderHeight - 4);
            if (Title.Length > 0)
                TechTitle.Draw(g, Title, head, Accent);

            if (TitleValue.Length > 0)
            {
                float tw = TechTitle.MeasureWidth(g, Title, Theme.TitleFont);
                float vw = TechTitle.MeasureWidth(g, TitleValue, Theme.TitleFont);
                if (12 + tw + 18 + vw < Width - 12)
                    TechTitle.Draw(g, TitleValue, head, Accent, true);
            }

            using (Pen p = new Pen(Theme.Grid))
                g.DrawLine(p, 10, HeaderHeight, Math.Max(10, Width - 11), HeaderHeight);

            // marcacoes de escala sob a linha do cabecalho (detalhe de instrumento)
            using (Pen tick = new Pen(Color.FromArgb(70, Accent)))
            {
                for (int x = 14; x < Width - 12; x += 14)
                    g.DrawLine(tick, x, HeaderHeight - 3, x, HeaderHeight);
            }
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

    /// <summary>
    /// Seletor segmentado (PT-BR | EN) usado no rodape para trocar o idioma da
    /// interface. O segmento ativo recebe fundo ciano e texto em destaque.
    /// </summary>
    internal sealed class NeonToggle : Control
    {
        private string[] _options = { "PT-BR", "EN" };
        private int _index;

        public string Caption = "";
        public Color Accent = Theme.Cyan;
        public event Action<int> IndexChanged;

        public NeonToggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            TabStop = true;
        }

        /// <summary>Indice da opcao ativa. Dispara IndexChanged ao mudar.</summary>
        public int Index
        {
            get { return _index; }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (v > _options.Length - 1) v = _options.Length - 1;
                if (_index == v) return;
                _index = v;
                Invalidate();
                if (IndexChanged != null) IndexChanged(_index);
            }
        }

        private int CaptionWidth(Graphics g)
        {
            if (Caption.Length == 0) return 0;
            return TextRenderer.MeasureText(g, Caption, Theme.Ui).Width + 10;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.DrawPanel(g, r, 4, Theme.PanelDeep, Theme.Border);

            int capW = CaptionWidth(g);
            if (capW > 0)
            {
                TextRenderer.DrawText(g, Caption, Theme.Ui,
                    new Rectangle(3, 0, capW, Height), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            int areaX = capW;
            int areaW = Math.Max(20, Width - areaX - 2);
            int segW = areaW / _options.Length;

            for (int i = 0; i < _options.Length; i++)
            {
                Rectangle seg = new Rectangle(areaX + i * segW + 1, 2, segW - 2, Height - 5);
                bool on = i == _index;
                if (on)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(38, Accent)))
                        g.FillRectangle(b, seg);
                    using (Pen p = new Pen(Color.FromArgb(120, Accent)))
                        g.DrawRectangle(p, seg);
                }
                TextRenderer.DrawText(g, _options[i], Theme.UiBold, seg,
                    on ? Accent : Theme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int capW;
            using (Graphics g = CreateGraphics())
                capW = CaptionWidth(g);

            int areaW = Math.Max(20, Width - capW - 2);
            int segW = Math.Max(1, areaW / _options.Length);
            int idx = (e.X - capW) / segW;
            if (idx < 0) idx = 0;
            if (idx > _options.Length - 1) idx = _options.Length - 1;
            Index = idx;
            Focus();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left)
            {
                Index = Math.Max(0, _index - 1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right)
            {
                Index = Math.Min(_options.Length - 1, _index + 1);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Fluxo de comandos desenhado a mao. As mensagens usam o mesmo formato do
    /// CLI e nao quebram linha (como no terminal); a rolagem vertical e
    /// horizontal sao proprias, sem as barras do sistema - que ficariam claras
    /// demais no tema escuro.
    /// </summary>
    internal sealed class NeonFlow : Control
    {
        private readonly List<HumanLine> _lines = new List<HumanLine>();
        private int _scrollY;
        private int _scrollX;
        private bool _follow = true;
        private int _charW;
        private int _maxChars;
        private bool _dragV, _dragH;
        private bool _hot;
        private int _dragOffset;

        private const int BarW = 6;
        private const int PadX = 10;

        public NeonFlow()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.PanelDeep;
        }

        public int LineHeight { get { return Theme.Mono.Height + 3; } }

        /// <summary>Quantas linhas o fluxo esta exibindo no total.</summary>
        public int LineCount { get { return _lines.Count; } }

        public int VisibleLines { get { return Math.Max(1, (Height - 6) / LineHeight); } }

        public int MaxScrollY { get { return Math.Max(0, _lines.Count - VisibleLines); } }

        public int MaxScrollX
        {
            get
            {
                int total = _maxChars * _charW + PadX;
                return Math.Max(0, total - (Width - BarW));
            }
        }

        /// <summary>Substitui todas as linhas (re-render do fluxo).</summary>
        public void SetLines(List<HumanLine> lines)
        {
            _lines.Clear();
            if (lines != null) _lines.AddRange(lines);
            Recalc();
            Clamp();
            Invalidate();
        }

        /// <summary>Acrescenta um bloco de linhas no final.</summary>
        public void Append(List<HumanLine> block)
        {
            if (block == null || block.Count == 0) return;
            _lines.AddRange(block);
            if (_lines.Count > 4000) _lines.RemoveRange(0, 1000);
            Recalc();
            if (_follow) _scrollY = MaxScrollY;
            else Clamp();
            Invalidate();
        }

        public void ClearFlow()
        {
            _lines.Clear();
            _scrollY = 0;
            _scrollX = 0;
            _follow = true;
            Invalidate();
        }

        private void Recalc()
        {
            int max = 0;
            for (int i = 0; i < _lines.Count; i++)
                if (_lines[i].Text.Length > max) max = _lines[i].Text.Length;
            _maxChars = max;
        }

        private void MeasureChar(Graphics g)
        {
            _charW = Math.Max(1, (int)Math.Round(g.MeasureString("M", Theme.Mono).Width));
        }

        private void Clamp()
        {
            if (_scrollY > MaxScrollY) _scrollY = MaxScrollY;
            if (_scrollY < 0) _scrollY = 0;
            if (_scrollX > MaxScrollX) _scrollX = MaxScrollX;
            if (_scrollX < 0) _scrollX = 0;
        }

        // ------------------------------------------------------ desenho
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.PanelDeep);
            if (_charW <= 1) MeasureChar(g);

            bool hasH = MaxScrollX > 0;
            int textW = Width - BarW;
            int textH = Height - (hasH ? BarW : 0);
            int lh = LineHeight;

            g.SetClip(new Rectangle(0, 0, textW, textH));
            for (int i = 0; i < VisibleLines && _scrollY + i < _lines.Count; i++)
            {
                HumanLine hl = _lines[_scrollY + i];
                TextRenderer.DrawText(g, hl.Text, Theme.Mono,
                    new Rectangle(PadX - _scrollX, 3 + i * lh, textW + _scrollX, lh),
                    Theme.FlowColor(hl.Role),
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoClipping);
            }
            g.ResetClip();

            // barra vertical: polegar arredondado, no estilo do Windows
            if (_lines.Count > VisibleLines)
            {
                Rectangle track = new Rectangle(Width - BarW, 0, BarW, textH);
                int thumb = Math.Max(28, track.Height * VisibleLines / Math.Max(1, _lines.Count));
                int max = MaxScrollY;
                int y = max == 0 ? 0 : (int)((double)_scrollY / max * (track.Height - thumb));
                Theme.DrawScrollThumb(g, new Rectangle(track.X + 1, y, BarW - 2, thumb), ThumbColor());
            }

            // barra horizontal: idem
            if (hasH)
            {
                Rectangle track = new Rectangle(0, Height - BarW, textW, BarW);
                int thumb = Math.Max(28, track.Width * (Width - BarW) / Math.Max(1, _maxChars * _charW + PadX));
                int max = MaxScrollX;
                int x = max == 0 ? 0 : (int)((double)_scrollX / max * (track.Width - thumb));
                Theme.DrawScrollThumb(g, new Rectangle(x, track.Y + 1, thumb, BarW - 2), ThumbColor());
            }
        }

        /// <summary>Cor do polegar: normal, sobre o mouse ou arrastando.</summary>
        private Color ThumbColor()
        {
            if (_dragV || _dragH) return ScrollTheme.ThumbActive;
            if (_hot) return ScrollTheme.ThumbHot;
            return ScrollTheme.Thumb;
        }

        private Rectangle VerticalTrack()
        {
            return new Rectangle(Width - BarW, 0, BarW, Height);
        }

        private Rectangle HorizontalTrack()
        {
            return new Rectangle(0, Height - BarW, Width - BarW, BarW);
        }

        // ---------------------------------------------------- interacao
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int step = e.Delta / 120 * 3;
            if (step == 0) step = e.Delta > 0 ? 1 : -1;
            if ((ModifierKeys & Keys.Shift) != 0) { ScrollXBy(step * _charW * 2); return; }
            ScrollYBy(step);
        }

        public void ScrollYBy(int delta)
        {
            _scrollY += delta;
            Clamp();
            _follow = _scrollY >= MaxScrollY;
            Invalidate();
        }

        public void ScrollXBy(int delta)
        {
            _scrollX += delta;
            Clamp();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Rectangle v = VerticalTrack();
            Rectangle h = HorizontalTrack();
            if (v.Contains(e.Location) && _lines.Count > VisibleLines)
            {
                _dragV = true;
                _dragOffset = e.Y - v.Y;
            }
            else if (h.Contains(e.Location) && MaxScrollX > 0)
            {
                _dragH = true;
                _dragOffset = e.X - h.X;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hot = VerticalTrack().Contains(e.Location) || HorizontalTrack().Contains(e.Location);
            if (hot != _hot) { _hot = hot; Invalidate(); }

            if (_dragV)
            {
                Rectangle v = VerticalTrack();
                int thumb = Math.Max(28, v.Height * VisibleLines / Math.Max(1, _lines.Count));
                int usable = Math.Max(1, v.Height - thumb);
                int rel = e.Y - v.Y - _dragOffset;
                if (rel < 0) rel = 0;
                if (rel > usable) rel = usable;
                int max = MaxScrollY;
                _scrollY = max == 0 ? 0 : (int)((double)rel / usable * max);
                _follow = _scrollY >= max;
                Invalidate();
            }
            else if (_dragH)
            {
                Rectangle h = HorizontalTrack();
                int total = Math.Max(1, _maxChars * _charW + PadX);
                int thumb = Math.Max(28, h.Width * (Width - BarW) / total);
                int usable = Math.Max(1, h.Width - thumb);
                int rel = e.X - h.X - _dragOffset;
                if (rel < 0) rel = 0;
                if (rel > usable) rel = usable;
                int max = MaxScrollX;
                _scrollX = max == 0 ? 0 : (int)((double)rel / usable * max);
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot) { _hot = false; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragV = false;
            _dragH = false;
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

        /// <summary>Cabecalhos das colunas (vêm do dicionario de idioma).</summary>
        private static string[] Headers()
        {
            return new string[]
            {
                Lang.T("colPid"), Lang.T("colScript"), Lang.T("colCaller"),
                Lang.T("colCommands"), Lang.T("colLine"), Lang.T("colStart"), Lang.T("colEnd")
            };
        }

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
            string[] headers = Headers();
            int x = 10;
            for (int i = 0; i < headers.Length; i++)
            {
                TextRenderer.DrawText(g, headers[i], Theme.UiBold,
                    new Rectangle(x, 0, w[i] - 6, HeaderH), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                x += w[i];
            }

            if (Rows.Count == 0)
            {
                TextRenderer.DrawText(g, Lang.T("noSessions"), Theme.Ui,
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
                    r.Closed ? Stamp(r.ClosedAt) : Lang.T("inProgress")
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
                    TextRenderer.DrawText(g, Lang.T("focus"), Theme.UiBold,
                        new Rectangle(Width - 56, y, 50, RowHeight), Theme.Cyan,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            if (Rows.Count > visible)
            {
                int trackH = visible * RowHeight;
                int barH = Math.Max(28, trackH * visible / Rows.Count);
                int barY = HeaderH + (trackH - barH) * _scroll / Math.Max(1, Rows.Count - visible);
                Theme.DrawScrollThumb(g, new Rectangle(Width - 6, barY, 4, barH), ScrollTheme.Thumb);
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
