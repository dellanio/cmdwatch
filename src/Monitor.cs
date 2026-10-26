using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CmdWatch
{
    // ---------------------------------------------------------- Modelo
    internal sealed class ProcInfo
    {
        public int Pid;
        public int ParentPid;
        public string Name = "";
        public string CmdLine = "";
        public string Path = "";
        public DateTime StartedAt = DateTime.MinValue;
        public uint SessionId;
        public string User = "";
        public bool HasCmdLine => !string.IsNullOrEmpty(CmdLine);

        public string ExeKey
        {
            get
            {
                try { return BatchFile.ExeKeyOf(CmdLine); } catch { return ""; }
            }
        }
    }

    internal sealed class BatchSession
    {
        public int CmdPid;
        public string ScriptPath = "";
        public string ScriptName = "";
        public string ScriptArgs = "";
        public string RawCmdLine = "";
        public DateTime StartedAt;
        public ProcInfo Caller;
        public readonly List<ProcInfo> Chain = new List<ProcInfo>();
        public BatchFile Analysis;
        public int LastLine;
        public int StepCount;
        public ScriptFrame CurrentFrame;
        public ScriptFrame RootFrame;
        public bool Closed;
    }

    internal sealed class WatchEvent
    {
        public string Type = "";
        public DateTime Timestamp;
        public BatchSession Session;
        public ProcInfo Process;
        public BatchStep Step;
        public ScriptFrame Frame;
        public string Message = "";
        public double Confidence;
    }

    /// <summary>Cada .cmd em execucao vira um "quadro" com sua propria analise.</summary>
    internal sealed class ScriptFrame
    {
        public int CmdPid;
        public string ScriptPath = "";
        public string ScriptName = "";
        public string ScriptArgs = "";
        public BatchFile Analysis;
        public BatchSession Session;
        public ScriptFrame Parent;
        public int Depth;
        public bool ChildrenBuilt;
        public readonly List<ScriptFrame> Children = new List<ScriptFrame>();

        // --- diagnostico da chamada ---
        /// <summary>True quando o quadro veio de um cmd.exe realmente observado.</summary>
        public bool Observed;
        /// <summary>
        /// True quando o cmd.exe roda um script que ainda nao sabemos qual.
        /// O proprio cmd.exe zera a linha de comando do filho quando vai executar
        /// um .cmd: o nome do script so existe no texto do script que o chamou.
        /// </summary>
        public bool UnknownScript;
        /// <summary>Scripts possiveis enquanto o script do processo nao e conhecido.</summary>
        public List<ScriptFrame> Candidates;
        /// <summary>True quando a chamada acontece no MESMO processo (linha 'call x.cmd').</summary>
        public bool SameProcess;
        /// <summary>Linha do script pai que chama este script.</summary>
        public BatchStep CallStep;
        /// <summary>True quando a linha de chamada ja foi registrada no fluxo.</summary>
        public bool CallEmitted;
    }

    // ------------------------------------------------------- Engines
    internal interface IEngine : IDisposable
    {
        string Name { get; }
        void Start(Action<ProcInfo> onStart, Action<int> onExit);
        void Stop();
    }

    /// <summary>Motor por consulta periodica a Win32_Process. Nao exige administracao.</summary>
    internal sealed class PollingEngine : IEngine
    {
        public static bool Verbose;
        private readonly int _intervalMs;
        private readonly Dictionary<int, ProcInfo> _known = new Dictionary<int, ProcInfo>();
        private Thread _thread;
        private volatile bool _running;

        public string Name { get { return "polling"; } }

        public PollingEngine(int intervalMs) { _intervalMs = Math.Max(50, intervalMs); }

        public void Start(Action<ProcInfo> onStart, Action<int> onExit)
        {
            _running = true;
            Seed();
            _thread = new Thread(() => Loop(onStart, onExit));
            _thread.IsBackground = true;
            _thread.Start();
        }

        private void Seed()
        {
            try
            {
                foreach (ProcInfo p in Snapshot()) _known[p.Pid] = p;
            }
            catch { }
        }

        private void Loop(Action<ProcInfo> onStart, Action<int> onExit)
        {
            while (_running)
            {
                try
                {
                    var snap = Snapshot();
                    var seen = new HashSet<int>();
                    foreach (ProcInfo p in snap)
                    {
                        seen.Add(p.Pid);
                        ProcInfo old;
                        bool isNew = !_known.TryGetValue(p.Pid, out old);
                        // Toolhelp nao expoe hora de criacao: detecta reuso de PID pela troca de nome
                        if (!isNew && old != null && old.Name.Length > 0 && p.Name.Length > 0 &&
                            !old.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase))
                            isNew = true;

                        if (isNew)
                        {
                            ProcInfo full = Hydrate(p);
                            _known[p.Pid] = full;
                            try { onStart(full); }
                            catch (Exception ex)
                            {
                                if (Verbose)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("  [erro ao processar pid " + p.Pid + "] " + ex.GetType().Name + ": " + ex.Message);
                                    Console.WriteLine(ex.StackTrace);
                                    Console.ForegroundColor = ConsoleColor.Gray;
                                }
                            }
                        }
                    }
                    var dead = _known.Keys.Where(k => !seen.Contains(k)).ToList();
                    foreach (int pid in dead)
                    {
                        _known.Remove(pid);
                        try { onExit(pid); } catch { }
                    }
                }
                catch { }
                Thread.Sleep(_intervalMs);
            }
        }

        /// <summary>Enumera processos: caminho nativo (rapido) com fallback para WMI.</summary>
        public static List<ProcInfo> Snapshot()
        {
            List<ProcInfo> native = null;
            try { native = NativeProcess.Snapshot(); } catch { }
            if (native != null && native.Count > 0) return native;
            return PollingEngine.QuerySnapshot(false);
        }

        /// <summary>Completa um processo com command line e caminho do executavel.</summary>
        /// <summary>Completa um processo com command line e caminho do executavel (via PEB, sem WMI).</summary>
        private static ProcInfo Hydrate(ProcInfo p)
        {
            try
            {
                string cl; string ip;
                if (NativeProcess.TryGetProcessStrings(p.Pid, p.Name, out cl, out ip))
                {
                    if (cl.Length > 0) p.CmdLine = cl;
                    if (ip.Length > 0) p.Path = ip;
                }
            }
            catch { }
            return p;
        }

        public static ProcInfo HydrateProcess(ProcInfo p) { return Hydrate(p); }

        public static List<ProcInfo> QuerySnapshot(bool withCmdLine)
        {
            var list = new List<ProcInfo>();
            string cols = withCmdLine
                ? "ProcessId, ParentProcessId, Name, CommandLine, CreationDate, SessionId, ExecutablePath"
                : "ProcessId, ParentProcessId, Name, CreationDate, SessionId";
            using (var searcher = new ManagementObjectSearcher("SELECT " + cols + " FROM Win32_Process"))
            using (ManagementObjectCollection res = searcher.Get())
            {
                foreach (ManagementObject mo in res)
                {
                    ProcInfo pi = new ProcInfo();
                    pi.Pid = ToInt(Prop(mo, "ProcessId"));
                    pi.ParentPid = ToInt(Prop(mo, "ParentProcessId"));
                    pi.Name = Str(Prop(mo, "Name"));
                    pi.CmdLine = Str(Prop(mo, "CommandLine"));
                    pi.Path = Str(Prop(mo, "ExecutablePath"));
                    pi.SessionId = (uint)ToInt(Prop(mo, "SessionId"));
                    pi.StartedAt = ToDate(Prop(mo, "CreationDate"));
                    list.Add(pi);
                }
            }
            return list;
        }

        public static ProcInfo FetchDetail(int pid)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, ParentProcessId, Name, CommandLine, CreationDate, SessionId, ExecutablePath FROM Win32_Process WHERE ProcessId=" + pid))
                using (ManagementObjectCollection res = searcher.Get())
                {
                    foreach (ManagementObject mo in res)
                    {
                        ProcInfo pi = new ProcInfo();
                        pi.Pid = ToInt(Prop(mo, "ProcessId"));
                        pi.ParentPid = ToInt(Prop(mo, "ParentProcessId"));
                        pi.Name = Str(Prop(mo, "Name"));
                        pi.CmdLine = Str(Prop(mo, "CommandLine"));
                        pi.Path = Str(Prop(mo, "ExecutablePath"));
                        pi.SessionId = (uint)ToInt(Prop(mo, "SessionId"));
                        pi.StartedAt = ToDate(Prop(mo, "CreationDate"));
                        return pi;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>Le uma propriedade do WMI sem lancar excecao quando o valor e NULL.</summary>
        internal static object Prop(ManagementBaseObject mo, string name)
        {
            try
            {
                foreach (PropertyData pd in mo.Properties)
                    if (string.Equals(pd.Name, name, StringComparison.OrdinalIgnoreCase))
                        return pd.Value;
            }
            catch { }
            return null;
        }

        internal static string Str(object o) { return o == null ? "" : o.ToString(); }

        internal static int ToInt(object o)
        {
            if (o == null) return 0;
            try { return Convert.ToInt32(o, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        internal static DateTime ToDate(object o)
        {
            if (o == null) return DateTime.MinValue;
            try
            {
                string s = o.ToString();
                DateTime d;
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d))
                    return d.ToLocalTime();
                return DateTime.MinValue;
            }
            catch { return DateTime.MinValue; }
        }

        public void Stop() { _running = false; }
        public void Dispose() { Stop(); }
    }

    /// <summary>Motor por evento WMI Win32_ProcessStartTrace. Exige execucao elevada (administrador).</summary>
    internal sealed class WmiEngine : IEngine
    {
        private ManagementEventWatcher _watcher;
        private Thread _sweeper;
        private volatile bool _running;
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly Dictionary<int, ProcInfo> _known = new Dictionary<int, ProcInfo>();

        public string Name { get { return "wmi"; } }

        public void Start(Action<ProcInfo> onStart, Action<int> onExit)
        {
            _running = true;
            foreach (ProcInfo p in PollingEngine.QuerySnapshot(false)) { _known[p.Pid] = p; _seen.Add(p.Pid); }

            _watcher = new ManagementEventWatcher("SELECT * FROM Win32_ProcessStartTrace");
            _watcher.Options.Timeout = TimeSpan.FromMilliseconds(300);
            _watcher.EventArrived += (s, e) =>
            {
                try
                {
                    var ne = e.NewEvent;
                    int pid = PollingEngine.ToInt(PollingEngine.Prop(ne, "ProcessID"));
                    int ppid = PollingEngine.ToInt(PollingEngine.Prop(ne, "ParentProcessID"));
                    string name = PollingEngine.Str(PollingEngine.Prop(ne, "ProcessName"));
                    if (pid <= 0) return;
                    ProcInfo detail = PollingEngine.FetchDetail(pid);
                    ProcInfo pi = detail ?? new ProcInfo();
                    pi.Pid = pid; pi.ParentPid = ppid; if (pi.Name.Length == 0) pi.Name = name;
                    lock (_seen) { _seen.Add(pid); _known[pid] = pi; }
                    onStart(pi);
                }
                catch { }
            };
            _watcher.Start();

            _sweeper = new Thread(() =>
            {
                while (_running)
                {
                    try
                    {
                        var alive = new HashSet<int>();
                        foreach (ProcInfo p in PollingEngine.QuerySnapshot(false)) alive.Add(p.Pid);
                        List<int> dead;
                        lock (_seen) dead = _seen.Where(x => !alive.Contains(x)).ToList();
                        foreach (int d in dead)
                        {
                            lock (_seen) { _seen.Remove(d); _known.Remove(d); }
                            onExit(d);
                        }
                    }
                    catch { }
                    Thread.Sleep(700);
                }
            });
            _sweeper.IsBackground = true;
            _sweeper.Start();
        }

        public void Stop()
        {
            _running = false;
            try { if (_watcher != null) _watcher.Stop(); } catch { }
        }

        public void Dispose() { Stop(); try { if (_watcher != null) _watcher.Dispose(); } catch { } }
    }

    // ------------------------------------------------- Extrator de script
    internal static class BatchCmdLine
    {
        private static readonly Regex RxSwitch = new Regex(@"(?i)^\s*/(c|k|r|s|d|a|f|v|e|x|u|i|t|q|y|n)\s*$");

        /// <summary>Extrai o caminho do .cmd/.bat e os argumentos da linha de comando do cmd.exe.</summary>
        public static string Extract(string cmdLine, out string args, out string switchUsed)
        {
            args = ""; switchUsed = "";
            if (string.IsNullOrEmpty(cmdLine)) return null;

            string rest = StripFirstToken(cmdLine);
            if (rest.Length == 0) return null;

            // varre os tokens guardando a posicao de cada um no texto original:
            // sem isso o caminho se perde quando tem espacos ("com espaco\meu script.cmd")
            List<Tok> tokens = TokenizeComPosicao(rest);
            int ini = -1;

            for (int idx = 0; idx < tokens.Count; idx++)
            {
                string t = tokens[idx].Text;
                if (t.Length == 0) continue;
                if (t.StartsWith("/"))
                {
                    if (RxSwitch.IsMatch(t))
                    {
                        string s = t.Substring(1).ToLowerInvariant();
                        if (s == "c" || s == "k" || s == "r")
                        {
                            switchUsed = "/" + s;
                            ini = idx + 1;
                            break;
                        }
                        continue;
                    }
                    // switch com argumento (ex.: /d /s) ou caminho que comecou com /
                    if (t.Length > 2) continue;
                }
                // primeiro token nao-switch: o proprio comando
                switchUsed = "(sem switch)";
                ini = idx;
                break;
            }
            if (ini < 0) return null;

            // o comando: do primeiro token do comando ate o fim da linha
            string cmd = rest.Substring(Math.Min(tokens[ini].Start, rest.Length)).Trim();

            // regra do cmd para as aspas: com exatamente duas aspas e espacos entre
            // elas, as aspas sao preservadas; caso contrario, o cmd tira o par externo
            // (""c:\pasta com espaco\x.cmd"" -> "c:\pasta com espaco\x.cmd")
            if (cmd.Length >= 2 && cmd[0] == '"' && cmd[cmd.Length - 1] == '"')
            {
                int aspas = 0;
                for (int k = 0; k < cmd.Length; k++) if (cmd[k] == '"') aspas++;
                int espaco = cmd.IndexOf(' ');
                bool preserva = aspas == 2 && espaco > 0 && espaco < cmd.Length - 1;
                if (!preserva) cmd = cmd.Substring(1, cmd.Length - 2).Trim();
            }

            // tokeniza o comando: as aspas internas protegem os espacos do caminho
            var ct = Tokenize(cmd);
            if (ct.Count == 0) return null;

            // procura o primeiro token que seja um script .cmd/.bat
            for (int i = 0; i < ct.Count; i++)
            {
                string tok = ct[i].Trim('"');
                string low = tok.ToLowerInvariant();
                if (low.EndsWith(".cmd") || low.EndsWith(".bat"))
                {
                    args = string.Join(" ", ct.Skip(i + 1).Select(x => x.Trim('"')).ToArray());
                    return tok;
                }
            }
            return null;
        }

        /// <summary>Um token com a posicao em que comeca no texto original.</summary>
        private struct Tok
        {
            public string Text;
            public int Start;
            public int End;
        }

        /// <summary>Tokeniza guardando a posicao de cada token no texto original.</summary>
        private static List<Tok> TokenizeComPosicao(string s)
        {
            var res = new List<Tok>();
            if (string.IsNullOrEmpty(s)) return res;
            int i = 0;
            while (i < s.Length)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i >= s.Length) break;
                int start = i;
                var sb = new StringBuilder();
                if (s[i] == '"')
                {
                    i++;
                    while (i < s.Length)
                    {
                        if (s[i] == '"')
                        {
                            if (i + 1 < s.Length && s[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                            i++; break;
                        }
                        sb.Append(s[i]); i++;
                    }
                }
                else
                {
                    while (i < s.Length && !char.IsWhiteSpace(s[i])) { sb.Append(s[i]); i++; }
                }
                Tok t = new Tok();
                t.Text = sb.ToString();
                t.Start = start;
                t.End = i;
                res.Add(t);
            }
            return res;
        }

        private static string StripFirstToken(string s)
        {
            s = s.Trim();
            if (s.Length == 0) return "";
            if (s[0] == '"')
            {
                int e = s.IndexOf('"', 1);
                return e > 0 ? s.Substring(e + 1) : "";
            }
            int sp = s.IndexOf(' ');
            return sp > 0 ? s.Substring(sp + 1) : "";
        }

        public static List<string> Tokenize(string s)
        {
            var res = new List<string>();
            if (string.IsNullOrEmpty(s)) return res;
            int i = 0;
            while (i < s.Length)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i >= s.Length) break;
                var sb = new StringBuilder();
                if (s[i] == '"')
                {
                    i++;
                    while (i < s.Length)
                    {
                        if (s[i] == '"')
                        {
                            if (i + 1 < s.Length && s[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                            i++; break;
                        }
                        sb.Append(s[i]); i++;
                    }
                }
                else
                {
                    while (i < s.Length && !char.IsWhiteSpace(s[i])) { sb.Append(s[i]); i++; }
                }
                res.Add(sb.ToString());
            }
            return res;
        }
    }

    // ----------------------------------------------------- Orquestrador
    internal sealed class Watcher
    {
        private readonly Dictionary<int, ProcInfo> _table = new Dictionary<int, ProcInfo>();
        private readonly Dictionary<int, ScriptFrame> _frames = new Dictionary<int, ScriptFrame>();
        private readonly Dictionary<int, BatchSession> _sessions = new Dictionary<int, BatchSession>();
        private readonly object _lock = new object();

        public Action<WatchEvent> OnEvent;
        public string Filter = "";
        public bool TrackChildren = true;
        public bool Verbose;

        public IReadOnlyList<BatchSession> Sessions
        {
            get { lock (_lock) { return _sessions.Values.OrderBy(s => s.StartedAt).ToList(); } }
        }

        private int _totalSessions;
        public int TotalSessions { get { lock (_lock) { return _totalSessions; } } }

        public void Seed()
        {
            try
            {
                // apenas nome/ppid: a linha de comando e lida sob demanda (Lookup)
                foreach (ProcInfo p in PollingEngine.Snapshot())
                {
                    _table[p.Pid] = p;
                    if (!IsCmd(p)) continue;

                    string args; string sw;
                    string raw = BatchCmdLine.Extract(p.CmdLine, out args, out sw);
                    if (raw == null) continue;
                    string resolved = ResolveScript(raw, p);
                    if (resolved == null) continue;
                    if (Filter.Length > 0 && resolved.IndexOf(Filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    ScriptFrame parentFrame;
                    lock (_lock) { _frames.TryGetValue(p.ParentPid, out parentFrame); }
                    bool isTop = parentFrame == null;
                    BatchSession session = isTop ? CreateSession(p, resolved, args) : parentFrame.Session;
                    var frame = new ScriptFrame
                    {
                        CmdPid = p.Pid,
                        ScriptPath = resolved,
                        ScriptName = System.IO.Path.GetFileName(resolved),
                        ScriptArgs = args,
                        Analysis = BatchParser.Parse(resolved),
                        Session = session,
                        Parent = parentFrame,
                        Depth = isTop ? 0 : parentFrame.Depth + 1
                    };
                    lock (_lock) { _frames[p.Pid] = frame; }
                    if (isTop) { session.CurrentFrame = frame; session.RootFrame = frame; }
                }
            }
            catch { }
        }

        private static bool IsCmd(ProcInfo p)
        {
            return p != null && p.Name != null &&
                   (p.Name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase));
        }

        public void OnProcessStart(ProcInfo p)
        {
            if (p == null || p.Pid <= 0) return;
            lock (_lock) { _table[p.Pid] = p; }

            if (Verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  [proc] pid=" + p.Pid + " ppid=" + p.ParentPid + " " + p.Name +
                                  "  cmd=" + (p.CmdLine.Length > 0 ? p.CmdLine : "(sem command line)"));
                Console.ForegroundColor = ConsoleColor.Gray;
            }

            if (IsCmd(p))
            {
                string args; string sw;
                string raw = BatchCmdLine.Extract(p.CmdLine, out args, out sw);
                if (raw != null)
                {
                    string resolved = ResolveScript(raw, p);
                    if (resolved != null && (Filter.Length == 0 ||
                        resolved.IndexOf(Filter, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        ScriptFrame parentFrame;
                        lock (_lock) { _frames.TryGetValue(p.ParentPid, out parentFrame); }

                        BatchSession session;
                        bool isTop = parentFrame == null;
                        if (isTop) session = CreateSession(p, resolved, args);
                        else session = parentFrame.Session;

                        var frame = new ScriptFrame
                        {
                            CmdPid = p.Pid,
                            ScriptPath = resolved,
                            ScriptName = System.IO.Path.GetFileName(resolved),
                            ScriptArgs = args,
                            Analysis = BatchParser.Parse(resolved),
                            Session = session,
                            Parent = parentFrame,
                            Depth = isTop ? 0 : parentFrame.Depth + 1,
                            Observed = true
                        };
                        lock (_lock) { _frames[p.Pid] = frame; }
                        if (isTop) { session.CurrentFrame = frame; session.RootFrame = frame; }

                        if (!isTop)
                        {
                            // o script chamado entra na arvore do pai: e assim que os
                            // comandos executados dentro dele sao atribuidos a ele
                            // (e nao ao script que o chamou)
                            LinkChild(parentFrame, frame);
                            // script aninhado: um passo da sessao pai
                            EmitStep(session, p, parentFrame.Analysis, p.CmdLine, true, frame);
                        }
                        return;
                    }
                }

                // cmd.exe SEM script na linha de comando. Quando o pai e um .cmd, isso
                // quer dizer que o cmd.exe foi criado para EXECUTAR um script: o proprio
                // cmd zera a linha de comando do filho, e o unico lugar onde o nome do
                // script aparece e o texto do script que o chamou. O quadro fica 'sem
                // script' e e resolvido pelo conteudo dos comandos executados dentro dele.
                ScriptFrame pf;
                lock (_lock) { _frames.TryGetValue(p.ParentPid, out pf); }
                if (pf != null)
                {
                    if (!pf.UnknownScript) CreateUnknownFrame(p, pf);
                    EmitStepInSession(pf.Session, p, p.CmdLine, pf);
                }
                return;
            }

            ScriptFrame f;
            lock (_lock) { _frames.TryGetValue(p.ParentPid, out f); }
            if (f != null && TrackChildren)
            {
                EmitStepInSession(f.Session, p, p.CmdLine, f);
            }
        }

        private BatchSession CreateSession(ProcInfo p, string resolved, string args)
        {
            var s = new BatchSession
            {
                CmdPid = p.Pid,
                ScriptPath = resolved,
                ScriptName = System.IO.Path.GetFileName(resolved),
                ScriptArgs = args,
                RawCmdLine = p.CmdLine,
                StartedAt = DateTime.Now,
                Analysis = BatchParser.Parse(resolved)
            };

            lock (_lock)
            {
                _sessions[p.Pid] = s;
                _totalSessions++;
            }

            s.Caller = Lookup(p.ParentPid);
            if (s.Caller != null)
                foreach (ProcInfo anc in Ancestors(s.Caller, 6)) s.Chain.Add(anc);

            Emit(new WatchEvent
            {
                Type = "batch_open",
                Timestamp = DateTime.Now,
                Session = s,
                Message = "sessão .cmd iniciada"
            });
            return s;
        }

        private void EmitStep(BatchSession s, ProcInfo child, BatchFile analysis, string cmdLine, bool isNested, ScriptFrame frame)
        {
            if (s == null) return;
            double conf = 0;
            BatchStep step = null;
            if (analysis != null && analysis.Exists)
            {
                step = analysis.Match(cmdLine, out conf);
                if (conf < 1.5) step = null;
            }

            s.StepCount++;
            if (step != null && step.Line > s.LastLine) s.LastLine = step.Line;

            if (isNested && frame != null)
            {
                // a linha do script pai que fez a chamada
                if (step != null) frame.CallStep = step;
                EmitCall(s, frame);
            }

            Emit(new WatchEvent
            {
                Type = "batch_step",
                Timestamp = DateTime.Now,
                Session = s,
                Process = child,
                Step = step,
                Frame = frame,
                Confidence = conf,
                Message = isNested ? "script aninhado" : "comando em execução"
            });
        }

        /// <summary>Registra no fluxo a chamada de um script dentro de outro.</summary>
        private void EmitCall(BatchSession s, ScriptFrame frame)
        {
            if (s == null || frame == null || frame.CallEmitted) return;
            frame.CallEmitted = true;
            Emit(new WatchEvent
            {
                Type = "batch_call",
                Timestamp = DateTime.Now,
                Session = s,
                Frame = frame,
                Step = frame.CallStep,
                Message = "chamada de script"
            });
        }

        /// <summary>
        /// Liga o script chamado ao quadro do pai. E o que permite atribuir os comandos
        /// executados dentro do script chamado ao script certo (e nao ao que o chamou).
        /// </summary>
        private void LinkChild(ScriptFrame parent, ScriptFrame child)
        {
            if (parent == null || child == null) return;
            if (!HasChild(parent, child.ScriptPath)) parent.Children.Add(child);
            EnsureCallTree(child, child.Depth);
        }

        private static bool HasChild(ScriptFrame parent, string path)
        {
            if (parent == null || path == null) return false;
            foreach (ScriptFrame c in parent.Children)
                if (c.ScriptPath.Equals(path, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Cria o quadro de um cmd.exe cujo script ainda nao se sabe qual.
        /// Os candidatos sao os scripts que o script pai pode estar chamando
        /// (as linhas de 'call', 'cmd /c' e 'start' do proprio pai).
        /// </summary>
        private void CreateUnknownFrame(ProcInfo p, ScriptFrame host)
        {
            EnsureCallTree(host, host.Depth);

            var frame = new ScriptFrame
            {
                CmdPid = p.Pid,
                ScriptPath = "",
                ScriptName = "",
                ScriptArgs = "",
                Analysis = null,
                Session = host.Session,
                Parent = host,
                Depth = host.Depth + 1,
                Observed = true,
                UnknownScript = true
            };

            var cands = new List<ScriptFrame>();
            foreach (ScriptFrame c in host.Children)
            {
                if (c.Observed) continue;          // ja sabemos qual e
                if (c.Analysis == null || !c.Analysis.Exists) continue;
                cands.Add(c);
            }
            frame.Candidates = cands;

            lock (_lock) { _frames[p.Pid] = frame; }
        }

        /// <summary>
        /// Descobre qual script o cmd.exe esta executando: o comando observado
        /// e casado com os scripts candidatos e o que melhor casar vence.
        /// </summary>
        private void ResolveUnknown(ScriptFrame unknown, string cmdLine)
        {
            if (unknown == null || !unknown.UnknownScript) return;
            if (unknown.Candidates == null || unknown.Candidates.Count == 0) return;
            if (string.IsNullOrEmpty(cmdLine)) return;

            ScriptFrame melhorCandidato = null;
            ScriptFrame melhorAlvo = null;
            double melhorConf = 0;

            foreach (ScriptFrame c in unknown.Candidates)
            {
                MatchResult r = FindBest(c, cmdLine);
                if (r == null || r.Frame == null) continue;
                if (r.Conf > melhorConf + 0.001)
                {
                    melhorConf = r.Conf;
                    melhorCandidato = c;
                    melhorAlvo = r.Frame;
                }
            }

            if (melhorAlvo == null || melhorConf < 1.5) return;

            unknown.ScriptPath = melhorAlvo.ScriptPath;
            unknown.ScriptName = melhorAlvo.ScriptName;
            unknown.Analysis = melhorAlvo.Analysis;
            unknown.CallStep = melhorCandidato.CallStep;   // a linha do pai que chamou
            unknown.Depth = melhorAlvo.Depth;              // o nivel real do script
            unknown.Candidates = null;
            unknown.UnknownScript = false;

            // a chamada existe: o script pai chamou esse script
            EmitCall(unknown.Session, unknown);
        }

        /// <summary>
        /// Casa um comando observado com a linha do .cmd.
        /// Como o cmd.exe executa 'call x.cmd' no mesmo processo (nenhum processo novo aparece),
        /// a atribuicao e feita procurando o melhor casamento em toda a arvore de chamadas
        /// do script: o comando e compared com o script atual e com todos os scripts
        /// chamados por ele, e fica com o que melhor casar.
        ///
        /// 'host' e o quadro do processo que disparou o comando: quando ele existe, a
        /// subarvore dele e a primeira a ser vasculhada (o comando nasceu ali).
        /// </summary>
        private void EmitStepInSession(BatchSession s, ProcInfo child, string cmdLine, ScriptFrame host)
        {
            if (s == null) return;

            EnsureCallTree(s.RootFrame, 0);

            // o host pode ser um cmd.exe cujo script ainda nao identificamos:
            // e este comando observado que revela qual script ele esta rodando
            if (host != null && host.UnknownScript) ResolveUnknown(host, cmdLine);

            // 1) o comando nasceu dentro do processo 'host': procura primeiro na
            //    subarvore dele (o 'call' nao cria processo, so o texto do script
            //    revela qual script foi chamado)
            MatchResult m = host != null ? FindBest(host, cmdLine) : null;
            if (m == null || m.Conf < 1.5) m = null;

            // 2) sem casamento suficiente: procura em toda a arvore de chamadas
            if (m == null)
            {
                m = FindBest(s.RootFrame, cmdLine);
                if (m != null && m.Conf < 1.5) m = null;
            }

            ScriptFrame best = m != null ? m.Frame : null;
            BatchStep bestStep = m != null ? m.Step : null;

            if (best != null)
            {
                s.CurrentFrame = best;
            }

            s.StepCount++;
            if (bestStep != null && bestStep.Line > s.LastLine) s.LastLine = bestStep.Line;

            // o comando rodou dentro de um script chamado: a chamada e real
            if (best != null && best.Depth > 0) EmitCall(s, best);

            Emit(new WatchEvent
            {
                Type = "batch_step",
                Timestamp = DateTime.Now,
                Session = s,
                Process = child,
                Step = bestStep,
                Frame = best,
                Confidence = m != null ? m.Conf : 0,
                Message = "comando em execução"
            });
        }

        /// <summary>Resultado do casamento de um comando observado com um script.</summary>
        private sealed class MatchResult
        {
            public ScriptFrame Frame;
            public BatchStep Step;
            public double Conf;
            public double Score;
        }

        /// <summary>Procura o melhor casamento do comando na arvore a partir de 'root'.</summary>
        private MatchResult FindBest(ScriptFrame root, string cmdLine)
        {
            MatchResult r = new MatchResult();
            SearchFrames(root, cmdLine, r);
            return r;
        }

        /// <summary>
        /// Monta a arvore de scripts chamados a partir de um quadro.
        /// Reconhece 'call x.cmd', 'cmd /c x.cmd', 'cmd /k x.cmd' e 'start x.cmd'.
        /// </summary>
        private void EnsureCallTree(ScriptFrame frame, int depth)
        {
            if (frame == null || depth > 6) return;
            if (frame.Analysis == null || !frame.Analysis.Exists) return;

            if (!frame.ChildrenBuilt)
            {
                frame.ChildrenBuilt = true;

                foreach (BatchStep st in frame.Analysis.Steps)
                {
                    if (st.IsComment || st.IsLabel) continue;

                    bool sameProcess;
                    string target = CallTargetOf(st, out sameProcess);
                    if (target == null) continue;

                    string resolved = ResolveRelativeTo(target, frame.ScriptPath);
                    if (resolved == null) continue;
                    if (HasChild(frame, resolved)) continue;

                    var child = new ScriptFrame
                    {
                        CmdPid = frame.CmdPid,
                        ScriptPath = resolved,
                        ScriptName = System.IO.Path.GetFileName(resolved),
                        ScriptArgs = "",
                        Analysis = BatchParser.Parse(resolved),
                        Session = frame.Session,
                        Parent = frame,
                        Depth = frame.Depth + 1,
                        SameProcess = sameProcess,
                        CallStep = st
                    };
                    frame.Children.Add(child);
                }
            }

            // os filhos observados (processo real) tambem precisam da arvore deles
            foreach (ScriptFrame c in frame.Children)
                EnsureCallTree(c, depth + 1);
        }

        /// <summary>
        /// Descobre o script chamado por uma linha do .cmd: 'call x.cmd', 'cmd /c x.cmd',
        /// 'cmd /k x.cmd' ou 'start x.cmd'. Devolve null quando a linha nao chama script.
        /// 'sameProcess' diz se a chamada roda no MESMO processo (linha 'call') ou em
        /// um cmd.exe novo (linha 'cmd /c' ou 'start').
        /// </summary>
        private static string CallTargetOf(BatchStep st, out bool sameProcess)
        {
            sameProcess = false;
            if (st == null) return null;
            if (st.IsCall && st.CallTarget != null && st.CallTarget.Length > 0)
            {
                sameProcess = true;    // 'call' roda no mesmo cmd.exe
                return st.CallTarget;
            }

            string cmd = (st.Command ?? "").Trim();
            if (cmd.Length == 0) return null;

            List<string> toks = BatchCmdLine.Tokenize(cmd);
            if (toks.Count == 0) return null;

            string exe = toks[0].Trim('"').ToLowerInvariant();
            if (exe != "cmd" && exe != "start") return null;

            for (int i = 1; i < toks.Count; i++)
            {
                string t = toks[i].Trim('"');
                if (t.Length == 0) continue;
                string low = t.ToLowerInvariant();
                // comandos internos do cmd que nao sao chamadas de script
                if (low == "echo" || low == "dir" || low == "type" || low == "copy" ||
                    low == "move" || low == "del" || low == "ren" || low == "md" ||
                    low == "rd" || low == "set" || low == "if" || low == "for" ||
                    low == "goto" || low == "exit" || low == "cd" || low == "pushd" ||
                    low == "popd") continue;
                if (low.EndsWith(".cmd") || low.EndsWith(".bat")) return t;
            }
            return null;
        }

        /// <summary>Busca o melhor casamento na arvore de scripts chamados.</summary>
        private void SearchFrames(ScriptFrame frame, string cmdLine, MatchResult res)
        {
            if (frame == null) return;

            if (frame.Analysis != null && frame.Analysis.Exists)
            {
                double conf = 0;
                BatchStep st = frame.Analysis.Match(cmdLine, out conf);
                if (st != null || conf > 0)
                {
                    double score = conf;
                    // um quadro observado (processo real) vale mais que um deduzido do texto
                    if (frame.Observed) score += 0.3;

                    bool melhor = res.Frame == null
                        || score > res.Score + 0.001
                        // empate: gana o script mais profundo - e ele que esta rodando
                        || (score >= res.Score - 0.5 && frame.Depth > res.Frame.Depth);

                    if (melhor)
                    {
                        res.Frame = frame;
                        res.Step = conf >= 1.5 ? st : null;
                        res.Conf = conf;
                        res.Score = score;
                    }
                }
            }

            foreach (ScriptFrame c in frame.Children)
            {
                // so desce por chamadas que rodam no MESMO processo: um script
                // chamado por 'cmd /c' tem o proprio cmd.exe, e seus comandos
                // tem o quadro dele como host
                if (!c.SameProcess) continue;
                SearchFrames(c, cmdLine, res);
            }
        }

        private string ResolveRelativeTo(string raw, string baseScriptPath)
        {
            try
            {
                if (raw == null) return null;
                string target = raw.Trim().Trim('"');

                // expande %~dp0 (diretorio do proprio script) e %~nx0
                if (target.IndexOf("%~dp0", StringComparison.OrdinalIgnoreCase) >= 0 && baseScriptPath != null)
                {
                    string dir = System.IO.Path.GetDirectoryName(baseScriptPath) ?? "";
                    target = target.Replace("%~dp0", dir.TrimEnd('\\') + "\\")
                                   .Replace("%~dp0\\", dir.TrimEnd('\\') + "\\");
                }
                if (target.IndexOf('%') >= 0) return null;   // variavel nao resolvida

                if (System.IO.Path.IsPathRooted(target))
                    return System.IO.File.Exists(target) ? target : null;

                if (baseScriptPath != null && baseScriptPath.Length > 0)
                {
                    string dir = System.IO.Path.GetDirectoryName(baseScriptPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        string cand = System.IO.Path.Combine(dir, target);
                        if (System.IO.File.Exists(cand)) return System.IO.Path.GetFullPath(cand);
                    }
                }
                string cand2 = System.IO.Path.Combine(Environment.CurrentDirectory, target);
                if (System.IO.File.Exists(cand2)) return System.IO.Path.GetFullPath(cand2);
                return null;
            }
            catch { return null; }
        }

        public void OnProcessExit(int pid)
        {
            BatchSession s = null;
            bool closeSession = false;
            lock (_lock)
            {
                _table.Remove(pid);
                ScriptFrame frame;
                if (_frames.TryGetValue(pid, out frame))
                {
                    _frames.Remove(pid);
                    // fecha a sessao apenas quando o cmd.exe raiz encerra
                    if (frame.Depth == 0 && frame.Session != null && !frame.Session.Closed)
                    {
                        s = frame.Session;
                        closeSession = true;
                        _sessions.Remove(pid);
                        s.Closed = true;
                    }
                }
            }
            if (closeSession && s != null)
            {
                Emit(new WatchEvent
                {
                    Type = "batch_close",
                    Timestamp = DateTime.Now,
                    Session = s,
                    Message = "sessão .cmd encerrada"
                });
            }
        }

        private ProcInfo Lookup(int pid)
        {
            ProcInfo p;
            lock (_lock) { _table.TryGetValue(pid, out p); }
            if (p != null)
            {
                if (p.CmdLine.Length == 0)
                {
                    PollingEngine.HydrateProcess(p);
                    lock (_lock) { _table[pid] = p; }
                }
                return p;
            }

            // processo anterior ao inicio do monitor: busca sob demanda (Toolhelp + PEB)
            try
            {
                foreach (ProcInfo cand in PollingEngine.Snapshot())
                {
                    if (cand.Pid != pid) continue;
                    PollingEngine.HydrateProcess(cand);
                    lock (_lock) { _table[pid] = cand; }
                    return cand;
                }
            }
            catch { }
            return null;
        }

        private List<ProcInfo> Ancestors(ProcInfo start, int max)
        {
            var res = new List<ProcInfo>();
            ProcInfo cur = start;
            var guard = new HashSet<int>();
            while (cur != null && res.Count < max && guard.Add(cur.Pid))
            {
                res.Add(cur);
                cur = Lookup(cur.ParentPid);
            }
            return res;
        }

        private string ResolveScript(string raw, ProcInfo cmdProc)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            try
            {
                if (System.IO.Path.IsPathRooted(raw))
                    return System.IO.File.Exists(raw) ? raw : raw;

                // tenta resolver relativo ao script do processo pai
                ScriptFrame parent;
                lock (_lock) { _frames.TryGetValue(cmdProc.ParentPid, out parent); }
                var candidates = new List<string>();
                if (parent != null && parent.ScriptPath.Length > 0)
                    candidates.Add(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(parent.ScriptPath), raw));
                candidates.Add(System.IO.Path.Combine(Environment.CurrentDirectory, raw));
                candidates.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), raw));
                candidates.Add(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(cmdProc.Path ?? ""), raw));
                foreach (string c in candidates)
                {
                    try { if (c != null && System.IO.File.Exists(c)) return System.IO.Path.GetFullPath(c); } catch { }
                }
                return null;
            }
            catch { return null; }
        }

        private void Emit(WatchEvent ev)
        {
            if (OnEvent != null) { try { OnEvent(ev); } catch { } }
        }
    }

    // ------------------------------------------- Descricao de origem
    internal static class CallerDesc
    {
        public static string Describe(ProcInfo caller)
        {
            if (caller == null) return "origem desconhecida (processo encerrado ou sem permissão de leitura)";

            string n = (caller.Name ?? "").ToLowerInvariant();
            switch (n)
            {
                case "explorer.exe": return "Explorer do Windows (abertura por duplo clique ou pelo menu de contexto)";
                case "cmd.exe": return "outro prompt de comando (cmd.exe)";
                case "powershell.exe": return "PowerShell";
                case "pwsh.exe": return "PowerShell 7";
                case "code.exe": return "Visual Studio Code";
                case "devenv.exe": return "Visual Studio";
                case "windowsterminal.exe": return "Windows Terminal";
                case "wt.exe": return "Windows Terminal";
                case "node.exe": return "Node.js (npm/yarn/pnpm ou aplicação Node)";
                case "services.exe": return "gerenciador de serviços do Windows";
                case "svchost.exe": return "serviço do Windows (svchost)";
                case "java.exe": return "aplicação Java";
                case "javaw.exe": return "aplicação Java";
                case "msbuild.exe": return "MSBuild";
                case "git.exe": return "cliente Git";
                case "jenkins.exe": return "Jenkins";
                case "teamcity.exe": return "TeamCity";
                case "bash.exe": return "Git Bash";
                case "sh.exe": return "shell POSIX";
                case "wsl.exe": return "WSL (subsistema Linux)";
                case "conhost.exe": return "console do Windows";
                case "taskhostw.exe": return "Task Host (Windows)";
                case "wininit.exe": return "instalador do Windows";
                case "msiexec.exe": return "instalador de pacotes MSI";
                case "rundll32.exe": return "executável de biblioteca do sistema (rundll32)";
            }

            string tail = "";
            try { tail = System.IO.Path.GetFileName(caller.Path ?? ""); } catch { }
            if (tail.Length > 0 && !tail.Equals(caller.Name, StringComparison.OrdinalIgnoreCase))
                return caller.Name + " (" + tail + ")";
            return caller.Name;
        }
    }
}
