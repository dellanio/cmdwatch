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
        private NotifyIcon _tray;
        private System.Windows.Forms.Timer _tick, _slow;
        private int _streamLines;
        private bool _followTail = true;
        private const int HintH = 34;

        public DashboardForm(Options opts)
        {
            _opts = opts;
            Text = "cmdwatch - execucao de scripts .cmd/.bat";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1120, 720);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            KeyPreview = true;

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
            _pSessions = new NeonPanel { Title = "SESSOES .CMD", Accent = Theme.Cyan };
            _pStream = new NeonPanel { Title = "FLUXO DE COMANDOS", Accent = Theme.Cyan };
            _pActivity = new NeonPanel { Title = "ATIVIDADE .CMD", Accent = Theme.Cyan };

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

            _activity = new LineChart { LabelA = "COMANDOS/S", LabelB = "SESSOES/S", Capacity = 60 };
            _pActivity.Controls.Add(_activity);

            _checkTray = new NeonCheckBox
            {
                Label = "Enviar ao relogio do Windows e continuar monitorando",
                Accent = Theme.Green
            };
            _checkTray.CheckedChanged += OnTrayCheckChanged;

            Controls.Add(_pSessions);
            Controls.Add(_pStream);
            Controls.Add(_pActivity);
            Controls.Add(_checkTray);

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
                info = "FOCO: " + script + " (pid " + _selectedPid +
                       ")   -   clique de novo na sessao para voltar a mostrar todas";
            }
            else
            {
                info = "Clique numa sessao para ver apenas o fluxo dela" +
                       (_paused ? "   -   PAUSADO" : "");
            }

            TextRenderer.DrawText(e.Graphics, info, Theme.Ui,
                new Rectangle(0, ClientSize.Height - HintH, ClientSize.Width, HintH),
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

            AppendLine("=== cmdwatch " + Program.Version + " - motor " + _engineName +
                       (_engineName == "polling" ? " (" + _opts.Interval + " ms)" : " (eventos WMI)") +
                       " - filtro: " + (_opts.Filter.Length == 0 ? "(todos)" : _opts.Filter) +
                       " - log: " + (_opts.LogPath.Length == 0 ? "(desativado)" : _opts.LogPath) + " ===",
                       Theme.Cyan, 0);
            AppendLine("monitorando execucao de scripts .cmd/.bat ...", Theme.Dim, 0);
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
                _tray.Icon = SystemIcons.Application;
                _tray.Text = "cmdwatch - monitor de execucao";
                ContextMenuStrip menu = new ContextMenuStrip();
                menu.Items.Add("Restaurar painel", null, delegate { RestoreFromTray(); });
                menu.Items.Add("Abrir arquivo de log", null, delegate { OpenLogFile(); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Sair do cmdwatch", null, delegate { ExitApp(); });
                _tray.ContextMenuStrip = menu;
                _tray.DoubleClick += delegate { RestoreFromTray(); };
                _tray.Visible = false;
            }
            catch { _tray = null; }
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
                _tray.ShowBalloonTip(2500, "cmdwatch",
                    "Monitorando em segundo plano. Clique no icone da bandeja para restaurar o painel.",
                    ToolTipIcon.Info);
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
                    _tray.ShowBalloonTip(2500, "cmdwatch", "Nenhum arquivo de log gerado ainda.", ToolTipIcon.Info);
            }
            catch { }
        }

        private void ExitApp()
        {
            try { if (_tray != null) _tray.Visible = false; } catch { }
            Close();
            Application.Exit();
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
            string line;
            Color color;
            int pid = ev.Session != null ? ev.Session.CmdPid : 0;
            FormatEvent(ev, out line, out color);
            AppendLine(line, color, pid);

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

        private void FormatEvent(WatchEvent ev, out string line, out Color color)
        {
            string ts = ev.Timestamp.ToString("HH:mm:ss.fff");
            BatchSession s = ev.Session;
            string kind, script, actor, detail;

            if (ev.Type == "batch_open" && s != null)
            {
                kind = "SESSAO ";
                script = s.ScriptName;
                actor = s.Caller != null ? s.Caller.Name + "(" + s.Caller.Pid + ")" : "?";
                string purpose = (s.Analysis != null && s.Analysis.Purpose.Length > 0) ? s.Analysis.Purpose : "sem descricao";
                detail = purpose;
                color = Theme.Cyan;
            }
            else if (ev.Type == "batch_step")
            {
                bool nested = ev.Frame != null && ev.Frame.Depth > 0;
                kind = nested ? "CHAMADA" : "COMANDO";
                script = (ev.Frame != null ? ev.Frame.ScriptName : (s != null ? s.ScriptName : "?")) +
                         (ev.Step != null ? ":" + ev.Step.Line : "");
                actor = ev.Process != null ? ev.Process.Name + "(" + ev.Process.Pid + ")" : "?";
                if (ev.Step != null && ev.Step.Meaning.Length > 0)
                    detail = ev.Step.Raw + "  ->  " + ev.Step.Meaning;
                else
                {
                    string cl = ev.Process != null ? ev.Process.CmdLine : "";
                    detail = cl.Length > 0 ? Explain.Truncate(cl, 90) : "(sem linha de comando)";
                }
                color = nested ? Theme.Magenta : Theme.Yellow;
            }
            else if (ev.Type == "batch_close" && s != null)
            {
                kind = "FIM    ";
                script = s.ScriptName;
                actor = s.CmdPid.ToString();
                double dur = DateTime.Now.Subtract(s.StartedAt).TotalSeconds;
                detail = dur.ToString("0.0") + "s  -  " + s.StepCount + " comando(s)";
                color = Theme.Green;
            }
            else
            {
                kind = "EVENTO ";
                script = "";
                actor = "";
                detail = ev.Message;
                color = Theme.Text;
            }

            line = ts + "  " + kind + "  " + Pad(script, 24) + " " + Pad(actor, 22) + " " + Explain.Truncate(detail, 62);
        }

        private static string Pad(string s, int width)
        {
            if (s == null) s = "";
            if (s.Length > width) s = s.Substring(0, Math.Max(1, width - 1)) + "~";
            return s.PadRight(width);
        }

        // ------------------------------------------------------- fluxo
        private void AppendLine(string line, Color color, int pid)
        {
            _entries.Add(new StreamEntry { Pid = pid, Line = line, Color = color });
            if (_entries.Count > 4000) _entries.RemoveRange(0, 1000);
            if (_selectedPid == 0 || pid == _selectedPid) WriteLine(line, color);
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

        /// <summary>Redesenha o fluxo aplicando (ou removendo) o filtro de sessao.</summary>
        private void RebuildStream()
        {
            _stream.Clear();
            _streamLines = 0;
            int start = Math.Max(0, _entries.Count - 2000);
            for (int i = start; i < _entries.Count; i++)
            {
                StreamEntry en = _entries[i];
                if (_selectedPid != 0 && en.Pid != _selectedPid) continue;
                WriteLine(en.Line, en.Color);
            }
        }

        private void OnSelectionChanged()
        {
            _selectedPid = _sessions.SelectedPid;
            RebuildStream();
            _pStream.TitleValue = _selectedPid == 0 ? "TODAS" : ("PID " + _selectedPid);
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

                _pSessions.TitleValue = _totalSessions + " SESSOES";
                _pSessions.Invalidate();
                _pActivity.TitleValue = _totalSteps + " PASSOS";
                _pActivity.Invalidate();
            }
            catch { }
        }
    }
}
