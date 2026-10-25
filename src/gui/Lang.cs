using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CmdWatch.Gui
{
    internal enum Idioma { PtBr = 0, En = 1 }

    /// <summary>
    /// Idiomas da interface. O dicionario guarda pt-BR e en lado a lado; a
    /// escolha fica salva em %APPDATA%\cmdwatch\ui.cfg.
    ///
    /// Observacao: o texto das explicacoes de comando (dicionario em Core.cs)
    /// e a descricao de origem do chamador permanecem em portugues - vem do
    /// nucleo compartilhado com a CLI.
    /// </summary>
    internal static class Lang
    {
        private static readonly Dictionary<string, string[]> Textos = new Dictionary<string, string[]>
        {
            { "windowTitle",   new[] { "cmdwatch - execucao de scripts .cmd/.bat", "cmdwatch - .cmd/.bat script execution" } },
            { "sessions",      new[] { "SESSOES .CMD",      "SESSIONS .CMD" } },
            { "flow",          new[] { "FLUXO DE COMANDOS", "COMMAND FLOW" } },
            { "activity",      new[] { "ATIVIDADE .CMD",    "ACTIVITY .CMD" } },
            { "sessionsShort", new[] { "SESSOES",           "SESSIONS" } },
            { "stepsShort",    new[] { "PASSOS",            "STEPS" } },
            { "all",           new[] { "TODAS",             "ALL" } },

            { "colPid",        new[] { "PID",      "PID" } },
            { "colScript",     new[] { "SCRIPT",   "SCRIPT" } },
            { "colCaller",     new[] { "CHAMADOR", "CALLER" } },
            { "colCommands",   new[] { "COMANDOS", "COMMANDS" } },
            { "colLine",       new[] { "LINHA",    "LINE" } },
            { "colStart",      new[] { "INICIO",   "STARTED" } },
            { "colEnd",        new[] { "FIM",      "ENDED" } },
            { "focus",         new[] { "FOCO",     "FOCUS" } },
            { "inProgress",    new[] { "em curso", "running" } },
            { "noSessions",    new[] { "nenhuma sessao .cmd observada ainda", "no .cmd session observed yet" } },
            { "cmdPerSec",     new[] { "COMANDOS/S", "CMDS/S" } },
            { "sessPerSec",    new[] { "SESSOES/S",  "SESS/S" } },

            { "kindSessao",    new[] { "SESSAO",  "SESSION" } },
            { "kindComando",   new[] { "COMANDO", "COMMAND" } },
            { "kindChamada",   new[] { "CHAMADA", "CALL" } },
            { "kindFim",       new[] { "FIM",     "END" } },
            { "kindEvento",    new[] { "EVENTO",  "EVENT" } },

            { "noDescription", new[] { "sem descricao",            "no description" } },
            { "noCmdLine",     new[] { "(sem linha de comando)",    "(no command line)" } },
            { "closed",        new[] { "{0}s  -  {1} comando(s)",   "{0}s  -  {1} command(s)" } },

            { "langLabel",     new[] { "IDIOMA",                   "LANGUAGE" } },
            { "switchLang",    new[] { "Trocar o idioma da interface", "Switch the interface language" } },
            { "trayTitle",     new[] { "cmdwatch - monitor de execucao", "cmdwatch - execution monitor" } },

            { "trayCheck",     new[] { "Enviar ao relogio do Windows e continuar monitorando",
                                        "Send to the Windows tray and keep monitoring" } },
            { "hintAll",       new[] { "Clique numa sessao para ver apenas o fluxo dela",
                                        "Click a session to see only its flow" } },
            { "hintFocus",     new[] { "FOCO: {0} (pid {1}) - clique de novo na sessao para voltar a mostrar todas",
                                        "FOCUS: {0} (pid {1}) - click the session again to show all of them" } },
            { "paused",        new[] { "PAUSADO", "PAUSED" } },

            { "trayRestore",   new[] { "Restaurar painel",     "Restore panel" } },
            { "trayOpenLog",   new[] { "Abrir arquivo de log", "Open log file" } },
            { "trayQuit",      new[] { "Sair do cmdwatch",     "Exit cmdwatch" } },
            { "trayTip",       new[] { "Monitorando em segundo plano. Clique no icone da bandeja para restaurar o painel.",
                                        "Monitoring in the background. Click the tray icon to restore the panel." } },
            { "trayNoLog",     new[] { "Nenhum arquivo de log gerado ainda.", "No log file generated yet." } },

            { "startup",       new[] { "=== cmdwatch {0} - motor {1}{2} - filtro: {3} - log: {4} ===",
                                        "=== cmdwatch {0} - engine {1}{2} - filter: {3} - log: {4} ===" } },
            { "monitoring",    new[] { "monitorando execucao de scripts .cmd/.bat ...",
                                        "monitoring .cmd/.bat script execution ..." } },
            { "motorPolling",  new[] { "polling", "polling" } },
            { "motorWmi",      new[] { " (eventos WMI)", " (WMI events)" } },
            { "filterNone",    new[] { "(todos)", "(all)" } },
            { "logOff",        new[] { "(desativado)", "(disabled)" } }
        };

        public static Idioma Atual { get; private set; }

        static Lang()
        {
            Atual = Idioma.PtBr;
        }

        public static void Set(Idioma idioma)
        {
            Atual = idioma;
        }

        public static string T(string chave)
        {
            string[] v;
            if (!Textos.TryGetValue(chave, out v)) return chave;
            int i = (int)Atual;
            if (i < 0 || i >= v.Length) i = 0;
            return v[i];
        }

        public static string F(string chave, params object[] args)
        {
            string t = T(chave);
            try { return string.Format(CultureInfo.CurrentCulture, t, args); }
            catch { return t; }
        }

        // ------------------------------------------------- persistencia
        private static string ConfigPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cmdwatch");
                return Path.Combine(dir, "ui.cfg");
            }
        }

        public static void Load()
        {
            try
            {
                string p = ConfigPath;
                if (!File.Exists(p)) return;
                foreach (string line in File.ReadAllLines(p))
                {
                    if (!line.StartsWith("lang=", StringComparison.OrdinalIgnoreCase)) continue;
                    string v = line.Substring(5).Trim();
                    Atual = v.Equals("en", StringComparison.OrdinalIgnoreCase) ? Idioma.En : Idioma.PtBr;
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                string p = ConfigPath;
                string dir = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(p, "lang=" + (Atual == Idioma.En ? "en" : "pt-br") + Environment.NewLine);
            }
            catch { }
        }
    }
}
