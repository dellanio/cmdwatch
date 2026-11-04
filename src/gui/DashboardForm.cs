using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace CmdWatch.Gui
{
    /// <summary>
    /// Painel visual do cmdwatch. Barra superior (nome, estado, busca, pausar,
    /// configuracoes), barra lateral de icones, faixa de estatisticas, tabelas
    /// de sessoes e de janelas, fluxo de comandos e grafico de atividade.
    /// Clicar numa sessao filtra o fluxo; clicar de novo volta a mostrar todas.
    /// O idioma da interface (PT-BR / EN) e trocavel no rodape.
    /// </summary>
    internal sealed class DashboardForm : Form
    {
        private readonly Options _opts;
        private readonly Watcher _watcher = new Watcher();
        private IEngine _engine;
        private StreamWriter _log;
        private string _engineName = "polling";
        private bool _paused;
        private bool _inTray;
        private readonly DateTime _inicio = DateTime.Now;

        // contadores
        private int _totalSessions, _totalSteps, _totalCalls;
        private int _evAcc, _openAcc, _callAcc;
        private readonly List<SessionRow> _rows = new List<SessionRow>();
        private readonly Dictionary<int, SessionRow> _rowByPid = new Dictionary<int, SessionRow>();
        private readonly Dictionary<IntPtr, WindowRow> _winByHwnd = new Dictionary<IntPtr, WindowRow>();
        private readonly List<IntPtr> _winOrder = new List<IntPtr>();
        private readonly Queue<double> _eventsPerSec = new Queue<double>();
        private readonly Queue<double> _opensPerSec = new Queue<double>();
        private readonly List<StreamEntry> _entries = new List<StreamEntry>();
        private int _selectedPid;

        // observador de janelas
        private WindowWatcher _janelas;
        private int _totalWindows;

        // controles
        private NeonPanel _pSessions, _pStream, _pActivity, _pWindows;
        private SessionTable _sessions;
        private NeonFlow _stream;
        private LineChart _activity;
        private WindowTable _windows;
        private NeonCheckBox _checkTray;
        private StatStrip _stats;
        private SideBar _side;
        private SearchBox _busca, _buscaFluxo;
        private IconButton _btnPausa, _btnFiltro, _btnSessao, _btnCopiar, _btnLimpar, _btnFaixa, _btnIdioma;
        private IconButton _navHome, _navLog, _navGrafico, _navConfig;
        private NotifyIcon _tray;
        private ToolStripMenuItem _miRestore, _miOpenLog, _miQuit;
        private readonly ContextMenuStrip _ctxMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip _dropMenu = new ContextMenuStrip();
        private System.Windows.Forms.Timer _tick, _slow;

        // filtro da tabela de sessoes: 0 todas, 1 em execucao, 2 encerradas
        private int _filtroSessoes;

        private const int TopH = 52;       // barra superior
        private const int SideW = 52;      // barra lateral
        private const int FootH = 38;      // rodape
        private const int Pad = 14;
        private const int Gap = 10;

        // aviso temporario no rodape (ex.: "script encerrado")
        private string _notice = "";
        private DateTime _noticeUntil = DateTime.MinValue;
        private Color _noticeColor = Theme.Text;

        public DashboardForm(Options opts)
        {
            _opts = opts;
            Text = Lang.T("v2Title");
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1000, 620);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            KeyPreview = true;

            try { Icon = AppIcon.Get(32); } catch { }

            BuildUi();
            ClientSize = new Size(1440, 880);

            _tick = new System.Windows.Forms.Timer();
            _tick.Interval = 1000;
            _tick.Tick += OnTick;
            _tick.Start();

            _slow = new System.Windows.Forms.Timer();
            _slow.Interval = 1000;
            _slow.Tick += OnSlowTick;
            _slow.Start();
        }

        // ------------------------------------------------------------ UI
        private IconButton Botao(Control pai, string glyph, string label, bool chevron, EventHandler click)
        {
            IconButton b = new IconButton { Glyph = glyph, Label = label, Chevron = chevron };
            b.BackColor = pai is NeonPanel ? Theme.Panel : Theme.Bg;
            if (click != null) b.Click += click;
            pai.Controls.Add(b);
            return b;
        }

        private void BuildUi()
        {
            _pSessions = new NeonPanel();
            _pStream = new NeonPanel();
            _pActivity = new NeonPanel();
            _pWindows = new NeonPanel();

            _sessions = new SessionTable();
            _sessions.Name = _sessions.Text = "tabelaSessoes";   // nome estavel para automacao de UI (tools\test-painel.ps1)
            _sessions.SelectionChanged += OnSelectionChanged;
            _sessions.ContextRequested += OnSessionContext;
            _pSessions.Controls.Add(_sessions);

            _stream = new NeonFlow();
            _pStream.Controls.Add(_stream);

            _activity = new LineChart { Capacity = 60 };
            _pActivity.Controls.Add(_activity);

            _windows = new WindowTable();
            _windows.Name = _windows.Text = "tabelaJanelas";
            _pWindows.Controls.Add(_windows);

            _stats = new StatStrip();

            // barra lateral: cada icone faz algo real (ver dicas)
            _side = new SideBar();
            _navHome = _side.Add("\uE80F");
            _navHome.Active = true;
            _navHome.Click += delegate { VisaoGeral(); };
            _navLog = _side.Add("\uE756");
            _navLog.Click += delegate { OpenLogFile(); };
            _navGrafico = _side.Add("\uE9D2");
            _navGrafico.Click += delegate { MenuFaixa(_navGrafico); };
            _navConfig = _side.Add("\uE713");
            _navConfig.Click += delegate { MostrarConfig(); };

            // barra superior
            _busca = new SearchBox { Name = "busca" };
            _busca.Box.TextChanged += delegate { OnSlowTick(null, EventArgs.Empty); };
            _btnPausa = Botao(this, "\uE769", "", false, delegate { SetPaused(!_paused); });

            // cabecalhos dos cartoes
            _btnFiltro = Botao(_pSessions, "\uE71C", "", true, delegate { MenuFiltro(); });
            _btnSessao = Botao(_pStream, "\uE756", "", true, delegate { MenuSessao(); });
            _buscaFluxo = new SearchBox { BackColor = Theme.Panel };
            _buscaFluxo.Box.TextChanged += delegate { RebuildStream(); };
            _pStream.Controls.Add(_buscaFluxo);
            _btnCopiar = Botao(_pStream, "\uE8C8", "", false, delegate { CopiarFluxo(); });
            _btnLimpar = Botao(_pStream, "\uE74D", "", false, delegate { LimparFluxo(); });
            _btnFaixa = Botao(_pActivity, "", "", true, delegate { MenuFaixa(_btnFaixa); });

            // rodape
            _checkTray = new NeonCheckBox();
            _checkTray.CheckedChanged += OnTrayCheckChanged;
            _btnIdioma = Botao(this, "", "", true, delegate { MenuIdioma(); });

            // menus (contexto da sessao e listas suspensas)
            foreach (ContextMenuStrip m in new[] { _ctxMenu, _dropMenu })
            {
                m.Renderer = new NeonMenuRenderer();
                m.ShowImageMargin = false;
                m.ShowCheckMargin = false;
                m.Font = Theme.Ui;
            }

            Controls.Add(_side);
            Controls.Add(_stats);
            Controls.Add(_pSessions);
            Controls.Add(_pWindows);
            Controls.Add(_pStream);
            Controls.Add(_pActivity);
            Controls.Add(_checkTray);
            Controls.Add(_busca);

            ApplyTexts();
            LayoutDashboard();
        }

        /// <summary>Textos de todos os controles no idioma corrente.</summary>
        private void ApplyTexts()
        {
            Text = Lang.T("v2Title");
            _pSessions.Title = Lang.T("v2Sessions");
            _pWindows.Title = Lang.T("v2Windows");
            _pStream.Title = Lang.T("v2Flow");
            _pActivity.Title = Lang.T("v2Activity");
            _activity.LabelA = Lang.T("v2LegCmd");
            _activity.LabelB = Lang.T("v2LegSess");
            _stats.Labels = new[] { Lang.T("v2StatSess"), Lang.T("v2StatWin"), Lang.T("v2StatCmd"), Lang.T("v2StatTime") };
            _busca.Cue = Lang.T("v2Search");
            _buscaFluxo.Cue = Lang.T("v2FlowSearch");
            _btnPausa.Glyph = _paused ? "\uE768" : "\uE769";
            _btnPausa.Label = _paused ? Lang.T("v2Resume") : Lang.T("v2Pause");
            _btnFiltro.Label = Lang.T(_filtroSessoes == 1 ? "v2FilterRun" : _filtroSessoes == 2 ? "v2FilterEnd" : "v2FilterAll");
            _btnSessao.Label = Lang.F("v2FlowSess", NomeSessao(_selectedPid));
            _btnCopiar.Tip = Lang.T("v2Copy");
            _btnLimpar.Tip = Lang.T("v2Clear");
            _btnFaixa.Label = Lang.F("v2Range", _activity.Capacity);
            _checkTray.Label = Lang.T("v2Tray");
            _btnIdioma.Label = Lang.Atual == Idioma.En ? "EN" : "PT-BR";
            _navHome.Tip = Lang.T("v2NavHome");
            _navLog.Tip = Lang.T("v2NavLog");
            _navGrafico.Tip = Lang.T("v2NavChart");
            _navConfig.Tip = Lang.T("v2Settings");
        }

        private void LayoutDashboard()
        {
            if (_pSessions == null || _btnIdioma == null) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w < 200 || h < 200) return;

            // barra superior: busca, pausar e engrenagem a direita
            int bh = 30, by = (TopH - bh) / 2;
            int pw = _btnPausa.PreferredW() + 6;
            _btnPausa.SetBounds(w - Pad - pw, by, pw, bh);   // configuracoes ficam so na barra lateral
            int sw = Math.Min(260, Math.Max(160, w / 6));
            _busca.SetBounds(_btnPausa.Left - 14 - sw, by, sw, bh);

            _side.SetBounds(0, TopH, SideW, h - TopH - FootH);

            int x0 = SideW + Pad, cw = w - x0 - Pad;
            int y = TopH + 62;                       // titulo da pagina + subtitulo
            bool compacto = h < 760;                 // janela baixa: cabecalhos e minimos menores
            int cab = compacto ? 34 : 40;
            foreach (NeonPanel np in new[] { _pSessions, _pWindows, _pStream, _pActivity }) np.HeaderHeight = cab;
            int statH = compacto ? 52 : 62;
            _stats.SetBounds(x0, y, cw, statH);
            y += statH + Gap;

            int resto = h - FootH - Gap - y;
            int minT = cab + 26 + 2 * 24 + 6;        // cabecalho + colunas + 2 linhas
            int sessH = Math.Max(minT, (int)(resto * 0.25));
            int winH = Math.Max(minT, (int)(resto * 0.30));
            int midH = Math.Max(130, resto - sessH - winH - Gap * 2);
            _pSessions.SetBounds(x0, y, cw, sessH);
            y += sessH + Gap;
            _pWindows.SetBounds(x0, y, cw, winH);
            y += winH + Gap;
            int flowW = (int)((cw - Gap) * 0.64);
            _pStream.SetBounds(x0, y, flowW, midH);
            _pActivity.SetBounds(x0 + flowW + Gap, y, cw - flowW - Gap, midH);

            // controles dos cabecalhos dos cartoes (alinhados a direita)
            int hb = 26, hy = (_pSessions.HeaderHeight - hb) / 2;
            int fw = Math.Max(140, _btnFiltro.PreferredW());
            _btnFiltro.SetBounds(_pSessions.Width - 12 - fw, hy, fw, hb);
            int fx = _pStream.Width - 12;
            _btnLimpar.SetBounds(fx - hb, hy, hb, hb); fx -= hb + 6;
            _btnCopiar.SetBounds(fx - hb, hy, hb, hb); fx -= hb + 10;
            int qw = Math.Min(170, Math.Max(90, _pStream.Width / 5));
            _buscaFluxo.SetBounds(fx - qw, hy, qw, hb); fx -= qw + 10;
            int ssw = Math.Min(220, Math.Max(120, _btnSessao.PreferredW()));
            ssw = Math.Min(ssw, fx - 170);
            _btnSessao.SetBounds(fx - ssw, hy, Math.Max(60, ssw), hb);
            int rw = _btnFaixa.PreferredW();
            _btnFaixa.SetBounds(_pActivity.Width - 12 - rw, hy, rw, hb);

            foreach (var par in new KeyValuePair<NeonPanel, Control>[] {
                new KeyValuePair<NeonPanel, Control>(_pSessions, _sessions),
                new KeyValuePair<NeonPanel, Control>(_pWindows, _windows) })
            {
                Rectangle a = par.Key.PlotArea;
                par.Value.SetBounds(a.X, a.Y, a.Width, a.Height);
            }
            Rectangle ta = _pStream.PlotArea;
            _stream.SetBounds(ta.X + 10, ta.Y, ta.Width - 16, ta.Height);
            Rectangle aa = _pActivity.PlotArea;
            _activity.SetBounds(aa.X + 6, aa.Y, aa.Width - 12, aa.Height);

            // rodape
            int fy = h - FootH;
            _checkTray.SetBounds(x0 + 150, fy + (FootH - 22) / 2, CheckboxWidth(), 22);
            int iw = 78;
            _btnIdioma.SetBounds(w - Pad - iw, fy + (FootH - 26) / 2, iw, 26);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutDashboard();
        }

        private int CheckboxWidth()
        {
            try { return 15 + 9 + TextRenderer.MeasureText(_checkTray.Label, Theme.Ui).Width + 10; }
            catch { return 260; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            int w = ClientSize.Width, h = ClientSize.Height;
            const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

            // barra superior: icone de terminal, nome, divisoria e o estado
            using (Pen p = new Pen(Theme.Border))
            {
                g.DrawLine(p, 0, TopH - 1, w, TopH - 1);
                g.DrawLine(p, 0, h - FootH, w, h - FootH);
            }
            Rectangle ic = new Rectangle(14, (TopH - 28) / 2, 32, 28);
            Theme.DrawPanel(g, ic, 6, Color.FromArgb(24, Theme.Teal), Color.FromArgb(170, Theme.Teal));
            Theme.DrawIcon(g, "\uE756", ic, Theme.Teal);
            int x = ic.Right + 14;
            TextRenderer.DrawText(g, "cmdwatch", Theme.UiTitle, new Rectangle(x, 0, 200, TopH), Theme.Text, L);
            x += TextRenderer.MeasureText(g, "cmdwatch", Theme.UiTitle, Size.Empty, L).Width + 16;
            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, x, 16, x, TopH - 16);
            x += 16;
            string est = _paused ? Lang.T("v2Paused") : Lang.T("v2Monitoring");
            Color cest = _paused ? Theme.Amber : Theme.Green;
            int ew = TextRenderer.MeasureText(g, est, Theme.Ui, Size.Empty, L).Width + 30;
            Rectangle pill = new Rectangle(x, (TopH - 24) / 2, ew, 24);
            Theme.DrawPanel(g, pill, 12, Color.FromArgb(28, cest), Color.FromArgb(150, cest));
            Theme.DrawDot(g, pill.X + 12, pill.Y + 12, 7, cest);
            TextRenderer.DrawText(g, est, Theme.Ui, new Rectangle(pill.X + 21, pill.Y, ew - 21, pill.Height), cest, L);

            // titulo da pagina
            int x0 = SideW + Pad;
            TextRenderer.DrawText(g, Lang.T("v2Page"), Theme.UiPage, new Rectangle(x0, TopH + 6, 600, 34), Theme.Text, L);
            TextRenderer.DrawText(g, Lang.T("v2PageSub"), Theme.Ui, new Rectangle(x0, TopH + 38, 700, 20), Theme.Dim, L);

            // rodape: estado, versao e aviso temporario / foco
            int fy = h - FootH;
            Theme.DrawDot(g, x0 + 4, fy + FootH / 2, 7, cest);
            TextRenderer.DrawText(g, _paused ? Lang.T("v2FooterPaused") : Lang.T("v2FooterOn"), Theme.Ui,
                new Rectangle(x0 + 14, fy, 140, FootH), Theme.Text, L);
            string ver = "v" + Program.Version;
            int vw = TextRenderer.MeasureText(g, ver, Theme.Ui, Size.Empty, L).Width;
            int vx = _btnIdioma.Left - 16 - vw;
            TextRenderer.DrawText(g, ver, Theme.Ui, new Rectangle(vx, fy, vw + 4, FootH), Theme.Dim, L);

            string info = "";
            Color ci = Theme.Dim;
            if (_notice.Length > 0 && DateTime.Now < _noticeUntil) { info = _notice; ci = _noticeColor; }
            else if (_selectedPid != 0) info = Lang.F("hintFocus", NomeSessao(_selectedPid), _selectedPid);
            if (info.Length > 0)
            {
                int ix = _checkTray.Right + 20;
                TextRenderer.DrawText(g, info, Theme.Ui, new Rectangle(ix, fy, Math.Max(20, vx - 20 - ix), FootH), ci,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }

        // ------------------------------------------------- acoes da interface
        private string NomeSessao(int pid)
        {
            if (pid == 0) return Lang.T("v2FlowAll");
            SessionRow r;
            return _rowByPid.TryGetValue(pid, out r) ? r.Script : ("PID " + pid);
        }

        /// <summary>Pausa (ou retoma) a exibicao: os eventos que chegam durante a pausa sao descartados pelo painel; o motor continua capturando.</summary>
        private void SetPaused(bool v)
        {
            _paused = v;
            ApplyTexts();
            LayoutDashboard();
            _btnPausa.Invalidate();
        }

        /// <summary>Icone "Visao geral": limpa a busca, o filtro e a selecao de sessao.</summary>
        private void VisaoGeral()
        {
            _busca.Box.Text = "";
            _buscaFluxo.Box.Text = "";
            _filtroSessoes = 0;
            _sessions.SetSelection(0);
            ApplyTexts();
            OnSlowTick(null, EventArgs.Empty);
        }

        private void AbrirMenu(Control ancora, IEnumerable<KeyValuePair<string, Action>> itens, string marcado)
        {
            _dropMenu.Items.Clear();
            foreach (var it in itens)
            {
                var acao = it.Value;
                var mi = new ToolStripMenuItem((it.Key == marcado ? "\u2713  " : "     ") + it.Key);
                mi.Click += delegate { acao(); };
                _dropMenu.Items.Add(mi);
            }
            try { _dropMenu.Show(ancora, new Point(0, ancora.Height + 2)); } catch { }
        }

        private void MenuFiltro()
        {
            var itens = new List<KeyValuePair<string, Action>>();
            string[] k = { "v2FilterAll", "v2FilterRun", "v2FilterEnd" };
            for (int i = 0; i < 3; i++)
            {
                int v = i;
                itens.Add(new KeyValuePair<string, Action>(Lang.T(k[i]), delegate { _filtroSessoes = v; ApplyTexts(); LayoutDashboard(); OnSlowTick(null, EventArgs.Empty); }));
            }
            AbrirMenu(_btnFiltro, itens, Lang.T(k[_filtroSessoes]));
        }

        private void MenuSessao()
        {
            var itens = new List<KeyValuePair<string, Action>>();
            itens.Add(new KeyValuePair<string, Action>(Lang.T("v2FlowAll"), delegate { _sessions.SetSelection(0); }));
            foreach (SessionRow r in _rows.OrderByDescending(r => r.StartedAt).Take(20))
            {
                int pid = r.Pid;
                itens.Add(new KeyValuePair<string, Action>(r.Script + "  (PID " + r.Pid + ")", delegate { _sessions.SetSelection(pid); }));
            }
            string marcado = _selectedPid == 0 ? Lang.T("v2FlowAll") : NomeSessao(_selectedPid) + "  (PID " + _selectedPid + ")";
            AbrirMenu(_btnSessao, itens, marcado);
        }

        private void MenuFaixa(Control ancora)
        {
            var itens = new List<KeyValuePair<string, Action>>();
            foreach (int s in new[] { 30, 60 })
            {
                int v = s;
                itens.Add(new KeyValuePair<string, Action>(Lang.F("v2Range", s), delegate
                {
                    _activity.Capacity = v; ApplyTexts(); LayoutDashboard(); _activity.Invalidate();
                }));
            }
            AbrirMenu(ancora, itens, Lang.F("v2Range", _activity.Capacity));
        }

        private void MenuIdioma()
        {
            var itens = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("PT-BR", delegate { OnLanguageIndexChanged(0); }),
                new KeyValuePair<string, Action>("EN", delegate { OnLanguageIndexChanged(1); })
            };
            AbrirMenu(_btnIdioma, itens, Lang.Atual == Idioma.En ? "EN" : "PT-BR");
        }

        private void CopiarFluxo()
        {
            try
            {
                string t = _stream.AllText();
                if (t.Length > 0) Clipboard.SetText(t);
                ShowNotice(Lang.F("v2Copied", _stream.LineCount), Theme.Teal, 4);
            }
            catch { }
        }

        private void LimparFluxo()
        {
            _entries.Clear();
            _stream.ClearFlow();
        }

        /// <summary>Engrenagem: mostra a configuracao em uso (somente leitura).</summary>
        private void MostrarConfig()
        {
            string msg = Lang.F("v2SettingsMsg", _engineName, _opts.Interval,
                _janelas != null ? Lang.T("v2On") : Lang.T("v2Off"),
                _opts.LogPath.Length > 0 ? _opts.LogPath : Lang.T("v2NoLog"),
                _opts.Filter.Length > 0 ? _opts.Filter : "*");
            NeonDialog.Confirm(this, Lang.T("v2Settings"), msg, "OK", "", Theme.Teal);
        }

        // --------------------------------------------------------- motor
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            AplicarMoldura(Handle);
        }

        /// <summary>
        /// Moldura da janela no Windows 11: modo claro/escuro conforme o tema,
        /// cantos arredondados e barra de titulo na cor do fundo do painel (a
        /// barra e o conteudo parecem uma peca so). Em sistemas sem esses
        /// atributos as chamadas falham em silencio.
        /// </summary>
        internal static void AplicarMoldura(IntPtr hwnd)
        {
            try
            {
                int dark = Theme.Dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
                DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));   // idem, builds antigas
                int canto = 2;                                            // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref canto, sizeof(int));  // DWMWA_WINDOW_CORNER_PREFERENCE
                int legenda = ColorTranslator.ToWin32(Theme.Bg);   // barra de titulo na cor do fundo
                DwmSetWindowAttribute(hwnd, 35, ref legenda, sizeof(int)); // DWMWA_CAPTION_COLOR
                int texto = ColorTranslator.ToWin32(Theme.Text);
                DwmSetWindowAttribute(hwnd, 36, ref texto, sizeof(int));   // DWMWA_TEXT_COLOR
            }
            catch { }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            _watcher.Filter = _opts.Filter;
            _watcher.TrackChildren = !_opts.NoChildren;
            _watcher.OnEvent = OnWatchEvent;

            try { _watcher.Seed(); } catch { }

            string engine, aviso;
            _engine = EngineFactory.Criar(_opts.Engine, _opts.Interval, Program.IsAdmin(), out engine, out aviso);
            _engineName = engine;

            // observador de janelas: cada janela que abre vira um evento e
            // acorda o motor de processos na hora
            if (!_opts.NoWindows)
            {
                _janelas = new WindowWatcher { Processos = _watcher };
                _janelas.AoDescobrirProcesso = _watcher.Despertar;
                _janelas.OnChanged = OnWindowChanged;
                _janelas.Start();
            }

            if (_engine is PollingEngine)
            {
                var pe = (PollingEngine)_engine;
                pe.Wake = _janelas != null ? _janelas.Wake : null;
                pe.NeedsDetail = _watcher.PrecisaDetalhes;
                pe.IntervalProvider = _watcher.IntervaloAtual;
            }
            _engine.Start(p => _watcher.OnProcessStart(p), pid => _watcher.OnProcessExit(pid));

            if (_opts.LogPath.Length > 0)
            {
                try
                {
                    _log = new StreamWriter(_opts.LogPath, true, Encoding.UTF8);
                    _log.AutoFlush = true;
                }
                catch { _log = null; }
            }

            BuildTray();

            // cabecalho no mesmo formato do CLI
            RunInfo info = new RunInfo();
            info.Version = Program.Version;
            info.Engine = _engineName;
            info.IntervalMs = _opts.Interval;
            info.Filter = _opts.Filter;
            info.LogPath = _opts.LogPath;
            info.IsGui = true;
            info.Janelas = _janelas != null;
            AppendBanner(info);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { if (_tick != null) _tick.Stop(); } catch { }
            try { if (_slow != null) _slow.Stop(); } catch { }
            try { if (_engine != null) { _engine.Stop(); _engine.Dispose(); } } catch { }
            try { if (_janelas != null) { _janelas.OnChanged = null; _janelas.Stop(); } } catch { }
            try { if (_log != null) { _log.Flush(); _log.Close(); _log = null; } } catch { }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; } } catch { }
            try { if (_ctxMenu != null) { _ctxMenu.Dispose(); } } catch { }
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                if (_selectedPid != 0) { _sessions.SelectedPid = 0; OnSelectionChanged(); return true; }
                if (_inTray) { RestoreFromTray(); return true; }
                Close();
                return true;
            }
            if (keyData == Keys.Space && !(ActiveControl is TextBox) && !(ActiveControl is SearchBox)) { SetPaused(!_paused); return true; }
            if (keyData == Keys.F5) { LimparFluxo(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ------------------------------------------------------- bandeja
        private void BuildTray()
        {
            try
            {
                _tray = new NotifyIcon();
                Icon ico = AppIcon.Get(24);
                _tray.Icon = ico != null ? ico : SystemIcons.Application;
                _tray.Text = Lang.T("trayTitle");

                ContextMenuStrip menu = new ContextMenuStrip();
                _miRestore = new ToolStripMenuItem(Lang.T("trayRestore"));
                _miRestore.Click += delegate { RestoreFromTray(); };
                _miOpenLog = new ToolStripMenuItem(Lang.T("trayOpenLog"));
                _miOpenLog.Click += delegate { OpenLogFile(); };
                _miQuit = new ToolStripMenuItem(Lang.T("trayQuit"));
                _miQuit.Click += delegate { ExitApp(); };
                menu.Items.Add(_miRestore);
                menu.Items.Add(_miOpenLog);
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(_miQuit);

                _tray.ContextMenuStrip = menu;
                _tray.DoubleClick += delegate { RestoreFromTray(); };
                _tray.Visible = false;
            }
            catch { _tray = null; }
        }

        /// <summary>Reaplica os textos da bandeja no idioma corrente.</summary>
        private void ApplyTrayTexts()
        {
            if (_tray == null) return;
            try
            {
                _tray.Text = Lang.T("trayTitle");
                if (_miRestore != null) _miRestore.Text = Lang.T("trayRestore");
                if (_miOpenLog != null) _miOpenLog.Text = Lang.T("trayOpenLog");
                if (_miQuit != null) _miQuit.Text = Lang.T("trayQuit");
            }
            catch { }
        }

        private void OnTrayCheckChanged(bool value)
        {
            if (value) SendToTray();
            else RestoreFromTray();
        }

        private void SendToTray()
        {
            _inTray = true;
            try
            {
                _tray.Visible = true;
                _tray.ShowBalloonTip(2500, "cmdwatch", Lang.T("trayTip"), ToolTipIcon.Info);
            }
            catch { }
            Hide();
        }

        private void RestoreFromTray()
        {
            _inTray = false;
            try { if (_tray != null) _tray.Visible = false; } catch { }
            _checkTray.Checked = false;   // sem disparar o evento
            _checkTray.Invalidate();
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OpenLogFile()
        {
            try
            {
                if (_opts.LogPath.Length > 0 && File.Exists(_opts.LogPath))
                    Process.Start("notepad.exe", "\"" + _opts.LogPath + "\"");
                else if (_tray != null)
                    _tray.ShowBalloonTip(2500, "cmdwatch", Lang.T("trayNoLog"), ToolTipIcon.Info);
            }
            catch { }
        }

        private void ExitApp()
        {
            try { if (_tray != null) _tray.Visible = false; } catch { }
            Close();
            Application.Exit();
        }

        // ------------------------------------------------------- idioma
        private void OnLanguageIndexChanged(int index)
        {
            Lang.Set(index == 1 ? Idioma.En : Idioma.PtBr);
            Lang.Save();
            ApplyLanguage();
        }

        /// <summary>Redesenha toda a interface no idioma escolhido.</summary>
        private void ApplyLanguage()
        {
            ApplyTexts();
            LayoutDashboard();
            RebuildStream();
            ApplyTrayTexts();
            // o menu de contexto fica com o rotulo antigo: fecha
            try { if (_ctxMenu.Visible) _ctxMenu.Close(); } catch { }
            foreach (Control c in new Control[] { _pSessions, _pStream, _pActivity, _pWindows, _sessions, _windows, _activity, _stats, _checkTray })
                c.Invalidate();
            Invalidate();
        }

        // ------------------------------------------- menu de contexto
        /// <summary>
        /// Botao direito sobre uma sessao: monta o menu com a opcao "Fechar",
        /// que encerra o script em execucao.
        /// </summary>
        private void OnSessionContext(int pid, Point screenPoint)
        {
            SessionRow r;
            if (!_rowByPid.TryGetValue(pid, out r)) return;

            _ctxMenu.Items.Clear();

            // cabecalho: identifica o alvo do comando
            ToolStripMenuItem head = new ToolStripMenuItem(r.Script + "   (PID " + r.Pid + ")");
            head.Enabled = false;
            _ctxMenu.Items.Add(head);
            _ctxMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem fechar = new ToolStripMenuItem(Lang.T("menuClose"));
            fechar.Enabled = !r.Closed;
            fechar.Click += delegate
            {
                SessionRow alvo = r;
                // adia para o menu terminar de fechar antes do dialogo modal
                try { BeginInvoke(new Action(delegate { AskCloseSession(alvo); })); }
                catch { AskCloseSession(alvo); }
            };
            _ctxMenu.Items.Add(fechar);

            try { _ctxMenu.Show(screenPoint); } catch { }
        }

        /// <summary>
        /// Confirma e encerra a sessao: mata o cmd.exe do script e os
        /// processos filhos que ele deixou rodando.
        /// </summary>
        private void AskCloseSession(SessionRow r)
        {
            string msg = Lang.F("confirmCloseMsg", r.Script, r.Pid);
            bool ok = NeonDialog.Confirm(this, Lang.T("confirmCloseTitle"), msg,
                Lang.T("confirmYes"), Lang.T("confirmNo"), Theme.Red);
            if (!ok) return;

            TerminateResult res = ProcessControl.Terminate(r.Pid, true, r.CmdStartedAt);
            if (res.Ok)
            {
                // a sessao morre agora: o monitor ainda emitira o batch_close
                r.Closed = true;
                r.ClosedAt = DateTime.Now;
                _sessions.Invalidate();
                ShowNotice(Lang.F("noticeClosed", r.Script), Theme.Green, 6);
            }
            else if (res.AlreadyGone)
            {
                ShowNotice(Lang.F("noticeCloseGone", r.Script), Theme.Amber, 6);
            }
            else
            {
                ShowNotice(Lang.F("noticeCloseFail", r.Script, res.Error), Theme.Red, 10);
            }
        }

        /// <summary>Mensagem temporaria no rodape do painel.</summary>
        private void ShowNotice(string text, Color color, int seconds)
        {
            _notice = text ?? "";
            _noticeColor = color;
            _noticeUntil = DateTime.Now.AddSeconds(seconds);
            Invalidate();
        }

        // ------------------------------------------------------- eventos
        private void OnWatchEvent(WatchEvent ev)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(new Action<WatchEvent>(HandleEvent), ev);
            }
            catch { }
        }

        private void HandleEvent(WatchEvent ev)
        {
            if (_paused) return;

            _evAcc++;
            AppendEvent(ev);

            if (_log != null)
            {
                try { _log.WriteLine(Program.ToJson(ev)); } catch { }
            }

            switch (ev.Type)
            {
                case "batch_open":
                    _openAcc++;
                    _totalSessions++;
                    AddRow(ev.Session);
                    break;
                case "batch_step":
                    _totalSteps++;
                    TouchRow(ev);
                    break;
                case "batch_call":
                    // a chamada de um script dentro de outro
                    _callAcc++;
                    _totalCalls++;
                    TouchRow(ev);
                    break;
                case "batch_close":
                    CloseRow(ev.Session);
                    break;
                case "window_open":
                    if (ev.Window == null || !ev.Window.Auxiliar) _totalWindows++;
                    ApplyWindow(ev.Window);
                    break;
                case "window_update":
                case "window_focus":
                case "window_hide":
                case "window_close":
                    ApplyWindow(ev.Window);
                    break;
            }
        }

        // ------------------------------------------------------- janelas
        /// <summary>
        /// Uma janela mudou (abriu, ganhou foco, escondeu, fechou). O evento
        /// entra pelo mesmo caminho dos outros: o log e o fluxo do painel falam
        /// a mesma lingua que a saida do CLI.
        /// </summary>
        private void OnWindowChanged(WindowRecord rec, string tipo)
        {
            try
            {
                if (rec == null) return;
                _watcher.OnEvent(new WatchEvent
                {
                    Type = "window_" + tipo,
                    Timestamp = DateTime.Now,
                    Window = rec,
                    Message = tipo
                });
            }
            catch { }
        }

        /// <summary>Atualiza a linha da janela na tabela.</summary>
        private void ApplyWindow(WindowRecord rec)
        {
            if (rec == null) return;
            // PseudoConsole auxiliar do Windows Terminal: nao conta em dobro
            if (rec.Auxiliar) return;
            try
            {
                WindowRow r;
                if (!_winByHwnd.TryGetValue(rec.Hwnd, out r))
                {
                    r = new WindowRow();
                    r.Hwnd = rec.Hwnd;
                    r.StartedAt = rec.FirstSeen;
                    _winByHwnd[rec.Hwnd] = r;
                    _winOrder.Add(rec.Hwnd);
                }
                r.Title = rec.Title;
                r.AuthorName = rec.AuthorName;
                r.Origin = "";
                if (rec.Chain.Count > 0 && rec.Chain[0] != null && rec.Chain[0].Name.Length > 0)
                    r.Origin = rec.Chain[0].Name + " (" + rec.Chain[0].Pid + ")";
                r.Author = (rec.AuthorName.Length > 0 ? rec.AuthorName : "?") + " (" + rec.AuthorPid + ")";
                if (rec.Chain.Count > 0)
                {
                    ProcInfo pai = rec.Chain[0];
                    if (pai != null && pai.Name.Length > 0)
                        r.Author = r.Author + " <= " + pai.Name + "(" + pai.Pid + ")";
                }
                r.CmdLine = Mascara.Aplicar(rec.AuthorCmdLine);
                if (rec.Chain.Count > 0 && rec.Chain[0] != null && rec.Chain[0].CmdLine.Length > 0
                    && (r.CmdLine == null || r.CmdLine.Length == 0))
                    r.CmdLine = Mascara.Aplicar(rec.Chain[0].CmdLine);
                r.StartedAt = rec.FirstSeen;
                r.Focus = rec.FocusCount;
                r.Visible = rec.Visible;
                r.Closed = rec.Fechada;
                r.ClosedAt = rec.ClosedAt;
                r.ByConsole = rec.AutorPeloConsole;

                // descarta as janelas fechadas mais antigas (a tabela nao cresce
                // para sempre)
                if (_winOrder.Count > 200)
                {
                    int removidas = 0;
                    for (int i = 0; i < _winOrder.Count && removidas < 40; i++)
                    {
                        IntPtr h = _winOrder[i];
                        WindowRow velho;
                        if (_winByHwnd.TryGetValue(h, out velho) && velho.Closed)
                        {
                            _winByHwnd.Remove(h);
                            _winOrder.RemoveAt(i);
                            i--;
                            removidas++;
                        }
                    }
                }
            }
            catch { }
        }

        private void AddRow(BatchSession s)
        {
            if (s == null) return;
            if (_rowByPid.ContainsKey(s.CmdPid)) return;
            SessionRow r = new SessionRow();
            r.Pid = s.CmdPid;
            r.Script = s.ScriptName;
            r.StartedAt = s.StartedAt;
            r.CmdStartedAt = s.CmdStartedAt;
            // teste do encerramento seguro pelo painel (tools\test-painel.ps1): simula
            // um registro velho, como se o pid tivesse sido reaproveitado
            if (r.CmdStartedAt != DateTime.MinValue && Environment.GetEnvironmentVariable("CMDWATCH_TESTE_PID_VELHO") == "1")
                r.CmdStartedAt = r.CmdStartedAt.AddSeconds(-10);
            r.Caller = s.Caller != null ? s.Caller.Name + " (" + s.Caller.Pid + ")" : "—";
            r.Origin = s.Caller != null ? CallerDesc.Describe(s.Caller) : "desconhecida";
            _rows.Add(r);
            _rowByPid[s.CmdPid] = r;
        }

        private void TouchRow(WatchEvent ev)
        {
            if (ev.Session == null) return;
            SessionRow r;
            if (!_rowByPid.TryGetValue(ev.Session.CmdPid, out r)) { AddRow(ev.Session); return; }
            r.Steps = ev.Session.StepCount;
            if (ev.Step != null && ev.Step.Line > r.LastLine) r.LastLine = ev.Step.Line;
        }

        private void CloseRow(BatchSession s)
        {
            if (s == null) return;
            SessionRow r;
            if (!_rowByPid.TryGetValue(s.CmdPid, out r)) return;
            r.Closed = true;
            r.ClosedAt = DateTime.Now;
            r.Steps = s.StepCount;
        }

        // ------------------------------------------------------- fluxo
        private void AppendEvent(WatchEvent ev)
        {
            int pid = ev.Session != null ? ev.Session.CmdPid : 0;
            AddEntry(new StreamEntry { Pid = pid, Event = ev });
        }

        private void AppendBanner(RunInfo info)
        {
            AddEntry(new StreamEntry { Pid = 0, Banner = info });
        }

        private void AddEntry(StreamEntry en)
        {
            _entries.Add(en);
            if (_entries.Count > 4000) _entries.RemoveRange(0, 1000);
            List<HumanLine> lines = FormatEntry(en);
            string q = _buscaFluxo != null ? _buscaFluxo.Box.Text.Trim() : "";
            if (q.Length > 0) lines = lines.Where(l => Contem(l.Text, q)).ToList();
            if (_selectedPid == 0 || en.Pid == _selectedPid) _stream.Append(lines);
        }

        /// <summary>Formata uma entrada do fluxo (evento ou cabecalho) no idioma corrente.</summary>
        private static List<HumanLine> FormatEntry(StreamEntry en)
        {
            if (en.Event != null) return HumanFormat.Render(en.Event);
            if (en.Banner != null) return HumanFormat.Banner(en.Banner);
            return new List<HumanLine>();
        }

        /// <summary>
        /// Redesenha o fluxo aplicando (ou removendo) o filtro de sessao.
        /// Tambem e usado na troca de idioma: as mensagens sao reformuladas.
        /// </summary>
        private void RebuildStream()
        {
            List<HumanLine> all = new List<HumanLine>();
            int start = Math.Max(0, _entries.Count - 2000);
            for (int i = start; i < _entries.Count; i++)
            {
                StreamEntry en = _entries[i];
                if (_selectedPid != 0 && en.Pid != _selectedPid) continue;
                all.AddRange(FormatEntry(en));
            }
            // busca no fluxo: so as linhas que contem o texto
            string q = _buscaFluxo != null ? _buscaFluxo.Box.Text.Trim() : "";
            if (q.Length > 0) all = all.Where(l => Contem(l.Text, q)).ToList();
            _stream.SetLines(all);
        }

        private void OnSelectionChanged()
        {
            _selectedPid = _sessions.SelectedPid;
            RebuildStream();
            ApplyTexts();
            LayoutDashboard();
            _btnSessao.Invalidate();
            Invalidate();
        }

        private static bool Contem(string texto, string busca)
        {
            return texto != null && texto.IndexOf(busca, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Titulo do painel de sessoes: sessoes + chamadas de script.</summary>
        private string SessionsTitle()
        {
            string t = _totalSessions + " " + Lang.T("sessionsShort");
            if (_totalCalls > 0) t += "  ·  " + _totalCalls + " " + Lang.T("callsShort");
            return t;
        }

        // -------------------------------------------------------- timers
        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                _eventsPerSec.Enqueue(_evAcc);
                _opensPerSec.Enqueue(_openAcc);
                _evAcc = 0; _openAcc = 0;
                while (_eventsPerSec.Count > 60) _eventsPerSec.Dequeue();
                while (_opensPerSec.Count > 60) _opensPerSec.Dequeue();

                _activity.SeriesA = new List<double>(_eventsPerSec);
                _activity.SeriesB = new List<double>(_opensPerSec);
                _activity.Invalidate();

                // aviso temporario: some sozinho
                if (_notice.Length > 0 && DateTime.Now >= _noticeUntil)
                {
                    _notice = "";
                    Invalidate();
                }
            }
            catch { }
        }

        private void OnSlowTick(object sender, EventArgs e)
        {
            try
            {
                // busca da barra superior: script, chamador ou pid; mais o filtro do cartao
                string q = _busca != null ? _busca.Box.Text.Trim() : "";
                _sessions.Rows = _rows.Where(r => (q.Length == 0 || Contem(r.Script, q) || Contem(r.Caller, q) || r.Pid.ToString() == q)
                                                  && (_filtroSessoes == 0 || (_filtroSessoes == 1) != r.Closed))
                                      .OrderByDescending(r => r.StartedAt).ToList();
                _sessions.Invalidate();
                _pSessions.TitleValue = _sessions.Rows.Count.ToString();
                _pSessions.Invalidate();

                // a tabela de janelas: duracao correndo e estado atual
                var winRows = new List<WindowRow>();
                foreach (IntPtr h in _winOrder)
                {
                    WindowRow r;
                    if (!_winByHwnd.TryGetValue(h, out r)) continue;
                    if (q.Length > 0 && !Contem(r.Title, q) && !Contem(r.Author, q) && !Contem(r.CmdLine, q)) continue;
                    winRows.Add(r);
                }
                _windows.Rows = winRows.OrderByDescending(r => r.StartedAt).ToList();
                _windows.Invalidate();
                _pWindows.TitleValue = _windows.Rows.Count.ToString();
                _pWindows.Invalidate();

                // estatisticas: valores reais do motor
                int ativas = 0;
                foreach (SessionRow r in _rows) if (!r.Closed) ativas++;
                TimeSpan t = DateTime.Now - _inicio;
                _stats.Values = new[]
                {
                    ativas.ToString(), _totalWindows.ToString(), _totalSteps.ToString(),
                    ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00")
                };
                _stats.Invalidate();
            }
            catch { }
        }
    }
}
