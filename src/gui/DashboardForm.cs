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
    /// Painel visual do cmdwatch. Mostra o fluxo de execucao de scripts .cmd/.bat:
    /// as sessoes abertas, o detalhe de cada comando executado e o grafico de atividade.
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

        // contadores
        private int _totalSessions, _totalSteps, _totalCalls;
        private int _evAcc, _openAcc, _callAcc;
        private readonly List<SessionRow> _rows = new List<SessionRow>();
        private readonly Dictionary<int, SessionRow> _rowByPid = new Dictionary<int, SessionRow>();
        private readonly Queue<double> _eventsPerSec = new Queue<double>();
        private readonly Queue<double> _opensPerSec = new Queue<double>();
        private readonly List<StreamEntry> _entries = new List<StreamEntry>();
        private int _selectedPid;

        // controles
        private NeonPanel _pSessions, _pStream, _pActivity;
        private SessionTable _sessions;
        private RichTextBox _stream;
        private LineChart _activity;
        private NeonCheckBox _checkTray;
        private NeonToggle _lang;
        private NotifyIcon _tray;
        private ToolStripMenuItem _miRestore, _miOpenLog, _miQuit;
        private System.Windows.Forms.Timer _tick, _slow;
        private int _streamLines;
        private bool _followTail = true;
        private const int HintH = 34;
        private const int LangW = 150;

        public DashboardForm(Options opts)
        {
            _opts = opts;
            Text = Lang.T("windowTitle");
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1120, 720);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            KeyPreview = true;

            try { Icon = AppIcon.Get(32); } catch { }

            BuildUi();
            ClientSize = new Size(1460, 900);

            _tick = new System.Windows.Forms.Timer();
            _tick.Interval = 1000;
            _tick.Tick += OnTick;
            _tick.Start();

            _slow = new System.Windows.Forms.Timer();
            _slow.Interval = 700;
            _slow.Tick += OnSlowTick;
            _slow.Start();
        }

        // ------------------------------------------------------------ UI
        private void BuildUi()
        {
            _pSessions = new NeonPanel { Title = Lang.T("sessions"), Accent = Theme.Cyan };
            _pStream = new NeonPanel { Title = Lang.T("flow"), Accent = Theme.Cyan };
            _pActivity = new NeonPanel { Title = Lang.T("activity"), Accent = Theme.Cyan };

            _sessions = new SessionTable();
            _sessions.SelectionChanged += OnSelectionChanged;
            _pSessions.Controls.Add(_sessions);

            _stream = new RichTextBox();
            _stream.BackColor = Theme.PanelDeep;
            _stream.ForeColor = Theme.Text;
            _stream.Font = Theme.Mono;
            _stream.BorderStyle = BorderStyle.None;
            _stream.ReadOnly = true;
            _stream.DetectUrls = false;
            _stream.WordWrap = true;
            _stream.ScrollBars = RichTextBoxScrollBars.None;
            _stream.TabStop = false;
            _stream.MouseWheel += OnStreamMouseWheel;
            _followTail = true;
            _pStream.Controls.Add(_stream);

            _activity = new LineChart { LabelA = Lang.T("cmdPerSec"), LabelB = Lang.T("sessPerSec"), Capacity = 60 };
            _pActivity.Controls.Add(_activity);

            _checkTray = new NeonCheckBox
            {
                Label = Lang.T("trayCheck"),
                Accent = Theme.Green
            };
            _checkTray.CheckedChanged += OnTrayCheckChanged;

            // seletor de idioma: o indice inicial vem da preferencia salva
            _lang = new NeonToggle { Caption = Lang.T("langLabel") };
            _lang.Index = (Lang.Atual == Idioma.En) ? 1 : 0;
            _lang.IndexChanged += OnLanguageIndexChanged;

            Controls.Add(_pSessions);
            Controls.Add(_pStream);
            Controls.Add(_pActivity);
            Controls.Add(_checkTray);
            Controls.Add(_lang);

            LayoutDashboard();
        }

        private void LayoutDashboard()
        {
            if (_pSessions == null || _pStream == null || _pActivity == null || _sessions == null) return;

            int pad = 8;
            int w = ClientSize.Width, h = ClientSize.Height;
            int bottom = h - pad - HintH;

            int sessH = (int)((bottom - pad) * 0.30);
            if (sessH < 132) sessH = 132;
            if (sessH > bottom - pad - 150) sessH = bottom - pad - 150;

            int sessY = pad;
            int midY = sessY + sessH + pad;
            int midH = bottom - midY;
            if (midH < 160) midH = 160;

            _pSessions.SetBounds(pad, sessY, w - pad * 2, sessH);

            int flowW = (int)((w - pad * 3) * 0.60);
            _pStream.SetBounds(pad, midY, flowW, midH);
            _pActivity.SetBounds(pad + flowW + pad, midY, Math.Max(240, w - pad * 3 - flowW), midH);

            _checkTray.SetBounds(pad + 2, h - pad - HintH + 4, 460, 24);
            _lang.SetBounds(w - pad - LangW - 2, h - pad - HintH + 4, LangW, 24);

            Rectangle sa = _pSessions.PlotArea;
            _sessions.SetBounds(sa.X, sa.Y, sa.Width, sa.Height);
            Rectangle ta = _pStream.PlotArea;
            _stream.SetBounds(ta.X, ta.Y, ta.Width, ta.Height);
            Rectangle aa = _pActivity.PlotArea;
            _activity.SetBounds(aa.X, aa.Y, aa.Width, aa.Height);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutDashboard();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Bg);

            string info;
            if (_selectedPid != 0)
            {
                string script = "";
                foreach (SessionRow r in _rows)
                    if (r.Pid == _selectedPid) { script = r.Script; break; }
                info = Lang.F("hintFocus", script, _selectedPid);
            }
            else
            {
                info = Lang.T("hintAll") +
                       (_paused ? "   -   " + Lang.T("paused") : "");
            }

            // a dica nao invade o seletor de idioma no canto direito
            Rectangle hint = new Rectangle(0, ClientSize.Height - HintH,
                Math.Max(200, ClientSize.Width - LangW - 24), HintH);

            TextRenderer.DrawText(e.Graphics, info, Theme.Ui, hint,
                Theme.Dim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        // --------------------------------------------------------- motor
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = 1;
                DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
                DwmSetWindowAttribute(Handle, 19, ref dark, sizeof(int));
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

            string engine = _opts.Engine;
            if (engine == "auto") engine = Program.IsAdmin() ? "wmi" : "polling";
            _engineName = engine;
            _engine = engine == "wmi" ? (IEngine)new WmiEngine() : new PollingEngine(_opts.Interval);
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

            BannerInfo banner = new BannerInfo();
            banner.Kind = 0;
            banner.Version = Program.Version;
            banner.Engine = _engineName;
            banner.EngineExtra = _engineName == "polling" ? " (" + _opts.Interval + " ms)" : Lang.T("motorWmi");
            banner.Filter = _opts.Filter.Length == 0 ? Lang.T("filterNone") : _opts.Filter;
            banner.LogPath = _opts.LogPath.Length == 0 ? Lang.T("logOff") : _opts.LogPath;
            AppendBanner(banner);

            BannerInfo aviso = new BannerInfo();
            aviso.Kind = 1;
            AppendBanner(aviso);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { if (_tick != null) _tick.Stop(); } catch { }
            try { if (_slow != null) _slow.Stop(); } catch { }
            try { if (_engine != null) { _engine.Stop(); _engine.Dispose(); } } catch { }
            try { if (_log != null) { _log.Flush(); _log.Close(); _log = null; } } catch { }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; } } catch { }
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
            if (keyData == Keys.Space) { _paused = !_paused; Invalidate(); return true; }
            if (keyData == Keys.F5) { _entries.Clear(); _stream.Clear(); _streamLines = 0; return true; }
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
            _checkTray.Checked = false;
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
            Text = Lang.T("windowTitle");

            _pSessions.Title = Lang.T("sessions");
            _pStream.Title = Lang.T("flow");
            _pActivity.Title = Lang.T("activity");

            _checkTray.Label = Lang.T("trayCheck");
            _activity.LabelA = Lang.T("cmdPerSec");
            _activity.LabelB = Lang.T("sessPerSec");
            _lang.Caption = Lang.T("langLabel");

            _pSessions.TitleValue = _totalSessions + " " + Lang.T("sessionsShort");
            _pActivity.TitleValue = _totalSteps + " " + Lang.T("stepsShort");
            _pStream.TitleValue = _selectedPid == 0 ? Lang.T("all") : ("PID " + _selectedPid);

            _pSessions.Invalidate();
            _pStream.Invalidate();
            _pActivity.Invalidate();
            _activity.Invalidate();
            _checkTray.Invalidate();
            _lang.Invalidate();
            _sessions.Invalidate();

            RebuildStream();
            ApplyTrayTexts();
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
                    if (ev.Frame != null && ev.Frame.Depth > 0) { _callAcc++; _totalCalls++; }
                    TouchRow(ev);
                    break;
                case "batch_close":
                    CloseRow(ev.Session);
                    break;
            }
        }

        private void AddRow(BatchSession s)
        {
            if (s == null) return;
            if (_rowByPid.ContainsKey(s.CmdPid)) return;
            SessionRow r = new SessionRow();
            r.Pid = s.CmdPid;
            r.Script = s.ScriptName;
            r.StartedAt = s.StartedAt;
            r.Caller = s.Caller != null ? s.Caller.Name + "(" + s.Caller.Pid + ")" : "?";
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

        /// <summary>Formata um evento na linha do fluxo, no idioma corrente.</summary>
        private void FormatEvent(WatchEvent ev, out string line, out Color color)
        {
            string ts = ev.Timestamp.ToString("HH:mm:ss.fff");
            BatchSession s = ev.Session;
            string kind, script, actor, detail;

            if (ev.Type == "batch_open" && s != null)
            {
                kind = Lang.T("kindSessao");
                script = s.ScriptName;
                actor = s.Caller != null ? s.Caller.Name + "(" + s.Caller.Pid + ")" : "?";
                string purpose = (s.Analysis != null && s.Analysis.Purpose.Length > 0) ? s.Analysis.Purpose : Lang.T("noDescription");
                detail = purpose;
                color = Theme.Cyan;
            }
            else if (ev.Type == "batch_step")
            {
                bool nested = ev.Frame != null && ev.Frame.Depth > 0;
                kind = nested ? Lang.T("kindChamada") : Lang.T("kindComando");
                script = (ev.Frame != null ? ev.Frame.ScriptName : (s != null ? s.ScriptName : "?")) +
                         (ev.Step != null ? ":" + ev.Step.Line : "");
                actor = ev.Process != null ? ev.Process.Name + "(" + ev.Process.Pid + ")" : "?";
                if (ev.Step != null && ev.Step.Meaning.Length > 0)
                    detail = ev.Step.Raw + "  ->  " + ev.Step.Meaning;
                else
                {
                    string cl = ev.Process != null ? ev.Process.CmdLine : "";
                    detail = cl.Length > 0 ? Explain.Truncate(cl, 90) : Lang.T("noCmdLine");
                }
                color = nested ? Theme.Magenta : Theme.Yellow;
            }
            else if (ev.Type == "batch_close" && s != null)
            {
                kind = Lang.T("kindFim");
                script = s.ScriptName;
                actor = s.CmdPid.ToString();
                double dur = DateTime.Now.Subtract(s.StartedAt).TotalSeconds;
                detail = Lang.F("closed", dur.ToString("0.0"), s.StepCount);
                color = Theme.Green;
            }
            else
            {
                kind = Lang.T("kindEvento");
                script = "";
                actor = "";
                detail = ev.Message;
                color = Theme.Text;
            }

            line = ts + "  " + Pad(kind, 8) + "  " + Pad(script, 24) + " " + Pad(actor, 22) + " " + Explain.Truncate(detail, 62);
        }

        private static string Pad(string s, int width)
        {
            if (s == null) s = "";
            if (s.Length > width) s = s.Substring(0, Math.Max(1, width - 1)) + "~";
            return s.PadRight(width);
        }

        // ------------------------------------------------------- fluxo
        private void AppendEvent(WatchEvent ev)
        {
            int pid = ev.Session != null ? ev.Session.CmdPid : 0;
            AddEntry(new StreamEntry { Pid = pid, Event = ev });
        }

        private void AppendBanner(BannerInfo b)
        {
            AddEntry(new StreamEntry { Pid = 0, Banner = b });
        }

        private void AddEntry(StreamEntry en)
        {
            _entries.Add(en);
            if (_entries.Count > 4000) _entries.RemoveRange(0, 1000);
            string line; Color color;
            FormatEntry(en, out line, out color);
            if (_selectedPid == 0 || en.Pid == _selectedPid) WriteLine(line, color);
        }

        /// <summary>Formata uma entrada do fluxo (evento ou banner) no idioma corrente.</summary>
        private void FormatEntry(StreamEntry en, out string line, out Color color)
        {
            if (en.Event != null) { FormatEvent(en.Event, out line, out color); return; }

            BannerInfo b = en.Banner;
            if (b != null && b.Kind == 1)
            {
                line = Lang.T("monitoring");
                color = Theme.Dim;
                return;
            }
            if (b == null)
            {
                line = "";
                color = Theme.Text;
                return;
            }
            line = Lang.F("startup", b.Version, b.Engine, b.EngineExtra, b.Filter, b.LogPath);
            color = Theme.Cyan;
        }

        private void WriteLine(string line, Color color)
        {
            try
            {
                _stream.Select(_stream.TextLength, 0);
                _stream.SelectionColor = color;
                _stream.AppendText(line + Environment.NewLine);
                _stream.SelectionColor = Theme.Text;
                if (_followTail) _stream.ScrollToCaret();
                _streamLines++;
                if (_streamLines > 3000)
                {
                    int cut = _stream.GetFirstCharIndexFromLine(800);
                    _stream.Select(0, cut);
                    _stream.SelectedText = "";
                    _streamLines -= 800;
                }
            }
            catch { }
        }

        /// <summary>
        /// Redesenha o fluxo aplicando (ou removendo) o filtro de sessao.
        /// Tambem e usado na troca de idioma: as linhas sao reformateadas.
        /// </summary>
        private void RebuildStream()
        {
            _stream.Clear();
            _streamLines = 0;
            int start = Math.Max(0, _entries.Count - 2000);
            for (int i = start; i < _entries.Count; i++)
            {
                StreamEntry en = _entries[i];
                if (_selectedPid != 0 && en.Pid != _selectedPid) continue;
                string line; Color color;
                FormatEntry(en, out line, out color);
                WriteLine(line, color);
            }
        }

        private void OnSelectionChanged()
        {
            _selectedPid = _sessions.SelectedPid;
            RebuildStream();
            _pStream.TitleValue = _selectedPid == 0 ? Lang.T("all") : ("PID " + _selectedPid);
            _pStream.Invalidate();
            Invalidate();
        }

        private void OnStreamMouseWheel(object sender, MouseEventArgs e)
        {
            try
            {
                int lines = _stream.Lines.Length;
                if (lines < 2) return;
                int current = _stream.GetLineFromCharIndex(_stream.SelectionStart);
                int target = current + ((e.Delta > 0) ? -3 : 3);
                if (target < 0) target = 0;
                if (target > lines - 1) target = lines - 1;
                int idx = _stream.GetFirstCharIndexFromLine(target);
                _stream.Select(idx, 0);
                _stream.ScrollToCaret();
                _followTail = target >= lines - 1;
            }
            catch { }
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
            }
            catch { }
        }

        private void OnSlowTick(object sender, EventArgs e)
        {
            try
            {
                _sessions.Rows = _rows.OrderByDescending(r => r.StartedAt).ToList();
                _sessions.Invalidate();

                _pSessions.TitleValue = _totalSessions + " " + Lang.T("sessionsShort");
                _pSessions.Invalidate();
                _pActivity.TitleValue = _totalSteps + " " + Lang.T("stepsShort");
                _pActivity.Invalidate();
            }
            catch { }
        }
    }
}
