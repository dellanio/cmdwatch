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
    /// Painel visual do cmdwatch no sistema estetico Hoody / Neo-Brutalista:
    /// cantos 100% retos (0px), bordas estruturais de 1px (#2A2A2A), barra
    /// superior industrial (#181818), sidebar retratil (204px / 52px), trilho
    /// utilitario direito (Pin no topo, Grade 40x40px, Exportar Log, Bandeja),
    /// gaveta lateral de configuracoes e amarelo acido (#E2FF26) como primario.
    /// </summary>
    internal sealed class DashboardForm : Form
    {
        private readonly Options _opts;
        private readonly Watcher _watcher = new Watcher();
        private IEngine _engine;
        private StreamWriter _log;
        private string _logPath = "";
        private string _engineName = "polling";
        private bool _paused;
        private bool _inTray;
        private readonly DateTime _inicio = DateTime.Now;

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

        private WindowWatcher _janelas;
        private int _totalWindows;

        private NeonPanel _pSessions, _pStream, _pActivity, _pWindows;
        private SessionTable _sessions;
        private NeonFlow _stream;
        private LineChart _activity;
        private WindowTable _windows;
        private CpuView _cpu;
        private CpuSampler _cpuSampler;
        private bool _abaCpu;
        private double _cpuTotal;
        private double _memPct;
        private CpuSnapshot _cpuUltimoRetrato;
        private string _cpuUltimoFiltro = "";
        private bool _cpuOcultoAnterior;
        private int _cpuLinhas;
        private readonly Dictionary<int, CpuUser> _cpuUsuarios = new Dictionary<int, CpuUser>();

        private NeonCheckBox _checkTray;
        private StatStrip _stats;
        private SideBar _side;
        private RightRail _rail;
        private SettingsDrawer _drawer;
        private SearchBox _busca, _buscaFluxo;
        private IconButton _btnPausa, _btnTopConfig, _btnFiltro, _btnSessao, _btnCopiar, _btnLimpar, _btnFaixa, _btnIdioma;
        private IconButton _btnLimparSess;
        private IconButton _btnOcultarWin;
        private IconButton _railPin, _railGrid, _railExport, _railTray;
        private bool _ocultarWindows;
        private bool _archGrid = true;
        private bool _pinTop;
        private bool _pulseBright = true;

        private readonly Dictionary<NeonPanel, IconButton> _chev = new Dictionary<NeonPanel, IconButton>();
        private readonly Dictionary<NeonPanel, string> _chave = new Dictionary<NeonPanel, string>();
        private readonly HashSet<string> _recolhidos = new HashSet<string>();
        private readonly Dictionary<int, DateTime> _limpas = new Dictionary<int, DateTime>();

        private IconButton _navHome, _navSessoes, _navCpu, _navJanelas, _navFluxo, _navGrafico, _navLog, _navConfig;
        private NotifyIcon _tray;
        private ToolStripMenuItem _miRestore, _miOpenLog, _miQuit;
        private readonly ContextMenuStrip _ctxMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip _dropMenu = new ContextMenuStrip();
        private System.Windows.Forms.Timer _tick, _slow;

        private int _filtroSessoes;

        private const int TopH = 52;
        private const int SideExpandedW = 204;
        private const int SideCollapsedW = 52;
        private const int RailW = 38;
        private const int FootH = 32;
        private const int Pad = 14;
        private const int Gap = 10;

        private string _notice = "";
        private DateTime _noticeUntil = DateTime.MinValue;
        private Color _noticeColor = Theme.Text;

        public DashboardForm(Options opts)
        {
            _opts = opts;
            _ocultarWindows = Lang.GetCfg("ocultarWin") == "1";
            _archGrid = Lang.GetCfg("archGrid") != "0";
            _pinTop = Lang.GetCfg("pinTop") == "1";
            TopMost = _pinTop;

            Text = Lang.T("v2Title");
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1020, 640);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            KeyPreview = true;

            try { Icon = AppIcon.Get(32); } catch { }

            BuildUi();
            ClientSize = new Size(1480, 900);

            _tick = new System.Windows.Forms.Timer { Interval = 1000 };
            _tick.Tick += OnTick;
            _tick.Start();

            _slow = new System.Windows.Forms.Timer { Interval = 1000 };
            _slow.Tick += OnSlowTick;
            _slow.Start();

            _cpuSampler = new CpuSampler(1500);
            _cpuSampler.Iniciar();
        }

        private IconButton Botao(Control pai, string glyph, string label, bool chevron, EventHandler click)
        {
            IconButton b = new IconButton { Glyph = glyph, Label = label, Chevron = chevron };
            b.BackColor = pai is NeonPanel ? Theme.HeaderBg : Theme.TopBar;
            if (click != null) b.Click += click;
            pai.Controls.Add(b);
            return b;
        }

        private void BuildUi()
        {
            _pSessions = new NeonPanel();
            _pStream = new NeonPanel { TerminalSurface = true };
            _pActivity = new NeonPanel();
            _pWindows = new NeonPanel();

            _sessions = new SessionTable();
            _sessions.Name = _sessions.Text = "tabelaSessoes";
            _sessions.SelectionChanged += OnSelectionChanged;
            _sessions.ContextRequested += OnSessionContext;
            _pSessions.Controls.Add(_sessions);

            _cpu = new CpuView();
            _cpu.Name = _cpu.Text = "cpuProcessos";
            _cpu.Visible = false;
            _cpu.Tabela.SelectionChanged += delegate { AtualizarDetalheCpu(); };
            _pSessions.Controls.Add(_cpu);
            _pSessions.Tabs = new[] { Lang.T("v2Sessions"), Lang.T("v2CpuTab") };
            _pSessions.TabGlyphs = new[] { "\uE756", "\uE950" };
            _pSessions.TabChanged += OnTabChanged;

            _stream = new NeonFlow();
            _pStream.Controls.Add(_stream);

            _activity = new LineChart { Capacity = 60 };
            _pActivity.Controls.Add(_activity);

            _windows = new WindowTable();
            _windows.Name = _windows.Text = "tabelaJanelas";
            _windows.ContextRequested += OnWindowContext;
            _pWindows.Controls.Add(_windows);

            _stats = new StatStrip();

            _side = new SideBar();
            _side.Collapsed = Lang.GetCfg("sideCollapsed") == "1";
            _side.CollapseToggled += delegate
            {
                Lang.SetCfg("sideCollapsed", _side.Collapsed ? "1" : "0");
                ApplyTexts();
                LayoutDashboard();
            };

            _navHome = _side.Add("\uE80F", "", false);
            _navHome.Active = true;
            _navHome.Click += delegate { FocarSecao("overview"); };

            _navSessoes = _side.Add("\uE756", "", false);
            _navSessoes.Click += delegate { FocarSecao("sessoes"); };

            _navCpu = _side.Add("\uE950", "", false);
            _navCpu.Click += delegate { FocarSecao("cpu"); };

            _navJanelas = _side.Add("\uE737", "", false);
            _navJanelas.Click += delegate { FocarSecao("janelas"); };

            _navFluxo = _side.Add("\uE943", "", false);
            _navFluxo.Click += delegate { FocarSecao("fluxo"); };

            _navGrafico = _side.Add("\uE9D2", "", false);
            _navGrafico.Click += delegate { FocarSecao("atividade"); };

            _navLog = _side.Add("\uE8A5", "", true);
            _navLog.Click += delegate { OpenLogFile(); };

            _navConfig = _side.Add("\uE713", "", true);
            _navConfig.Click += delegate { AlternarDrawer(); };

            _rail = new RightRail();
            _railPin = _rail.Add("\uE718", delegate { AlternarPinTop(!_pinTop); });
            _railPin.Active = _pinTop;
            _railGrid = _rail.Add("\uE80A", delegate { AlternarGrade(!_archGrid); });
            _railGrid.Active = _archGrid;
            _railExport = _rail.Add("\uEDE1", delegate { ExportarFluxo(); });
            _railTray = _rail.Add("\uE747", delegate { SendToTray(); });

            _drawer = new SettingsDrawer { Visible = false };
            _drawer.CloseRequested += delegate { FecharDrawer(); };
            _drawer.PinToggled += delegate(bool v) { AlternarPinTop(v); };
            _drawer.GridToggled += delegate(bool v) { AlternarGrade(v); };
            _drawer.TrayToggled += delegate(bool v) { if (v) { FecharDrawer(); SendToTray(); } };
            _drawer.OpenLogRequested += delegate { OpenLogFile(); };
            _drawer.ExportLogRequested += delegate { ExportarFluxo(); };

            _busca = new SearchBox { Name = "busca", BackColor = Theme.TopBar };
            _busca.Box.TextChanged += delegate { OnSlowTick(null, EventArgs.Empty); };
            _btnPausa = Botao(this, "\uE769", "", false, delegate { SetPaused(!_paused); });
            _btnPausa.PrimaryAcid = true;
            _btnTopConfig = Botao(this, "\uE713", "", false, delegate { AlternarDrawer(); });

            _btnFiltro = Botao(_pSessions, "\uE71C", "", true, delegate { MenuFiltro(); });
            _btnSessao = Botao(_pStream, "\uE756", "", true, delegate { MenuSessao(); });
            _buscaFluxo = new SearchBox { BackColor = Theme.HeaderBg };
            _buscaFluxo.Box.TextChanged += delegate { RebuildStream(); };
            _pStream.Controls.Add(_buscaFluxo);
            _btnCopiar = Botao(_pStream, "\uE8C8", "", false, delegate { CopiarFluxo(); });
            _btnLimpar = Botao(_pStream, "\uE74D", "", false, delegate { LimparFluxo(); });
            _btnFaixa = Botao(_pActivity, "", "", true, delegate { MenuFaixa(_btnFaixa); });
            _btnLimparSess = Botao(_pSessions, "\uE894", "", false, delegate { LimparSessoes(); });
            _btnLimparSess.Name = _btnLimparSess.Text = "btnLimparSessoes";
            _btnOcultarWin = Botao(_pSessions, "\uE71C", "", false, delegate { AlternarOcultarWindows(); });
            _btnOcultarWin.Name = _btnOcultarWin.Text = "btnOcultarWindows";

            _chave[_pSessions] = "sessoes"; _chave[_pWindows] = "janelas";
            _chave[_pStream] = "fluxo"; _chave[_pActivity] = "atividade";
            CarregarRecolhidos();
            foreach (KeyValuePair<NeonPanel, string> c in _chave)
            {
                NeonPanel np = c.Key;
                np.Name = np.Text = "cartao_" + c.Value;
                IconButton b = Botao(np, "", "", false, delegate { AlternarRecolher(np); });
                b.Name = b.Text = "recolher_" + c.Value;
                _chev[np] = b;
            }

            _checkTray = new NeonCheckBox { BackColor = Theme.TopBar };
            _checkTray.CheckedChanged += OnTrayCheckChanged;
            _btnIdioma = Botao(this, "", "", true, delegate { MenuIdioma(); });

            foreach (ContextMenuStrip m in new[] { _ctxMenu, _dropMenu })
            {
                m.Renderer = new NeonMenuRenderer();
                m.ShowImageMargin = false;
                m.ShowCheckMargin = false;
                m.Font = Theme.MonoSmall;
            }

            Controls.Add(_drawer);
            Controls.Add(_side);
            Controls.Add(_rail);
            Controls.Add(_stats);
            Controls.Add(_pSessions);
            Controls.Add(_pWindows);
            Controls.Add(_pStream);
            Controls.Add(_pActivity);
            Controls.Add(_checkTray);
            Controls.Add(_busca);
            _drawer.BringToFront();

            ApplyTexts();
            LayoutDashboard();
        }

        private void ApplyTexts()
        {
            Text = Lang.T("v2Title");
            _pSessions.Title = Lang.T("v2Sessions");
            _pSessions.Tabs = new[] { Lang.T("v2Sessions"), Lang.T("v2CpuTab") };
            _pSessions.TabGlyphs = new[] { "\uE756", "\uE950" };
            _pWindows.Title = Lang.T("v2Windows");
            _pStream.Title = Lang.T("v2Flow");
            _pStream.Subtitle = Lang.T("v2LiveFeed");
            _pActivity.Title = Lang.T("v2Activity");
            _pActivity.Subtitle = Lang.T("v2Telemetry");
            _activity.LabelA = Lang.T("v2LegCmd");
            _activity.LabelB = Lang.T("v2LegSess");

            _stats.Labels = new[] { Lang.T("v2StatSess"), Lang.T("v2StatWin"), Lang.T("v2StatCpu"),
                Lang.T("v2StatMem"), Lang.T("v2StatCmd"), Lang.T("v2StatTime") };
            _stats.Glyphs = new[] { "\uE756", "\uE737", "\uE950", "\uE8B3", "\uE81E", "\uE823" };
            _stats.SubLabels[0] = Lang.T("v2SubSess");
            _stats.SubLabels[1] = Lang.T("v2SubWin");
            _stats.SubLabels[4] = Lang.T("v2SubCmd");
            _stats.SubLabels[5] = Lang.T("v2SubTime");

            _busca.Cue = Lang.T("v2Search");
            _buscaFluxo.Cue = Lang.T("v2FlowSearch");
            _btnPausa.Glyph = _paused ? "\uE768" : "\uE769";
            _btnPausa.Label = _paused ? Lang.T("v2Resume") : Lang.T("v2Pause");
            _btnPausa.PrimaryAcid = !_paused;
            _btnPausa.Active = _paused;
            _btnTopConfig.Tip = Lang.T("v2Settings");
            _btnTopConfig.Active = _drawer != null && _drawer.Visible;

            _btnFiltro.Label = Lang.T(_filtroSessoes == 1 ? "v2FilterRun" : _filtroSessoes == 2 ? "v2FilterEnd" : "v2FilterAll");
            _btnSessao.Label = Lang.F("v2FlowSess", NomeSessao(_selectedPid));
            _btnCopiar.Tip = Lang.T("v2Copy");
            _btnLimpar.Tip = Lang.T("v2Clear");
            _btnLimparSess.Label = Lang.T("v2ClearSess");
            _btnLimparSess.Tip = Lang.T("v2ClearSessTip");
            _btnOcultarWin.Label = Lang.T("v2OcultarWin");
            _btnOcultarWin.Active = _ocultarWindows;
            _btnOcultarWin.Tip = _ocultarWindows ? Lang.T("v2OcultarWinAtivo")
                                                 : Lang.F("v2OcultarWinTip", ClassificadorProcessos.DirWindows);

            foreach (KeyValuePair<NeonPanel, IconButton> c in _chev)
            {
                bool rec = Recolhido(c.Key);
                c.Value.Glyph = rec ? "\uE70D" : "\uE70E";
                c.Value.Tip = Lang.T(rec ? "v2Expand" : "v2Collapse");
                c.Value.Invalidate();
            }

            _btnFaixa.Label = Lang.F("v2Range", _activity.Capacity);
            _checkTray.Label = Lang.T("v2Tray");
            _btnIdioma.Label = Lang.Atual == Idioma.En ? "EN" : "PT-BR";

            _side.SetEntryText(_navHome, Lang.T("v2SideOverview"), Lang.T("v2NavHome"));
            _side.SetEntryText(_navSessoes, Lang.T("v2SideSessions"), Lang.T("v2Sessions"));
            _side.SetEntryText(_navCpu, Lang.T("v2SideCpu"), Lang.T("v2CpuTab"));
            _side.SetEntryText(_navJanelas, Lang.T("v2SideWindows"), Lang.T("v2Windows"));
            _side.SetEntryText(_navFluxo, Lang.T("v2SideStream"), Lang.T("v2Flow"));
            _side.SetEntryText(_navGrafico, Lang.T("v2SideActivity"), Lang.T("v2NavChart"));
            _side.SetEntryText(_navLog, Lang.T("v2SideLogs"), Lang.T("v2NavLog"));
            _side.SetEntryText(_navConfig, Lang.T("v2Settings"), Lang.T("v2Settings"));
            _side.CollapseLabel = Lang.T(_side.Collapsed ? "v2SideExpand" : "v2SideCollapse");

            _railPin.Tip = Lang.T("v2AlwaysOnTop");
            _railGrid.Tip = Lang.T("v2ArchGrid");
            _railExport.Tip = Lang.T("v2ExportLog");
            _railTray.Tip = Lang.T("v2Tray");

            SincronizarDrawer();
        }

        private void LayoutDashboard()
        {
            if (_pSessions == null || _btnIdioma == null) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w < 200 || h < 200) return;

            bool estreito = w < 1160;
            int sideW = (_side.Collapsed || estreito) ? SideCollapsedW : SideExpandedW;
            int railW = RailW;

            int bh = 30, by = (TopH - bh) / 2;
            _btnTopConfig.SetBounds(w - 12 - 30, by, 30, bh);
            int pw = Math.Max(98, _btnPausa.PreferredW() + 6);
            _btnPausa.SetBounds(_btnTopConfig.Left - 8 - pw, by, pw, bh);
            int sw = Math.Min(310, Math.Max(170, w / 5));
            _busca.SetBounds(_btnPausa.Left - 10 - sw, by, sw, bh);

            int bodyH = Math.Max(100, h - TopH - FootH);
            _side.SetBounds(0, TopH, sideW, bodyH);
            _rail.SetBounds(w - railW, TopH, railW, bodyH);

            if (_drawer.Visible)
            {
                int dw = Math.Min(410, Math.Max(320, w / 3));
                _drawer.SetBounds(w - railW - dw, TopH, dw, bodyH);
                _drawer.BringToFront();
            }

            int x0 = sideW + Pad;
            int cw = Math.Max(300, w - x0 - railW - Pad);
            bool compacto = h < 760;
            int headerAreaH = compacto ? 46 : 56;
            int y = TopH + headerAreaH;

            int cab = compacto ? 34 : 40;
            foreach (NeonPanel np in new[] { _pSessions, _pWindows, _pStream, _pActivity }) np.HeaderHeight = cab;

            int statH = compacto ? 60 : 76;
            _stats.SetBounds(x0, y, cw, statH);
            y += statH + Gap;

            int resto = h - FootH - Gap - y;
            int minT = cab + 26 + 2 * 24 + 4;
            bool cS = Recolhido(_pSessions), cW = Recolhido(_pWindows), cF = Recolhido(_pStream), cA = Recolhido(_pActivity);
            bool midAberto = !(cF && cA);
            int midExtra = cF != cA ? cab + Gap : 0;
            int fixo = (cS ? cab : 0) + (cW ? cab : 0) + (midAberto ? 0 : cab);
            double peso = (cS ? 0 : 0.27) + (cW ? 0 : 0.28) + (midAberto ? 0.45 : 0);
            int livre = Math.Max(0, resto - fixo - midExtra);
            if (peso <= 0) peso = 1;

            int sessH = cS ? cab : Math.Max(minT, (int)(livre * 0.27 / peso));
            int winH = cW ? cab : Math.Max(minT, (int)(livre * 0.28 / peso));
            int midH = midAberto ? Math.Max(126, resto - sessH - winH - Gap * 2 - midExtra) : cab;

            _pSessions.SetBounds(x0, y, cw, sessH);
            y += sessH + Gap;
            _pWindows.SetBounds(x0, y, cw, winH);
            y += winH + Gap;

            if (cF == cA)
            {
                int flowW = (int)((cw - Gap) * 0.63);
                _pStream.SetBounds(x0, y, flowW, midH);
                _pActivity.SetBounds(x0 + flowW + Gap, y, cw - flowW - Gap, midH);
            }
            else
            {
                NeonPanel fechado = cF ? _pStream : _pActivity, aberto = cF ? _pActivity : _pStream;
                fechado.SetBounds(x0, y, cw, cab);
                aberto.SetBounds(x0, y + cab + Gap, cw, midH);
            }

            _sessions.Visible = !cS && !_abaCpu;
            _cpu.Visible = !cS && _abaCpu;
            _windows.Visible = !cW;
            _stream.Visible = !cF;
            _activity.Visible = !cA;

            int hb = 24, hy = (_pSessions.HeaderHeight - hb) / 2;
            foreach (KeyValuePair<NeonPanel, IconButton> c in _chev)
                c.Value.SetBounds(c.Key.Width - 10 - hb, hy, hb, hb);

            int dir = 10 + hb + 6;
            int fw = Math.Max(148, _btnFiltro.PreferredW());
            _btnFiltro.SetBounds(_pSessions.Width - dir - fw, hy, fw, hb);
            _btnFiltro.Visible = !_abaCpu;

            int lsw = _btnLimparSess.PreferredW() + 4;
            int rightAnchor = _abaCpu ? (_pSessions.Width - dir) : (_btnFiltro.Left - 6);
            _btnLimparSess.SetBounds(rightAnchor - lsw, hy, lsw, hb);

            int osw = _btnOcultarWin.PreferredW() + 4;
            _btnOcultarWin.SetBounds(_btnLimparSess.Left - 6 - osw, hy, osw, hb);
            _btnOcultarWin.Visible = _abaCpu;

            int fx = _pStream.Width - dir;
            _btnLimpar.SetBounds(fx - hb, hy, hb, hb); fx -= hb + 5;
            _btnCopiar.SetBounds(fx - hb, hy, hb, hb); fx -= hb + 8;
            int qw = Math.Min(175, Math.Max(90, _pStream.Width / 5));
            _buscaFluxo.SetBounds(fx - qw, hy, qw, hb); fx -= qw + 8;
            int ssw = Math.Min(210, Math.Max(110, _btnSessao.PreferredW()));
            ssw = Math.Min(ssw, Math.Max(70, fx - 180));
            _btnSessao.SetBounds(fx - ssw, hy, Math.Max(60, ssw), hb);

            int rw = _btnFaixa.PreferredW();
            _btnFaixa.SetBounds(_pActivity.Width - dir - rw, hy, rw, hb);

            foreach (var par in new KeyValuePair<NeonPanel, Control>[] {
                new KeyValuePair<NeonPanel, Control>(_pSessions, _sessions),
                new KeyValuePair<NeonPanel, Control>(_pSessions, _cpu),
                new KeyValuePair<NeonPanel, Control>(_pWindows, _windows) })
            {
                Rectangle a = par.Key.PlotArea;
                par.Value.SetBounds(a.X, a.Y, a.Width, a.Height);
            }

            Rectangle ta = _pStream.PlotArea;
            _stream.SetBounds(ta.X, ta.Y, ta.Width, ta.Height);
            Rectangle aa = _pActivity.PlotArea;
            _activity.SetBounds(aa.X + 4, aa.Y, Math.Max(10, aa.Width - 8), aa.Height);

            int fy = h - FootH;
            _checkTray.SetBounds(x0 + 280, fy + (FootH - 20) / 2, CheckboxWidth(), 20);
            int iw = 76;
            _btnIdioma.SetBounds(w - 12 - iw, fy + (FootH - 22) / 2, iw, 22);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutDashboard();
        }

        private int CheckboxWidth()
        {
            try { return 14 + 8 + TextRenderer.MeasureText(_checkTray.Label.ToUpperInvariant(), Theme.MonoTiny).Width + 10; }
            catch { return 240; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            int w = ClientSize.Width, h = ClientSize.Height;
            const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

            int sideW = _side != null ? _side.Width : SideExpandedW;
            Rectangle canvasArea = new Rectangle(sideW, TopH, Math.Max(10, w - sideW - RailW), Math.Max(10, h - TopH - FootH));
            if (_archGrid) Theme.DrawArchGrid(g, canvasArea);

            using (SolidBrush tb = new SolidBrush(Theme.TopBar))
            {
                g.FillRectangle(tb, 0, 0, w, TopH - 1);
                g.FillRectangle(tb, 0, h - FootH, w, FootH);
            }
            using (Pen p = new Pen(Theme.Border))
            {
                g.DrawLine(p, 0, TopH - 1, w, TopH - 1);
                g.DrawLine(p, 0, h - FootH, w, h - FootH);
            }

            Rectangle logo = new Rectangle(12, (TopH - 28) / 2, 28, 28);
            Theme.DrawPanel(g, logo, 0, Theme.Teal, Color.Empty);
            TextRenderer.DrawText(g, ">_", Theme.MonoBold, logo, Theme.OnAcid,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            int x = logo.Right + 10;
            int bwCmd = TextRenderer.MeasureText(g, "CMD", Theme.UiHeader, Size.Empty, L).Width;
            TextRenderer.DrawText(g, "CMD", Theme.UiHeader, new Rectangle(x, 0, bwCmd + 2, TopH), Theme.Text, L);
            x += bwCmd;
            int bwWatch = TextRenderer.MeasureText(g, "watch", Theme.UiHeader, Size.Empty, L).Width;
            TextRenderer.DrawText(g, "watch", Theme.UiHeader, new Rectangle(x, 0, bwWatch + 4, TopH), Theme.Teal, L);
            x += bwWatch + 10;

            string verTag = "v" + Program.Version;
            int vtw = TextRenderer.MeasureText(g, verTag, Theme.MonoTiny, Size.Empty, L).Width + 10;
            Rectangle verRect = new Rectangle(x, (TopH - 18) / 2, vtw, 18);
            Theme.DrawPanel(g, verRect, 0, Theme.PanelDeep, Theme.Border);
            TextRenderer.DrawText(g, verTag, Theme.MonoTiny, verRect, Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            x = verRect.Right + 14;

            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, x, 14, x, TopH - 14);
            x += 14;

            string est = (_paused ? Lang.T("v2Paused") : Lang.T("v2Monitoring")).ToUpperInvariant();
            Color cest = _paused ? Theme.Amber : Theme.Teal;
            int ew = TextRenderer.MeasureText(g, est, Theme.MonoBold, Size.Empty, L).Width + 28;
            Rectangle pill = new Rectangle(x, (TopH - 24) / 2, ew, 24);
            Theme.DrawPanel(g, pill, 0, Color.FromArgb(24, cest), cest);
            Color dotColor = (!_paused && !_pulseBright) ? Color.FromArgb(100, cest) : cest;
            Theme.DrawDot(g, pill.X + 10, pill.Y + 12, 6, dotColor);
            TextRenderer.DrawText(g, est, Theme.MonoBold, new Rectangle(pill.X + 19, pill.Y, ew - 20, pill.Height), cest, L);

            int x0 = sideW + Pad;
            bool compacto = h < 760;
            string pageUp = Lang.T("v2Page").ToUpperInvariant();
            TextRenderer.DrawText(g, pageUp, Theme.UiPage, new Rectangle(x0, TopH + (compacto ? 4 : 7), 420, 26), Theme.Text, L);
            if (!compacto)
                TextRenderer.DrawText(g, Lang.T("v2PageSub"), Theme.MonoTiny, new Rectangle(x0, TopH + 33, 620, 16), Theme.Dim, L);

            string tele = "ENGINE: " + _engineName.ToUpperInvariant() + " · " + _opts.Interval + "MS   |   MASK: " +
                          (Mascara.Ativa ? "ON" : "OFF");
            int teleRight = w - RailW - Pad;
            TextRenderer.DrawText(g, tele, Theme.MonoTiny, new Rectangle(x0 + 340, TopH + (compacto ? 12 : 16), Math.Max(40, teleRight - (x0 + 340)), 20),
                Theme.Gray, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            int fy = h - FootH;
            Theme.DrawDot(g, 16, fy + FootH / 2, 6, cest);
            string footStatus = (_paused ? Lang.T("v2FooterPaused") : Lang.T("v2FooterOn")).ToUpperInvariant();
            TextRenderer.DrawText(g, footStatus, Theme.MonoTiny, new Rectangle(26, fy, 190, FootH), cest, L);

            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, 220, fy + 8, 220, h - 8);
            string engFoot = _engineName.ToUpperInvariant() + " (" + _opts.Interval + "MS)";
            TextRenderer.DrawText(g, engFoot, Theme.MonoTiny, new Rectangle(232, fy, 170, FootH), Theme.Dim, L);

            string info = "";
            Color ci = Theme.Dim;
            if (_notice.Length > 0 && DateTime.Now < _noticeUntil) { info = _notice; ci = _noticeColor; }
            else if (_selectedPid != 0) info = Lang.F("hintFocus", NomeSessao(_selectedPid), _selectedPid);
            if (info.Length > 0)
            {
                int ix = _checkTray.Right + 14;
                int maxW = Math.Max(20, _btnIdioma.Left - 14 - ix);
                TextRenderer.DrawText(g, info, Theme.MonoTiny, new Rectangle(ix, fy, maxW, FootH), ci,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }

        private void MarcarNavAtivo(IconButton ativo)
        {
            foreach (IconButton b in new[] { _navHome, _navSessoes, _navCpu, _navJanelas, _navFluxo, _navGrafico })
                if (b != null) b.Active = ReferenceEquals(b, ativo);
            _side.Invalidate();
        }

        private void FocarSecao(string secao)
        {
            FecharDrawer();
            if (secao == "overview")
            {
                MarcarNavAtivo(_navHome);
                VisaoGeral();
                return;
            }

            if (secao == "sessoes")
            {
                MarcarNavAtivo(_navSessoes);
                _recolhidos.Remove("sessoes");
                _pSessions.SelecionarAba(0);
            }
            else if (secao == "cpu")
            {
                MarcarNavAtivo(_navCpu);
                _recolhidos.Remove("sessoes");
                _pSessions.SelecionarAba(1);
            }
            else if (secao == "janelas")
            {
                MarcarNavAtivo(_navJanelas);
                _recolhidos.Remove("janelas");
            }
            else if (secao == "fluxo")
            {
                MarcarNavAtivo(_navFluxo);
                _recolhidos.Remove("fluxo");
            }
            else if (secao == "atividade")
            {
                MarcarNavAtivo(_navGrafico);
                _recolhidos.Remove("atividade");
            }

            Lang.SetCfg("recolhidos", string.Join(",", _recolhidos.OrderBy(s => s)));
            ApplyTexts();
            LayoutDashboard();
        }

        private void AlternarPinTop(bool valor)
        {
            _pinTop = valor;
            TopMost = _pinTop;
            Lang.SetCfg("pinTop", _pinTop ? "1" : "0");
            if (_railPin != null) { _railPin.Active = _pinTop; _railPin.Invalidate(); }
            SincronizarDrawer();
            ShowNotice(Lang.T(_pinTop ? "v2PinOn" : "v2PinOff"), Theme.Teal, 4);
        }

        private void AlternarGrade(bool valor)
        {
            _archGrid = valor;
            Lang.SetCfg("archGrid", _archGrid ? "1" : "0");
            if (_railGrid != null) { _railGrid.Active = _archGrid; _railGrid.Invalidate(); }
            SincronizarDrawer();
            Invalidate();
        }

        private void ExportarFluxo()
        {
            try
            {
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Title = Lang.T("v2ExportLog");
                    sfd.Filter = "Log (*.log)|*.log|Text (*.txt)|*.txt|All files (*.*)|*.*";
                    sfd.FileName = "cmdwatch-stream-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log";
                    if (sfd.ShowDialog(this) != DialogResult.OK) return;
                    File.WriteAllText(sfd.FileName, _stream.AllText(), Encoding.UTF8);
                    ShowNotice(Lang.F("v2Exported", Path.GetFileName(sfd.FileName)), Theme.Teal, 6);
                }
            }
            catch (Exception ex)
            {
                ShowNotice(ex.Message, Theme.Red, 6);
            }
        }

        private void AlternarDrawer()
        {
            if (_drawer.Visible) FecharDrawer();
            else
            {
                SincronizarDrawer();
                _drawer.Visible = true;
                _navConfig.Active = true;
                _btnTopConfig.Active = true;
                _side.Invalidate();
                _btnTopConfig.Invalidate();
                LayoutDashboard();
            }
        }

        private void FecharDrawer()
        {
            if (_drawer == null || !_drawer.Visible) return;
            _drawer.Visible = false;
            _navConfig.Active = false;
            _btnTopConfig.Active = false;
            _side.Invalidate();
            _btnTopConfig.Invalidate();
            LayoutDashboard();
        }

        private void SincronizarDrawer()
        {
            if (_drawer == null) return;
            _drawer.EngineName = _engineName;
            _drawer.IntervalMs = _opts != null ? _opts.Interval : 150;
            _drawer.WindowWatcherOn = _janelas != null || (_opts != null && !_opts.NoWindows);
            _drawer.MaskOn = Mascara.Ativa;
            _drawer.LogPath = _logPath;
            _drawer.Filter = _opts != null ? _opts.Filter : "";
            _drawer.PinOnTop = _pinTop;
            _drawer.ArchGridOn = _archGrid;
            _drawer.TrayOn = _inTray;
            _drawer.Invalidate();
        }

        private string NomeSessao(int pid)
        {
            if (pid == 0) return Lang.T("v2FlowAll");
            SessionRow r;
            return _rowByPid.TryGetValue(pid, out r) ? r.Script : ("PID " + pid);
        }

        private void SetPaused(bool v)
        {
            _paused = v;
            ApplyTexts();
            LayoutDashboard();
            _btnPausa.Invalidate();
        }

        private void VisaoGeral()
        {
            _busca.Box.Text = "";
            _buscaFluxo.Box.Text = "";
            _filtroSessoes = 0;
            _recolhidos.Clear();
            Lang.SetCfg("recolhidos", "");
            _sessions.SetSelection(0);
            _pSessions.SelecionarAba(0);
            ApplyTexts();
            LayoutDashboard();
            OnSlowTick(null, EventArgs.Empty);
        }

        private void OnTabChanged(int indice)
        {
            _abaCpu = indice == 1;
            MarcarNavAtivo(_abaCpu ? _navCpu : _navSessoes);
            LayoutDashboard();
            OnSlowTick(null, EventArgs.Empty);
        }

        private void AbrirMenu(Control ancora, IEnumerable<KeyValuePair<string, Action>> itens, string marcado)
        {
            _dropMenu.Items.Clear();
            foreach (var it in itens)
            {
                var acao = it.Value;
                var mi = new ToolStripMenuItem((it.Key == marcado ? "■  " : "    ") + it.Key);
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

        private void LimparSessoes()
        {
            if (_abaCpu)
            {
                _cpu.Tabela.Selecionar(-1);
                _cpu.Detalhe.Selecionar(null, null);
                OnSlowTick(null, EventArgs.Empty);
                return;
            }
            foreach (SessionRow r in _rows) _limpas[r.Pid] = r.CmdStartedAt;
            _rows.Clear();
            _rowByPid.Clear();
            _totalSessions = 0; _totalCalls = 0;
            if (_selectedPid != 0) { _sessions.SetSelection(0); _selectedPid = 0; }
            ApplyTexts();
            OnSlowTick(null, EventArgs.Empty);
            ShowNotice(Lang.F("v2ClearedSess", DateTime.Now.ToString("HH:mm:ss")), Theme.Teal, 5);
        }

        private bool Recolhido(NeonPanel np)
        {
            string k;
            return _chave.TryGetValue(np, out k) && _recolhidos.Contains(k);
        }

        private void AlternarOcultarWindows()
        {
            _ocultarWindows = !_ocultarWindows;
            Lang.SetCfg("ocultarWin", _ocultarWindows ? "1" : "0");
            ApplyTexts();
            LayoutDashboard();
            OnSlowTick(null, EventArgs.Empty);

            CpuSnapshot snap = CpuFeed.Atual;
            if (snap != null && snap.Processos.Count > 0)
            {
                int total = snap.Processos.Count;
                int fora = ClassificadorProcessos.SemWindows(snap.Processos).Count;
                if (_ocultarWindows)
                    ShowNotice(Lang.F("v2OcultarWinAviso", total - fora, total), Theme.Teal, 5);
                else
                    ShowNotice(Lang.F("v2MostrarWinAviso", fora, total), Theme.Teal, 5);
            }
        }

        private void AlternarRecolher(NeonPanel np)
        {
            string k;
            if (!_chave.TryGetValue(np, out k)) return;
            if (!_recolhidos.Remove(k)) _recolhidos.Add(k);
            Lang.SetCfg("recolhidos", string.Join(",", _recolhidos.OrderBy(s => s)));
            ApplyTexts();
            LayoutDashboard();
            foreach (NeonPanel p in _chave.Keys) p.Invalidate(true);
        }

        private void CarregarRecolhidos()
        {
            string v = Lang.GetCfg("recolhidos");
            foreach (string s in v.Split(','))
                if (_chave.ContainsValue(s.Trim())) _recolhidos.Add(s.Trim());
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            AplicarMoldura(Handle);
        }

        internal static void AplicarMoldura(IntPtr hwnd)
        {
            try
            {
                int dark = Theme.Dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
                DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
                int canto = 1;
                DwmSetWindowAttribute(hwnd, 33, ref canto, sizeof(int));
                int legenda = ColorTranslator.ToWin32(Theme.TopBar);
                DwmSetWindowAttribute(hwnd, 35, ref legenda, sizeof(int));
                int texto = ColorTranslator.ToWin32(Theme.Text);
                DwmSetWindowAttribute(hwnd, 36, ref texto, sizeof(int));
                int borda = ColorTranslator.ToWin32(Theme.Border);
                DwmSetWindowAttribute(hwnd, 34, ref borda, sizeof(int));
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

            LogOpen ab = new LogOpen();
            if (_opts.LogPath.Length > 0)
            {
                ab = LogFile.Abrir(_opts.LogPath);
                _log = ab.Writer;
                _logPath = ab.Caminho;
            }

            BuildTray();
            SincronizarDrawer();

            RunInfo info = new RunInfo();
            info.Version = Program.Version;
            info.Engine = _engineName;
            info.IntervalMs = _opts.Interval;
            info.Filter = _opts.Filter;
            info.LogPath = _logPath;
            info.Aviso = ab.Aviso();
            info.IsGui = true;
            info.Janelas = _janelas != null;
            AppendBanner(info);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { if (_tick != null) _tick.Stop(); } catch { }
            try { if (_slow != null) _slow.Stop(); } catch { }
            try { if (_cpuSampler != null) { _cpuSampler.Parar(); _cpuSampler = null; } } catch { }
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
                if (_drawer != null && _drawer.Visible) { FecharDrawer(); return true; }
                if (_selectedPid != 0) { _sessions.SetSelection(0); return true; }
                if (_inTray) { RestoreFromTray(); return true; }
                Close();
                return true;
            }
            if (keyData == Keys.Space && !(ActiveControl is TextBox) && !(ActiveControl is SearchBox)) { SetPaused(!_paused); return true; }
            if (keyData == Keys.F5) { LimparFluxo(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void BuildTray()
        {
            try
            {
                _tray = new NotifyIcon();
                Icon ico = AppIcon.Get(24);
                _tray.Icon = ico != null ? ico : SystemIcons.Application;
                _tray.Text = Lang.T("trayTitle");

                ContextMenuStrip menu = new ContextMenuStrip { Renderer = new NeonMenuRenderer(), ShowImageMargin = false };
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
                if (_tray != null)
                {
                    _tray.Visible = true;
                    _tray.ShowBalloonTip(2500, "cmdwatch", Lang.T("trayTip"), ToolTipIcon.Info);
                }
            }
            catch { }
            Hide();
            try { if (_tick != null) _tick.Stop(); } catch { }
            try { if (_slow != null) _slow.Stop(); } catch { }
            try { if (_cpuSampler != null) _cpuSampler.Parar(); } catch { }
        }

        private void RestoreFromTray()
        {
            _inTray = false;
            try { if (_tray != null) _tray.Visible = false; } catch { }
            _checkTray.Checked = false;
            _checkTray.Invalidate();
            SincronizarDrawer();
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            try { if (_tick != null && !_tick.Enabled) _tick.Start(); } catch { }
            try { if (_slow != null && !_slow.Enabled) _slow.Start(); } catch { }
            try { if (_cpuSampler != null) _cpuSampler.Iniciar(); } catch { }
            OnSlowTick(null, EventArgs.Empty);
        }

        private void OpenLogFile()
        {
            try
            {
                if (_logPath.Length > 0 && File.Exists(_logPath))
                    Process.Start("notepad.exe", "\"" + _logPath + "\"");
                else
                    ShowNotice(Lang.T("trayNoLog"), Theme.Amber, 4);
            }
            catch { }
        }

        private void ExitApp()
        {
            try { if (_tray != null) _tray.Visible = false; } catch { }
            Close();
            Application.Exit();
        }

        private void OnLanguageIndexChanged(int index)
        {
            Lang.Set(index == 1 ? Idioma.En : Idioma.PtBr);
            Lang.Save();
            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            ApplyTexts();
            LayoutDashboard();
            RebuildStream();
            ApplyTrayTexts();
            try { if (_ctxMenu.Visible) _ctxMenu.Close(); } catch { }
            foreach (Control c in new Control[] { _pSessions, _pStream, _pActivity, _pWindows, _sessions, _windows, _activity, _stats, _checkTray, _cpu, _cpu.Tabela, _cpu.Detalhe, _side, _rail, _drawer })
                if (c != null) c.Invalidate();
            Invalidate();
        }

        private void OnSessionContext(int pid, Point screenPoint)
        {
            SessionRow r;
            if (!_rowByPid.TryGetValue(pid, out r)) return;

            _ctxMenu.Items.Clear();
            ToolStripMenuItem head = new ToolStripMenuItem(r.Script + "   (PID " + r.Pid + ")   ") { Enabled = false };
            _ctxMenu.Items.Add(head);
            _ctxMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem fechar = new ToolStripMenuItem(Lang.T("menuClose") + "   ");
            fechar.Enabled = !r.Closed;
            fechar.Click += delegate
            {
                SessionRow alvo = r;
                try { BeginInvoke(new Action(delegate { AskCloseSession(alvo); })); }
                catch { AskCloseSession(alvo); }
            };
            _ctxMenu.Items.Add(fechar);

            try { _ctxMenu.Show(screenPoint); } catch { }
        }

        private void AskCloseSession(SessionRow r)
        {
            string msg = Lang.F("confirmCloseMsg", r.Script, r.Pid);
            bool ok = NeonDialog.Confirm(this, Lang.T("confirmCloseTitle"), msg,
                Lang.T("confirmYes"), Lang.T("confirmNo"), Theme.Red);
            if (!ok) return;

            TerminateResult res = ProcessControl.Terminate(r.Pid, true, r.CmdStartedAt);
            if (res.Ok)
            {
                r.Closed = true;
                r.ClosedAt = DateTime.Now;
                _sessions.Invalidate();
                ShowNotice(Lang.F("noticeClosed", r.Script), Theme.Teal, 6);
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

        private void OnWindowContext(IntPtr hwnd, Point screenPoint)
        {
            WindowRow r;
            if (!_winByHwnd.TryGetValue(hwnd, out r)) return;
            _ctxMenu.Items.Clear();
            string nome = r.AuthorName.Length > 0 ? r.AuthorName : "?";
            ToolStripMenuItem head = new ToolStripMenuItem(nome + "   (PID " + r.AuthorPid + ")   ") { Enabled = false };
            _ctxMenu.Items.Add(head);
            _ctxMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem encerrar = new ToolStripMenuItem(Lang.T("menuCloseWin") + "   ");
            encerrar.Enabled = !r.Closed && r.AuthorPid > 0;
            encerrar.Click += delegate
            {
                WindowRow alvo = r;
                try { BeginInvoke(new Action(delegate { AskCloseWindow(alvo); })); }
                catch { AskCloseWindow(alvo); }
            };
            _ctxMenu.Items.Add(encerrar);
            try { _ctxMenu.Show(screenPoint); } catch { }
        }

        private void AskCloseWindow(WindowRow r)
        {
            string nome = r.AuthorName.Length > 0 ? r.AuthorName : ("PID " + r.AuthorPid);
            string msg = Lang.F("confirmCloseWinMsg", nome, r.AuthorPid);
            string baixo = r.AuthorName.ToLowerInvariant();
            if (baixo == "explorer.exe" || baixo == "winlogon.exe" || baixo == "csrss.exe"
                || baixo == "services.exe" || baixo == "lsass.exe" || baixo == "dwm.exe"
                || baixo == "system" || baixo == "smss.exe")
                msg += Environment.NewLine + Environment.NewLine + Lang.T("confirmCloseWinCritical");
            bool ok = NeonDialog.Confirm(this, Lang.T("confirmCloseWinTitle"), msg,
                Lang.T("confirmYes"), Lang.T("confirmNo"), Theme.Red);
            if (!ok) return;
            TerminateResult res = ProcessControl.Terminate(r.AuthorPid, true, r.AuthorStartedAt);
            if (res.Ok)
            {
                r.Closed = true;
                r.ClosedAt = DateTime.Now;
                _windows.Invalidate();
                ShowNotice(Lang.F("noticeWinClosed", nome), Theme.Teal, 6);
            }
            else if (res.AlreadyGone)
            {
                ShowNotice(Lang.F("noticeCloseGone", nome), Theme.Amber, 6);
            }
            else
            {
                ShowNotice(Lang.F("noticeCloseFail", nome, res.Error), Theme.Red, 10);
            }
        }

        private void ShowNotice(string text, Color color, int seconds)
        {
            _notice = text ?? "";
            _noticeColor = color;
            _noticeUntil = DateTime.Now.AddSeconds(seconds);
            Invalidate();
        }

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

        private void ApplyWindow(WindowRecord rec)
        {
            if (rec == null || rec.Auxiliar) return;
            try
            {
                WindowRow r;
                if (!_winByHwnd.TryGetValue(rec.Hwnd, out r))
                {
                    r = new WindowRow { Hwnd = rec.Hwnd, StartedAt = rec.FirstSeen };
                    _winByHwnd[rec.Hwnd] = r;
                    _winOrder.Add(rec.Hwnd);
                }
                r.Title = rec.Title;
                r.AuthorName = rec.AuthorName;
                r.AuthorPid = rec.AuthorPid;
                r.AuthorStartedAt = rec.AuthorStartedAt;
                r.Origin = "";
                if (rec.Chain.Count > 0)
                    r.Origin = string.Join(" <= ", rec.Chain.Where(p => p != null && p.Name.Length > 0).Select(p => p.Name + " (" + p.Pid + ")"));
                r.Author = (rec.AuthorName.Length > 0 ? rec.AuthorName : "?") + " (" + rec.AuthorPid + ")";
                if (rec.Chain.Count > 0)
                {
                    ProcInfo pai = rec.Chain[0];
                    if (pai != null && pai.Name.Length > 0)
                        r.Author = r.Author + " <= " + pai.Name + "(" + pai.Pid + ")";
                }
                r.OriginTip = r.Origin;
                foreach (ProcInfo p in rec.Chain.Where(p => p != null && p.CmdLine.Length > 0))
                    r.OriginTip += Environment.NewLine + p.Name + " (" + p.Pid + "): " + Mascara.Aplicar(p.CmdLine);
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
            if (s == null || _rowByPid.ContainsKey(s.CmdPid)) return;
            DateTime limpa;
            if (_limpas.TryGetValue(s.CmdPid, out limpa) && limpa == s.CmdStartedAt) return;
            SessionRow r = new SessionRow
            {
                Pid = s.CmdPid,
                Script = s.ScriptName,
                StartedAt = s.StartedAt,
                CmdStartedAt = s.CmdStartedAt
            };
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
            if (!_rowByPid.TryGetValue(s.CmdPid, out r)) { _limpas.Remove(s.CmdPid); return; }
            r.Closed = true;
            r.ClosedAt = DateTime.Now;
            r.Steps = s.StepCount;
        }

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

        private static List<HumanLine> FormatEntry(StreamEntry en)
        {
            if (en.Event != null) return HumanFormat.Render(en.Event);
            if (en.Banner != null) return HumanFormat.Banner(en.Banner);
            return new List<HumanLine>();
        }

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

        private void AtualizarDetalheCpu()
        {
            CpuSnapshot snap = CpuFeed.Atual;
            if (snap == null || snap.Processos.Count == 0) return;
            CpuRow sel = null;
            foreach (CpuRow r in snap.Processos)
                if (r.Pid == _cpu.Tabela.SelecionadoPid) { sel = CopiaCpu(r); break; }
            if (sel != null)
            {
                CpuUser dono;
                if (!_cpuUsuarios.TryGetValue(sel.Pid, out dono) || dono.Inicio != sel.StartedAt)
                {
                    string usr;
                    bool elev;
                    if (NativeProcess.TryGetUserAndIntegrity(sel.Pid, out usr, out elev))
                    {
                        dono = new CpuUser { Inicio = sel.StartedAt, User = usr, Elevado = elev };
                        _cpuUsuarios[sel.Pid] = dono;
                        if (_cpuUsuarios.Count > 2000) _cpuUsuarios.Clear();
                    }
                    else dono = null;
                }
                if (dono != null) { sel.User = dono.User; sel.Elevado = dono.Elevado; }
                _cpu.Detalhe.Selecionar(sel, snap.Processos);
            }
            else _cpu.Detalhe.Selecionar(null, snap.Processos);
        }

        private static bool Contem(string texto, string busca)
        {
            return texto != null && texto.IndexOf(busca, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                _pulseBright = !_pulseBright;
                _stream.CursorVisible = _pulseBright;
                _stream.Invalidate();

                _eventsPerSec.Enqueue(_evAcc);
                _opensPerSec.Enqueue(_openAcc);
                _evAcc = 0; _openAcc = 0;
                while (_eventsPerSec.Count > 60) _eventsPerSec.Dequeue();
                while (_opensPerSec.Count > 60) _opensPerSec.Dequeue();

                _activity.SeriesA = new List<double>(_eventsPerSec);
                _activity.SeriesB = new List<double>(_opensPerSec);
                _activity.Invalidate();

                if (_notice.Length > 0 && DateTime.Now >= _noticeUntil)
                {
                    _notice = "";
                }
                Invalidate(new Rectangle(0, 0, ClientSize.Width, TopH));
            }
            catch { }
        }

        private void OnSlowTick(object sender, EventArgs e)
        {
            try
            {
                string q = _busca != null ? _busca.Box.Text.Trim() : "";
                _sessions.Rows = _rows.Where(r => (q.Length == 0 || Contem(r.Script, q) || Contem(r.Caller, q) || r.Pid.ToString() == q)
                                                  && (_filtroSessoes == 0 || (_filtroSessoes == 1) != r.Closed))
                                      .OrderByDescending(r => r.StartedAt).ToList();
                _sessions.Invalidate();
                _pSessions.TitleValue = _sessions.Rows.Count.ToString();

                var winRows = new List<WindowRow>();
                foreach (IntPtr h in _winOrder)
                {
                    WindowRow r;
                    if (!_winByHwnd.TryGetValue(h, out r)) continue;
                    if (q.Length > 0 && !Contem(r.Title, q) && !Contem(r.Author, q) && !Contem(r.Origin, q) && !Contem(r.OriginTip, q) && !Contem(r.CmdLine, q)) continue;
                    winRows.Add(r);
                }
                _windows.Rows = _windows.Ordenar(winRows);
                _windows.Invalidate();
                _pWindows.TitleValue = _windows.Rows.Count.ToString();
                _pWindows.Invalidate();

                CpuSnapshot snap = CpuFeed.Atual;
                int visiveisProcessos = 0;
                if (snap != null && snap.Processos.Count > 0)
                {
                    _cpuTotal = snap.UsoTotal;
                    List<CpuRow> visiveis = snap.Processos;
                    if (_ocultarWindows) visiveis = ClassificadorProcessos.SemWindows(snap.Processos);
                    visiveisProcessos = visiveis.Count;

                    bool retratoNovo = !ReferenceEquals(snap, _cpuUltimoRetrato);
                    bool filtroNovo = _cpuUltimoFiltro != q;
                    bool ocultoNovo = _cpuOcultoAnterior != _ocultarWindows;
                    if (_abaCpu && (retratoNovo || filtroNovo || ocultoNovo))
                    {
                        _cpuUltimoRetrato = snap;
                        _cpuUltimoFiltro = q;
                        _cpuOcultoAnterior = _ocultarWindows;

                        _cpu.Tabela.Filtrar(visiveis, q);
                        _cpuLinhas = _cpu.Tabela.Rows.Count;

                        if ((_cpu.Tabela.Filtro.Length > 0 || _ocultarWindows) && _cpu.Tabela.SelecionadoPid >= 0)
                        {
                            bool visivel = false;
                            foreach (CpuRow r in _cpu.Tabela.Rows)
                                if (r.Pid == _cpu.Tabela.SelecionadoPid) { visivel = true; break; }
                            if (!visivel) _cpu.Tabela.Selecionar(-1);
                        }

                        if (_cpu.Tabela.SelecionadoPid < 0 && _cpu.Tabela.Rows.Count > 0)
                            _cpu.Tabela.Selecionar(_cpu.Tabela.Rows[0].Pid);

                        AtualizarDetalheCpu();
                    }
                }

                _pSessions.TabValues = new[] { _sessions.Rows.Count.ToString(), visiveisProcessos.ToString() };
                _pSessions.Invalidate();

                _side.SetBadge(_navSessoes, _sessions.Rows.Count.ToString());
                _side.SetBadge(_navCpu, visiveisProcessos > 0 ? visiveisProcessos.ToString() : "");
                _side.SetBadge(_navJanelas, _windows.Rows.Count.ToString());

                string ts = "cartao_sessoes|linhas=" + _sessions.Rows.Count + "|" +
                            string.Join(",", _sessions.Rows.Take(12).Select(r => r.Script)) +
                            "|cpu=" + visiveisProcessos + "|cpu_linhas=" + _cpuLinhas +
                            "|cpu_nomes=" + string.Join(",", _cpu.Tabela.Rows.Take(10).Select(r => r.Name));
                if (_pSessions.Text != ts) _pSessions.Text = ts;

                MemInfo mem;
                if (MemoriaSistema.Ler(out mem))
                {
                    _memPct = mem.Porcentagem;
                    _stats.SubLabels[3] = HumanFormat.Memoria(mem.Usado) + " / " + HumanFormat.Memoria(mem.Total);
                }
                _stats.SubLabels[2] = Lang.F("v2SubCpu", visiveisProcessos);
                _stats.CpuPercent = _cpuTotal;
                _stats.MemPercent = _memPct;

                int ativas = 0;
                foreach (SessionRow r in _rows) if (!r.Closed) ativas++;
                TimeSpan t = DateTime.Now - _inicio;
                _stats.Values = new[]
                {
                    ativas.ToString(),
                    _totalWindows.ToString(),
                    _cpuTotal.ToString("0.0") + "%",
                    _memPct.ToString("0.0") + "%",
                    _totalSteps.ToString(),
                    ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00")
                };
                _stats.Invalidate();
            }
            catch { }
        }

        private static CpuRow CopiaCpu(CpuRow r)
        {
            return new CpuRow
            {
                Pid = r.Pid,
                ParentPid = r.ParentPid,
                Name = r.Name,
                CmdLine = r.CmdLine,
                Path = r.Path,
                Cpu = r.Cpu,
                CpuMedia = r.CpuMedia,
                MemBytes = r.MemBytes,
                Threads = r.Threads,
                StartedAt = r.StartedAt,
                SessionId = r.SessionId,
                User = r.User,
                Elevado = r.Elevado
            };
        }

        private sealed class CpuUser
        {
            public string User = "";
            public bool Elevado;
            public DateTime Inicio = DateTime.MinValue;
        }
    }
}
