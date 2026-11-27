using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CmdWatch.Gui
{
    /// <summary>
    /// Aba "Processos CPU": lista de processos ordenada pelo consumo (a
    /// esquerda) e o detalhe do processo selecionado (a direita) com cantos
    /// retos, barra de consumo amarelo acido e arvore de ancestrais.
    /// </summary>
    internal sealed class CpuView : Control
    {
        public readonly CpuTable Tabela = new CpuTable();
        public readonly CpuDetail Detalhe = new CpuDetail();
        private int _larguraEsq = -1;   // -1 = 62% automatico

        public int LarguraEsq
        {
            get { return _larguraEsq < 0 ? (int)((Width - 1) * 0.62) : _larguraEsq; }
            set
            {
                int min = 220;
                int max = Math.Max(min, Width - 180);
                if (value < min) value = min;
                if (value > max) value = max;
                _larguraEsq = value;
                PerformLayout();
                Invalidate();
            }
        }

        public CpuView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
            Controls.Add(Tabela);
            Controls.Add(Detalhe);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int div = 1;
            int wEsq = LarguraEsq;
            if (wEsq < 120) wEsq = 120;
            Tabela.SetBounds(0, 0, wEsq, Height);
            Detalhe.SetBounds(wEsq + div, 0, Math.Max(80, Width - wEsq - div), Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Panel);
            int div = Tabela.Width;
            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, div, 0, div, Height);
            if (_sobreDivisor)
            {
                using (Pen p = new Pen(Theme.Teal))
                {
                    g.DrawLine(p, div - 1, 0, div - 1, Height);
                    for (int i = 0; i < 3; i++)
                    {
                        int y = Height / 2 - 8 + i * 8;
                        g.DrawLine(p, div - 3, y, div + 1, y);
                    }
                }
            }
        }

        private bool _sobreDivisor;
        private bool _arrastandoDivisor;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_arrastandoDivisor)
            {
                LarguraEsq = e.X - 1;
                return;
            }
            bool sobre = Math.Abs(e.X - Tabela.Width) <= 4;
            if (sobre != _sobreDivisor) { _sobreDivisor = sobre; Invalidate(); }
            Cursor = sobre ? Cursors.VSplit : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_sobreDivisor) { _sobreDivisor = false; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (Math.Abs(e.X - Tabela.Width) > 4) return;
            _arrastandoDivisor = true;
            Capture = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_arrastandoDivisor)
            {
                _arrastandoDivisor = false;
                Capture = false;
                Invalidate();
            }
        }
    }

    internal sealed class CpuTable : TabelaBase
    {
        public List<CpuRow> Rows = new List<CpuRow>();
        public int SelecionadoPid = -1;
        public event Action SelectionChanged;
        public string Filtro = "";

        private int _sortCol = 2;
        private bool _sortDesc = true;

        protected override int Count { get { return Rows.Count; } }

        private static readonly int[] Larg = { 62, 168, 72, 78, 56, 70, 220 };

        public CpuTable()
        {
            SetColunas(Larg, 6, 16);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int[] xs = Xs();
            string[] titulos = {
                Lang.T("v2CpuColPid"), Lang.T("v2CpuColName"), Lang.T("v2CpuColCpu"),
                Lang.T("v2CpuColMem"), Lang.T("v2CpuColTh"), Lang.T("v2CpuColStart"),
                Lang.T("v2CpuColCmd") };
            if (_sortCol >= 0 && _sortCol < titulos.Length) titulos[_sortCol] += _sortDesc ? " ▼" : " ▲";
            Cabecalho(g, titulos, xs);
            if (Rows.Count == 0) { Vazio(g, Lang.T("v2CpuVazio")); return; }

            for (int i = 0; i < Visiveis && _scroll + i < Rows.Count; i++)
            {
                CpuRow r = Rows[_scroll + i];
                int y = HeaderH + i * RowHeight;
                bool sel = r.Pid == SelecionadoPid;
                if (sel)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(20, Theme.Teal)))
                        g.FillRectangle(b, 0, y, Width, RowHeight);
                    using (SolidBrush acc = new SolidBrush(Theme.Teal))
                        g.FillRectangle(acc, 0, y, 3, RowHeight);
                }
                else if ((_scroll + i) == _hover)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Hover))
                        g.FillRectangle(b, 0, y, Width, RowHeight);
                }
                if (i > 0) Separador(g, y);

                // Barra de CPU neo-brutalista na celula de CPU
                int maxBarW = Math.Max(12, xs[3] - xs[2] - 10);
                int fillW = Math.Max(0, Math.Min(maxBarW, (int)(maxBarW * Math.Min(100.0, r.Cpu * 2.5) / 100.0)));
                Color corCpu = r.Cpu >= 20 ? Theme.Red : (r.Cpu >= 8 ? Theme.Teal : Theme.Dim);
                using (SolidBrush tr = new SolidBrush(Theme.PanelDeep))
                    g.FillRectangle(tr, xs[2], y + RowHeight - 5, maxBarW, 2);
                if (fillW > 0)
                {
                    using (SolidBrush fb = new SolidBrush(corCpu))
                        g.FillRectangle(fb, xs[2], y + RowHeight - 5, fillW, 2);
                }

                TextRenderer.DrawText(g, r.Pid.ToString(), Theme.MonoSmall,
                    new Rectangle(xs[0], y, xs[1] - xs[0] - 8, RowHeight), Theme.Dim, CellF);
                TextRenderer.DrawText(g, r.Name, Theme.MonoBold,
                    new Rectangle(xs[1], y, xs[2] - xs[1] - 8, RowHeight), sel ? Theme.Teal : Theme.Text, CellF);
                TextRenderer.DrawText(g, r.Cpu.ToString("0.0") + "%", Theme.MonoBold,
                    new Rectangle(xs[2], y - 1, xs[3] - xs[2] - 8, RowHeight - 2),
                    r.Cpu >= 20 ? Theme.Red : (r.Cpu >= 8 ? Theme.Teal : Theme.Text), CellF);
                TextRenderer.DrawText(g, HumanFormat.Memoria(r.MemBytes), Theme.MonoSmall,
                    new Rectangle(xs[3], y, xs[4] - xs[3] - 8, RowHeight), Theme.Text, CellF);
                TextRenderer.DrawText(g, r.Threads.ToString(), Theme.MonoSmall,
                    new Rectangle(xs[4], y, xs[5] - xs[4] - 8, RowHeight), Theme.Dim, CellF);
                TextRenderer.DrawText(g, r.StartedAt == DateTime.MinValue ? "—" : r.StartedAt.ToString("HH:mm:ss"),
                    Theme.MonoSmall, new Rectangle(xs[5], y, xs[6] - xs[5] - 8, RowHeight), Theme.Dim, CellF);
                TextRenderer.DrawText(g, r.CmdLine, Theme.MonoSmall,
                    new Rectangle(xs[6], y, Math.Max(10, xs[7] - xs[6] - 8), RowHeight), Theme.Dim, CellF);
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
            if (e.Button != MouseButtons.Left) return;

            if (e.Y < HeaderH)
            {
                int col = ColunaEm(e.X);
                if (col >= 0)
                {
                    if (_sortCol == col) _sortDesc = !_sortDesc;
                    else { _sortCol = col; _sortDesc = col == 2 || col == 3; }
                    Rows = Ordenar(Rows);
                    Invalidate();
                }
                return;
            }

            int idx = (e.Y - HeaderH) / RowHeight;
            if (e.Y < HeaderH || idx < 0 || _scroll + idx >= Rows.Count) return;
            int pid = Rows[_scroll + idx].Pid;
            Selecionar(pid == SelecionadoPid ? 0 : pid);
        }

        private int ColunaEm(int x)
        {
            int[] xs = Xs();
            for (int i = 0; i < Larg.Length; i++)
                if (x >= xs[i] && x < xs[i + 1]) return i;
            return -1;
        }

        public List<CpuRow> Ordenar(List<CpuRow> src)
        {
            List<CpuRow> l = new List<CpuRow>(src);
            l.Sort(delegate (CpuRow a, CpuRow b)
            {
                int r = 0;
                if (_sortCol == 1) r = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                else if (_sortCol == 2) r = a.Cpu.CompareTo(b.Cpu);
                else if (_sortCol == 3) r = a.MemBytes.CompareTo(b.MemBytes);
                else if (_sortCol == 4) r = a.Threads.CompareTo(b.Threads);
                else if (_sortCol == 5) r = a.StartedAt.CompareTo(b.StartedAt);
                else if (_sortCol == 6) r = string.Compare(a.CmdLine, b.CmdLine, StringComparison.OrdinalIgnoreCase);
                else r = a.Pid.CompareTo(b.Pid);
                if (_sortDesc) r = -r;
                if (r != 0) return r;
                return _sortDesc ? b.Cpu.CompareTo(a.Cpu) : a.Cpu.CompareTo(b.Cpu);
            });
            return l;
        }

        public void Selecionar(int pid)
        {
            if (pid <= 0) pid = -1;
            if (SelecionadoPid == pid) return;
            SelecionadoPid = pid;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged();
        }

        public void Filtrar(List<CpuRow> todos, string texto)
        {
            Filtro = texto ?? "";
            if (Filtro.Length == 0) { Rows = Ordenar(todos); Invalidate(); return; }
            string f = Filtro.ToLowerInvariant();
            var kept = new List<CpuRow>();
            foreach (CpuRow r in todos)
            {
                if (r.Pid.ToString().Contains(f) ||
                    (r.Name ?? "").ToLowerInvariant().Contains(f) ||
                    (r.CmdLine ?? "").ToLowerInvariant().Contains(f))
                    kept.Add(r);
            }
            Rows = Ordenar(kept);
            Invalidate();
        }
    }

    internal sealed class CpuDetail : Control
    {
        private CpuRow _sel;
        private readonly List<CpuRow> _cadeia = new List<CpuRow>();
        private int _scroll;

        public CpuDetail()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.PanelDeep;
        }

        public void Selecionar(CpuRow r)
        {
            _sel = r;
            _cadeia.Clear();
            _scroll = 0;
            Invalidate();
        }

        public void Selecionar(CpuRow r, List<CpuRow> todos)
        {
            _sel = r;
            _cadeia.Clear();
            _scroll = 0;
            if (r != null && todos != null)
            {
                var porPid = new Dictionary<int, CpuRow>();
                foreach (CpuRow x in todos) porPid[x.Pid] = x;
                int pid = r.ParentPid;
                int guarda = 0;
                while (pid > 0 && guarda++ < 16)
                {
                    CpuRow p;
                    if (!porPid.TryGetValue(pid, out p)) break;
                    _cadeia.Add(p);
                    pid = p.ParentPid;
                }
            }
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _scroll -= e.Delta / 120 * 18;
            if (_scroll < 0) _scroll = 0;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.PanelDeep);
            if (_sel == null)
            {
                TextRenderer.DrawText(g, Lang.T("v2CpuSemSelecao").ToUpperInvariant(), Theme.MonoSmall,
                    new Rectangle(0, 0, Width, Math.Max(30, Height)), Theme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }

            const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            int x = 14;
            int y = 12 - _scroll;
            int w = Math.Max(80, Width - 28);

            // Cabecalho do inspetor: quadrado acido + nome + badge PID / elevado
            Theme.DrawDot(g, x + 3, y + 9, 6, Theme.Teal);
            TextRenderer.DrawText(g, _sel.Name, Theme.MonoBold,
                new Rectangle(x + 12, y, w - 120, 18), Theme.Teal, L | TextFormatFlags.VerticalCenter);

            string pidTxt = "PID " + _sel.Pid;
            int pidW = TextRenderer.MeasureText(pidTxt, Theme.MonoTiny).Width + 14;
            Rectangle pidBox = new Rectangle(Width - 14 - pidW, y, pidW, 18);
            Theme.DrawPanel(g, pidBox, 0, Theme.Surface, Theme.Border);
            TextRenderer.DrawText(g, pidTxt, Theme.MonoTiny, pidBox, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            if (_sel.Elevado)
            {
                string av = Lang.T("v2CpuElevado");
                int pw = TextRenderer.MeasureText(av.ToUpperInvariant(), Theme.MonoTiny).Width + 24;
                Theme.DrawPill(g, new Rectangle(pidBox.Left - 6 - pw, y, pw, 18), av, Theme.Amber, true);
            }
            y += 24;

            using (Pen sep = new Pen(Theme.Border))
                g.DrawLine(sep, x, y, x + w, y);
            y += 8;

            y = Campo(g, Lang.T("v2CpuUsuario"), string.IsNullOrEmpty(_sel.User) ? "—" : _sel.User, x, y, w, L);
            y = Campo(g, Lang.T("v2CpuSessao"), _sel.SessionId.ToString(), x, y, w, L);
            y = Campo(g, Lang.T("v2CpuInicio"),
                _sel.StartedAt == DateTime.MinValue ? "—" : _sel.StartedAt.ToString("dd/MM/yyyy HH:mm:ss") +
                    "  (" + HumanFormat.Duracao(DateTime.Now - _sel.StartedAt) + ")", x, y, w, L);
            y = Campo(g, Lang.T("v2CpuCpu"), _sel.Cpu.ToString("0.0") + "%   ·   " +
                Lang.T("v2CpuMedia") + " " + _sel.CpuMedia.ToString("0.0") + "%", x, y, w, L);
            y = Campo(g, Lang.T("v2CpuMemoria"), HumanFormat.Memoria(_sel.MemBytes) +
                "   ·   " + Lang.T("v2CpuThreads") + " " + _sel.Threads.ToString(), x, y, w, L);
            if (!string.IsNullOrEmpty(_sel.Path)) y = Campo(g, Lang.T("v2CpuCaminho"), _sel.Path, x, y, w, L);

            y += 6;
            TextRenderer.DrawText(g, Lang.T("v2CpuComando").ToUpperInvariant(), Theme.MonoTiny, new Rectangle(x, y, w, 14), Theme.Dim, L);
            y += 16;
            y = TextoLongo(g, _sel.CmdLine, x, y, w, Theme.MonoSmall, Theme.Text);

            if (_cadeia.Count > 0)
            {
                y += 8;
                TextRenderer.DrawText(g, Lang.T("v2CpuCadeia").ToUpperInvariant(), Theme.MonoTiny, new Rectangle(x, y, w, 14), Theme.Dim, L);
                y += 16;
                for (int i = 0; i < _cadeia.Count; i++)
                {
                    CpuRow a = _cadeia[i];
                    int recuo = x + 6 + i * 10;
                    if (recuo > x + w - 60) break;
                    using (Pen p = new Pen(Theme.Border))
                        g.DrawLine(p, recuo - 6, y + RowHeight / 2, recuo, y + RowHeight / 2);
                    Theme.DrawDot(g, recuo, y + RowHeight / 2, 5, Theme.Teal);
                    TextRenderer.DrawText(g, a.Name, Theme.MonoSmall,
                        new Rectangle(recuo + 8, y, x + w - recuo - 8, RowHeight), Theme.Text, L | TextFormatFlags.VerticalCenter);
                    TextRenderer.DrawText(g, "PID " + a.Pid, Theme.MonoTiny,
                        new Rectangle(recuo + 8, y, x + w - recuo - 8, RowHeight), Theme.Dim,
                        L | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                    y += RowHeight;
                }
            }

            int conteudo = y + _scroll - 12;
            int max = conteudo - Height + 6;
            if (max > 0)
            {
                if (_scroll > max) { _scroll = max; Invalidate(); }
                int trackH = Height - 8;
                int barH = Math.Max(24, trackH * Height / conteudo);
                int barY = 4 + (trackH - barH) * _scroll / Math.Max(1, max);
                Theme.DrawScrollThumb(g, new Rectangle(Width - 6, barY, 4, barH), ScrollTheme.Thumb);
            }
            else if (_scroll > 0) { _scroll = 0; Invalidate(); }
        }

        private const int RowHeight = 19;

        private int Campo(Graphics g, string rotulo, string valor, int x, int y, int w, TextFormatFlags L)
        {
            TextRenderer.DrawText(g, rotulo.ToUpperInvariant(), Theme.MonoTiny, new Rectangle(x, y, 120, RowHeight), Theme.Dim, L | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, valor, Theme.MonoSmall, new Rectangle(x + 124, y, Math.Max(20, w - 124), RowHeight), Theme.Text,
                L | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return y + RowHeight;
        }

        private int TextoLongo(Graphics g, string texto, int x, int y, int w, Font f, Color cor)
        {
            if (string.IsNullOrEmpty(texto)) return y;
            int max = Math.Max(1, (w - 16) / Math.Max(6, (int)(f.SizeInPoints * 1.45)));
            string[] partes = texto.Length <= max ? new[] { texto } : Divide(texto, max);
            int boxH = partes.Length * (f.Height + 2) + 8;
            Theme.DrawPanel(g, new Rectangle(x, y, w - 1, boxH), 0, Theme.Surface, Theme.Border);
            int ty = y + 4;
            foreach (string p in partes)
            {
                TextRenderer.DrawText(g, p, f, new Rectangle(x + 6, ty, w - 12, f.Height + 2), cor,
                    TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                ty += f.Height + 2;
            }
            return y + boxH + 2;
        }

        private static string[] Divide(string s, int max)
        {
            var res = new List<string>();
            int i = 0;
            while (i < s.Length && res.Count < 12)
            {
                int n = Math.Min(max, s.Length - i);
                res.Add(s.Substring(i, n));
                i += n;
            }
            return res.ToArray();
        }
    }
}
