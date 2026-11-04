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
            // -------------------------------------------- painel v2
            { "v2Title", new[] { "CMDwatch — Monitor de sessões", "CMDwatch — Session monitor" } },
            { "v2Monitoring", new[] { "Monitorando", "Monitoring" } },
            { "v2Paused", new[] { "Pausado", "Paused" } },
            { "v2Search", new[] { "Buscar sessões", "Search sessions" } },
            { "v2Pause", new[] { "Pausar", "Pause" } },
            { "v2Resume", new[] { "Retomar", "Resume" } },
            { "v2Page", new[] { "Visão geral", "Overview" } },
            { "v2PageSub", new[] { "Acompanhe suas sessões e comandos em tempo real.", "Follow your sessions and commands in real time." } },
            { "v2StatSess", new[] { "Sessões ativas", "Active sessions" } },
            { "v2StatWin", new[] { "Janelas detectadas", "Windows detected" } },
            { "v2StatCmd", new[] { "Comandos executados", "Commands executed" } },
            { "v2StatTime", new[] { "Tempo de monitoramento", "Monitoring time" } },
            { "v2Sessions", new[] { "Sessões CMD", "CMD sessions" } },
            { "v2FilterAll", new[] { "Todas as sessões", "All sessions" } },
            { "v2FilterRun", new[] { "Em execução", "Running" } },
            { "v2FilterEnd", new[] { "Encerradas", "Ended" } },
            { "v2ColScript", new[] { "Script", "Script" } },
            { "v2ColPid", new[] { "PID", "PID" } },
            { "v2ColOrigin", new[] { "Processo de origem", "Parent process" } },
            { "v2ColCmds", new[] { "Comandos", "Commands" } },
            { "v2ColStart", new[] { "Início", "Started" } },
            { "v2ColStatus", new[] { "Status", "Status" } },
            { "v2Running", new[] { "Em execução", "Running" } },
            { "v2Ended", new[] { "Encerrada", "Ended" } },
            { "v2Windows", new[] { "Janelas detectadas", "Detected windows" } },
            { "v2ColProc", new[] { "Processo", "Process" } },
            { "v2ColPath", new[] { "Caminho da janela", "Window path" } },
            { "v2ColDur", new[] { "Duração", "Duration" } },
            { "v2WinVisible", new[] { "Visível", "Visible" } },
            { "v2WinHidden", new[] { "Oculta", "Hidden" } },
            { "v2WinClosed", new[] { "Encerrada", "Closed" } },
            { "v2NoSessions", new[] { "Nenhuma sessão .cmd observada ainda", "No .cmd session observed yet" } },
            { "v2NoWindows", new[] { "Nenhuma janela observada ainda", "No window observed yet" } },
            { "v2Flow", new[] { "Fluxo de comandos", "Command flow" } },
            { "v2FlowSess", new[] { "Sessão: {0}", "Session: {0}" } },
            { "v2FlowAll", new[] { "todas", "all" } },
            { "v2FlowSearch", new[] { "Buscar no fluxo...", "Search the flow..." } },
            { "v2Copy", new[] { "Copiar o fluxo exibido", "Copy the shown flow" } },
            { "v2Clear", new[] { "Limpar o fluxo (F5)", "Clear the flow (F5)" } },
            { "v2ClearSess", new[] { "Limpar", "Clear" } },
            { "v2ClearSessTip", new[] { "Limpar a lista: mostrar s\u00f3 as sess\u00f5es que abrirem a partir de agora", "Clear the list: show only sessions that start from now on" } },
            { "v2ClearedSess", new[] { "Sess\u00f5es limpas: mostrando as que abrirem a partir de {0}", "Sessions cleared: showing those that start from {0}" } },
            { "v2Collapse", new[] { "Recolher", "Collapse" } },
            { "v2Expand", new[] { "Expandir", "Expand" } },
            { "v2Copied", new[] { "Fluxo copiado ({0} linhas)", "Flow copied ({0} lines)" } },
            { "v2Activity", new[] { "Atividade", "Activity" } },
            { "v2Range", new[] { "Últimos {0} s", "Last {0} s" } },
            { "v2LegCmd", new[] { "Comandos", "Commands" } },
            { "v2LegSess", new[] { "Sessões", "Sessions" } },
            { "v2FooterOn", new[] { "Monitoramento ativo", "Monitoring active" } },
            { "v2FooterPaused", new[] { "Exibição pausada", "Display paused" } },
            { "v2Tray", new[] { "Minimizar para a bandeja", "Minimize to tray" } },
            { "v2NavHome", new[] { "Visão geral: limpa a busca, o filtro e a sessão selecionada", "Overview: clears search, filter and selected session" } },
            { "v2NavLog", new[] { "Abrir o arquivo de log", "Open the log file" } },
            { "v2NavChart", new[] { "Intervalo do gráfico de atividade", "Activity chart range" } },
            { "v2Settings", new[] { "Configurações", "Settings" } },
            { "v2SettingsMsg", new[] { "Motor de captura: {0}\nIntervalo: {1} ms\nObservador de janelas: {2}\nArquivo de log: {3}\nFiltro: {4}\nMáscara de segredos: sempre ativa\nTema: escuro (CMDWATCH_TEMA=claro para o claro)\n\nAs opções são definidas na linha de comando ao abrir o painel.", "Capture engine: {0}\nInterval: {1} ms\nWindow watcher: {2}\nLog file: {3}\nFilter: {4}\nSecret masking: always on\nTheme: dark (CMDWATCH_TEMA=claro for light)\n\nOptions are set on the command line when the panel starts." } },
            { "v2On", new[] { "ligado", "on" } },
            { "v2Off", new[] { "desligado", "off" } },
            { "v2NoLog", new[] { "(sem log)", "(no log)" } },

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

            // -------------------------------------------- painel de janelas
            { "windows",       new[] { "JANELAS VISIVEIS", "VISIBLE WINDOWS" } },
            { "windowsShort",  new[] { "JANELAS",          "WINDOWS" } },
            { "colWindow",     new[] { "JANELA",   "WINDOW" } },
            { "colAuthor",     new[] { "AUTOR",    "AUTHOR" } },
            { "colFocus",      new[] { "FOCO",     "FOCUS" } },
            { "colDuration",   new[] { "DURACAO",  "DURATION" } },
            { "colState",      new[] { "ESTADO",   "STATE" } },
            { "noWindows",     new[] { "nenhuma janela visivel observada ainda", "no visible window observed yet" } },
            { "winVisible",    new[] { "visivel",  "visible" } },
            { "winHidden",     new[] { "oculta",   "hidden" } },
            { "winClosed",     new[] { "fechada",  "closed" } },
            { "winByConsole",  new[] { "pelo console", "by console" } },

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

            // -------------------------------------------- menu de contexto
            { "search",        new[] { "Buscar", "Search" } },
            { "menuClose",     new[] { "Fechar", "Close" } },
            { "confirmCloseTitle", new[] { "Encerrar o script?", "Terminate the script?" } },
            { "confirmCloseMsg",   new[] { "O script {0} (PID {1}) e os processos que ele deixou rodando serão encerrados.\nO cmdwatch continua monitorando.",
                                           "The script {0} (PID {1}) and the processes it left running will be terminated.\ncmdwatch keeps monitoring." } },
            { "confirmYes",    new[] { "Fechar",    "Terminate" } },
            { "confirmNo",     new[] { "Cancelar",  "Cancel" } },
            { "noticeClosed",  new[] { "Script encerrado: {0}", "Script terminated: {0}" } },
            { "noticeCloseGone", new[] { "{0} já havia terminado", "{0} had already ended" } },
            { "noticeCloseFail", new[] { "Não foi possível encerrar {0}: {1}", "Could not terminate {0}: {1}" } },

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
            { "logOff",           new[] { "(desativado)",      "(disabled)" } },

            // -------------------------------------------- eventos de janela
            { "bannerWindows",    new[] { "janelas",  "windows" } },
            { "bannerWindowsOn",  new[] { "ligado (abrir, foco, esconder, fechar)", "on (open, focus, hide, close)" } },
            { "winOpenTitle",     new[] { "JANELA ABERTA",      "WINDOW OPENED" } },
            { "winFocusTitle",    new[] { "JANELA EM FOCO",     "WINDOW FOCUSED" } },
            { "winHideTitle",     new[] { "JANELA ESCONDIDA",   "WINDOW HIDDEN" } },
            { "winCloseTitle",    new[] { "JANELA FECHADA",     "WINDOW CLOSED" } },
            { "fOwner",           new[] { "dono",     "owner" } },
            { "fAuthor",          new[] { "autor",    "author" } },
            { "fProgram",         new[] { "programa", "program" } },
            { "fConsole",         new[] { "console",  "console" } },
            { "fDuration",        new[] { "duracao",  "duration" } },
            { "fFocusCount",      new[] { "foco",     "focus" } },
            { "fNoTitle",         new[] { "sem titulo", "no title" } },
            { "fOthers",          new[] { "outros",   "others" } }
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
                SetCfg("lang", Atual == Idioma.En ? "en" : "pt-br");
            }
            catch { }
        }

        /// <summary>Valor de uma chave do ui.cfg ("" se ausente).</summary>
        public static string GetCfg(string chave)
        {
            try
            {
                string p = ConfigPath;
                if (!File.Exists(p)) return "";
                foreach (string line in File.ReadAllLines(p))
                    if (line.StartsWith(chave + "=", StringComparison.OrdinalIgnoreCase))
                        return line.Substring(chave.Length + 1).Trim();
            }
            catch { }
            return "";
        }

        /// <summary>Grava uma chave no ui.cfg preservando as demais linhas.</summary>
        public static void SetCfg(string chave, string valor)
        {
            try
            {
                string p = ConfigPath;
                string dir = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var linhas = new System.Collections.Generic.List<string>();
                if (File.Exists(p))
                    foreach (string line in File.ReadAllLines(p))
                        if (line.Trim().Length > 0 && !line.StartsWith(chave + "=", StringComparison.OrdinalIgnoreCase)) linhas.Add(line);
                linhas.Add(chave + "=" + valor);
                File.WriteAllLines(p, linhas);
            }
            catch { }
        }
    }
}
