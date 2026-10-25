using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CmdWatch
{
    internal static class Program
    {
        internal const string Version = "1.0.4";
        private static ConsoleColor _lastColor = ConsoleColor.Gray;

        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // sem argumentos (duplo clique no .exe): abre o painel grafico
            if (args.Length == 0) { return RunGui(new Options()); }

            string cmd = args[0].ToLowerInvariant();
            var opts = Options.Parse(args.Skip(1).ToArray());

            switch (cmd)
            {
                case "monitor":
                case "watch":
                    return RunMonitor(opts);
                case "analyze":
                case "analyse":
                    return RunAnalyze(opts, args.Skip(1).ToArray());
                case "trace":
                    return RunTrace(opts, args.Skip(1).ToArray());
                case "gui":
                case "dashboard":
                    return RunGui(opts);
                case "version":
                case "--version":
                    Console.WriteLine("cmdwatch " + Version);
                    return 0;
                case "help":
                case "--help":
                case "-h":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine("comando desconhecido: " + args[0]);
                    PrintUsage();
                    return 1;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine(@"cmdwatch " + Version + @" — monitor de execucao de scripts .cmd/.bat

uso:
  cmdwatch.exe                     abre o painel grafico (duplo clique tambem funciona)
  cmdwatch monitor [opcoes]        monitora o sistema e reporta cada .cmd aberto
  cmdwatch gui [opcoes]            painel grafico do fluxo de execucao de .cmd/.bat
  cmdwatch analyze <arquivo.cmd>    explica o que um script faz (analise estatica)
  cmdwatch trace <arquivo.cmd>      executa o script e captura a sequencia exata de comandos
  cmdwatch version

opcoes do monitor (tambem valem para o gui):
  --log <arquivo>     arquivo JSONL de saida (padrao: cmdwatch-log.jsonl)
  --interval <ms>     intervalo de amostragem (padrao 50)
  --engine <auto|polling|wmi>  motor de captura (auto detecta)
  --duration <seg>    encerra apos N segundos (0 = infinito)
  --filter <texto>    so reporta .cmd cujo caminho contenha o texto
  --no-children       nao reporta os comandos internos do script
  --json              saida em JSONL no stdout
  --quiet             nao escreve o log em arquivo

opcoes do analyze:
  --json              saida em JSON

opcoes do trace:
  --args <texto>      argumentos passados ao script
  --json              saida em JSONL
  --raw               mostra toda a saida capturada (nao so os comandos)
");
        }

        // -------------------------------------------------- monitor
        private static int RunMonitor(Options o)
        {
            bool admin = IsAdmin();
            string engine = o.Engine;
            if (engine == "auto") engine = admin ? "wmi" : "polling";

            IEngine eng;
            if (engine == "wmi")
            {
                if (!admin)
                {
                    Console.Error.WriteLine("[!] motor wmi exige administrador; usando 'polling'.");
                    engine = "polling";
                }
            }
            if (engine == "wmi") eng = new WmiEngine();
            else eng = new PollingEngine(o.Interval);

            var watcher = new Watcher { Filter = o.Filter, TrackChildren = o.NoChildren == false, Verbose = o.Verbose };
            StreamWriter log = null;

            if (!o.Quiet && o.LogPath != null)
            {
                try
                {
                    log = new StreamWriter(o.LogPath, true, new UTF8Encoding(false)) { AutoFlush = true };
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[!] nao foi possivel abrir o log: " + ex.Message);
                }
            }

            watcher.OnEvent = ev =>
            {
                string json = ToJson(ev);
                if (log != null) { try { log.WriteLine(json); } catch { } }
                if (o.Json) Console.WriteLine(json);
                else RenderHuman(ev);
            };

            // semeia a tabela de processos para resolver quem chamou os .cmd ja existentes
            watcher.Seed();

            // cabecalho: o mesmo formato usado no painel grafico
            RunInfo info = new RunInfo();
            info.Version = Version;
            info.Engine = engine;
            info.IntervalMs = o.Interval;
            info.Filter = o.Filter;
            info.LogPath = (log == null) ? "" : o.LogPath;
            foreach (HumanLine hl in HumanFormat.Banner(info))
                Console.WriteLine(hl.Text);
            Console.WriteLine();

            if (engine == "polling")
            {
                PollingEngine.Verbose = o.Verbose;
                var pe = (PollingEngine)eng;
                pe.Start(watcher.OnProcessStart, watcher.OnProcessExit);
            }
            else
            {
                var we = (WmiEngine)eng;
                we.Start(watcher.OnProcessStart, watcher.OnProcessExit);
            }

            DateTime deadline = o.Duration > 0 ? DateTime.Now.AddSeconds(o.Duration) : DateTime.MaxValue;
            try
            {
                while (DateTime.Now < deadline) Thread.Sleep(200);
            }
            catch { }

            eng.Stop();
            if (log != null) { try { log.Flush(); log.Dispose(); } catch { } }
            Console.WriteLine();
            Console.WriteLine("encerrado. sessoes observadas: " + watcher.TotalSessions);
            return 0;
        }

        // -------------------------------------------------- analyze
        private static int RunAnalyze(Options o, string[] rest)
        {
            string path = rest.FirstOrDefault(x => !x.StartsWith("--"));
            if (path == null) { Console.Error.WriteLine("informe o arquivo .cmd"); return 1; }
            if (!File.Exists(path)) { Console.Error.WriteLine("arquivo nao encontrado: " + path); return 1; }

            BatchFile bf = BatchParser.Parse(path);

            if (o.Json)
            {
                var sb = new StringBuilder();
                sb.Append("{");
                sb.Append("\"file\":" + J.Str(bf.Path) + ",");
                sb.Append("\"purpose\":" + J.Str(bf.Purpose) + ",");
                sb.Append("\"category\":" + J.Str(bf.Category) + ",");
                sb.Append("\"totalLines\":" + J.Num(bf.TotalLines) + ",");
                sb.Append("\"executableSteps\":" + J.Num(bf.ExecutableSteps) + ",");
                sb.Append("\"labels\":" + J.Arr(bf.Labels) + ",");
                sb.Append("\"variables\":" + J.Arr(bf.Variables) + ",");
                sb.Append("\"steps\":[");
                var real = bf.Steps.Where(s => !s.IsComment).ToList();
                for (int i = 0; i < real.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("{\"line\":" + J.Num(real[i].Line) + ",\"raw\":" + J.Str(real[i].Raw) + ",\"meaning\":" + J.Str(real[i].Meaning) + "}");
                }
                sb.Append("]}");
                Console.WriteLine(sb.ToString());
                return 0;
            }

            Console.WriteLine();
            Console.WriteLine("ARQUIVO   : " + bf.Path);
            Console.WriteLine("RESUMO    : " + bf.Purpose);
            Console.WriteLine("CATEGORIA : " + bf.Category);
            Console.WriteLine("TAMANHO   : " + bf.TotalLines + " linhas, " + bf.ExecutableSteps + " passos executáveis");
            if (bf.Labels.Count > 0) Console.WriteLine("RÓTULOS   : " + string.Join(", ", bf.Labels.ToArray()));
            if (bf.Variables.Count > 0) Console.WriteLine("VARIÁVEIS : " + string.Join(", ", bf.Variables.ToArray()));
            Console.WriteLine();
            Console.WriteLine("PASSOS:");
            foreach (BatchStep s in bf.Steps)
            {
                if (s.IsComment) continue;
                if (s.IsLabel) { WriteDim("  linha " + Pad(s.Line) + "  [" + s.Label + "]"); continue; }
                Console.WriteLine("  linha " + Pad(s.Line) + "  " + s.Raw);
                WriteDim("               -> " + s.Meaning);
            }
            Console.WriteLine();
            return 0;
        }

        // -------------------------------------------------- trace
        private static int RunTrace(Options o, string[] rest)
        {
            string path = rest.FirstOrDefault(x => !x.StartsWith("--"));
            if (path == null) { Console.Error.WriteLine("informe o arquivo .cmd"); return 1; }
            if (!File.Exists(path)) { Console.Error.WriteLine("arquivo nao encontrado: " + path); return 1; }

            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full);
            string copyName = "__cmdwatch_trace_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".cmd";
            string copyPath = Path.Combine(dir, copyName);

            // Copia instrumentada: lemos o script original, desligamos os 'echo off' e
            // ligamos o eco para o cmd.exe imprimir cada comando antes de executa-lo.
            string[] srcLines = BatchParser.ReadLinesSmart(full);
            var instrumented = new List<string>();
            instrumented.Add("@echo on");
            foreach (string l in srcLines)
            {
                string t = (l ?? "").Trim();
                if (t.StartsWith("@echo off", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("echo off", StringComparison.OrdinalIgnoreCase))
                {
                    instrumented.Add("@echo on");
                    continue;
                }
                instrumented.Add(l ?? "");
            }
            File.WriteAllLines(copyPath, instrumented.ToArray(), new UTF8Encoding(false));

            BatchFile bf = BatchParser.Parse(full);
            var rxPrompt = new Regex(@"^(?<p>[A-Za-z]:\\[^\r\n>]*|[^>\r\n]{0,120}[^>\r\n ])>(?<c>.*)$");

            var lines = new List<KeyValuePair<DateTime, string>>();
            var sync = new object();

            var psi = new ProcessStartInfo("cmd.exe", "/c \"" + copyPath + "\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = dir
            };

            Console.WriteLine();
            Console.WriteLine("TRACE DE " + full);
            Console.WriteLine("resumo: " + bf.Purpose);
            Console.WriteLine(new string('-', 78));

            var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) { lock (sync) lines.Add(new KeyValuePair<DateTime, string>(DateTime.Now, e.Data)); } };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) { lock (sync) lines.Add(new KeyValuePair<DateTime, string>(DateTime.Now, e.Data)); } };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();

            try { File.Delete(copyPath); } catch { }

            DateTime t0 = lines.Count > 0 ? lines[0].Key : DateTime.Now;
            int stepNo = 0;
            var outLines = new List<string>();

            foreach (var kv in lines)
            {
                var m = rxPrompt.Match(kv.Value);
                if (!m.Success)
                {
                    if (o.Raw) WriteDim("   | " + kv.Value);
                    continue;
                }
                string cmdText = m.Groups["c"].Value.Trim();
                if (cmdText.Length == 0) continue;
                if (cmdText.Equals("@echo on", StringComparison.OrdinalIgnoreCase) ||
                    cmdText.Equals("echo on", StringComparison.OrdinalIgnoreCase)) continue;
                if (cmdText.StartsWith("call \"" + full, StringComparison.OrdinalIgnoreCase)) continue;

                stepNo++;
                double conf = 0;
                BatchStep step = bf.Match(cmdText, out conf);
                string meaning = (step != null && conf >= 1.5) ? step.Meaning : Explain.Describe(new BatchStep { Command = cmdText, Exe = BatchFile.FirstToken(cmdText), ExeKey = BatchFile.ExeKeyOf(cmdText) });
                if (step != null && conf >= 1.5) meaning = step.Meaning + "   [" + step.ExeKey + " ← linha " + step.Line + "]";

                if (o.Json)
                {
                    outLines.Add("{\"ts\":" + J.Ts(kv.Key) + ",\"n\":" + J.Num(stepNo) + ",\"cmd\":" + J.Str(cmdText) +
                                 (step != null && conf >= 1.5 ? ",\"line\":" + J.Num(step.Line) : "") +
                                 ",\"meaning\":" + J.Str(meaning) + "}");
                }
                else
                {
                    Console.WriteLine("[" + (kv.Key - t0).TotalSeconds.ToString("0.00") + "s] " + cmdText);
                    WriteDim("          -> " + meaning);
                }
            }

            if (o.Json) foreach (string l in outLines) Console.WriteLine(l);

            Console.WriteLine();
            Console.WriteLine(new string('-', 78));
            Console.WriteLine("total de comandos executados: " + stepNo);
            return 0;
        }

        // -------------------------------------------------- painel grafico
        private static int RunGui(Options o)
        {
            if (o.Quiet) o.LogPath = "";
            return Gui.GuiRunner.Run(o);
        }

        // -------------------------------------------------- helpers
        internal static bool IsAdmin()
        {
            try
            {
                var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static string Pad(int n) { return n.ToString().PadLeft(4); }

        private static void WriteDim(string s)
        {
            ConsoleColor old = _lastColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(s);
            Console.ForegroundColor = old;
            _lastColor = old;
        }

        private static void RenderHuman(WatchEvent ev)
        {
            // o mesmo formato do fluxo do painel grafico (HumanFormat.cs)
            foreach (HumanLine hl in HumanFormat.Render(ev))
            {
                if (hl.Role == HumanRole.Body || hl.Role == HumanRole.Banner)
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine(hl.Text);
                    _lastColor = ConsoleColor.Gray;
                    continue;
                }

                ConsoleColor old = _lastColor;
                Console.ForegroundColor = RoleColor(hl.Role);
                Console.WriteLine(hl.Text);
                Console.ForegroundColor = old;
                _lastColor = old;
            }
        }

        private static ConsoleColor RoleColor(HumanRole role)
        {
            switch (role)
            {
                case HumanRole.Open: return ConsoleColor.Cyan;
                case HumanRole.Call: return ConsoleColor.Magenta;
                case HumanRole.Step: return ConsoleColor.Yellow;
                case HumanRole.Close: return ConsoleColor.DarkCyan;
                default: return ConsoleColor.Gray;
            }
        }

        internal static string ToJson(WatchEvent ev)
        {
            BatchSession s = ev.Session;
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"ts\":" + J.Ts(ev.Timestamp));
            sb.Append(",\"type\":" + J.Str(ev.Type));
            if (s != null)
            {
                sb.Append(",\"cmdPid\":" + J.Num(s.CmdPid));
                sb.Append(",\"script\":" + J.Str(s.ScriptPath));
                sb.Append(",\"scriptName\":" + J.Str(s.ScriptName));
                sb.Append(",\"args\":" + J.Str(s.ScriptArgs));
                sb.Append(",\"cmdLine\":" + J.Str(s.RawCmdLine));
                sb.Append(",\"durationMs\":" + J.Num((long)(ev.Timestamp - s.StartedAt).TotalMilliseconds));
                sb.Append(",\"lastLine\":" + J.Num(s.LastLine));
                sb.Append(",\"stepCount\":" + J.Num(s.StepCount));

                if (s.Caller != null)
                {
                    sb.Append(",\"caller\":{");
                    sb.Append("\"pid\":" + J.Num(s.Caller.Pid) + ",");
                    sb.Append("\"name\":" + J.Str(s.Caller.Name) + ",");
                    sb.Append("\"cmdLine\":" + J.Str(s.Caller.CmdLine) + ",");
                    sb.Append("\"path\":" + J.Str(s.Caller.Path) + ",");
                    sb.Append("\"sessionId\":" + J.Num(s.Caller.SessionId) + ",");
                    sb.Append("\"description\":" + J.Str(CallerDesc.Describe(s.Caller)));
                    sb.Append("}");
                }
                else sb.Append(",\"caller\":null");

                if (s.Chain.Count > 0)
                {
                    sb.Append(",\"chain\":[");
                    for (int i = 0; i < s.Chain.Count; i++)
                    {
                        if (i > 0) sb.Append(",");
                        sb.Append("{\"pid\":" + J.Num(s.Chain[i].Pid) + ",\"name\":" + J.Str(s.Chain[i].Name) + ",\"cmdLine\":" + J.Str(s.Chain[i].CmdLine) + "}");
                    }
                    sb.Append("]");
                }

                if (s.Analysis != null && s.Analysis.Exists)
                {
                    sb.Append(",\"analysis\":{\"purpose\":" + J.Str(s.Analysis.Purpose) +
                              ",\"category\":" + J.Str(s.Analysis.Category) +
                              ",\"executableSteps\":" + J.Num(s.Analysis.ExecutableSteps) + "}");
                }
            }
            if (ev.Process != null)
            {
                sb.Append(",\"process\":{");
                sb.Append("\"pid\":" + J.Num(ev.Process.Pid) + ",");
                sb.Append("\"ppid\":" + J.Num(ev.Process.ParentPid) + ",");
                sb.Append("\"name\":" + J.Str(ev.Process.Name) + ",");
                sb.Append("\"cmdLine\":" + J.Str(ev.Process.CmdLine) + "}");
            }
            if (ev.Step != null)
            {
                sb.Append(",\"step\":{\"line\":" + J.Num(ev.Step.Line) + ",\"raw\":" + J.Str(ev.Step.Raw) + ",\"meaning\":" + J.Str(ev.Step.Meaning) + "}");
            }
            sb.Append("}");
            return sb.ToString();
        }
    }

    internal sealed class Options
    {
        public string LogPath = "cmdwatch-log.jsonl";
        public int Interval = 50;
        public string Engine = "auto";
        public int Duration = 0;
        public string Filter = "";
        public bool NoChildren = false;
        public bool Json = false;
        public bool Quiet = false;
        public bool Verbose = false;
        public bool Raw = false;
        public string Args = "";

        public static Options Parse(string[] argv)
        {
            var o = new Options();
            for (int i = 0; i < argv.Length; i++)
            {
                string a = argv[i];
                switch (a)
                {
                    case "--log": if (i + 1 < argv.Length) o.LogPath = argv[++i]; break;
                    case "--interval": if (i + 1 < argv.Length) int.TryParse(argv[++i], out o.Interval); break;
                    case "--engine": if (i + 1 < argv.Length) o.Engine = argv[++i].ToLowerInvariant(); break;
                    case "--duration": if (i + 1 < argv.Length) int.TryParse(argv[++i], out o.Duration); break;
                    case "--filter": if (i + 1 < argv.Length) o.Filter = argv[++i]; break;
                    case "--no-children": o.NoChildren = true; break;
                    case "--json": o.Json = true; break;
                    case "--quiet": o.Quiet = true; break;
                    case "--verbose": o.Verbose = true; break;
                    case "--raw": o.Raw = true; break;
                    case "--args": if (i + 1 < argv.Length) o.Args = argv[++i]; break;
                    default: break;
                }
            }
            if (o.Interval < 50) o.Interval = 50;
            return o;
        }
    }
}
