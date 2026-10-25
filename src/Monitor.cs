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

            // encontra o switch /c ou /k
            string tail = null;
            var tokens = Tokenize(rest);
            int idx = 0;
            for (; idx < tokens.Count; idx++)
            {
                string t = tokens[idx];
                if (t.Length == 0) continue;
                if (t.StartsWith("/"))
                {
                    if (RxSwitch.IsMatch(t))
                    {
                        string s = t.Substring(1).ToLowerInvariant();
                        if (s == "c" || s == "k" || s == "r")
                        {
                            switchUsed = "/" + s;
                            tail = RemainderAfterSwitch(rest, tokens, idx);
                            break;
                        }
                        continue;
                    }
                    // switch com argumento (ex.: /d /s) ou caminho que comecou com /
                    if (t.Length > 2) continue;
                }
                // primeiro token nao-switch: pode ser o proprio comando
                tail = RemainderAfterSwitch(rest, tokens, idx - 1);
                switchUsed = "(sem switch)";
                break;
            }
            if (tail == null) return null;

            tail = tail.Trim();
            if (tail.Length == 0) return null;

            // convencao do cmd: aspas externas envelopam o comando inteiro
            if (tail.Length >= 2 && tail[0] == '"' && tail[tail.Length - 1] == '"')
                tail = tail.Substring(1, tail.Length - 2);

            var ct = Tokenize(tail);
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

        private static string RemainderAfterSwitch(string rest, List<string> tokens, int switchIdx)
        {
            // reconstroi o resto a partir da posicao do switch
            int pos = -1;
            int consumed = 0;
            for (int i = 0; i < tokens.Count; i++)
            {
                consumed += tokens[i].Length;
                if (i == switchIdx) { pos = consumed; break; }
            }
            if (pos < 0) return null;
            // descarta o switch em si
            string tmp = rest.Substring(Math.Min(pos, rest.Length)).TrimStart();
            if (tmp.StartsWith("/")) tmp = tmp.Substring(1).TrimStart();
            if (tmp.StartsWith("c") || tmp.StartsWith("k") || tmp.StartsWith("r")) tmp = tmp.Substring(1).TrimStart();
            return tmp;
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
                            Depth = isTop ? 0 : parentFrame.Depth + 1
                        };
                        lock (_lock) { _frames[p.Pid] = frame; }
                        if (isTop) { session.CurrentFrame = frame; session.RootFrame = frame; }

                        if (!isTop)
                        {
                            // script aninhado: um passo da sessao pai
                            EmitStep(session, p, parentFrame.Analysis, p.CmdLine, true, frame);
                        }
                        return;
                    }
                }

                // cmd.exe sem script (prompt interativo ou comando solto)
                ScriptFrame pf;
                lock (_lock) { _frames.TryGetValue(p.ParentPid, out pf); }
                if (pf != null) EmitStepInSession(pf.Session, p, p.CmdLine);
                return;
            }

            ScriptFrame f;
            lock (_lock) { _frames.TryGetValue(p.ParentPid, out f); }
            if (f != null && TrackChildren)
            {
                EmitStepInSession(f.Session, p, p.CmdLine);
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

        /// <summary>
        /// Casa um comando observado com a linha do .cmd.
        /// Como o cmd.exe executa 'call x.cmd' no mesmo processo (nenhum processo novo aparece),
        /// a atribuicao e feita procurando o melhor casamento em toda a arvore de chamadas
        /// do script: o comando e compared com o script atual e com todos os scripts
        /// chamados por ele, e fica com o que melhor casar.
        /// </summary>
        private void EmitStepInSession(BatchSession s, ProcInfo child, string cmdLine)
        {
            if (s == null) return;

            EnsureCallTree(s.RootFrame, 0);

            ScriptFrame best = null;
            BatchStep bestStep = null;
            double bestConf = 0;
            SearchFrames(s.RootFrame, cmdLine, s.CurrentFrame, ref best, ref bestStep, ref bestConf);

            if (best != null && bestConf >= 1.5)
            {
                s.CurrentFrame = best;
                s.LastLine = Math.Max(s.LastLine, 0);
            }

            s.StepCount++;
            if (bestStep != null && bestStep.Line > s.LastLine) s.LastLine = bestStep.Line;

            Emit(new WatchEvent
            {
                Type = "batch_step",
                Timestamp = DateTime.Now,
                Session = s,
                Process = child,
                Step = bestStep,
                Frame = best,
                Confidence = bestConf,
                Message = "comando em execução"
            });
        }

        /// <summary>Monta a arvore de scripts chamados (call x.cmd) a partir de um quadro.</summary>
        private void EnsureCallTree(ScriptFrame frame, int depth)
        {
            if (frame == null || depth > 5) return;
            if (frame.Analysis == null || !frame.Analysis.Exists) return;
            if (frame.ChildrenBuilt) return;
            frame.ChildrenBuilt = true;

            foreach (BatchStep st in frame.Analysis.Steps)
            {
                if (!st.IsCall || st.CallTarget == null) continue;
                string low = st.CallTarget.ToLowerInvariant();
                if (!low.EndsWith(".cmd") && !low.EndsWith(".bat")) continue;

                string resolved = ResolveRelativeTo(st.CallTarget, frame.ScriptPath);
                if (resolved == null) continue;

                bool already = false;
                foreach (ScriptFrame c in frame.Children)
                    if (c.ScriptPath.Equals(resolved, StringComparison.OrdinalIgnoreCase)) { already = true; break; }
                if (already) continue;

                var child = new ScriptFrame
                {
                    CmdPid = frame.CmdPid,
                    ScriptPath = resolved,
                    ScriptName = System.IO.Path.GetFileName(resolved),
                    ScriptArgs = "",
                    Analysis = BatchParser.Parse(resolved),
                    Session = frame.Session,
                    Parent = frame,
                    Depth = frame.Depth + 1
                };
                frame.Children.Add(child);
                EnsureCallTree(child, depth + 1);
            }
        }

        /// <summary>Busca o melhor casamento na arvore de scripts chamados.</summary>
        private void SearchFrames(ScriptFrame frame, string cmdLine, ScriptFrame preferred,
                                  ref ScriptFrame best, ref BatchStep bestStep, ref double bestConf)
        {
            if (frame == null) return;

            if (frame.Analysis != null && frame.Analysis.Exists)
            {
                double conf = 0;
                BatchStep st = frame.Analysis.Match(cmdLine, out conf);
                if (st != null || conf > 0)
                {
                    double score = conf;
                    // desempate a favor do script atualmente em execucao
                    if (preferred != null && frame == preferred) score += 0.4;
                    if (score > bestConf)
                    {
                        bestConf = score;
                        best = frame;
                        bestStep = conf >= 1.5 ? st : null;
                    }
                }
            }

            foreach (ScriptFrame c in frame.Children)
                SearchFrames(c, cmdLine, preferred, ref best, ref bestStep, ref bestConf);
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
