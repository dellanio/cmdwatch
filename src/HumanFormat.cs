using System;
using System.Collections.Generic;
using System.Linq;

namespace CmdWatch
{
    /// <summary>Papel de uma linha da saida humana (define a cor de cada uma).</summary>
    internal enum HumanRole { Open, Call, Step, Close, Body, Banner }

    /// <summary>Uma linha da saida humana do cmdwatch.</summary>
    internal sealed class HumanLine
    {
        public string Text = "";
        public HumanRole Role = HumanRole.Body;

        public HumanLine() { }

        public HumanLine(string text, HumanRole role)
        {
            Text = text;
            Role = role;
        }
    }

    /// <summary>Dados da execucao para o cabecalho do log.</summary>
    internal sealed class RunInfo
    {
        public string Version = "";
        public string Engine = "polling";
        public int IntervalMs = 50;
        public string Filter = "";
        public string LogPath = "";
        public bool IsGui;
    }

    /// <summary>
    /// Formato humano das mensagens do cmdwatch. E o MESMO na saida do CLI e no
    /// fluxo do painel grafico: o painel fala a mesma lingua do terminal.
    /// </summary>
    internal static class HumanFormat
    {
        /// <summary>
        /// Linha de campo do bloco de sessao ("script : ...", "chamador: ...").
        /// O separador acompanha o tamanho do rotulo para manter a coluna dos
        /// dois-pontos alinhada.
        /// </summary>
        private static string Field(string label, string value)
        {
            string sep = label.Length >= 7 ? ": " : " : ";
            return "           " + label + sep + value;
        }

        /// <summary>Cabecalho do log - o mesmo no terminal e no painel.</summary>
        public static List<HumanLine> Banner(RunInfo info)
        {
            List<HumanLine> lines = new List<HumanLine>();
            if (info == null) return lines;

            lines.Add(new HumanLine(Lang.F("bannerTitle", info.Version), HumanRole.Banner));
            lines.Add(new HumanLine(Row(Lang.T("bannerMotor"),
                info.Engine + (info.Engine == "wmi" ? " " + Lang.T("engineAdmin") : " " + Lang.T("engineNoAdmin"))), HumanRole.Body));
            lines.Add(new HumanLine(Row(Lang.T("bannerInterval"),
                info.Engine == "polling" ? info.IntervalMs + " ms" : Lang.T("engineInstant")), HumanRole.Body));
            lines.Add(new HumanLine(Row(Lang.T("bannerLog"),
                info.LogPath.Length == 0 ? Lang.T("logOff") : info.LogPath), HumanRole.Body));
            lines.Add(new HumanLine(Row(Lang.T("bannerFilter"),
                info.Filter.Length == 0 ? Lang.T("filterNone") : info.Filter), HumanRole.Body));
            lines.Add(new HumanLine(info.IsGui ? Lang.T("bannerCaptureGui") : Lang.T("bannerCaptureCli"), HumanRole.Body));
            return lines;
        }

        private static string Row(string label, string value)
        {
            return "    " + label.PadRight(11) + ": " + value;
        }

