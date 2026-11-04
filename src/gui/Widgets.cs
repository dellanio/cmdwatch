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
        /// <summary>Hora real de inicio do cmd.exe (MinValue se desconhecida).</summary>
        public DateTime CmdStartedAt = DateTime.MinValue;

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

    /// <summary>Linha da tabela de janelas visiveis exibida no painel.</summary>
    internal sealed class WindowRow
    {
        public IntPtr Hwnd;
        public string Title = "";
        public string Author = "";
        public string CmdLine = "";
        public DateTime StartedAt = DateTime.Now;
        public DateTime? ClosedAt;
        public int Focus;
        public bool Visible = true;
        public bool Closed;
        public bool ByConsole;
        /// <summary>Nome do executavel autor (coluna "Processo") e o processo pai ("Processo de origem").</summary>
        public string AuthorName = "";
        public string Origin = "";

        /// <summary>Quanto tempo a janela ficou na tela (correndo enquanto aberta).</summary>
        public double DuracaoSeg
        {
            get
            {
                DateTime fim = ClosedAt ?? DateTime.Now;
                double s = (fim - StartedAt).TotalSeconds;
                return s < 0 ? 0 : s;
            }
        }

        public string Estado
        {
            get
            {
                if (Closed) return Lang.T("winClosed");
                if (!Visible) return Lang.T("winHidden");
                return Lang.T("winVisible");
            }
        }
    }

    /// <summary>
    /// Cartao do painel: superficie um tom acima do fundo, borda fina e cantos
    /// de 8 px. O titulo fica a esquerda com o contador num selo (TitleValue);
    /// os controles do cabecalho (filtros, botoes) sao filhos posicionados pelo
    /// formulario na faixa HeaderHeight.
    /// </summary>
    internal sealed class NeonPanel : Control
    {
        public string Title = "";
        public string TitleValue = "";
        public Color Accent = Theme.Teal;
        public int HeaderHeight = 40;

        public NeonPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
        }

        public Rectangle PlotArea
        {
            get { return new Rectangle(1, HeaderHeight, Math.Max(10, Width - 2), Math.Max(10, Height - HeaderHeight - 6)); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Bitmap fundo = Fundo();
            if (fundo != null) g.DrawImage(fundo, 0, 0);

            const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            Rectangle head = new Rectangle(14, 0, Math.Max(10, Width - 28), HeaderHeight);
            TextRenderer.DrawText(g, Title, Theme.UiTitle, head, Theme.Text, L);
            if (TitleValue.Length > 0)
            {
                int tw = TextRenderer.MeasureText(g, Title, Theme.UiTitle, Size.Empty, L).Width;
                int vw = TextRenderer.MeasureText(g, TitleValue, Theme.UiSmall, Size.Empty, L).Width + 12;
                Rectangle selo = new Rectangle(head.X + tw + 9, HeaderHeight / 2 - 9, Math.Max(20, vw), 18);
                Theme.DrawPanel(g, selo, 4, Theme.Fill, Theme.Border);
                TextRenderer.DrawText(g, TitleValue, Theme.UiSmall, selo, Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        private Bitmap _fundo;
        private int _fundoW = -1, _fundoH = -1;

        // o fundo (cartao arredondado com antialias) so muda com o tamanho
        private int _fundoCab;
        private Bitmap Fundo()
        {
            if (Width <= 0 || Height <= 0) return null;
            if (_fundo != null && _fundoW == Width && _fundoH == Height && _fundoCab == HeaderHeight) return _fundo;
            try { if (_fundo != null) _fundo.Dispose(); } catch { }
            _fundo = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            _fundoW = Width; _fundoH = Height; _fundoCab = HeaderHeight;
            using (Graphics g = Graphics.FromImage(_fundo))
            {
                g.Clear(Theme.Bg);
                Theme.DrawPanel(g, new Rectangle(0, 0, Width - 1, Height - 1), 8, Theme.Panel, Theme.Border);
            }
            return _fundo;
        }
    }

    /// <summary>
    /// Botao desenhado: icone (glifo), texto opcional e seta de lista. Estilos:
    /// contorno (cabecalhos), fantasma (barra lateral) e ativo (verde-agua).
    /// </summary>
    internal sealed class IconButton : Control
    {
        public string Glyph = "";
        public string Label = "";
        public bool Chevron;
        public bool Ghost;
        public bool Active;
        public Color GlyphColor = Color.Empty;
        private static readonly Font ChevFont = new Font(Theme.IconFont.FontFamily, 7f);
        private bool _hot;
        private readonly ToolTip _tip = new ToolTip();

        public IconButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
        }

        public string Tip { set { _tip.SetToolTip(this, value ?? ""); } }

        /// <summary>Largura que cabe o icone, o texto e a seta.</summary>
        public int PreferredW()
        {
            int w = 12;
            if (Glyph.Length > 0) w += 18;
            if (Label.Length > 0) w += TextRenderer.MeasureText(Label, Theme.Ui).Width + 4;
            if (Chevron) w += 16;
            return w + 6;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (Active)
                Theme.DrawPanel(g, r, 8, Color.FromArgb(34, Theme.Teal), Color.FromArgb(150, Theme.Teal));
            else if (Ghost)
            {
                if (_hot) Theme.DrawPanel(g, r, 8, Theme.Hover, Color.Empty);
            }
            else
                Theme.DrawPanel(g, r, 6, _hot ? Theme.Hover : Theme.Fill, Theme.Border);

            Color fg = Active ? Theme.Teal : (Ghost ? (_hot ? Theme.Text : Theme.Dim) : Theme.Text);
            int x = Glyph.Length > 0 && (Label.Length > 0 || Chevron) ? 10 : 0;
            if (Glyph.Length > 0)
            {
                int gw = x == 0 ? Width : 18;
                Theme.DrawIcon(g, Glyph, new Rectangle(x, 0, gw, Height), GlyphColor.A > 0 ? GlyphColor : fg,
                    Ghost ? Theme.IconBig : Theme.IconFont);
                x += gw + 4;
            }
            if (Label.Length > 0)
            {
                if (Glyph.Length == 0) x = 10;
                int lw = Width - x - (Chevron ? 22 : 6);
                TextRenderer.DrawText(g, Label, Theme.Ui, new Rectangle(x, 0, Math.Max(4, lw), Height), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
            if (Chevron)
                Theme.DrawIcon(g, "\uE70D", new Rectangle(Width - 22, 1, 16, Height), Theme.Dim,
                    ChevFont);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Campo de busca: caixa arredondada com lupa e texto de dica.</summary>
    internal sealed class SearchBox : Control
    {
        public readonly TextBox Box = new TextBox();
        private string _cue = "";

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public SearchBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Box.BorderStyle = BorderStyle.None;
            Box.Font = Theme.Ui;
            Box.BackColor = Theme.Fill;
            Box.ForeColor = Theme.Text;
            Box.HandleCreated += delegate { AplicarDica(); };
            Controls.Add(Box);
            Cursor = Cursors.IBeam;
        }

        public string Cue { get { return _cue; } set { _cue = value ?? ""; AplicarDica(); } }

        private void AplicarDica()
        {
            try { if (Box.IsHandleCreated) SendMessage(Box.Handle, 0x1501, (IntPtr)1, _cue); } catch { }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int h = Box.PreferredHeight;
            Box.SetBounds(30, (Height - h) / 2 + 1, Math.Max(10, Width - 38), h);
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Box.Focus(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Theme.DrawPanel(g, new Rectangle(0, 0, Width - 1, Height - 1), 6, Theme.Fill, Box.Focused ? Theme.Teal : Theme.Border);
            Theme.DrawIcon(g, "\uE721", new Rectangle(8, 0, 18, Height), Theme.Dim);
        }
    }

    /// <summary>Faixa de estatisticas: icone, numero grande e rotulo, separados por divisorias.</summary>
    internal sealed class StatStrip : Control
    {
        public string[] Glyphs = { "\uE756", "\uE737", "\uE81E", "\uE823" };
        public string[] Values = { "0", "0", "0", "00:00:00" };
        public string[] Labels = { "", "", "", "" };

        public StatStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            Theme.DrawPanel(g, new Rectangle(0, 0, Width - 1, Height - 1), 8, Theme.Panel, Theme.Border);
            int n = Values.Length;
            int cw = Width / n;
            const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            for (int i = 0; i < n; i++)
            {
                int x = i * cw;
                if (i > 0) using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, x, 12, x, Height - 12);
                Rectangle ic = new Rectangle(x + 16, (Height - 30) / 2, 32, 30);
                if (i == 0) Theme.DrawPanel(g, ic, 6, Color.FromArgb(30, Theme.Teal), Color.FromArgb(120, Theme.Teal));
                Theme.DrawIcon(g, Glyphs[i], ic, Theme.Teal, Theme.IconBig);
                int tx = ic.Right + 14;
                int nh = Theme.UiStat.Height;
                int top = (Height - nh - Theme.UiSmall.Height) / 2;
                TextRenderer.DrawText(g, Values[i], Theme.UiStat, new Rectangle(tx, top, cw - (tx - x) - 8, nh), Theme.Text, L);
                TextRenderer.DrawText(g, Labels[i], Theme.UiSmall, new Rectangle(tx, top + nh, cw - (tx - x) - 8, Theme.UiSmall.Height + 2), Theme.Dim, L);
            }
        }
    }

    /// <summary>Barra lateral estreita de icones; o item ativo fica em verde-agua.</summary>
    internal sealed class SideBar : Control
    {
        public readonly List<IconButton> Items = new List<IconButton>();

        public SideBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        public IconButton Add(string glyph)
        {
            IconButton b = new IconButton { Glyph = glyph, Ghost = true, BackColor = Theme.Bg };
            Items.Add(b);
            Controls.Add(b);
            PerformLayout();
            return b;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int s = Math.Min(36, Width - 12);
            for (int i = 0; i < Items.Count; i++)
                Items[i].SetBounds((Width - 1 - s) / 2, 12 + i * (s + 12), s, s);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Bg);
            using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, Width - 1, 0, Width - 1, Height);
        }
    }

    /// <summary>Caixa de selecao do rodape (quadrado com contorno; marcada em verde-agua).</summary>
    internal sealed class NeonCheckBox : Control
    {
        public bool Checked;
        public string Label = "";
        public Color Accent = Theme.Teal;
        public event Action<bool> CheckedChanged;

        public NeonCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int box = 15;
            int y = (Height - box) / 2;
            Theme.DrawPanel(g, new Rectangle(0, y, box, box), 3, Checked ? Accent : Theme.Panel, Checked ? Accent : Theme.Gray);
            if (Checked)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen p = new Pen(Theme.Bg, 2f))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    g.DrawLines(p, new PointF[] { new PointF(3.5f, y + 7.5f), new PointF(6.5f, y + 10.5f), new PointF(11.5f, y + 4.5f) });
                }
                g.SmoothingMode = SmoothingMode.None;
            }
            TextRenderer.DrawText(g, Label, Theme.Ui, new Rectangle(box + 9, 0, Width - box - 9, Height), Theme.Text,
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
                DrawFlowLine(g, _lines[_scrollY + i], PadX - _scrollX, 3 + i * lh, lh);
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

        private const TextFormatFlags LF = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoClipping | TextFormatFlags.NoPadding;

        private void Seg(Graphics g, string t, ref int x, int y, int lh, Color c)
        {
            if (t.Length == 0) return;
            TextRenderer.DrawText(g, t, Theme.Mono, new Rectangle(x, y, t.Length * _charW + 8, lh), c, LF);
            x += t.Length * _charW;
        }

        /// <summary>
        /// Uma linha do fluxo em cores: hora apagada, palavra-chave do evento na
        /// cor dele (aberta azul, foco laranja, escondida cinza, fechada verde) e
        /// o resto em texto normal. Os detalhes (dono, autor, cadeia...) ganham
        /// o ramo "└". O texto e o mesmo do CLI; so o desenho muda.
        /// </summary>
        private void DrawFlowLine(Graphics g, HumanLine hl, int x, int y, int lh)
        {
            string t = hl.Text ?? "";
            int fim = t.StartsWith("[") ? t.IndexOf("] ") : -1;
            if (fim > 0)
            {
                Seg(g, t.Substring(0, fim + 1), ref x, y, lh, Theme.Dim);
                string resto = t.Substring(fim + 1);
                int ini = 0;
                while (ini < resto.Length && resto[ini] == ' ') ini++;
                int corte = resto.IndexOf("  ", ini);
                if (corte < 0) corte = resto.Length;
                string chave = resto.Substring(0, corte);
                Color c = Theme.FlowColor(hl.Role);
                if (chave.TrimStart().StartsWith("..")) c = Theme.Gray;
                Seg(g, chave, ref x, y, lh, c);
                // o alinhamento em colunas do CLI vira um espaco fixo (como na referencia)
                Seg(g, "   " + resto.Substring(corte).TrimStart(), ref x, y, lh, hl.Role == HumanRole.Body ? Theme.Dim : Theme.Text);
                return;
            }

            int n = 0;
            while (n < t.Length && t[n] == ' ') n++;
            int dois = t.IndexOf(" : ", n);
            if (hl.Role == HumanRole.Body && n >= 4 && dois > n && dois - n < 16)
            {
                int bx = x + (n - 2) * _charW;
                Seg(g, "\u2514", ref bx, y, lh, Theme.Gray);
                x += n * _charW;
                Seg(g, t.Substring(n, dois - n).TrimEnd() + ":  ", ref x, y, lh, Theme.Dim);
                Seg(g, t.Substring(dois + 2).TrimStart(), ref x, y, lh, Theme.Text);
                return;
            }
            Seg(g, t, ref x, y, lh, hl.Role == HumanRole.Body ? Theme.Dim : Theme.FlowColor(hl.Role));
        }

        /// <summary>Texto de todas as linhas exibidas (botao copiar).</summary>
        public string AllText()
        {
            var sb = new System.Text.StringBuilder();
            foreach (HumanLine l in _lines) sb.AppendLine(l.Text);
            return sb.ToString();
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

    /// <summary>Grafico de area com duas series (comandos em azul, sessoes em verde-agua) e eixo de tempo.</summary>
    internal sealed class LineChart : Control
    {
        public List<double> SeriesA = new List<double>();
        public List<double> SeriesB = new List<double>();
        public string LabelA = "A";
        public string LabelB = "B";
        /// <summary>Quantos segundos (pontos) mostrar: o fim e sempre "agora".</summary>
        public int Capacity = 60;

        public LineChart()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
        }

        private List<double> Ultimos(List<double> d)
        {
            if (d.Count <= Capacity) return d;
            return d.GetRange(d.Count - Capacity, Capacity);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Panel);
            const TextFormatFlags C = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;

            // legenda no alto
            int lx = 40;
            lx = Legenda(g, LabelA, Theme.Cyan, lx, 2) + 70;
            Legenda(g, LabelB, Theme.Teal, lx, 2);

            int left = 40, right = 16, top = 30, bottom = 22;
            Rectangle plot = new Rectangle(left, top, Math.Max(10, Width - left - right), Math.Max(10, Height - top - bottom));
            List<double> a = Ultimos(SeriesA), b = Ultimos(SeriesB);

            double max = 1;
            foreach (double v in a) if (v > max) max = v;
            foreach (double v in b) if (v > max) max = v;

            using (Pen gp = new Pen(Theme.Grid))
            {
                gp.DashStyle = DashStyle.Dot;
                for (int i = 0; i <= 4; i++)
                {
                    int y = plot.Bottom - (int)(plot.Height * i / 4.0);
                    g.DrawLine(gp, plot.Left, y, plot.Right, y);
                    string txt = (max * i / 4.0).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);
                    TextRenderer.DrawText(g, txt, Theme.UiSmall, new Rectangle(0, y - 8, left - 8, 16), Theme.Dim, C | TextFormatFlags.Right);
                }
            }

            // eixo de tempo: 6 marcas de hora, a ultima e agora
            DateTime agora = DateTime.Now;
            for (int i = 0; i <= 6; i++)
            {
                float x = plot.Left + plot.Width * i / 6f;
                DateTime t = agora.AddSeconds(-Capacity + Capacity * i / 6.0);
                string txt = t.ToString("HH:mm:ss");
                int tw = TextRenderer.MeasureText(g, txt, Theme.UiSmall).Width;
                int tx = (int)x - tw / 2;
                if (i == 0) tx = (int)x - 6;
                if (i == 6) tx = (int)x - tw + 4;
                if (i % 2 == 1 && plot.Width < 300) continue;   // estreito: metade das marcas
                TextRenderer.DrawText(g, txt, Theme.UiSmall, new Rectangle(tx, plot.Bottom + 4, tw + 4, 16), Theme.Dim, C);
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawSeries(g, a, plot, max, Theme.Cyan);
            DrawSeries(g, b, plot, max, Theme.Teal);
            g.SmoothingMode = SmoothingMode.None;
        }

        private static int Legenda(Graphics g, string texto, Color cor, int x, int y)
        {
            int tw = TextRenderer.MeasureText(g, texto, Theme.UiSmall).Width;
            Theme.DrawDot(g, x + 4, y + 10, 7, cor);
            TextRenderer.DrawText(g, texto, Theme.UiSmall, new Rectangle(x + 12, y, tw + 4, 20),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return x + 12 + tw;
        }

        private void DrawSeries(Graphics g, List<double> data, Rectangle plot, double max, Color color)
        {
            if (data.Count < 2) return;
            int n = data.Count;
            // a serie encosta na direita (agora); com menos pontos que a janela, comeca no meio
            float passo = plot.Width / (float)Math.Max(1, Capacity - 1);
            float x0 = plot.Right - passo * (n - 1);
            PointF[] pts = new PointF[n];
            for (int i = 0; i < n; i++)
                pts[i] = new PointF(x0 + passo * i, plot.Bottom - (float)(plot.Height * Math.Min(1.0, data[i] / max)));
            PointF[] area = new PointF[n + 2];
            Array.Copy(pts, area, n);
            area[n] = new PointF(pts[n - 1].X, plot.Bottom);
            area[n + 1] = new PointF(pts[0].X, plot.Bottom);
            using (LinearGradientBrush fill = new LinearGradientBrush(new Rectangle(plot.X, plot.Y - 1, plot.Width, plot.Height + 2),
                Color.FromArgb(90, color), Color.FromArgb(8, color), LinearGradientMode.Vertical))
                g.FillPolygon(fill, area);
            using (Pen core = new Pen(color, 1.8f))
            {
                core.LineJoin = LineJoin.Round;
                g.DrawLines(core, pts);
            }
        }
    }

    /// <summary>Partes comuns das duas tabelas: cabecalho, linhas, rolagem e realce sob o mouse.</summary>
    internal abstract class TabelaBase : Control
    {
        public int RowHeight = 24;
        public const int HeaderH = 26;
        protected int _scroll;
        protected int _hover = -1;
        protected const TextFormatFlags CellF = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        protected TabelaBase()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
        }

        protected abstract int Count { get; }

        protected int Visiveis { get { return Math.Max(1, (Height - HeaderH) / RowHeight); } }

        /// <summary>Cabecalho: rotulos apagados e a linha divisoria.</summary>
        protected void Cabecalho(Graphics g, string[] titulos, int[] xs)
        {
            g.Clear(Theme.Panel);
            using (Pen p = new Pen(Theme.Border))
            {
                g.DrawLine(p, 0, 0, Width, 0);
                g.DrawLine(p, 0, HeaderH - 1, Width, HeaderH - 1);
            }
            for (int i = 0; i < titulos.Length; i++)
                TextRenderer.DrawText(g, titulos[i], Theme.UiSmall,
                    new Rectangle(xs[i], 0, Math.Max(10, xs[i + 1] - xs[i] - 8), HeaderH), Theme.Dim, CellF);
            int vis = Visiveis;
            if (_scroll > Count - vis) _scroll = Math.Max(0, Count - vis);
            if (_scroll < 0) _scroll = 0;
        }

        protected void Rolagem(Graphics g)
        {
            int vis = Visiveis;
            if (Count <= vis) return;
            int trackH = vis * RowHeight;
            int barH = Math.Max(24, trackH * vis / Count);
            int barY = HeaderH + (trackH - barH) * _scroll / Math.Max(1, Count - vis);
            Theme.DrawScrollThumb(g, new Rectangle(Width - 6, barY, 4, barH), ScrollTheme.Thumb);
        }

        protected void Separador(Graphics g, int y)
        {
            using (Pen p = new Pen(Theme.Grid)) g.DrawLine(p, 14, y, Width - 14, y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = e.Y >= HeaderH ? _scroll + (e.Y - HeaderH) / RowHeight : -1;
            if (idx >= Count) idx = -1;
            if (idx != _hover) { _hover = idx; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int max = Math.Max(0, Count - Visiveis);
            _scroll -= e.Delta / 120;
            if (_scroll > max) _scroll = max;
            if (_scroll < 0) _scroll = 0;
            Invalidate();
        }

        protected void Vazio(Graphics g, string texto)
        {
            TextRenderer.DrawText(g, texto, Theme.Ui, new Rectangle(0, HeaderH, Width, Math.Max(30, Height - HeaderH)), Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        /// <summary>Posicoes x das colunas: larguras fixas e a coluna flexivel (indice flex) fica com a sobra.</summary>
        protected int[] Xs(int[] larguras, int flex, int x0)
        {
            int soma = 0;
            foreach (int l in larguras) soma += l;
            int[] w = (int[])larguras.Clone();
            w[flex] += Math.Max(0, Width - x0 - 14 - soma);
            int[] xs = new int[w.Length + 1];
            xs[0] = x0;
            for (int i = 0; i < w.Length; i++) xs[i + 1] = xs[i] + w[i];
            return xs;
        }
    }

    /// <summary>
    /// Tabela de sessoes: script, pid, processo de origem, comandos, inicio e
    /// a pilula de estado. Clique filtra o fluxo pela sessao; botao direito abre
    /// o menu (Fechar).
    /// </summary>
    internal sealed class SessionTable : TabelaBase
    {
        public List<SessionRow> Rows = new List<SessionRow>();
        public int SelectedPid;
        public event Action SelectionChanged;
        public event Action<int, Point> ContextRequested;

        protected override int Count { get { return Rows.Count; } }

        private static readonly int[] Larg = { 240, 90, 200, 90, 100, 110 };

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int[] xs = Xs(Larg, 0, 30);
            xs[0] = 30;
            Cabecalho(g, new[] { Lang.T("v2ColScript"), Lang.T("v2ColPid"), Lang.T("v2ColOrigin"),
                Lang.T("v2ColCmds"), Lang.T("v2ColStart"), Lang.T("v2ColStatus") }, xs);
            if (Rows.Count == 0) { Vazio(g, Lang.T("v2NoSessions")); return; }

            for (int i = 0; i < Visiveis && _scroll + i < Rows.Count; i++)
            {
                SessionRow r = Rows[_scroll + i];
                int y = HeaderH + i * RowHeight;
                bool sel = r.Pid == SelectedPid;
                if (sel)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(22, Theme.Teal)))
                        g.FillRectangle(b, 4, y + 1, Width - 8, RowHeight - 2);
                    Theme.DrawPanel(g, new Rectangle(4, y + 3, 3, RowHeight - 6), 1, Theme.Teal, Color.Empty);
                }
                else if ((_scroll + i) == _hover)
                    using (SolidBrush b = new SolidBrush(Theme.Hover)) g.FillRectangle(b, 4, y + 1, Width - 8, RowHeight - 2);
                if (i > 0 && !sel) Separador(g, y);

                Theme.DrawDot(g, 18, y + RowHeight / 2, 7, r.Closed ? Theme.Gray : Theme.Green);
                string[] cells = { r.Script, r.Pid.ToString(), r.Caller, r.Steps.ToString(), r.StartedAt.ToString("HH:mm:ss") };
                for (int c = 0; c < cells.Length; c++)
                    TextRenderer.DrawText(g, cells[c], c == 0 ? Theme.UiBold : Theme.Ui,
                        new Rectangle(xs[c], y, Math.Max(10, xs[c + 1] - xs[c] - 8), RowHeight),
                        c == 0 ? Theme.Text : (c == 2 ? Theme.Text : Theme.Dim), CellF);

                string st = r.Closed ? Lang.T("v2Ended") : Lang.T("v2Running");
                int pw = TextRenderer.MeasureText(st, Theme.UiSmall).Width + 18;
                Theme.DrawPill(g, new Rectangle(xs[5], y + (RowHeight - 18) / 2, pw, 18), st, r.Closed ? Theme.Gray : Theme.Green, !r.Closed);
            }
            Rolagem(g);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = _hover >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int idx = (e.Y - HeaderH) / RowHeight;
            bool onRow = e.Y >= HeaderH && idx >= 0 && _scroll + idx < Rows.Count;
            if (e.Button == MouseButtons.Right)
            {
                if (onRow && ContextRequested != null)
                    ContextRequested(Rows[_scroll + idx].Pid, PointToScreen(e.Location));
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (!onRow) { SetSelection(0); return; }
            int pid = Rows[_scroll + idx].Pid;
            SetSelection(pid == SelectedPid ? 0 : pid);
        }

        public void SetSelection(int pid)
        {
            if (SelectedPid == pid) return;
            SelectedPid = pid;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged();
        }
    }

    /// <summary>
    /// Tabela de janelas: processo autor (com o ponto de estado), titulo/caminho
    /// da janela em fonte monoespacada, processo de origem, duracao e estado.
    /// </summary>
    internal sealed class WindowTable : TabelaBase
    {
        public List<WindowRow> Rows = new List<WindowRow>();
        protected override int Count { get { return Rows.Count; } }

        private static readonly int[] Larg = { 150, 300, 200, 90, 110 };

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int[] xs = Xs(Larg, 1, 30);
            Cabecalho(g, new[] { Lang.T("v2ColProc"), Lang.T("v2ColPath"), Lang.T("v2ColOrigin"),
                Lang.T("v2ColDur"), Lang.T("v2ColStatus") }, xs);
            if (Rows.Count == 0) { Vazio(g, Lang.T("v2NoWindows")); return; }

            for (int i = 0; i < Visiveis && _scroll + i < Rows.Count; i++)
            {
                WindowRow r = Rows[_scroll + i];
                int y = HeaderH + i * RowHeight;
                if ((_scroll + i) == _hover)
                    using (SolidBrush b = new SolidBrush(Theme.Hover)) g.FillRectangle(b, 4, y + 1, Width - 8, RowHeight - 2);
                else if (i > 0) Separador(g, y);

                Color estado = r.Closed ? Theme.Gray : (r.Visible ? Theme.Green : Theme.Gray);
                Theme.DrawDot(g, 18, y + RowHeight / 2, 7, r.Closed ? Theme.Gray : (r.Visible ? Theme.Green : Theme.Amber));
                string nome = r.AuthorName.Length > 0 ? r.AuthorName : "?";
                string titulo = r.Title.Length > 0 ? r.Title : Lang.T("fNoTitle");
                TextRenderer.DrawText(g, nome, Theme.UiBold, new Rectangle(xs[0], y, xs[1] - xs[0] - 8, RowHeight), Theme.Text, CellF);
                TextRenderer.DrawText(g, titulo, Theme.MonoSmall, new Rectangle(xs[1], y, xs[2] - xs[1] - 8, RowHeight), Theme.Text, CellF);
                TextRenderer.DrawText(g, r.Origin, Theme.Ui, new Rectangle(xs[2], y, xs[3] - xs[2] - 8, RowHeight),
                    Theme.Text, CellF);
                TextRenderer.DrawText(g, r.DuracaoSeg.ToString("0.0") + " s", Theme.Ui, new Rectangle(xs[3], y, xs[4] - xs[3] - 8, RowHeight), Theme.Dim, CellF);

                string st = r.Closed ? Lang.T("v2WinClosed") : (r.Visible ? Lang.T("v2WinVisible") : Lang.T("v2WinHidden"));
                int pw = Math.Max(70, TextRenderer.MeasureText(st, Theme.UiSmall).Width + 18);
                Theme.DrawPill(g, new Rectangle(xs[4], y + (RowHeight - 18) / 2, pw, 18), st, estado, !r.Closed && r.Visible);
            }
            Rolagem(g);
        }
    }
}
