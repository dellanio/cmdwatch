using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CmdWatch.Gui
{
    /// <summary>
    /// Aba "Processos CPU": lista de processos ordenada pelo consumo (a
    /// esquerda) e o detalhe do processo selecionado (a direita). Mesma
    /// linguagem visual do resto do painel: fundo do cartao, borda fina,
    /// verde-agua no destaque e pilulas de estado.
    /// </summary>
    internal sealed class CpuView : Control
    {
        public readonly CpuTable Tabela = new CpuTable();
        public readonly CpuDetail Detalhe = new CpuDetail();
        private bool _detalheLargo = true;   // 62% / 38%

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
            int wEsq = (int)((Width - div) * (_detalheLargo ? 0.62 : 1.0));
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
        }
    }

    /// <summary>
    /// Tabela de processos por consumo de CPU: PID, nome, CPU, memoria,
    /// threads, inicio e comando. Clique escolhe a linha (o painel de detalhe
    /// acompanha); clique no cabecalho ordena pela coluna.
    /// </summary>
    internal sealed class CpuTable : TabelaBase
    {
        public List<CpuRow> Rows = new List<CpuRow>();
        /// <summary>PID da linha escolhida (-1 = nenhuma).</summary>
        public int SelecionadoPid = -1;
        public event Action SelectionChanged;

        /// <summary>Filtro de texto (o campo de busca do cabecalho do cartao).</summary>
        public string Filtro = "";

        private int _sortCol = 2;      // 0 = pid; 1 = nome; 2 = cpu; 3 = memoria;
        private bool _sortDesc = true; // 4 = threads; 5 = inicio; 6 = comando

        protected override int Count { get { return Rows.Count; } }

        // PID, processo, CPU, memoria, threads, inicio e comando (a ultima
        // coluna e a flexivel: fica com a sobra da largura).
        private static readonly int[] Larg = { 58, 168, 62, 74, 52, 68, 220 };

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int[] xs = Xs(Larg, 6, 26);
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
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(22, Theme.Teal)))
                        g.FillRectangle(b, 4, y + 1, Width - 8, RowHeight - 2);
                    Theme.DrawPanel(g, new Rectangle(4, y + 3, 3, RowHeight - 6), 1, Theme.Teal, Color.Empty);
                }
                else if ((_scroll + i) == _hover)
                    using (SolidBrush b = new SolidBrush(Theme.Hover)) g.FillRectangle(b, 4, y + 1, Width - 8, RowHeight - 2);
                if (i > 0 && !sel) Separador(g, y);

                // barra de CPU: o quanto o processo consome da maquina
                int barraW = Math.Max(0, Math.Min(xs[3] - xs[2] - 8, (int)(r.Cpu * 1.2)));
                if (barraW > 2)
                {
                    Color cor = r.Cpu >= 25 ? Theme.Red : (r.Cpu >= 10 ? Theme.Amber : Theme.Teal);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(120, cor)))
                        g.FillRectangle(b, xs[2], y + RowHeight - 5, barraW, 3);
                }

                TextRenderer.DrawText(g, r.Pid.ToString(), Theme.MonoSmall,
                    new Rectangle(xs[0], y, xs[1] - xs[0] - 8, RowHeight), Theme.Dim, CellF);
                TextRenderer.DrawText(g, r.Name, Theme.UiBold,
                    new Rectangle(xs[1], y, xs[2] - xs[1] - 8, RowHeight), Theme.Text, CellF);
                TextRenderer.DrawText(g, r.Cpu.ToString("0.0") + "%", Theme.MonoSmall,
                    new Rectangle(xs[2], y, xs[3] - xs[2] - 8, RowHeight),
                    r.Cpu >= 10 ? Theme.Amber : Theme.Text, CellF);
                TextRenderer.DrawText(g, HumanFormat.Memoria(r.MemBytes), Theme.MonoSmall,
                    new Rectangle(xs[3], y, xs[4] - xs[3] - 8, RowHeight), Theme.Dim, CellF);
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
            Cursor = _hover >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            // cabecalho: ordena
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
            int[] xs = Xs(Larg, 6, 26);
            for (int i = 0; i < Larg.Length; i++)
                if (x >= xs[i] && x < xs[i + 1]) return i;
            return -1;
        }

        /// <summary>Ordena pela coluna ativa (CPU e memoria: do maior para o menor).</summary>
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
                // _sortDesc = true significa "maior primeiro"
                if (_sortDesc) r = -r;
                if (r != 0) return r;
                return _sortDesc ? b.Cpu.CompareTo(a.Cpu) : a.Cpu.CompareTo(b.Cpu);
            });
            return l;
        }

        /// <summary>Escolhe a linha (pid &lt;= 0 = limpa a selecao).</summary>
        public void Selecionar(int pid)
        {
            if (pid <= 0) pid = -1;   // o PID 0 e o processo ocioso: nao e escolha
            if (SelecionadoPid == pid) return;
            SelecionadoPid = pid;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged();
        }

        /// <summary>Filtra por texto (pid, nome ou comando).</summary>
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

    /// <summary>
    /// Detalhe do processo escolhido na lista: identidade, comando, inicio,
    /// consumo, cadeia de ancestrais e a pasta de trabalho. Mesmo desenho do
    /// fluxo de comandos do painel (rotulo a esquerda, valor a direita).
    /// </summary>
    internal sealed class CpuDetail : Control
    {
        private CpuRow _sel;
        private readonly List<CpuRow> _cadeia = new List<CpuRow>();
        private int _scroll;

        public CpuDetail()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
        }

        /// <summary>Escolhe o processo e monta a cadeia de ancestrais.</summary>
        public void Selecionar(CpuRow r)
        {
            _sel = r;
            _cadeia.Clear();
            _scroll = 0;
            Invalidate();
        }

        /// <summary>
        /// Monta a cadeia de ancestrais (pai, avo...) a partir do retrato:
        /// o PID do pai ja vem no buffer do kernel, entao a arvore sai de
        /// graca.
        /// </summary>
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
            _scroll -= e.Delta / 120;
            if (_scroll < 0) _scroll = 0;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Panel);
            if (_sel == null)
            {
                TextRenderer.DrawText(g, Lang.T("v2CpuSemSelecao"), Theme.Ui,
                    new Rectangle(0, 0, Width, Math.Max(30, Height)), Theme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }

            const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            int x = 14;
            int y = 12 - _scroll;          // o conteudo rola dentro do painel
            int w = Width - 28;

            // cabecalho: PID + nome, e a pilula de "elevado" quando for o caso
            TextRenderer.DrawText(g, Lang.T("v2CpuPid") + " " + _sel.Pid, Theme.UiTitle,
                new Rectangle(x, y, w - 90, 20), Theme.Text, L | TextFormatFlags.VerticalCenter);
            if (_sel.Elevado)
            {
                string av = Lang.T("v2CpuElevado");
                int pw = TextRenderer.MeasureText(av, Theme.UiSmall).Width + 18;
                Theme.DrawPill(g, new Rectangle(Width - 14 - pw, y + 1, pw, 18), av, Theme.Amber, false);
            }
            y += 24;

            TextRenderer.DrawText(g, _sel.Name, Theme.UiBold, new Rectangle(x, y, w, 18), Theme.Teal, L);
            y += 22;

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

            // comando: fonte monoespacada, quebrando em varias linhas
            y += 4;
            TextRenderer.DrawText(g, Lang.T("v2CpuComando"), Theme.UiSmall, new Rectangle(x, y, w, 14), Theme.Dim, L);
            y += 16;
            y = TextoLongo(g, _sel.CmdLine, x, y, w, Theme.MonoSmall, Theme.Text);

            // cadeia de ancestrais
            if (_cadeia.Count > 0)
            {
                y += 8;
                TextRenderer.DrawText(g, Lang.T("v2CpuCadeia"), Theme.UiSmall, new Rectangle(x, y, w, 14), Theme.Dim, L);
                y += 16;
                for (int i = 0; i < _cadeia.Count; i++)
                {
                    CpuRow a = _cadeia[i];
                    int recuo = 12 + i * 10;
                    if (recuo > w - 60) break;
                    using (Pen p = new Pen(Theme.Border))
                        g.DrawLine(p, recuo - 6, y + RowHeight / 2, recuo, y + RowHeight / 2);
                    Theme.DrawDot(g, recuo, y + RowHeight / 2, 5, Theme.Teal);
                    TextRenderer.DrawText(g, a.Name, Theme.Ui,
                        new Rectangle(recuo + 8, y, w - recuo - 8, RowHeight), Theme.Text, L | TextFormatFlags.VerticalCenter);
                    TextRenderer.DrawText(g, a.Pid.ToString(), Theme.MonoSmall,
                        new Rectangle(recuo + 8, y, w - recuo - 8, RowHeight), Theme.Dim,
                        L | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                    y += RowHeight;
                }
            }

            // o conteudo pode passar da altura: mostra a barra de rolagem
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

        private const int RowHeight = 18;

        /// <summary>Uma linha "rotulo : valor" e devolve o Y da proxima.</summary>
        private int Campo(Graphics g, string rotulo, string valor, int x, int y, int w, TextFormatFlags L)
        {
            TextRenderer.DrawText(g, rotulo, Theme.UiSmall, new Rectangle(x, y, 118, 18), Theme.Dim, L | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, valor, Theme.Ui, new Rectangle(x + 124, y, w - 124, 18), Theme.Text,
                L | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return y + RowHeight;
        }

        /// <summary>Texto que ocupa varias linhas (o comando pode ser longo).</summary>
        private int TextoLongo(Graphics g, string texto, int x, int y, int w, Font f, Color cor)
        {
            if (string.IsNullOrEmpty(texto)) return y;
            int max = Math.Max(1, (w - 4) / Math.Max(6, (int)(f.SizeInPoints * 1.6)));
            string[] partes = texto.Length <= max ? new[] { texto }
                : Divide(texto, max);
            foreach (string p in partes)
            {
                TextRenderer.DrawText(g, p, f, new Rectangle(x, y, w, f.Height + 2), cor,
                    TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                y += f.Height + 2;
                if (y + _scroll > Height - 20) break;
            }
            return y;
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