        /// <summary>Mensagens de um evento, no formato humano do cmdwatch.</summary>
        public static List<HumanLine> Render(WatchEvent ev)
        {
            List<HumanLine> lines = new List<HumanLine>();
            if (ev == null) return lines;

            BatchSession s = ev.Session;
            if (s == null) return lines;
            string ts = ev.Timestamp.ToString("HH:mm:ss.fff");

            if (ev.Type == "batch_open")
            {
                lines.Add(new HumanLine("[" + ts + "] >> " + Lang.T("openTitle"), HumanRole.Open));
                lines.Add(new HumanLine(Field(Lang.T("fScript"),
                    s.ScriptPath + (s.ScriptArgs.Length > 0 ? " " + s.ScriptArgs : "")), HumanRole.Body));
                lines.Add(new HumanLine(Field(Lang.T("fOrigin"),
                    CallerDesc.Describe(s.Caller)), HumanRole.Body));
                if (s.Caller != null)
                {
                    lines.Add(new HumanLine(Field(Lang.T("fCaller"),
                        s.Caller.Name + " (PID " + s.Caller.Pid + ")" +
                        (s.Caller.CmdLine.Length > 0 ? " -> " + Explain.Truncate(s.Caller.CmdLine, 110) : "")), HumanRole.Body));
                }
                if (s.Chain.Count > 0)
                {
                    List<string> parts = s.Chain.Select(c => c.Name + "(" + c.Pid + ")").ToList();
                    lines.Add(new HumanLine(Field(Lang.T("fChain"),
                        string.Join(" -> ", parts.ToArray())), HumanRole.Body));
                }
                if (s.Analysis != null && s.Analysis.Exists)
                {
                    lines.Add(new HumanLine(Field(Lang.T("fSummary"), s.Analysis.Purpose), HumanRole.Body));
                    lines.Add(new HumanLine(Field(Lang.T("fSteps"),
                        Lang.F("fMappedSteps", s.Analysis.ExecutableSteps)), HumanRole.Body));
                }
                lines.Add(new HumanLine("", HumanRole.Body));
                return lines;
            }

            if (ev.Type == "batch_call")
            {
                ScriptFrame fr = ev.Frame;
                string parent = fr != null && fr.Parent != null ? fr.Parent.ScriptName : "?";
                string nome = fr != null ? fr.ScriptName : "?";
                string nivel = fr != null && fr.Depth > 0 ? "  (" + Lang.F("fLevel", fr.Depth) + ")" : "";
                lines.Add(new HumanLine("[" + ts + "]   +-> " + nome + nivel +
                    (ev.Step != null ? "  " + Lang.F("fCalledAt", ev.Step.Line, parent) : ""), HumanRole.Call));
                return lines;
            }

            if (ev.Type == "batch_step")
            {
                ProcInfo c = ev.Process;
                string who = c == null ? "?" : c.Name;
                bool nested = ev.Frame != null && ev.Frame.Depth > 0;
                lines.Add(new HumanLine("[" + ts + "]   | " + who +
                    (c != null ? " (PID " + c.Pid + ")" : ""),
                    nested ? HumanRole.Call : HumanRole.Step));
                if (nested)
                {
                    lines.Add(new HumanLine("             " + Lang.T("fIn") + ": " +
                        ev.Frame.ScriptName + " (" + Lang.F("fLevel", ev.Frame.Depth) + ")", HumanRole.Body));
                }
                if (ev.Step != null)
                {
                    lines.Add(new HumanLine("             " + Lang.F("fLine", ev.Step.Line) + ": " +
                        Explain.Truncate(ev.Step.Raw, 100), HumanRole.Body));
                    lines.Add(new HumanLine("             -> " + ev.Step.Meaning, HumanRole.Body));
                }
                else
                {
                    string cl = c == null ? "" : c.CmdLine;
                    lines.Add(new HumanLine("             " + Lang.T("fCommand") + ": " +
                        Explain.Truncate(cl, 100), HumanRole.Body));
                    string m = Explain.DescribeObserved(cl);
                    if (m.Length > 0) lines.Add(new HumanLine("             -> " + m, HumanRole.Body));
                }
                return lines;
            }

            if (ev.Type == "batch_close")
            {
                double dur = (ev.Timestamp - s.StartedAt).TotalSeconds;
                lines.Add(new HumanLine("[" + ts + "] ## " + Lang.T("closeTitle") + "  " + s.ScriptName +
                    "  (" + Lang.F("fExecuted", dur.ToString("0.0"), s.StepCount) +
                    (s.LastLine > 0 ? ", " + Lang.F("fLastLine", s.LastLine) : "") + ")", HumanRole.Close));
                lines.Add(new HumanLine("", HumanRole.Body));
                return lines;
            }

            lines.Add(new HumanLine("[" + ts + "] " + ev.Message, HumanRole.Body));
            return lines;
        }
    }
}
