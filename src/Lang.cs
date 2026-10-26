using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CmdWatch
{
    internal enum Idioma { PtBr = 0, En = 1 }

    /// <summary>
    /// Idiomas do produto. O dicionario guarda pt-BR e en lado a lado e e usado
    /// tanto pela saida do CLI quanto pela interface do painel, para que os dois
    /// falem com as mesmas palavras. A escolha da interface fica salva em
    /// %APPDATA%\cmdwatch\ui.cfg.
    ///
    /// Observacao: o texto das explicacoes de comando (dicionario em Core.cs)
    /// e a descricao de origem do chamador permanecem em portugues - vem do
    /// nucleo compartilhado com a CLI.
    /// </summary>
    internal static class Lang
    {
        private static readonly Dictionary<string, string[]> Textos = new Dictionary<string, string[]>
        {
            // -------------------------------------------- painel (chrome)
            { "windowTitle",   new[] { "cmdwatch - execucao de scripts .cmd/.bat", "cmdwatch - .cmd/.bat script execution" } },
            { "sessions",      new[] { "SESSOES .CMD",      "SESSIONS .CMD" } },
            { "flow",          new[] { "FLUXO DE COMANDOS", "COMMAND FLOW" } },
            { "activity",      new[] { "ATIVIDADE .CMD",    "ACTIVITY .CMD" } },
            { "sessionsShort", new[] { "SESSOES",           "SESSIONS" } },
            { "callsShort",    new[] { "CHAMADAS",          "CALLS" } },
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

            // -------------------------------------------- formato das mensagens
            // (identico ao da saida do CLI - ver HumanFormat.cs)
            { "openTitle",     new[] { "SESSÃO .CMD ABERTA",  "SESSION .CMD OPENED" } },
            { "closeTitle",    new[] { "SESSÃO ENCERRADA",   "SESSION ENDED" } },
            { "fScript",       new[] { "script",   "script" } },
            { "fOrigin",       new[] { "origem",   "origin" } },
            { "fCaller",       new[] { "chamador", "caller" } },
            { "fChain",        new[] { "cadeia",   "chain" } },
            { "fSummary",      new[] { "resumo",   "summary" } },
            { "fSteps",        new[] { "passos",   "steps" } },
            { "fIn",           new[] { "em",       "in" } },
            { "fLine",         new[] { "linha {0} do script",  "line {0} of the script" } },
            { "fCommand",      new[] { "comando",  "command" } },
            { "fLevel",        new[] { "nivel {0}",          "level {0}" } },
            { "fCalledAt",     new[] { "(chamado na linha {0} de {1})", "(called at line {0} of {1})" } },
            { "fExecuted",     new[] { "{0}s, {1} comandos executados",  "{0}s, {1} commands executed" } },
            { "fLastLine",     new[] { "última linha mapeada: {0}",    "last mapped line: {0}" } },
            { "fMappedSteps",  new[] { "{0} comandos mapeados no arquivo", "{0} commands mapped in the file" } },

            // -------------------------------------------- cabecalho do log
            { "bannerTitle",      new[] { "cmdwatch {0} — monitorando execucao de .cmd/.bat",
                                          "cmdwatch {0} — monitoring .cmd/.bat execution" } },
            { "bannerMotor",      new[] { "motor",      "engine" } },
            { "bannerInterval",   new[] { "intervalo",  "interval" } },
            { "bannerLog",        new[] { "log",        "log" } },
            { "bannerFilter",     new[] { "filtro",     "filter" } },
            { "bannerCaptureCli", new[] { "capturando ... (Ctrl+C para sair)", "capturing ... (Ctrl+C to exit)" } },
            { "bannerCaptureGui", new[] { "capturando ... (ESC fecha o painel)", "capturing ... (ESC closes the panel)" } },
            { "engineAdmin",      new[] { "(privilegiado)",   "(privileged)" } },
            { "engineNoAdmin",    new[] { "(sem admin)",       "(no admin)" } },
            { "engineInstant",    new[] { "evento instantaneo", "instant event" } },
            { "filterNone",       new[] { "(todos)",           "(all)" } },
            { "logOff",           new[] { "(desativado)",      "(disabled)" } }
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
