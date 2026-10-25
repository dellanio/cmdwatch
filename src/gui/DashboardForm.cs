using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace CmdWatch.Gui
{
    /// <summary>
    /// Painel visual do cmdwatch. Mostra apenas o fluxo de execucao de scripts
    /// .cmd/.bat: sessoes ativas, comandos observados, chamadas encadeadas e a
    /// linha do tempo, desenhados no mesmo idioma visual da referencia.
    /// </summary>
    internal sealed class DashboardForm : Form
    {
        private readonly Options _opts;
        private readonly Watcher _watcher = new Watcher();
        private IEngine _engine;
        private StreamWriter _log;
        private string _engineName = "polling";
        private bool _paused;

        // contadores
        private int _totalSessions, _totalSteps, _totalCalls;
        private int _evAcc, _openAcc, _callAcc;
        private int _cumSteps;
        private readonly List<SessionRow> _rows = new List<SessionRow>();
        private readonly Dictionary<int, SessionRow> _rowByPid = new Dictionary<int, SessionRow>();
        private readonly Queue<double> _eventsPerSec = new Queue<double>();
        private readonly Queue<double> _opensPerSec = new Queue<double>();
        private readonly List<double> _timelinePoints = new List<double>();

        // controles
        private NeonPanel _pMeters, _pActivity, _pSessions, _pStream, _pTimeline;
        private readonly MeterColumn[] _meters = new MeterColumn[4];
        private LineChart _activity;
        private AreaChart _timeline;
        private SessionTable _sessions;
        private RichTextBox _stream;
        private readonly MiniStat[] _minis = new MiniStat[4];
        private System.Windows.Forms.Timer _tick, _slow;
        private int _streamLines;
        private bool _followTail = true;
        private const int HintH = 16;

        private static readonly Color[] RampSession =
        {
            Theme.Green, Color.FromArgb(0x9E, 0xC8, 0x32), Theme.Red
        };

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
            _pMeters = new NeonPanel { Title = "CMDWATCH", Accent = Theme.Cyan };
            _pActivity = new NeonPanel { Title = "ATIVIDADE .CMD", Accent = Theme.Cyan };
            _pSessions = new NeonPanel { Title = "SESSOES .CMD", Accent = Theme.Cyan };
            _pStream = new NeonPanel { Title = "FLUXO DE COMANDOS", Accent = Theme.Cyan };
            _pTimeline = new NeonPanel { Title = "LINHA DO TEMPO", Accent = Theme.Cyan };

            string[] meterLabels = { "SESS", "CMD", "CALL", "RATE" };
            Color[] meterColors = { Theme.Green, Theme.Red, Theme.Amber, Theme.Blue };
            for (int i = 0; i < 4; i++)
            {
                MeterColumn m = new MeterColumn();
                m.Label = meterLabels[i];
                m.Ramp = i == 0 ? RampSession : new Color[] { meterColors[i] };
                m.ValueText = "0";
                _meters[i] = m;
                _pMeters.Controls.Add(m);
            }

            _activity = new LineChart { LabelA = "COMANDOS/S", LabelB = "SESSOES/S", Capacity = 60 };
            _pActivity.Controls.Add(_activity);

            _sessions = new SessionTable();
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

            _timeline = new AreaChart { Capacity = 120 };
            _pTimeline.Controls.Add(_timeline);

            string[] miniTitles = { "Motor", "Log", "Filtro", "Sessoes" };
            Color[] miniColors = { Theme.Blue, Theme.Green, Theme.Cyan, Theme.Red };
            for (int i = 0; i < 4; i++)
            {
                MiniStat s = new MiniStat();
                s.Title = miniTitles[i];
                s.Accent = miniColors[i];
                _minis[i] = s;
            }

            Controls.Add(_pMeters);
            Controls.Add(_pActivity);
            Controls.Add(_pSessions);
            Controls.Add(_pStream);
            Controls.Add(_pTimeline);
            for (int i = 0; i < 4; i++) Controls.Add(_minis[i]);

            LayoutDashboard();
        }

        private void LayoutDashboard()
        {
            if (_pMeters == null || _pActivity == null || _pSessions == null ||
                _pStream == null || _pTimeline == null || _sessions == null) return;

            int pad = 8;
            int hintH = HintH;
            int w = ClientSize.Width, h = ClientSize.Height;

            int bottom = h - pad - hintH;
            int row3H = Math.Min(104, Math.Max(72, (bottom - pad) * 17 / 100));
            int row3Y = bottom - row3H;
            int avail = row3Y - pad;
            int row2H = avail * 30 / 100;
            int row1H = avail - row2H - pad;
            int row2Y = pad + row1H + pad;

            int metersW = 218;
            int activityW = (int)((w - pad * 3 - metersW) * 0.40);

            _pMeters.SetBounds(pad, pad, metersW, row1H);
            _pActivity.SetBounds(pad + metersW + pad, pad, activityW, row1H);
            _pSessions.SetBounds(pad + metersW + pad + activityW + pad, pad,
                                 Math.Max(200, w - pad * 3 - metersW - activityW), row1H);

            int streamW = (int)((w - pad * 3) * 0.60);
            _pStream.SetBounds(pad, row2Y, streamW, row2H);
            _pTimeline.SetBounds(pad + streamW + pad, row2Y, Math.Max(200, w - pad * 3 - streamW), row2H);

            int miniW = (w - pad * 5) / 4;
            for (int i = 0; i < 4; i++)
                _minis[i].SetBounds(pad + i * (miniW + pad), row3Y, miniW, row3H);

            Rectangle pa = _pMeters.PlotArea;
            int mw = pa.Width / 4;
            for (int i = 0; i < 4; i++)
                _meters[i].SetBounds(pa.X + i * mw + 2, pa.Y, mw - 4, pa.Height);

            Rectangle aa = _pActivity.PlotArea;
            _activity.SetBounds(aa.X, aa.Y, aa.Width, aa.Height);

            Rectangle sa = _pSessions.PlotArea;
            _sessions.SetBounds(sa.X, sa.Y, sa.Width, sa.Height);

            Rectangle ta = _pStream.PlotArea;
            _stream.SetBounds(ta.X, ta.Y, ta.Width, ta.Height);

            Rectangle la = _pTimeline.PlotArea;
            _timeline.SetBounds(la.X, la.Y, la.Width, la.Height);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutDashboard();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Bg);
            string hint = _paused
                ? "PAUSADO - ESPACO retoma   |   F5 limpa o fluxo   |   ESC fecha"
                : "ESPACO pausa   |   F5 limpa o fluxo   |   ESC fecha   |   " + _engineName +
                  " " + _opts.Interval + "ms";
            TextRenderer.DrawText(e.Graphics, hint, Theme.Ui,
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
                DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
                DwmSetWindowAttribute(Handle, 19, ref dark, sizeof(int));   // idem em versoes anteriores
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

            AppendLine("=== cmdwatch " + Program.Version + " - motor " + _engineName +
                       (_engineName == "polling" ? " (" + _opts.Interval + " ms)" : " (eventos WMI)") +
                       " - filtro: " + (_opts.Filter.Length == 0 ? "(todos)" : _opts.Filter) + " ===", Theme.Cyan);
            AppendLine("monitorando execucao de scripts .cmd/.bat ...", Theme.Dim);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { if (_tick != null) _tick.Stop(); } catch { }
            try { if (_slow != null) _slow.Stop(); } catch { }
            try { if (_engine != null) { _engine.Stop(); _engine.Dispose(); } } catch { }
            try { if (_log != null) { _log.Flush(); _log.Close(); _log = null; } } catch { }
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == Keys.Space) { _paused = !_paused; Invalidate(); return true; }
            if (keyData == Keys.F5) { _stream.Clear(); _streamLines = 0; return true; }
            return base.ProcessCmdKey(ref msg, keyData);
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
            FormatEvent(ev, out line, out color);
            AppendLine(line, color);

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
                    _cumSteps++;
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

        private void AppendLine(string line, Color color)
        {
            try
            {
                _stream.Select(_stream.TextLength, 0);
                _stream.SelectionColor = color;
                _stream.AppendText(line + Environment.NewLine);
                _stream.SelectionColor = Theme.Text;
                if (_followTail) _stream.ScrollToCaret();
                _streamLines++;
                if (_streamLines > 2500)
                {
                    int cut = _stream.GetFirstCharIndexFromLine(600);
                    _stream.Select(0, cut);
                    _stream.SelectedText = "";
                    _streamLines -= 600;
                }
            }
            catch { }
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

                _timelinePoints.Add(_cumSteps);
                while (_timelinePoints.Count > 120) _timelinePoints.RemoveAt(0);

                _activity.SeriesA = new List<double>(_eventsPerSec);
                _activity.SeriesB = new List<double>(_opensPerSec);
                _activity.Invalidate();

                _timeline.Values = new List<double>(_timelinePoints);
                _timeline.TopLabel = _cumSteps.ToString();
                _timeline.BottomLabel = "0";
                _timeline.Invalidate();

                UpdateMeters();
                Invalidate();
            }
            catch { }
        }

        private void OnSlowTick(object sender, EventArgs e)
        {
            try
            {
                RefreshSessions();
                UpdateMinis();
                _pActivity.TitleValue = _totalSteps + " PASSOS";
                _pActivity.Invalidate();
            }
            catch { }
        }

        private void UpdateMeters()
        {
            int active = 0;
            foreach (SessionRow r in _rows) if (!r.Closed) active++;

            _meters[0].Ratio = Math.Min(1.0, active / 8.0);
            _meters[0].ValueText = active.ToString();
            _meters[1].Ratio = Math.Min(1.0, _totalSteps / 100.0);
            _meters[1].ValueText = _totalSteps.ToString();
            _meters[2].Ratio = Math.Min(1.0, _totalCalls / 20.0);
            _meters[2].ValueText = _totalCalls.ToString();
            _meters[3].Ratio = Math.Min(1.0, (_eventsPerSec.Count > 0 ? _eventsPerSec.Last() : 0) / 10.0);
            _meters[3].ValueText = (_eventsPerSec.Count > 0 ? _eventsPerSec.Last() : 0).ToString();

            for (int i = 0; i < 4; i++) _meters[i].Invalidate();
        }

        private void RefreshSessions()
        {
            List<SessionRow> ordered = _rows.OrderByDescending(r => r.StartedAt).ToList();
            _sessions.Rows = ordered;
            _sessions.Invalidate();
        }

        private void UpdateMinis()
        {
            int active = 0;
            foreach (SessionRow r in _rows) if (!r.Closed) active++;

            _minis[0].Value = _engineName;
            _minis[0].Subtitle = _engineName == "wmi"
                ? "eventos de processo (WMI)"
                : "consulta periodica a cada " + _opts.Interval + " ms";
            _minis[0].Ratio = 1.0;
            _minis[0].Invalidate();

            _minis[1].Value = _log != null ? "ativo" : "off";
            _minis[1].Subtitle = _opts.LogPath.Length > 0 ? _opts.LogPath : "(sem log)";
            _minis[1].Ratio = _log != null ? 1.0 : 0.0;
            _minis[1].Invalidate();

            _minis[2].Value = _opts.Filter.Length > 0 ? "ativo" : "off";
            _minis[2].Subtitle = _opts.Filter.Length > 0 ? _opts.Filter : "(todos os scripts)";
            _minis[2].Ratio = _opts.Filter.Length > 0 ? 1.0 : 0.0;
            _minis[2].Invalidate();

            _minis[3].Value = _totalSessions.ToString();
            _minis[3].Subtitle = _totalSteps + " comandos  -  " + active + " ativas";
            _minis[3].Ratio = _totalSessions > 0 ? Math.Min(1.0, active / 5.0) : 0.0;
            _minis[3].Invalidate();
        }
    }
}
