using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CmdWatch.Gui
{
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
        public DateTime CmdStartedAt = DateTime.MinValue;

        public double Elapsed
        {
            get { return (Closed ? ClosedAt : DateTime.Now).Subtract(StartedAt).TotalSeconds; }
        }
    }

    internal sealed class StreamEntry
    {
        public int Pid;
        public WatchEvent Event;
        public RunInfo Banner;
    }

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
        public string AuthorName = "";
        public int AuthorPid;
        public DateTime AuthorStartedAt = DateTime.MinValue;
        public string OriginTip = "";
        public string Origin = "";

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
    /// Cartao do painel no estilo Hoody / Neo-Brutalista: cantos 0px, borda 1px
    /// #2A2A2A, faixa de cabecalho #141414 separada por linha estrutural,
    /// marcador quadrado amarelo acido e contadores tabulares.
    /// </summary>
    internal sealed class NeonPanel : Control
    {
        public string Title = "";
        public string Subtitle = "";
        public string TitleValue = "";
        public Color Accent = Theme.Teal;
        public int HeaderHeight = 40;
        public bool TerminalSurface;

        public string[] Tabs;
        public string[] TabValues;
        public string[] TabGlyphs;
        public int AbaAtiva;
        public event Action<int> TabChanged;

        private Rectangle[] _tabHit = new Rectangle[0];

        public NeonPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
        }

        public Rectangle PlotArea
        {
            get { return new Rectangle(1, HeaderHeight, Math.Max(10, Width - 2), Math.Max(10, Height - HeaderHeight - 1)); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color body = TerminalSurface ? Theme.PanelDeep : Theme.Panel;
            using (SolidBrush b = new SolidBrush(body))
                g.FillRectangle(b, 0, 0, Width, Height);

            int hh = Math.Min(HeaderHeight, Height);
            using (SolidBrush hb = new SolidBrush(Theme.HeaderBg))
                g.FillRectangle(hb, 1, 1, Math.Max(0, Width - 2), Math.Max(0, hh - 1));

            using (Pen p = new Pen(Theme.Border))
            {
                if (Height > HeaderHeight)
                    g.DrawLine(p, 0, HeaderHeight - 1, Width, HeaderHeight - 1);
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }

            const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            if (Tabs != null && Tabs.Length > 0)
            {
                DesenharAbas(g, L);
                return;
            }

            // Quadrado indicador + titulo uppercase mono + contador + subtitulo discreto
            int x = 14;
            Theme.DrawDot(g, x + 3, hh / 2, 7, Accent);
            x += 13;

            string upTitle = (Title ?? "").ToUpperInvariant();
            int tw = TextRenderer.MeasureText(g, upTitle, Theme.UiTitle, Size.Empty, L).Width;
            TextRenderer.DrawText(g, upTitle, Theme.UiTitle, new Rectangle(x, 0, tw + 6, hh), Theme.Text, L);
            x += tw + 10;

            if (TitleValue.Length > 0)
            {
                int vw = TextRenderer.MeasureText(g, TitleValue, Theme.MonoBold, Size.Empty, L).Width + 14;
                Rectangle selo = new Rectangle(x, (hh - 18) / 2, Math.Max(22, vw), 18);
                Theme.DrawPanel(g, selo, 0, Theme.PanelDeep, Theme.Border);
                TextRenderer.DrawText(g, TitleValue, Theme.MonoBold, selo, Theme.Teal,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                x = selo.Right + 12;
            }

            if (Subtitle.Length > 0 && Width - x > 220)
            {
                using (Pen div = new Pen(Theme.Border))
                    g.DrawLine(div, x, hh / 2 - 6, x, hh / 2 + 6);
                x += 10;
                TextRenderer.DrawText(g, Subtitle, Theme.MonoTiny, new Rectangle(x, 0, Math.Max(20, Width - x - 200), hh), Theme.Dim, L);
            }
        }

        private void DesenharAbas(Graphics g, TextFormatFlags L)
        {
            _tabHit = new Rectangle[Tabs.Length];
            int hh = Math.Min(HeaderHeight, Height);
            int x = 0;
            for (int i = 0; i < Tabs.Length; i++)
            {
                int ini = x;
                bool ativa = i == AbaAtiva;
                string up = (Tabs[i] ?? "").ToUpperInvariant();

                int padL = 14;
                int cur = x + padL;

                // Quadrado indicador na aba ativa ou icone discreto
                if (ativa)
                {
                    using (SolidBrush bgTab = new SolidBrush(Theme.Panel))
                        g.FillRectangle(bgTab, ini + 1, 1, 1, 1); // reservado, pintamos abaixo apos medir
                }

                int iconW = (TabGlyphs != null && i < TabGlyphs.Length && TabGlyphs[i].Length > 0) ? 18 : 0;
                int tw = TextRenderer.MeasureText(g, up, Theme.UiTitle, Size.Empty, L).Width;
                int valW = 0;
                if (TabValues != null && i < TabValues.Length && TabValues[i].Length > 0)
                    valW = Math.Max(22, TextRenderer.MeasureText(g, TabValues[i], Theme.MonoTiny, Size.Empty, L).Width + 12) + 8;

                int tabW = padL + iconW + tw + valW + 16;
                // Garante que a aba 1 inclua x=230 para preservar o clique de automacao (tools\test-ocultar-windows.ps1)
                if (i == 0 && tabW < 185) tabW = 185;
                if (i == 1 && ini + tabW < 260) tabW = 260 - ini;

                Rectangle tabRect = new Rectangle(ini, 0, tabW, hh);
                _tabHit[i] = tabRect;

                if (ativa)
                {
                    using (SolidBrush ab = new SolidBrush(Theme.Panel))
                        g.FillRectangle(ab, tabRect.X + 1, 1, tabRect.Width - 1, hh - 2);
                    using (SolidBrush line = new SolidBrush(Theme.Teal))
                        g.FillRectangle(line, tabRect.X + 1, hh - 2, tabRect.Width - 1, 2);
                }

                Color fg = ativa ? Theme.Teal : Theme.Dim;
                cur = ini + padL;
                if (iconW > 0)
                {
                    Theme.DrawIcon(g, TabGlyphs[i], new Rectangle(cur, (hh - 16) / 2, 14, 16), fg, Theme.IconFont);
                    cur += 18;
                }

                TextRenderer.DrawText(g, up, Theme.UiTitle, new Rectangle(cur, 0, tw + 4, hh), ativa ? Theme.Text : Theme.Dim, L);
                cur += tw + 8;

                if (valW > 0)
                {
                    int bw = valW - 8;
                    Rectangle selo = new Rectangle(cur, (hh - 17) / 2, bw, 17);
                    if (ativa)
                    {
                        Theme.DrawPanel(g, selo, 0, Theme.Teal, Color.Empty);
                        TextRenderer.DrawText(g, TabValues[i], Theme.MonoBold, selo, Theme.OnAcid,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    }
                    else
                    {
                        Theme.DrawPanel(g, selo, 0, Theme.PanelDeep, Theme.Border);
                        TextRenderer.DrawText(g, TabValues[i], Theme.MonoTiny, selo, Theme.Dim,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    }
                }

                using (Pen div = new Pen(Theme.Border))
                    g.DrawLine(div, tabRect.Right, 0, tabRect.Right, hh - 1);

                x = tabRect.Right;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (Tabs == null || Tabs.Length == 0 || e.Y >= HeaderHeight) return;
            for (int i = 0; i < _tabHit.Length; i++)
            {
                if (_tabHit[i].Width > 0 && _tabHit[i].Contains(e.Location))
                {
                    SelecionarAba(i);
                    return;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Tabs == null || Tabs.Length == 0) return;
            bool overTab = false;
            if (e.Y < HeaderHeight)
            {
                for (int i = 0; i < _tabHit.Length; i++)
                    if (_tabHit[i].Contains(e.Location)) { overTab = true; break; }
            }
            Cursor = overTab ? Cursors.Hand : Cursors.Default;
        }

        public void SelecionarAba(int i)
        {
            if (Tabs == null || i < 0 || i >= Tabs.Length) return;
            if (AbaAtiva == i) return;
            AbaAtiva = i;
            Invalidate();
            if (TabChanged != null) TabChanged(i);
        }
    }

    /// <summary>
    /// Botao neo-brutalista (0px radius): suporta modo solido amarelo acido
    /// (PrimaryAcid), modo ativo com borda acida (Active), fantasma (Ghost)
    /// e contorno padrao (#2A2A2A). Preserva acessibilidade MSAA Invoke.
    /// </summary>
    internal sealed class IconButton : Control
    {
        public string Glyph = "";
        public string Label = "";
        public bool Chevron;
        public bool Ghost;
        public bool Active;
        public bool PrimaryAcid;
        public Color GlyphColor = Color.Empty;
        private static readonly Font ChevFont = new Font(Theme.IconFont.FontFamily, 6.5f);
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

        public int PreferredW()
        {
            int w = 14;
            if (Glyph.Length > 0) w += 17;
            if (Label.Length > 0) w += TextRenderer.MeasureText(Label.ToUpperInvariant(), Theme.MonoSmall).Width + 4;
            if (Chevron) w += 15;
            return w + 6;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fg;
            if (PrimaryAcid)
            {
                Color fill = _hot ? Theme.AcidHover : Theme.Teal;
                Theme.DrawPanel(g, r, 0, fill, Color.Empty);
                fg = Theme.OnAcid;
            }
            else if (Active)
            {
                Theme.DrawPanel(g, r, 0, Color.FromArgb(26, Theme.Teal), Theme.Teal);
                fg = Theme.Teal;
            }
            else if (Ghost)
            {
                if (_hot) Theme.DrawPanel(g, r, 0, Theme.Hover, Theme.Border);
                fg = _hot ? Theme.Teal : Theme.Dim;
            }
            else
            {
                Theme.DrawPanel(g, r, 0, _hot ? Theme.Hover : Theme.PanelDeep, _hot ? Theme.Teal : Theme.Border);
                fg = _hot ? Theme.Teal : Theme.Text;
            }

            string upLabel = (Label ?? "").ToUpperInvariant();
            int x = Glyph.Length > 0 && (upLabel.Length > 0 || Chevron) ? 8 : 0;
            if (Glyph.Length > 0)
            {
                int gw = x == 0 ? Width : 16;
                Color ic = PrimaryAcid ? Theme.OnAcid : (GlyphColor.A > 0 ? GlyphColor : fg);
                Theme.DrawIcon(g, Glyph, new Rectangle(x, 0, gw, Height), ic, Ghost ? Theme.IconBig : Theme.IconFont);
                x += gw + 4;
            }
            if (upLabel.Length > 0)
            {
                if (Glyph.Length == 0) x = 9;
                int lw = Width - x - (Chevron ? 18 : 6);
                Font lf = (PrimaryAcid || Active) ? Theme.MonoBold : Theme.MonoSmall;
                TextRenderer.DrawText(g, upLabel, lf, new Rectangle(x, 0, Math.Max(4, lw), Height), fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
            if (Chevron)
                Theme.DrawIcon(g, "\uE70D", new Rectangle(Width - 18, 1, 14, Height), PrimaryAcid ? Theme.OnAcid : Theme.Dim, ChevFont);
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Acesso(this); }
        private sealed class Acesso : ControlAccessibleObject
        {
            private readonly IconButton _b;
            public Acesso(IconButton b) : base(b) { _b = b; }
            public override AccessibleRole Role { get { return AccessibleRole.PushButton; } }
            public override string DefaultAction { get { return "Press"; } }
            public override string Name { get { return _b.Text; } set { } }
            public override void DoDefaultAction() { if (_b.Enabled) _b.OnClick(EventArgs.Empty); }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Campo de busca neo-brutalista com lupa, borda acida em foco e botao [×] para limpar.</summary>
    internal sealed class SearchBox : Control
    {
        public readonly TextBox Box = new TextBox();
        private string _cue = "";
        private Rectangle _clearRect = Rectangle.Empty;
        private bool _hoverClear;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public SearchBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.TopBar;
            Box.BorderStyle = BorderStyle.None;
            Box.Font = Theme.MonoSmall;
            Box.BackColor = Theme.PanelDeep;
            Box.ForeColor = Theme.Text;
            Box.HandleCreated += delegate { AplicarDica(); };
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Box.TextChanged += delegate { PerformLayout(); Invalidate(); };
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
            bool hasText = Box.Text.Length > 0;
            int rightReserve = hasText ? 24 : 8;
            _clearRect = hasText ? new Rectangle(Width - 22, (Height - 18) / 2, 18, 18) : Rectangle.Empty;
            Box.SetBounds(28, (Height - h) / 2 + 1, Math.Max(10, Width - 28 - rightReserve), h);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hc = !_clearRect.IsEmpty && _clearRect.Contains(e.Location);
            if (hc != _hoverClear) { _hoverClear = hc; Invalidate(); }
            Cursor = hc ? Cursors.Hand : Cursors.IBeam;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverClear) { _hoverClear = false; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!_clearRect.IsEmpty && _clearRect.Contains(e.Location))
            {
                Box.Text = "";
                Box.Focus();
                return;
            }
            Box.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Theme.DrawPanel(g, new Rectangle(0, 0, Width - 1, Height - 1), 0, Theme.PanelDeep, Box.Focused ? Theme.Teal : Theme.Border);
            Theme.DrawIcon(g, "\uE721", new Rectangle(6, 0, 18, Height), Box.Focused ? Theme.Teal : Theme.Dim);
            if (!_clearRect.IsEmpty)
            {
                TextRenderer.DrawText(g, "×", Theme.MonoBold, _clearRect, _hoverClear ? Theme.Teal : Theme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>
    /// Grade de 6 cartoes KPI do topo no layout Hoody:
    /// rotulo mono uppercase + icone no canto superior, numero grande display,
    /// subtitulo / barra de progresso linear (CPU e Memoria) e celula 0 com
    /// barra lateral amarelo acido.
    /// </summary>
    internal sealed class StatStrip : Control
    {
        public string[] Glyphs = { "\uE756", "\uE737", "\uE950", "\uE8B3", "\uE81E", "\uE823" };
        public string[] Values = { "0", "0", "0.0%", "0.0%", "0", "00:00:00" };
        public string[] Labels = { "", "", "", "", "", "" };
        public string[] SubLabels = { "", "", "", "", "", "" };
        public double CpuPercent;
        public double MemPercent;

        public StatStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            int n = Values.Length;
            if (n == 0) return;

            const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                                      TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

            for (int i = 0; i < n; i++)
            {
                int x0 = i * Width / n;
                int x1 = (i == n - 1) ? Width : ((i + 1) * Width / n);
                int cw = x1 - x0;

                if (i == 0)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(14, Theme.Teal)))
                        g.FillRectangle(b, x0 + 1, 1, cw - 1, Height - 2);
                    using (SolidBrush bar = new SolidBrush(Theme.Teal))
                        g.FillRectangle(bar, x0 + 1, 1, 3, Height - 2);
                }

                if (i > 0)
                {
                    using (Pen div = new Pen(Theme.Border))
                        g.DrawLine(div, x0, 0, x0, Height - 1);
                }

                int padX = x0 + (i == 0 ? 14 : 12);
                int innerW = Math.Max(20, cw - (i == 0 ? 24 : 22));

                // Linha 1: Rotulo mono uppercase + icone a direita
                string lbl = (i < Labels.Length ? Labels[i] ?? "" : "").ToUpperInvariant();
                TextRenderer.DrawText(g, lbl, Theme.MonoTiny, new Rectangle(padX, 6, innerW - 18, 14),
                    i == 0 ? Theme.Teal : Theme.Dim, L);
                if (i < Glyphs.Length)
                    Theme.DrawIcon(g, Glyphs[i], new Rectangle(x1 - 24, 5, 16, 16), i == 0 ? Theme.Teal : Theme.Gray, Theme.IconFont);

                // Linha 2: Valor grande
                Font valFont = Height < 68 ? Theme.UiHeader : Theme.UiStat;
                int valY = Height < 68 ? 20 : 21;
                int valH = Height < 68 ? 22 : 28;
                Color valColor = (i == 0 || i == 5) ? Theme.Teal : Theme.Text;
                Font vf = i == 5 ? (Height < 68 ? Theme.MonoBold : new Font(Theme.Mono.FontFamily, 14f, FontStyle.Bold)) : valFont;
                try
                {
                    TextRenderer.DrawText(g, Values[i], vf, new Rectangle(padX, valY, innerW, valH), valColor, L);
                }
                finally
                {
                    if (!ReferenceEquals(vf, valFont) && !ReferenceEquals(vf, Theme.MonoBold)) vf.Dispose();
                }

                // Linha 3: Barra de progresso (CPU=2, Mem=3) ou subtitulo
                if (Height >= 58)
                {
                    int subY = Height - 17;
                    if (i == 2 || i == 3)
                    {
                        double pct = Math.Max(0, Math.Min(100, i == 2 ? CpuPercent : MemPercent));
                        string sub = i < SubLabels.Length ? SubLabels[i] ?? "" : "";
                        int barH = 3;
                        int barY = subY + 10;
                        if (sub.Length > 0 && Height >= 72)
                        {
                            TextRenderer.DrawText(g, sub, Theme.MonoTiny, new Rectangle(padX, subY - 5, innerW, 12), Theme.Dim, L);
                            barY = Height - 7;
                        }
                        using (SolidBrush tr = new SolidBrush(Theme.PanelDeep))
                            g.FillRectangle(tr, padX, barY, innerW, barH);
                        int fw = (int)(innerW * pct / 100.0);
                        if (fw > 0)
                        {
                            Color bc = pct >= 85 ? Theme.Red : (i == 2 ? Theme.Teal : Theme.Text);
                            using (SolidBrush fb = new SolidBrush(bc))
                                g.FillRectangle(fb, padX, barY, fw, barH);
                        }
                    }
                    else
                    {
                        string sub = i < SubLabels.Length ? SubLabels[i] ?? "" : "";
                        if (sub.Length > 0)
                            TextRenderer.DrawText(g, sub, Theme.MonoTiny, new Rectangle(padX, subY, innerW, 13), Theme.Gray, L);
                    }
                }
            }

            using (Pen border = new Pen(Theme.Border))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>
    /// Barra lateral retratil no estilo Hoody (206px expandida, 52px recolhida):
    /// itens reais de navegacao com indicador amarelo acido, badges de contagem
    /// ao vivo, botao de configuracoes e botao de recolher no rodape.
    /// </summary>
    internal sealed class SideBar : Control
    {
        public sealed class NavEntry
        {
            public IconButton Button;
            public string Glyph = "";
            public string Label = "";
            public string Badge = "";
            public bool BottomGroup;
        }

        public readonly List<IconButton> Items = new List<IconButton>();
        public readonly List<NavEntry> Entries = new List<NavEntry>();
        public bool Collapsed;
        // modo de icones: pedido pelo usuario OU imposto pela largura (janela estreita)
        private bool Compact { get { return Collapsed || Width < 120; } }
        public string CollapseLabel = "";
        public event Action CollapseToggled;

        private int _hoverIdx = -1;
        private bool _hoverCollapse;
        private Rectangle _collapseRect;
        private readonly List<Rectangle> _itemRects = new List<Rectangle>();
        private readonly ToolTip _tip = new ToolTip();
        private string _lastTip = "";

        public SideBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Sidebar;
        }

        public IconButton Add(string glyph)
        {
            return Add(glyph, "", false);
        }

        public IconButton Add(string glyph, string label, bool bottomGroup)
        {
            // Mantem o IconButton invisivel para compatibilidade caso alguem chame PerformClick/Tip,
            // mas desenha a lista inteira com nitidez cirurgica no OnPaint da SideBar.
            IconButton b = new IconButton { Glyph = glyph, Label = label, Ghost = true, Visible = false };
            Items.Add(b);
            Entries.Add(new NavEntry { Button = b, Glyph = glyph, Label = label, BottomGroup = bottomGroup });
            Controls.Add(b);
            RecalcRects();
            Invalidate();
            return b;
        }

        public void SetEntryText(IconButton btn, string label, string tip)
        {
            btn.Label = label ?? "";
            btn.Tip = tip ?? "";
            foreach (NavEntry e in Entries)
            {
                if (ReferenceEquals(e.Button, btn))
                {
                    e.Label = label ?? "";
                    break;
                }
            }
            Invalidate();
        }

        public void SetBadge(IconButton btn, string badge)
        {
            foreach (NavEntry e in Entries)
            {
                if (ReferenceEquals(e.Button, btn))
                {
                    if (e.Badge != (badge ?? ""))
                    {
                        e.Badge = badge ?? "";
                        Invalidate();
                    }
                    return;
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RecalcRects();
        }

        private void RecalcRects()
        {
            _itemRects.Clear();
            int itemH = 36;
            int topY = Compact ? 10 : 30;
            int botCount = 0;
            foreach (NavEntry n in Entries) if (n.BottomGroup) botCount++;

            int collapseH = 34;
            _collapseRect = new Rectangle(0, Math.Max(0, Height - collapseH), Width - 1, collapseH);
            int botStartY = _collapseRect.Top - 8 - botCount * (itemH + 2);

            int curTop = topY;
            int curBot = botStartY;
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].BottomGroup)
                {
                    _itemRects.Add(new Rectangle(0, curBot, Width - 1, itemH));
                    curBot += itemH + 2;
                }
                else
                {
                    _itemRects.Add(new Rectangle(0, curTop, Width - 1, itemH));
                    curTop += itemH + 2;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Sidebar);
            RecalcRects();

            const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

            if (!Compact)
            {
                TextRenderer.DrawText(g, "MONITORAMENTO", Theme.MonoTiny,
                    new Rectangle(14, 8, Width - 20, 16), Theme.Gray, L);
            }

            bool drewBotSep = false;
            for (int i = 0; i < Entries.Count && i < _itemRects.Count; i++)
            {
                NavEntry en = Entries[i];
                Rectangle r = _itemRects[i];
                if (en.BottomGroup && !drewBotSep)
                {
                    drewBotSep = true;
                    using (Pen p = new Pen(Theme.Border))
                        g.DrawLine(p, 10, r.Top - 5, Width - 11, r.Top - 5);
                }

                bool active = en.Button.Active;
                bool hot = i == _hoverIdx;

                if (active)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(20, Theme.Teal)))
                        g.FillRectangle(b, r);
                    using (SolidBrush bar = new SolidBrush(Theme.Teal))
                        g.FillRectangle(bar, 0, r.Y, 3, r.Height);
                }
                else if (hot)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Hover))
                        g.FillRectangle(b, r);
                }

                Color ic = active ? Theme.Teal : (hot ? Theme.Text : Theme.Dim);
                if (Compact)
                {
                    Theme.DrawIcon(g, en.Glyph, r, ic, Theme.IconBig);
                }
                else
                {
                    Theme.DrawIcon(g, en.Glyph, new Rectangle(12, r.Y, 20, r.Height), ic, Theme.IconFont);
                    int badgeReserve = en.Badge.Length > 0 ? 38 : 8;
                    TextRenderer.DrawText(g, en.Label, active ? Theme.UiBold : Theme.Ui,
                        new Rectangle(38, r.Y, Math.Max(20, Width - 38 - badgeReserve), r.Height),
                        active ? Theme.Teal : (hot ? Theme.Text : Theme.Dim), L);

                    if (en.Badge.Length > 0)
                    {
                        int bw = Math.Max(22, TextRenderer.MeasureText(en.Badge, Theme.MonoTiny).Width + 10);
                        Rectangle br = new Rectangle(Width - 12 - bw, r.Y + (r.Height - 17) / 2, bw, 17);
                        if (active)
                        {
                            Theme.DrawPanel(g, br, 0, Color.FromArgb(32, Theme.Teal), Theme.Teal);
                            TextRenderer.DrawText(g, en.Badge, Theme.MonoTiny, br, Theme.Teal,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                        }
                        else
                        {
                            Theme.DrawPanel(g, br, 0, Theme.PanelDeep, Theme.Border);
                            TextRenderer.DrawText(g, en.Badge, Theme.MonoTiny, br, Theme.Dim,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                        }
                    }
                }
            }

            // Botao de recolher no rodape da sidebar
            using (Pen p = new Pen(Theme.Border))
            {
                g.DrawLine(p, 0, _collapseRect.Top, Width - 1, _collapseRect.Top);
                g.DrawLine(p, Width - 1, 0, Width - 1, Height);
            }
            if (_hoverCollapse)
            {
                using (SolidBrush b = new SolidBrush(Theme.Hover))
                    g.FillRectangle(b, _collapseRect.X, _collapseRect.Y + 1, _collapseRect.Width, _collapseRect.Height - 1);
            }
            string colGlyph = Compact ? "\uE76C" : "\uE76B"; // seta direita / esquerda
            Color colColor = _hoverCollapse ? Theme.Teal : Theme.Dim;
            if (Compact)
            {
                Theme.DrawIcon(g, colGlyph, _collapseRect, colColor, Theme.IconFont);
            }
            else
            {
                Theme.DrawIcon(g, colGlyph, new Rectangle(12, _collapseRect.Y, 20, _collapseRect.Height), colColor, Theme.IconFont);
                TextRenderer.DrawText(g, (CollapseLabel ?? "").ToUpperInvariant(), Theme.MonoTiny,
                    new Rectangle(38, _collapseRect.Y, Width - 46, _collapseRect.Height), colColor, L);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = -1;
            for (int i = 0; i < _itemRects.Count; i++)
                if (_itemRects[i].Contains(e.Location)) { idx = i; break; }
            bool hc = _collapseRect.Contains(e.Location);
            if (idx != _hoverIdx || hc != _hoverCollapse)
            {
                _hoverIdx = idx;
                _hoverCollapse = hc;
                Invalidate();
            }
            Cursor = (idx >= 0 || hc) ? Cursors.Hand : Cursors.Default;

            string tip = "";
            if (Compact && idx >= 0 && idx < Entries.Count) tip = Entries[idx].Label;
            else if (Compact && hc) tip = CollapseLabel;
            if (tip != _lastTip) { _lastTip = tip; _tip.SetToolTip(this, tip); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverIdx != -1 || _hoverCollapse)
            {
                _hoverIdx = -1;
                _hoverCollapse = false;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (_collapseRect.Contains(e.Location))
            {
                Collapsed = !Collapsed;
                if (CollapseToggled != null) CollapseToggled();
                Invalidate();
                return;
            }
            for (int i = 0; i < _itemRects.Count && i < Entries.Count; i++)
            {
                if (_itemRects[i].Contains(e.Location))
                {
                    InvokeBtn(Entries[i].Button);
                    return;
                }
            }
        }

        private static void InvokeBtn(IconButton b)
        {
            try { b.AccessibilityObject.DoDefaultAction(); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Trilho utilitario direito de 38px do layout Hoody (Pin no topo, Grade, Exportar, Bandeja).</summary>
    internal sealed class RightRail : Control
    {
        public readonly List<IconButton> Buttons = new List<IconButton>();

        public RightRail()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        public IconButton Add(string glyph, EventHandler click)
        {
            IconButton b = new IconButton { Glyph = glyph, Ghost = true, BackColor = Theme.Surface };
            if (click != null) b.Click += click;
            Buttons.Add(b);
            Controls.Add(b);
            PerformLayout();
            return b;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int s = 28;
            int x = (Width - s) / 2;
            int y = 10;
            for (int i = 0; i < Buttons.Count; i++)
            {
                Buttons[i].SetBounds(x, y, s, s);
                y += s + 6;
                if (i == 1) y += 8; // separador visual entre janela/grade e log/bandeja
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            using (Pen p = new Pen(Theme.Border))
            {
                g.DrawLine(p, 0, 0, 0, Height);
                if (Buttons.Count > 2)
                {
                    int sepY = Buttons[1].Bottom + 7;
                    g.DrawLine(p, 7, sepY, Width - 7, sepY);
                }
            }
        }
    }

    /// <summary>Gaveta lateral deslizante de Configuracoes e Estado dos Motores (estilo Hoody).</summary>
    internal sealed class SettingsDrawer : Control
    {
        public string EngineName = "polling";
        public int IntervalMs = 150;
        public bool WindowWatcherOn = true;
        public bool MaskOn = true;
        public string LogPath = "";
        public string Filter = "";

        public bool PinOnTop;
        public bool ArchGridOn = true;
        public bool TrayOn;

        public event Action CloseRequested;
        public event Action<bool> PinToggled;
        public event Action<bool> GridToggled;
        public event Action<bool> TrayToggled;
        public event Action OpenLogRequested;
        public event Action ExportLogRequested;

        private Rectangle _closeBtn, _footerCloseBtn, _togglePin, _toggleGrid, _toggleTray, _openLogBtn, _exportBtn;
        private int _hover = -1;

        public SettingsDrawer()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);

            using (Pen border = new Pen(Theme.Border))
            {
                g.DrawLine(border, 0, 0, 0, Height);
                g.DrawLine(border, 0, 52, Width, 52);
                g.DrawLine(border, 0, Height - 50, Width, Height - 50);
            }

            const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            int pad = 16;
            int w = Width - pad * 2;

            // Cabecalho
            Theme.DrawDot(g, pad + 3, 20, 6, Theme.Teal);
            TextRenderer.DrawText(g, Lang.T("v2Settings").ToUpperInvariant(), Theme.UiTitle,
                new Rectangle(pad + 12, 8, w - 40, 20), Theme.Text, L);
            TextRenderer.DrawText(g, Lang.T("v2SettingsSub"), Theme.MonoTiny,
                new Rectangle(pad + 12, 28, w - 40, 16), Theme.Dim, L);

            _closeBtn = new Rectangle(Width - pad - 26, 13, 26, 26);
            Theme.DrawPanel(g, new Rectangle(_closeBtn.X, _closeBtn.Y, 25, 25), 0, Theme.PanelDeep, _hover == 0 ? Theme.Teal : Theme.Border);
            TextRenderer.DrawText(g, "×", Theme.MonoBold, _closeBtn, _hover == 0 ? Theme.Teal : Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            int y = 66;

            // Secao 1: Motores de captura
            TextRenderer.DrawText(g, Lang.T("v2SettingsEngines").ToUpperInvariant(), Theme.MonoTiny,
                new Rectangle(pad, y, w, 14), Theme.Dim, L);
            y += 20;

            y = DrawStatusCard(g, pad, y, w, Lang.T("v2EngineBatch"),
                EngineName.ToUpperInvariant() + " · " + IntervalMs + " MS", true, Theme.Teal);
            y = DrawStatusCard(g, pad, y, w, Lang.T("v2EngineWin"),
                (WindowWatcherOn ? Lang.T("v2On") : Lang.T("v2Off")).ToUpperInvariant(), WindowWatcherOn, WindowWatcherOn ? Theme.Teal : Theme.Gray);
            y = DrawStatusCard(g, pad, y, w, Lang.T("v2EngineMask"),
                MaskOn ? Lang.T("v2MaskActive") : Lang.T("v2MaskOff"), MaskOn, MaskOn ? Theme.Teal : Theme.Amber);

            // Card do arquivo de log + acoes rapidas
            Rectangle logCard = new Rectangle(pad, y, w - 1, 66);
            Theme.DrawPanel(g, logCard, 0, Theme.PanelDeep, Theme.Border);
            TextRenderer.DrawText(g, Lang.T("v2EngineLog").ToUpperInvariant(), Theme.MonoTiny,
                new Rectangle(pad + 10, y + 6, w - 20, 14), Theme.Dim, L);
            string lp = LogPath.Length > 0 ? LogPath : Lang.T("v2NoLog");
            TextRenderer.DrawText(g, lp, Theme.MonoTiny,
                new Rectangle(pad + 10, y + 22, w - 20, 16), Theme.Text, L);

            int btnW = (w - 26) / 2;
            _openLogBtn = new Rectangle(pad + 10, y + 41, btnW, 19);
            _exportBtn = new Rectangle(pad + 16 + btnW, y + 41, btnW, 19);
            DrawSmallAction(g, _openLogBtn, Lang.T("trayOpenLog"), _hover == 5);
            DrawSmallAction(g, _exportBtn, Lang.T("v2ExportLog"), _hover == 6);
            y += 78;

            // Secao 2: Preferencias interativas
            TextRenderer.DrawText(g, Lang.T("v2SettingsPrefs").ToUpperInvariant(), Theme.MonoTiny,
                new Rectangle(pad, y, w, 14), Theme.Dim, L);
            y += 20;

            _togglePin = DrawToggleCard(g, pad, y, w, Lang.T("v2AlwaysOnTop"), Lang.T("v2AlwaysOnTopDesc"), PinOnTop, _hover == 1);
            y += 50;
            _toggleGrid = DrawToggleCard(g, pad, y, w, Lang.T("v2ArchGrid"), Lang.T("v2ArchGridDesc"), ArchGridOn, _hover == 2);
            y += 50;
            _toggleTray = DrawToggleCard(g, pad, y, w, Lang.T("v2Tray"), Lang.T("trayCheck"), TrayOn, _hover == 3);

            // Rodape da gaveta
            _footerCloseBtn = new Rectangle(pad, Height - 39, w - 1, 28);
            Theme.DrawPanel(g, _footerCloseBtn, 0, _hover == 4 ? Theme.AcidHover : Theme.Teal, Color.Empty);
            TextRenderer.DrawText(g, Lang.T("v2SettingsClose").ToUpperInvariant(), Theme.MonoBold, _footerCloseBtn, Theme.OnAcid,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private static int DrawStatusCard(Graphics g, int x, int y, int w, string label, string status, bool on, Color accent)
        {
            Rectangle r = new Rectangle(x, y, w - 1, 36);
            Theme.DrawPanel(g, r, 0, Theme.PanelDeep, Theme.Border);
            TextRenderer.DrawText(g, label, Theme.Ui, new Rectangle(x + 10, y, w - 130, 36), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            int pw = Math.Max(68, TextRenderer.MeasureText(status, Theme.MonoTiny).Width + 24);
            Theme.DrawPill(g, new Rectangle(x + w - 10 - pw, y + 9, pw, 18), status, accent, on);
            return y + 42;
        }

        private static void DrawSmallAction(Graphics g, Rectangle r, string text, bool hot)
        {
            Theme.DrawPanel(g, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), 0, hot ? Theme.Hover : Theme.Surface, hot ? Theme.Teal : Theme.Border);
            TextRenderer.DrawText(g, text.ToUpperInvariant(), Theme.MonoTiny, r, hot ? Theme.Teal : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        private static Rectangle DrawToggleCard(Graphics g, int x, int y, int w, string title, string sub, bool active, bool hot)
        {
            Rectangle r = new Rectangle(x, y, w - 1, 44);
            Theme.DrawPanel(g, r, 0, hot ? Theme.Hover : Theme.PanelDeep, active ? Color.FromArgb(140, Theme.Teal) : Theme.Border);
            TextRenderer.DrawText(g, title, Theme.UiBold, new Rectangle(x + 10, y + 5, w - 64, 18), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, sub, Theme.MonoTiny, new Rectangle(x + 10, y + 23, w - 64, 15), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            // Chave quadrada neo-brutalista (36x18)
            Rectangle sw = new Rectangle(x + w - 46, y + 13, 36, 18);
            Theme.DrawPanel(g, sw, 0, active ? Theme.Teal : Theme.Surface, active ? Theme.Teal : Theme.Border);
            Rectangle knob = active ? new Rectangle(sw.Right - 15, sw.Y + 3, 12, 12) : new Rectangle(sw.X + 3, sw.Y + 3, 12, 12);
            using (SolidBrush kb = new SolidBrush(active ? Theme.OnAcid : Theme.Dim))
                g.FillRectangle(kb, knob);
            return r;
        }

        private int Hit(Point p)
        {
            if (_closeBtn.Contains(p)) return 0;
            if (_togglePin.Contains(p)) return 1;
            if (_toggleGrid.Contains(p)) return 2;
            if (_toggleTray.Contains(p)) return 3;
            if (_footerCloseBtn.Contains(p)) return 4;
            if (_openLogBtn.Contains(p)) return 5;
            if (_exportBtn.Contains(p)) return 6;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = Hit(e.Location);
            if (h != _hover)
            {
                _hover = h;
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int h = Hit(e.Location);
            if (h == 0 || h == 4) { if (CloseRequested != null) CloseRequested(); }
            else if (h == 1) { PinOnTop = !PinOnTop; Invalidate(); if (PinToggled != null) PinToggled(PinOnTop); }
            else if (h == 2) { ArchGridOn = !ArchGridOn; Invalidate(); if (GridToggled != null) GridToggled(ArchGridOn); }
            else if (h == 3) { TrayOn = !TrayOn; Invalidate(); if (TrayToggled != null) TrayToggled(TrayOn); }
            else if (h == 5) { if (OpenLogRequested != null) OpenLogRequested(); }
            else if (h == 6) { if (ExportLogRequested != null) ExportLogRequested(); }
        }
    }

    /// <summary>Caixa de selecao quadrada neo-brutalista do rodape.</summary>
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
            BackColor = Theme.TopBar;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int box = 14;
            int y = (Height - box) / 2;
            Theme.DrawPanel(g, new Rectangle(0, y, box, box), 0, Checked ? Accent : Theme.PanelDeep, Checked ? Accent : Theme.Border);
            if (Checked)
            {
                using (Pen p = new Pen(Theme.OnAcid, 2f))
                    g.DrawLines(p, new Point[] { new Point(3, y + 7), new Point(6, y + 10), new Point(11, y + 4) });
            }
            TextRenderer.DrawText(g, (Label ?? "").ToUpperInvariant(), Theme.MonoTiny, new Rectangle(box + 8, 0, Width - box - 8, Height), Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
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
    /// Terminal de fluxo de comandos (#0E0E0E) com realce neo-brutalista:
    /// timestamp cinza, eventos em amarelo acido/laranja, arvore em linhas
    /// tecnicas, cursor quadrado piscante no fim e barra de 6px acida no hover.
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
        public bool CursorVisible = true;

        private const int BarW = 6;
        private const int PadX = 12;

        public NeonFlow()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.PanelDeep;
        }

        public int LineHeight { get { return Theme.Mono.Height + 4; } }
        public int LineCount { get { return _lines.Count; } }
        public int VisibleLines { get { return Math.Max(1, (Height - 10) / LineHeight); } }
        public int MaxScrollY { get { return Math.Max(0, _lines.Count - VisibleLines + 1); } }

        public int MaxScrollX
        {
            get
            {
                int total = _maxChars * _charW + PadX + 20;
                return Math.Max(0, total - (Width - BarW));
            }
        }

        public void SetLines(List<HumanLine> lines)
        {
            _lines.Clear();
            if (lines != null) _lines.AddRange(lines);
            Recalc();
            Clamp();
            Invalidate();
        }

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
            _charW = Math.Max(6, TextRenderer.MeasureText(g, "MMMMMMMMMM", Theme.Mono, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width / 10);
        }

        private void Clamp()
        {
            if (_scrollY > MaxScrollY) _scrollY = MaxScrollY;
            if (_scrollY < 0) _scrollY = 0;
            if (_scrollX > MaxScrollX) _scrollX = MaxScrollX;
            if (_scrollX < 0) _scrollX = 0;
        }

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
            int drawn = 0;
            for (int i = 0; i < VisibleLines && _scrollY + i < _lines.Count; i++)
            {
                DrawFlowLine(g, _lines[_scrollY + i], PadX - _scrollX, 4 + i * lh, lh);
                drawn = i + 1;
            }

            // Prompt ao vivo com cursor quadrado amarelo acido piscando no fim do fluxo
            if (_follow && drawn < VisibleLines + 1)
            {
                int py = 4 + drawn * lh;
                if (py + lh <= textH + 4)
                {
                    int px = PadX - _scrollX;
                    Seg(g, "cmdwatch://stream>", ref px, py, lh, Theme.Gray);
                    if (CursorVisible)
                    {
                        using (SolidBrush cb = new SolidBrush(Theme.Teal))
                            g.FillRectangle(cb, px + 4, py + 3, Math.Max(6, _charW - 1), lh - 6);
                    }
                }
            }
            g.ResetClip();

            // Trilho e polegar vertical
            if (_lines.Count > VisibleLines)
            {
                Rectangle track = new Rectangle(Width - BarW, 0, BarW, textH);
                using (SolidBrush tb = new SolidBrush(ScrollTheme.Track)) g.FillRectangle(tb, track);
                using (Pen tp = new Pen(Theme.LineSubtle)) g.DrawLine(tp, track.X, 0, track.X, track.Bottom);
                int thumb = Math.Max(26, track.Height * VisibleLines / Math.Max(1, _lines.Count));
                int max = MaxScrollY;
                int y = max == 0 ? 0 : (int)((double)_scrollY / max * (track.Height - thumb));
                Theme.DrawScrollThumb(g, new Rectangle(track.X + 1, y, BarW - 1, thumb), ThumbColor());
            }

            if (hasH)
            {
                Rectangle track = new Rectangle(0, Height - BarW, textW, BarW);
                using (SolidBrush tb = new SolidBrush(ScrollTheme.Track)) g.FillRectangle(tb, track);
                int thumb = Math.Max(26, track.Width * (Width - BarW) / Math.Max(1, _maxChars * _charW + PadX));
                int max = MaxScrollX;
                int x = max == 0 ? 0 : (int)((double)_scrollX / max * (track.Width - thumb));
                Theme.DrawScrollThumb(g, new Rectangle(x, track.Y + 1, thumb, BarW - 1), ThumbColor());
            }
        }

        private const TextFormatFlags LF = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoClipping | TextFormatFlags.NoPadding;

        private void Seg(Graphics g, string t, ref int x, int y, int lh, Color c)
        {
            if (string.IsNullOrEmpty(t)) return;
            TextRenderer.DrawText(g, t, Theme.Mono, new Rectangle(x, y, t.Length * _charW + 12, lh), c, LF);
            x += t.Length * _charW;
        }

        private void DrawFlowLine(Graphics g, HumanLine hl, int x, int y, int lh)
        {
            string t = hl.Text ?? "";
            int fim = t.StartsWith("[") ? t.IndexOf("] ") : -1;
            if (fim > 0)
            {
                Seg(g, t.Substring(0, fim + 1), ref x, y, lh, Theme.Gray);
                string resto = t.Substring(fim + 1);
                int ini = 0;
                while (ini < resto.Length && resto[ini] == ' ') ini++;
                int corte = resto.IndexOf("  ", ini);
                if (corte < 0) corte = resto.Length;
                string chave = resto.Substring(0, corte);
                Color c = Theme.FlowColor(hl.Role);
                if (chave.TrimStart().StartsWith("..")) c = Theme.Gray;
                Seg(g, chave, ref x, y, lh, c);
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

        public string AllText()
        {
            var sb = new System.Text.StringBuilder();
            foreach (HumanLine l in _lines) sb.AppendLine(l.Text);
            return sb.ToString();
        }

        private Color ThumbColor()
        {
            if (_dragV || _dragH) return ScrollTheme.ThumbActive;
            if (_hot) return ScrollTheme.ThumbHot;
            return ScrollTheme.Thumb;
        }

        private Rectangle VerticalTrack() { return new Rectangle(Width - BarW, 0, BarW, Height); }
        private Rectangle HorizontalTrack() { return new Rectangle(0, Height - BarW, Width - BarW, BarW); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int step = e.Delta / 120 * 3;
            if (step == 0) step = e.Delta > 0 ? 1 : -1;
            if ((ModifierKeys & Keys.Shift) != 0) { ScrollXBy(step * _charW * 2); return; }
            ScrollYBy(-step);
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
                int thumb = Math.Max(26, v.Height * VisibleLines / Math.Max(1, _lines.Count));
                int usable = Math.Max(1, v.Height - thumb);
                int rel = Math.Max(0, Math.Min(usable, e.Y - v.Y - _dragOffset));
                int max = MaxScrollY;
                _scrollY = max == 0 ? 0 : (int)((double)rel / usable * max);
                _follow = _scrollY >= max;
                Invalidate();
            }
            else if (_dragH)
            {
                Rectangle h = HorizontalTrack();
                int total = Math.Max(1, _maxChars * _charW + PadX);
                int thumb = Math.Max(26, h.Width * (Width - BarW) / total);
                int usable = Math.Max(1, h.Width - thumb);
                int rel = Math.Max(0, Math.Min(usable, e.X - h.X - _dragOffset));
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

    /// <summary>
    /// Grafico de telemetria no estilo Hoody: serie A (Comandos/s) com linha
    /// solida amarelo acido (#E2FF26) e preenchimento translucido; serie B
    /// (Sessoes/s) em linha tracejada branca (#CCCCCC), grade pontilhada #242424.
    /// </summary>
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

            List<double> a = Ultimos(SeriesA), b = Ultimos(SeriesB);
            double curA = a.Count > 0 ? a[a.Count - 1] : 0;
            double curB = b.Count > 0 ? b[b.Count - 1] : 0;

            // Legenda com leitura ao vivo
            int lx = 38;
            lx = Legenda(g, LabelA.ToUpperInvariant() + " [" + curA.ToString("0") + "/S]", Theme.Teal, false, lx, 3) + 22;
            Legenda(g, LabelB.ToUpperInvariant() + " [" + curB.ToString("0") + "/S]", Theme.Purple, true, lx, 3);

            int left = 38, right = 14, top = 28, bottom = 22;
            Rectangle plot = new Rectangle(left, top, Math.Max(10, Width - left - right), Math.Max(10, Height - top - bottom));

            double max = 4;
            foreach (double v in a) if (v > max) max = v;
            foreach (double v in b) if (v > max) max = v;

            using (Pen gp = new Pen(Theme.Grid))
            {
                gp.DashStyle = DashStyle.Dash;
                for (int i = 0; i <= 4; i++)
                {
                    int y = plot.Bottom - (int)(plot.Height * i / 4.0);
                    g.DrawLine(gp, plot.Left, y, plot.Right, y);
                    string txt = (max * i / 4.0).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);
                    TextRenderer.DrawText(g, txt, Theme.MonoTiny, new Rectangle(0, y - 8, left - 6, 16), Theme.Gray, C | TextFormatFlags.Right);
                }
            }

            DateTime agora = DateTime.Now;
            int ticks = plot.Width < 300 ? 3 : 5;
            for (int i = 0; i <= ticks; i++)
            {
                float x = plot.Left + plot.Width * i / (float)ticks;
                DateTime t = agora.AddSeconds(-Capacity + Capacity * i / (double)ticks);
                string txt = t.ToString("HH:mm:ss");
                int tw = TextRenderer.MeasureText(g, txt, Theme.MonoTiny).Width;
                int tx = (int)x - tw / 2;
                if (i == 0) tx = (int)x;
                if (i == ticks) tx = (int)x - tw;
                TextRenderer.DrawText(g, txt, Theme.MonoTiny, new Rectangle(tx, plot.Bottom + 4, tw + 4, 15), Theme.Gray, C);
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawSeries(g, a, plot, max, Theme.Teal, true, false);
            DrawSeries(g, b, plot, max, Theme.Purple, false, true);
            g.SmoothingMode = SmoothingMode.None;

            using (Pen frame = new Pen(Theme.Border))
                g.DrawRectangle(frame, plot.X, plot.Y, plot.Width, plot.Height);
        }

        private static int Legenda(Graphics g, string texto, Color cor, bool dashed, int x, int y)
        {
            int tw = TextRenderer.MeasureText(g, texto, Theme.MonoTiny).Width;
            if (dashed)
            {
                using (Pen p = new Pen(cor, 2f))
                {
                    p.DashStyle = DashStyle.Dash;
                    g.DrawLine(p, x, y + 10, x + 12, y + 10);
                }
            }
            else
            {
                Theme.DrawDot(g, x + 5, y + 10, 6, cor);
            }
            TextRenderer.DrawText(g, texto, Theme.MonoTiny, new Rectangle(x + 15, y, tw + 6, 20),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return x + 15 + tw;
        }

        private void DrawSeries(Graphics g, List<double> data, Rectangle plot, double max, Color color, bool fillArea, bool dashed)
        {
            if (data.Count < 2) return;
            int n = data.Count;
            float passo = plot.Width / (float)Math.Max(1, Capacity - 1);
            float x0 = plot.Right - passo * (n - 1);
            PointF[] pts = new PointF[n];
            for (int i = 0; i < n; i++)
                pts[i] = new PointF(x0 + passo * i, plot.Bottom - (float)(plot.Height * Math.Min(1.0, data[i] / max)));

            if (fillArea)
            {
                PointF[] area = new PointF[n + 2];
                Array.Copy(pts, area, n);
                area[n] = new PointF(pts[n - 1].X, plot.Bottom);
                area[n + 1] = new PointF(pts[0].X, plot.Bottom);
                using (LinearGradientBrush fill = new LinearGradientBrush(
                    new Rectangle(plot.X, plot.Y - 1, plot.Width, plot.Height + 2),
                    Color.FromArgb(60, color), Color.FromArgb(4, color), LinearGradientMode.Vertical))
                    g.FillPolygon(fill, area);
            }

            using (Pen core = new Pen(color, dashed ? 1.5f : 2f))
            {
                if (dashed) core.DashStyle = DashStyle.Dash;
                core.LineJoin = LineJoin.Miter;
                g.DrawLines(core, pts);
            }
        }
    }

    /// <summary>Base das tabelas de dados com cabecalho mono uppercase, resize de colunas e cantos vivos.</summary>
    internal abstract class TabelaBase : Control
    {
        public int RowHeight = 24;
        public const int HeaderH = 26;
        protected int _scroll;
        protected int _hover = -1;
        protected const TextFormatFlags CellF = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        private int[] _larg;
        private int _flex = -1;
        private int _x0 = 30;
        private int[] _xs;
        private int _xsW = -1;
        private bool _arrastando;
        private int _colArraste = -1;
        private int _xArraste;
        private int[] _largInicial;
        private int _bordaColuna = -1;

        protected TabelaBase()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
        }

        protected abstract int Count { get; }
        protected int Visiveis { get { return Math.Max(1, (Height - HeaderH) / RowHeight); } }

        protected void SetColunas(int[] larguras, int flex, int x0)
        {
            _larg = (int[])larguras.Clone();
            _flex = flex;
            _x0 = x0;
            _xs = null;
        }

        protected int[] Xs()
        {
            if (_xs != null && _xsW == Width) return _xs;
            int soma = 0;
            foreach (int l in _larg) soma += l;
            int[] w = (int[])_larg.Clone();
            w[_flex] += Math.Max(0, Width - _x0 - 14 - soma);
            int[] xs = new int[w.Length + 1];
            xs[0] = _x0;
            for (int i = 0; i < w.Length; i++) xs[i + 1] = xs[i] + w[i];
            _xs = xs; _xsW = Width;
            return xs;
        }

        protected bool Redimensionando { get { return _arrastando; } }

        protected Cursor CursorColuna()
        {
            if (_bordaColuna >= 0) return Cursors.VSplit;
            return _hover >= 0 ? Cursors.Hand : Cursors.Default;
        }

        private int BordaEm(int x)
        {
            if (_larg == null || _larg.Length < 2) return -1;
            int[] xs = Xs();
            for (int i = 0; i < _larg.Length - 1; i++)
                if (Math.Abs(x - xs[i + 1]) <= 4) return i;
            return -1;
        }

        private void Redimensionar(int x)
        {
            int delta = x - _xArraste;
            int nova = _largInicial[_colArraste] + delta;
            if (nova < 40) nova = 40;
            int real = nova - _largInicial[_colArraste];
            _larg[_colArraste] = nova;
            if (_flex != _colArraste)
            {
                int flex = _largInicial[_flex] - real;
                if (flex < 60)
                {
                    flex = 60;
                    _larg[_colArraste] = _largInicial[_colArraste] + (_largInicial[_flex] - 60);
                }
                _larg[_flex] = flex;
            }
            _xs = null;
            Invalidate();
        }

        protected void Cabecalho(Graphics g, string[] titulos, int[] xs)
        {
            g.Clear(Theme.Panel);
            using (SolidBrush hb = new SolidBrush(Theme.Fill))
                g.FillRectangle(hb, 0, 0, Width, HeaderH);
            using (Pen p = new Pen(Theme.Border))
                g.DrawLine(p, 0, HeaderH - 1, Width, HeaderH - 1);

            for (int i = 0; i < titulos.Length; i++)
            {
                TextRenderer.DrawText(g, (titulos[i] ?? "").ToUpperInvariant(), Theme.MonoTiny,
                    new Rectangle(xs[i], 0, Math.Max(10, xs[i + 1] - xs[i] - 8), HeaderH), Theme.Dim, CellF);
                if (i > 0)
                {
                    using (Pen sep = new Pen(Theme.LineSubtle))
                        g.DrawLine(sep, xs[i] - 5, 5, xs[i] - 5, HeaderH - 6);
                }
            }
            if (_bordaColuna >= 0 && _bordaColuna + 1 < xs.Length)
                using (Pen p = new Pen(Theme.Teal))
                    g.DrawLine(p, xs[_bordaColuna + 1], 1, xs[_bordaColuna + 1], HeaderH - 2);

            int vis = Visiveis;
            if (_scroll > Count - vis) _scroll = Math.Max(0, Count - vis);
            if (_scroll < 0) _scroll = 0;
        }

        protected void Rolagem(Graphics g)
        {
            int vis = Visiveis;
            if (Count <= vis) return;
            int trackH = Height - HeaderH;
            using (SolidBrush tb = new SolidBrush(ScrollTheme.Track))
                g.FillRectangle(tb, Width - 6, HeaderH, 6, trackH);
            int barH = Math.Max(24, trackH * vis / Count);
            int barY = HeaderH + (trackH - barH) * _scroll / Math.Max(1, Count - vis);
            Theme.DrawScrollThumb(g, new Rectangle(Width - 5, barY, 5, barH), ScrollTheme.Thumb);
        }

        protected void Separador(Graphics g, int y)
        {
            using (Pen p = new Pen(Theme.LineSubtle)) g.DrawLine(p, 0, y, Width, y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_arrastando) { Redimensionar(e.X); return; }
            int borda = e.Y < HeaderH ? BordaEm(e.X) : -1;
            if (borda != _bordaColuna) { _bordaColuna = borda; Invalidate(); }
            int idx = e.Y >= HeaderH ? _scroll + (e.Y - HeaderH) / RowHeight : -1;
            if (idx >= Count) idx = -1;
            if (idx != _hover) { _hover = idx; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
            if (_bordaColuna != -1) { _bordaColuna = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (e.Y >= HeaderH) return;
            int col = BordaEm(e.X);
            if (col < 0 || _larg == null) return;
            _arrastando = true;
            _colArraste = col;
            _xArraste = e.X;
            _largInicial = (int[])_larg.Clone();
            Capture = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_arrastando)
            {
                _arrastando = false;
                _colArraste = -1;
                Capture = false;
                Invalidate();
            }
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
            TextRenderer.DrawText(g, (texto ?? "").ToUpperInvariant(), Theme.MonoSmall,
                new Rectangle(0, HeaderH, Width, Math.Max(30, Height - HeaderH)), Theme.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

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

    internal sealed class SessionTable : TabelaBase
    {
        public List<SessionRow> Rows = new List<SessionRow>();
        public int SelectedPid;
        public event Action SelectionChanged;
        public event Action<int, Point> ContextRequested;

        protected override int Count { get { return Rows.Count; } }

        private static readonly int[] Larg = { 240, 88, 210, 90, 96, 126 };

        public SessionTable()
        {
            SetColunas(Larg, 0, 28);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int[] xs = Xs();
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
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(20, Theme.Teal)))
                        g.FillRectangle(b, 0, y, Width, RowHeight);
                    using (SolidBrush bar = new SolidBrush(Theme.Teal))
                        g.FillRectangle(bar, 0, y, 3, RowHeight);
                }
                else if ((_scroll + i) == _hover)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Hover))
                        g.FillRectangle(b, 0, y, Width, RowHeight);
                }
                if (i > 0) Separador(g, y);

                Theme.DrawDot(g, 15, y + RowHeight / 2, 6, r.Closed ? Theme.Gray : Theme.Teal);

                TextRenderer.DrawText(g, r.Script, Theme.MonoBold,
                    new Rectangle(xs[0], y, Math.Max(10, xs[1] - xs[0] - 8), RowHeight),
                    sel ? Theme.Teal : Theme.Text, CellF);
                TextRenderer.DrawText(g, r.Pid.ToString(), Theme.MonoSmall,
                    new Rectangle(xs[1], y, Math.Max(10, xs[2] - xs[1] - 8), RowHeight), Theme.Dim, CellF);
                TextRenderer.DrawText(g, r.Caller, Theme.MonoSmall,
                    new Rectangle(xs[2], y, Math.Max(10, xs[3] - xs[2] - 8), RowHeight), Theme.Text, CellF);
                TextRenderer.DrawText(g, r.Steps.ToString(), Theme.MonoBold,
                    new Rectangle(xs[3], y, Math.Max(10, xs[4] - xs[3] - 8), RowHeight),
                    r.Steps > 0 ? Theme.Teal : Theme.Dim, CellF);
                TextRenderer.DrawText(g, r.StartedAt.ToString("HH:mm:ss"), Theme.MonoSmall,
                    new Rectangle(xs[4], y, Math.Max(10, xs[5] - xs[4] - 8), RowHeight), Theme.Dim, CellF);

                string st = r.Closed ? Lang.T("v2Ended") : Lang.T("v2Running");
                int pw = Math.Max(78, TextRenderer.MeasureText(st.ToUpperInvariant(), Theme.MonoTiny).Width + 24);
                Theme.DrawPill(g, new Rectangle(xs[5], y + (RowHeight - 17) / 2, pw, 17), st, r.Closed ? Theme.Gray : Theme.Teal, !r.Closed);
            }
            Rolagem(g);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = CursorColuna();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Redimensionando) return;
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

    internal sealed class WindowTable : TabelaBase
    {
        public List<WindowRow> Rows = new List<WindowRow>();
        public event Action<IntPtr, Point> ContextRequested;
        protected override int Count { get { return Rows.Count; } }

        private static readonly int[] Larg = { 150, 240, 270, 92, 118 };
        private readonly ToolTip _originTip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 400, ReshowDelay = 100 };
        private string _tipText = "";
        private int _sortCol = -1;
        private bool _sortAsc = true;

        public List<WindowRow> Ordenar(IEnumerable<WindowRow> src)
        {
            List<WindowRow> list = new List<WindowRow>(src);
            list.Sort(Comparar);
            return list;
        }

        private int Comparar(WindowRow a, WindowRow b)
        {
            int c;
            if (_sortCol < 0)
                c = b.StartedAt.CompareTo(a.StartedAt);
            else
            {
                switch (_sortCol)
                {
                    case 0: c = string.Compare(a.AuthorName, b.AuthorName, StringComparison.CurrentCultureIgnoreCase); break;
                    case 1: c = string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase); break;
                    case 2: c = string.Compare(a.Origin, b.Origin, StringComparison.CurrentCultureIgnoreCase); break;
                    case 3: c = a.DuracaoSeg.CompareTo(b.DuracaoSeg); break;
                    case 4: c = StatusKey(a).CompareTo(StatusKey(b)); break;
                    default: c = 0; break;
                }
                if (!_sortAsc) c = -c;
            }
            if (c != 0) return c;
            return b.StartedAt.CompareTo(a.StartedAt);
        }

        private static int StatusKey(WindowRow r)
        {
            if (r.Closed) return 2;
            if (!r.Visible) return 1;
            return 0;
        }

        private int ColunaEm(int x)
        {
            int[] xs = Xs();
            for (int i = 0; i < Larg.Length; i++)
                if (x >= xs[i] && x < xs[i + 1]) return i;
            return -1;
        }

        public WindowTable()
        {
            SetColunas(Larg, 1, 28);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = e.Y < HeaderH && !Redimensionando ? Cursors.Hand : CursorColuna();
            int[] xs = Xs();
            string text = _hover >= 0 && _hover < Rows.Count && e.X >= xs[2] && e.X < xs[3]
                ? Rows[_hover].OriginTip : "";
            if (text != _tipText) { _tipText = text; _originTip.SetToolTip(this, text); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Cursor = Cursors.Default;
            _tipText = ""; _originTip.SetToolTip(this, "");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _originTip.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Redimensionando) return;
            if (e.Button == MouseButtons.Left && e.Y < HeaderH)
            {
                int col = ColunaEm(e.X);
                if (col < 0) return;
                if (_sortCol == col) _sortAsc = !_sortAsc;
                else { _sortCol = col; _sortAsc = true; }
                Rows = Ordenar(Rows);
                Invalidate();
                return;
            }
            if (e.Button != MouseButtons.Right) return;
            int idx = (e.Y - HeaderH) / RowHeight;
            bool onRow = e.Y >= HeaderH && idx >= 0 && _scroll + idx < Rows.Count;
            if (onRow && ContextRequested != null)
                ContextRequested(Rows[_scroll + idx].Hwnd, PointToScreen(e.Location));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int[] xs = Xs();
            string[] titulos = {
                Lang.T("v2ColProc"), Lang.T("v2ColPath"), Lang.T("v2ColOrigin"),
                Lang.T("v2ColDur"), Lang.T("v2ColStatus")
            };
            if (_sortCol >= 0 && _sortCol < titulos.Length)
                titulos[_sortCol] = titulos[_sortCol] + (_sortAsc ? " ▲" : " ▼");
            Cabecalho(g, titulos, xs);
            if (Rows.Count == 0) { Vazio(g, Lang.T("v2NoWindows")); return; }

            for (int i = 0; i < Visiveis && _scroll + i < Rows.Count; i++)
            {
                WindowRow r = Rows[_scroll + i];
                int y = HeaderH + i * RowHeight;
                if ((_scroll + i) == _hover)
                    using (SolidBrush b = new SolidBrush(Theme.Hover)) g.FillRectangle(b, 0, y, Width, RowHeight);
                if (i > 0) Separador(g, y);

                Color estado = r.Closed ? Theme.Gray : (r.Visible ? Theme.Teal : Theme.Amber);
                Theme.DrawDot(g, 15, y + RowHeight / 2, 6, estado);

                string nome = r.AuthorName.Length > 0 ? r.AuthorName : "?";
                string titulo = r.Title.Length > 0 ? r.Title : Lang.T("fNoTitle");
                TextRenderer.DrawText(g, nome, Theme.MonoBold, new Rectangle(xs[0], y, xs[1] - xs[0] - 8, RowHeight), Theme.Text, CellF);
                TextRenderer.DrawText(g, titulo, Theme.MonoSmall, new Rectangle(xs[1], y, xs[2] - xs[1] - 8, RowHeight), Theme.Text, CellF);
                TextRenderer.DrawText(g, r.Origin, Theme.MonoSmall, new Rectangle(xs[2], y, xs[3] - xs[2] - 8, RowHeight), Theme.Dim, CellF);
                TextRenderer.DrawText(g, r.DuracaoSeg.ToString("0.0") + "s", Theme.MonoSmall, new Rectangle(xs[3], y, xs[4] - xs[3] - 8, RowHeight), Theme.Dim, CellF);

                string st = r.Closed ? Lang.T("v2WinClosed") : (r.Visible ? Lang.T("v2WinVisible") : Lang.T("v2WinHidden"));
                int pw = Math.Max(74, TextRenderer.MeasureText(st.ToUpperInvariant(), Theme.MonoTiny).Width + 24);
                Theme.DrawPill(g, new Rectangle(xs[4], y + (RowHeight - 17) / 2, pw, 17), st, estado, !r.Closed);
            }
            Rolagem(g);
        }
    }
}
