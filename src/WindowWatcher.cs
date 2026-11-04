using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CmdWatch
{
    /// <summary>Uma janela observada na tela.</summary>
    internal sealed class WindowRecord
    {
        public IntPtr Hwnd;
        public string Title = "";
        public string ClassName = "";
        public int OwnerPid;
        public string OwnerName = "";
        public string OwnerCmdLine = "";
        /// <summary>Processo por tras da janela (o autor).</summary>
        public int AuthorPid;
        public string AuthorName = "";
        public string AuthorCmdLine = "";
        /// <summary>
        /// O programa que esta rodando na janela agora (o cliente mais novo do
        /// console). Quando ha um intermediario - um script que chamou um
        /// programa - o autor e esse intermediario e o programa fica aqui.
        /// </summary>
        public int ProgramPid;
        public string ProgramName = "";
        /// <summary>Processos ligados ao console da janela.</summary>
        public readonly List<int> ConsoleClients = new List<int>();
        public DateTime FirstSeen;
        public DateTime LastSeen;
        public DateTime? HiddenAt;
        public DateTime? ClosedAt;
        public bool Visible = true;
        public int FocusCount;
        public DateTime? LastFocusAt;
        /// <summary>True quando o autor foi resolvido pelo console (nao pelo dono).</summary>
        public bool AutorPeloConsole;
        /// <summary>Ultima vez que o autor foi recalculado.</summary>
        public DateTime UltimaResolucao = DateTime.MinValue;
        /// <summary>
        /// Cadeia de ancestrais do autor (pai, avo...), com linhas de comando.
        /// Mesmo formato do chain do batch_open: permite ver quem chamou mesmo
        /// quando o autor da janela e so o executavel final (git.exe, etc.).
        /// </summary>
        public readonly List<ProcInfo> Chain = new List<ProcInfo>();
        /// <summary>
        /// Janela auxiliar (PseudoConsoleWindow) associada a uma janela Cascadia
        /// do Windows Terminal. Nao conta em dobro no painel/log.
        /// </summary>
        public bool Auxiliar;
        /// <summary>Hwnd da janela Cascadia a que esta PseudoConsole esta ligada.</summary>
        public IntPtr LinkedHwnd = IntPtr.Zero;

        public bool Fechada { get { return ClosedAt.HasValue; } }

        /// <summary>Quanto tempo a janela ficou na tela (ate fechar ou esconder).</summary>
        public double DuracaoSeg
        {
            get
            {
                DateTime fim = ClosedAt ?? (HiddenAt ?? DateTime.Now);
                return (fim - FirstSeen).TotalSeconds;
            }
        }
    }

    /// <summary>
    /// Pergunta ao console quem esta ligado a ele.
    ///
    /// A janela de um terminal NAO pertence ao terminal: no Windows Terminal a
    /// janela e do WindowsTerminal.exe (que foi criado pelo broker do sistema,
    /// sem relacao de processo com o que roda dentro). A aba aparece como uma
    /// janela propria (classe PseudoConsoleWindow), e o console dela tem como
    /// cliente quem esta rodando dentro - e o dono da aba e um dos clientes.
    ///
    /// O truque e o mesmo do monitor original: anexar ao console (AttachConsole)
    /// e perguntar a lista de processos ligada a ele (GetConsoleProcessList).
    /// Isso funciona sem permissao para processos comuns e devolve o grupo
    /// exato: por exemplo {cmdwatch.exe, powershell.exe} na aba do terminal.
    ///
    /// Anexar a um console e uma operacao do PROCESSO inteiro (nao da thread),
    /// entao tudo aqui e serializado por uma trava - e so e feito quando o
    /// cmdwatch nao tem console proprio (no modo CLI o console pertence a quem
    /// chamou, e nao se pode trocar isso no meio da saida).
    /// </summary>
    internal static class ConsoleProbe
    {
        private static readonly object _lock = new object();
        private static bool _podeAnexar;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetConsoleProcessList(uint[] list, uint count);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);

        /// <summary>
        /// Descobre se o processo pode anexar a um console. So da para anexar
        /// quando o processo NAO tem console proprio.
        /// </summary>
        public static void Init()
        {
            try
            {
                uint[] buf = new uint[4];
                _podeAnexar = GetConsoleProcessList(buf, 4) == 0;
            }
            catch { _podeAnexar = false; }
        }

        public static bool PodeAnexar { get { return _podeAnexar; } }

        /// <summary>Processos ligados ao console do processo indicado.</summary>
        public static List<int> Clientes(int pid)
        {
            var res = new List<int>();
            if (pid <= 0 || !_podeAnexar) return res;

            lock (_lock)
            {
                bool anexado = false;
                try
                {
                    FreeConsole();
                    if (!AttachConsole((uint)pid)) return res;
                    anexado = true;
                    // sem isso, fechar o console anexado (ou Ctrl+C nele) pode
                    // matar o proprio cmdwatch junto com o grupo do console
                    try { SetConsoleCtrlHandler(IntPtr.Zero, true); } catch { }

                    uint[] buf = new uint[64];
                    uint n = GetConsoleProcessList(buf, (uint)buf.Length);
                    uint eu = GetCurrentProcessId();
                    for (uint i = 0; i < n && i < buf.Length; i++)
                    {
                        int p = (int)buf[i];
                        if (p <= 0 || p == (int)eu) continue;
                        if (!res.Contains(p)) res.Add(p);
                    }
                }
                catch { }
                finally
                {
                    try { if (anexado) FreeConsole(); } catch { }
                }
            }
            return res;
        }
    }

    /// <summary>
    /// Observa as janelas da tela.
    ///
    /// O cmdwatch antes so listava processos: um cmd.exe oculto e um que tomou
    /// a tela eram a mesma coisa. Aqui as janelas viram eventos: abriu, ganhou
    /// foco, escondeu, fechou - com a duracao e o autor de cada uma.
    ///
    /// Como os eventos chegam: um gancho de WinEvent (SetWinEventHook) entrega
    /// as mudancas de janela do sistema inteiro, sem varrer a tela. Cada evento
    /// novo tambem ACORDA o motor de processos, que senao esperaria o proximo
    /// retrato: e assim que uma janela aparece, o cmdwatch ja sabe quem a
    /// abriu.
    /// </summary>
    internal sealed class WindowWatcher : IDisposable
    {
        // eventos de janela
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint EVENT_OBJECT_CREATE = 0x8000;
        private const uint EVENT_OBJECT_DESTROY = 0x8001;
        private const uint EVENT_OBJECT_SHOW = 0x8002;
        private const uint EVENT_OBJECT_HIDE = 0x8003;

        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        private const int GWL_STYLE = -16;
        private const long WS_CHILD = 0x40000000L;

        private const int QS_ALLINPUT = 0x0400;
        private const uint PM_REMOVE = 0x0001;

        private readonly object _lock = new object();
        private readonly Dictionary<IntPtr, WindowRecord> _janelas = new Dictionary<IntPtr, WindowRecord>();
        private readonly List<IntPtr> _lixo = new List<IntPtr>();

        private Thread _thread;
        private volatile bool _running;
        private IntPtr _hookJanelas;
        private IntPtr _hookFoco;

        // o gancho e um delegate: se o GC o coletar, o sistema deixa de chamar
        private WinEventDelegate _callback;

        /// <summary>Quem resolve processos (o orquestrador do cmdwatch).</summary>
        public Watcher Processos;

        /// <summary>
        /// Chamado quando a janela revela um cmd.exe: o dono da janela e o
        /// processo recem-nascido, e o monitor pode processa-lo agora, sem
        /// esperar o proximo retrato.
        /// </summary>
        public Action<int> AoDescobrirProcesso;

        /// <summary>Sinal para o motor de processos acordar na hora de uma janela.</summary>
        public readonly ManualResetEvent Wake = new ManualResetEvent(false);

        /// <summary>Notifica: (registro, tipo de evento).</summary>
        public Action<WindowRecord, string> OnChanged;

        /// <summary>Janelas conhecidas (copia segura para a interface).</summary>
        public List<WindowRecord> Windows
        {
            get { lock (_lock) { return new List<WindowRecord>(_janelas.Values); } }
        }

        public bool Ativo { get { return _running; } }

        // ------------------------------------------------------ ciclo de vida
        public void Start()
        {
            if (_running) return;
            ConsoleProbe.Init();
            _running = true;
            _callback = OnWinEvent;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "cmdwatch-janelas";
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { Wake.Set(); } catch { }
            try { if (_thread != null && !_thread.Join(1500)) _thread.Interrupt(); } catch { }
        }

        public void Dispose()
        {
            Stop();
            try { Wake.Dispose(); } catch { }
        }

        /// <summary>
        /// Thread do gancho: instala os ganchos de janela e bombeia mensagens
        /// (o WinEvent entrega os eventos pela fila de mensagens da thread).
        /// </summary>
        private void Loop()
        {
            try
            {
                _hookJanelas = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_HIDE, IntPtr.Zero,
                    _callback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
                _hookFoco = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                    _callback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            }
            catch { }

            // varredura inicial: as janelas que ja estavam na tela quando o
            // monitor abriu (o gancho so avisa das que surgem depois). Elas
            // entram no fluxo tambem: e o retrato do que esta na tela agora -
            // com o autor de cada uma.
            try { Varrer(); } catch { }

            DateTime ultimaVarredura = DateTime.Now;
            MSG msg;
            while (_running)
            {
                try
                {
                    MsgWaitForMultipleObjects(0, new IntPtr[0], false, 100, QS_ALLINPUT);
                    while (PeekMessage(out msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                    {
                        TranslateMessage(ref msg);
                        DispatchMessage(ref msg);
                    }
                }
                catch { }

                if (DateTime.Now - ultimaVarredura > TimeSpan.FromSeconds(2))
                {
                    ultimaVarredura = DateTime.Now;
                    try { Varrer(); } catch { }
                }
            }

            try { if (_hookJanelas != IntPtr.Zero) UnhookWinEvent(_hookJanelas); } catch { }
            try { if (_hookFoco != IntPtr.Zero) UnhookWinEvent(_hookFoco); } catch { }
        }

        // ------------------------------------------------------ gancho
        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return;

                if (eventType == EVENT_SYSTEM_FOREGROUND)
                {
                    MarcarFoco(hwnd);
                    return;
                }

                // evento da JANELA, e nao de um objeto dentro dela: um
                // EVENT_OBJECT_DESTROY de um controle filho chega com o hwnd
                // da janela-pai, e fechava a janela que continuava de pe.
                // (OBJID_WINDOW = 0, CHILDID_SELF = 0)
                if (idObject != 0 || idChild != 0) return;

                if (eventType == EVENT_OBJECT_CREATE || eventType == EVENT_OBJECT_SHOW)
                {
                    bool visivel = EhJanelaRelevante(hwnd);
                    if (visivel)
                    {
                        Registrar(hwnd);
                    }
                    else if (EhJanelaConsole(hwnd))
                    {
                        // janela de console ESCONDIDA: nao entra na lista (nao esta
                        // na tela), mas e o aviso de que um script comecou. O
                        // dono dela e o proprio cmd.exe, entao uma consulta
                        // dirigida acha a linha de comando na hora - sem esperar
                        // o proximo retrato do sistema, que perderia um script
                        // que vive menos de 100 ms.
                        AcordarConsole(hwnd);
                    }
                    else return;

                    // acorda o motor de processos: a janela acabou de aparecer
                    try { Wake.Set(); } catch { }
                    return;
                }

                if (eventType == EVENT_OBJECT_HIDE)
                {
                    Esconder(hwnd);
                    return;
                }

                if (eventType == EVENT_OBJECT_DESTROY)
                {
                    Fechar(hwnd);
                    return;
                }
            }
            catch { }
        }

        /// <summary>
        /// Uma janela que interessa: de primeiro nivel, visivel e com titulo
        /// (as janelas de console valem mesmo sem titulo - e por elas que se
        /// descobre quem roda no terminal).
        /// </summary>
        private bool EhJanelaRelevante(IntPtr hwnd)
        {
            try
            {
                if (!IsWindowVisible(hwnd)) return false;
                if (GetAncestor(hwnd, GA_ROOT) != hwnd) return false;

                // a propria janela do monitor nao entra na lista
                uint dono;
                GetWindowThreadProcessId(hwnd, out dono);
                if ((int)dono == GetCurrentProcessId()) return false;

                int len = GetWindowTextLengthW(hwnd);
                if (len > 0) return true;

                string cls = NomeDaClasse(hwnd);
                return cls == "ConsoleWindowClass" || cls == "PseudoConsoleWindow";
            }
            catch { return false; }
        }

        /// <summary>
        /// Uma janela de CONSOLE - visivel ou nao.
        ///
        /// Isso e o que faz o script curto e ESCONDIDO nao escapar: a janela de
        /// console oculta (SW_HIDE) dispara evento de criacao do mesmo jeito
        /// que a visivel (medido), mas nao passa no filtro de visibilidade.
        /// Como a janela e criada junto com o cmd.exe, ela e o aviso de que
        /// um script comecou - e o aviso chega antes do processo sumir.
        /// </summary>
        private bool EhJanelaConsole(IntPtr hwnd)
        {
            try
            {
                if (GetAncestor(hwnd, GA_ROOT) != hwnd) return false;
                uint dono;
                GetWindowThreadProcessId(hwnd, out dono);
                if ((int)dono == GetCurrentProcessId()) return false;
                string cls = NomeDaClasse(hwnd);
                return cls == "ConsoleWindowClass" || cls == "PseudoConsoleWindow";
            }
            catch { return false; }
        }

        private string NomeDaClasse(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            try { GetClassNameW(hwnd, sb, 256); } catch { }
            return sb.ToString();
        }

        // ------------------------------------------------------ registro
        private void Registrar(IntPtr hwnd)
        {
            Registrar(hwnd, false);
        }

        /// <summary>
        /// Registra (ou atualiza) uma janela.
        ///
        /// A varredura periodica passa por aqui a cada 2 segundos: nao faz
        /// sentido gerar um evento a cada pasada (a janela nao mudou). O
        /// evento so sai quando algo muda de verdade - e o autor e recalculado
        /// de tempos em tempos, porque quem esta rodando no console pode
        /// mudar (um programa novo comeca no terminal).
        /// </summary>
        private void Registrar(IntPtr hwnd, bool notificarSempre)
        {
            try
            {
                WindowRecord rec;
                bool novo = false;
                lock (_lock)
                {
                    if (!_janelas.TryGetValue(hwnd, out rec))
                    {
                        rec = new WindowRecord();
                        rec.Hwnd = hwnd;
                        rec.FirstSeen = DateTime.Now;
                        _janelas[hwnd] = rec;
                        novo = true;
                    }
                }

                rec.LastSeen = DateTime.Now;

                string tituloAntes = rec.Title;
                int donoAntes = rec.OwnerPid;
                bool visivelAntes = rec.Visible;

                LerDados(hwnd, rec);
                DespertarCmd(rec);
                // janela de console: o dono e o proprio cmd.exe, mesmo quando ele
                // ainda nao passou pelo retrato do sistema (e por isso o nome do
                // dono ainda e "?")
                if (EhJanelaConsole(hwnd)) AcordarConsole(hwnd);

                bool mudou = novo
                    || !tituloAntes.Equals(rec.Title, StringComparison.Ordinal)
                    || donoAntes != rec.OwnerPid
                    || visivelAntes != rec.Visible;

                bool recalcular = novo || mudou || notificarSempre ||
                    (DateTime.Now - rec.UltimaResolucao) > TimeSpan.FromSeconds(5);

                if (!recalcular) return;

                // so marca como reexibida se a janela estiver visivel de verdade
                if (rec.Visible) rec.HiddenAt = null;
                rec.UltimaResolucao = DateTime.Now;

                int autorAntes = rec.AuthorPid;
                ResolverAutor(rec);
                AssociarTerminal(rec);
                MontarCadeia(rec);

                // PseudoConsole auxiliar: nao gera open proprio (evita dobra
                // com a janela Cascadia do Windows Terminal)
                if (rec.Auxiliar)
                {
                    if (novo || mudou || autorAntes != rec.AuthorPid)
                        PropagarAutorParaLigada(rec);
                    return;
                }

                if (novo) Notificar(rec, "open");
                else if (mudou || autorAntes != rec.AuthorPid) Notificar(rec, "update");
            }
            catch { }
        }

        private void LerDados(IntPtr hwnd, WindowRecord rec)
        {
            try
            {
                var sb = new StringBuilder(1024);
                GetWindowTextW(hwnd, sb, 1024);
                rec.Title = sb.ToString();
                rec.ClassName = NomeDaClasse(hwnd);

                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                rec.OwnerPid = (int)pid;

                // o estado real da janela: e o que permite distinguir "escondida"
                // de "fechada" quando ela volta a aparecer
                rec.Visible = IsWindowVisible(hwnd);

                if (Processos != null)
                {
                    ProcInfo dono = Processos.Buscar(rec.OwnerPid);
                    if (dono != null)
                    {
                        rec.OwnerName = dono.Name;
                        rec.OwnerCmdLine = dono.CmdLine;
                    }
                }
                if (rec.OwnerName.Length == 0) rec.OwnerName = "?";
            }
            catch { }
        }

        /// <summary>
        /// Acorda o motor a partir de uma janela de console ESCONDIDA.
        ///
        /// O dono da janela de console e o proprio cmd.exe (medido: a janela do
        /// console tem o dono no cliente inicial do console). Como a janela e
        /// criada junto com o processo, uma consulta dirigida acha a linha de
        /// comando na hora - inclusive de um script que vive menos de 100 ms,
        /// que o retrato periodico perderia.
        /// </summary>
        private void AcordarConsole(IntPtr hwnd)
        {
            try
            {
                if (AoDescobrirProcesso == null) return;
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return;
                if ((int)pid == GetCurrentProcessId()) return;
                AoDescobrirProcesso((int)pid);
            }
            catch { }
        }

        /// <summary>
        /// A janela revelou um cmd.exe: o dono (ou o autor) e o processo que
        /// acabou de nascer. O monitor pode processa-lo agora - sem esperar o
        /// proximo retrato, que custaria caro para cada evento de janela.
        /// </summary>
        private void DespertarCmd(WindowRecord rec)
        {
            try
            {
                if (AoDescobrirProcesso == null) return;
                if (EhCmd(rec.OwnerName)) { AoDescobrirProcesso(rec.OwnerPid); return; }
                if (EhCmd(rec.AuthorName)) AoDescobrirProcesso(rec.AuthorPid);
            }
            catch { }
        }

        private static bool EhCmd(string nome)
        {
            return !string.IsNullOrEmpty(nome) && nome.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Hospedeiro de console (conhost/OpenConsole): e a casa do terminal, nao
        /// o programa que roda dentro dela.
        /// </summary>
        private static bool EhHospedeiroConsole(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return false;
            return nome.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase)
                || nome.Equals("OpenConsole.exe", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Descobre quem esta por tras da janela.
        ///
        /// A ordem importa: primeiro se pergunta ao CONSOLE (a aba do terminal
        /// responde quem roda nela), depois se usa a arvore de processos - mas
        /// so quando o dono e um shell (um navegador tem dezenas de filhos que
        /// nada tem a ver com a janela). Por ultimo, o proprio dono.
        /// </summary>
        private void ResolverAutor(WindowRecord rec)
        {
            rec.ConsoleClients.Clear();
            rec.AutorPeloConsole = false;

            // (1) o console: a aba do terminal responde quem esta rodando nela
            List<int> clients = ConsoleProbe.Clientes(rec.OwnerPid);
            if (clients.Count > 0)
            {
                rec.ConsoleClients.AddRange(clients);

                // consulta dirigida a cada cliente do console: nome, pai e hora
                // de inicio. Sem retrato geral - e funciona para processo elevado.
                var dados = new Dictionary<int, ProcInfo>();
                foreach (int p in clients)
                {
                    ProcInfo pi;
                    if (NativeProcess.TryGetProcessInfo(p, out pi) && pi != null && pi.Name.Length > 0)
                        dados[p] = pi;
                }

                if (dados.Count > 0)
                {
                    // o mais antigo abriu o console (a raiz); o mais novo e o
                    // programa que esta rodando agora. A lista do console NAO
                    // vem ordenada - a ordem e a de anexacao - entao ordena por
                    // hora de inicio.
                    var ordem = new List<int>(dados.Keys);
                    ordem.Sort(delegate (int a, int b)
                    {
                        DateTime ta = dados[a].StartedAt;
                        DateTime tb = dados[b].StartedAt;
                        return ta.CompareTo(tb);
                    });

                    int raiz = ordem[0];
                    int programa = ordem[ordem.Count - 1];
                    rec.ProgramPid = programa;
                    rec.ProgramName = dados[programa].Name;

                    // O AUTOR e quem CHAMOU o programa: o ancestral mais profundo
                    // do programa dentro do console, sem contar a raiz.
                    //   console: cmd.exe -> python.exe -> git.exe
                    //       programa = git.exe   autor = python.exe (quem chamou)
                    //   console: cmd.exe -> ping.exe
                    //       programa = ping.exe  autor = ping.exe (o shell nao
                    //       tem intermediario - o programa e o autor)
                    int autor = programa;
                    int atual = programa;
                    for (int guarda = 0; guarda < 8; guarda++)
                    {
                        int pai = dados[atual].ParentPid;
                        if (pai <= 0) break;
                        if (!dados.ContainsKey(pai)) break;   // saiu do console
                        if (pai == raiz)
                        {
                            // a raiz so fica de fora quando e um shell. Quando a
                            // raiz e um programa (python.exe abriu o console e
                            // chamou git.exe), ela e quem chamou: e o autor.
                            if (!EhShellDeComando(dados[raiz].Name)) autor = raiz;
                            break;
                        }
                        autor = pai;
                        atual = pai;
                    }

                    AplicarAutor(rec, autor);
                    rec.AutorPeloConsole = true;
                    return;
                }

                // sem dados: fica com o proprio dono
                AplicarAutor(rec, rec.OwnerPid);
                rec.AutorPeloConsole = false;
                return;
            }

            // (2) dono que e um shell: o autor e o filho mais recente (o
            //     programa que o shell acabou de lancar). O conhost/OpenConsole
            //     e o hospedeiro do console, nao o programa: fica de fora.
            if (EhShell(rec.OwnerName))
            {
                int herdeiro = 0;
                DateTime maisNovoFilho = DateTime.MinValue;
                if (Processos != null)
                {
                    foreach (int f in Processos.FilhosDe(rec.OwnerPid))
                    {
                        ProcInfo pi = Processos.Buscar(f);
                        if (pi == null) continue;
                        if (EhHospedeiroConsole(pi.Name)) continue;
                        if (pi.StartedAt == DateTime.MinValue || pi.StartedAt <= maisNovoFilho) continue;
                        maisNovoFilho = pi.StartedAt; herdeiro = f;
                    }
                }
                if (herdeiro != 0)
                {
                    AplicarAutor(rec, herdeiro);
                    return;
                }
            }

            // (3) janela de console sem resposta do console (no modo terminal a
            //     ConsoleProbe nao roda). O dono e o programa que abriu o console
            //     (git.exe chamado por um script python): o autor e quem o chamou,
            //     o ancestral mais proximo que nao e shell, validado pela hora de
            //     inicio. Mesma regra do caminho (1).
            if (EhClasseConsole(rec.ClassName) && !EhShellDeComando(rec.OwnerName))
            {
                int chamador = ChamadorDe(rec.OwnerPid);
                if (chamador > 0)
                {
                    rec.ProgramPid = rec.OwnerPid;
                    rec.ProgramName = rec.OwnerName;
                    AplicarAutor(rec, chamador);
                    return;
                }
            }

            // (4) janela normal: o proprio dono e o autor
            AplicarAutor(rec, rec.OwnerPid);
        }

        private static bool EhClasseConsole(string cls)
        {
            return cls == "ConsoleWindowClass" || cls == "PseudoConsoleWindow";
        }

        /// <summary>
        /// Quem chamou o programa: sobe pelos pais pulando shells de comando
        /// (python -> cmd /c -> git) ate o primeiro que nao e shell. Cada pai so
        /// vale se nasceu antes do filho (pid reaproveitado nao conta). Para em
        /// processo do sistema/terminal: ai nao ha chamador (0).
        /// </summary>
        private int ChamadorDe(int pid)
        {
            ProcInfo atual = BuscarProc(pid);
            for (int guarda = 0; atual != null && guarda < 4; guarda++)
            {
                if (atual.ParentPid <= 0) return 0;
                ProcInfo pai = BuscarProc(atual.ParentPid);
                if (pai == null || pai.Name.Length == 0) return 0;
                if (pai.StartedAt != DateTime.MinValue && atual.StartedAt != DateTime.MinValue
                    && pai.StartedAt > atual.StartedAt) return 0;
                if (EhTerminalHost(pai.Name) || EhHospedeiroConsole(pai.Name) || EhSistema(pai.Name)) return 0;
                if (!EhShellDeComando(pai.Name)) return pai.Pid;
                atual = pai;
            }
            return 0;
        }

        private ProcInfo BuscarProc(int pid)
        {
            if (pid <= 0) return null;
            ProcInfo pi = Processos != null ? Processos.Buscar(pid) : null;
            if (pi != null && pi.Name.Length > 0) return pi;
            ProcInfo dir;
            if (NativeProcess.TryGetProcessInfo(pid, out dir) && dir != null && dir.Name.Length > 0) return dir;
            return null;
        }

        private static bool EhSistema(string nome)
        {
            foreach (string s in ProcessosDoSistema)
                if (nome.Equals(s, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static readonly string[] ProcessosDoSistema =
        {
            "explorer.exe", "svchost.exe", "services.exe", "wininit.exe", "winlogon.exe",
            "userinit.exe", "sihost.exe", "taskhostw.exe", "taskeng.exe", "smss.exe", "csrss.exe"
        };

        /// <summary>
        /// Processos que costumam ter um console e lancar programas dentro
        /// dele. So para eles a arvore de filhos serve para descobrir o autor.
        /// </summary>
        /// <summary>
        /// Shell de comando de verdade (o "prompt"). Diferente de EhShell, que
        /// inclui interpretadores (python, node...) para achar filhos: aqui o
        /// python que abriu o console e chamou git.exe conta como autor.
        /// </summary>
        private static bool EhShellDeComando(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return false;
            foreach (string s in ShellsDeComando)
                if (nome.Equals(s, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static readonly string[] ShellsDeComando =
        {
            "cmd.exe", "powershell.exe", "pwsh.exe", "bash.exe", "sh.exe", "zsh.exe", "fish.exe", "wsl.exe", "nu.exe"
        };

        private static bool EhShell(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return false;
            foreach (string s in Shells)
                if (nome.Equals(s, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static readonly string[] Shells =
        {
            "cmd.exe", "powershell.exe", "pwsh.exe", "bash.exe", "sh.exe", "zsh.exe", "fish.exe",
            "python.exe", "pythonw.exe", "node.exe", "deno.exe", "bun.exe", "ruby.exe", "perl.exe",
            "php.exe", "java.exe", "javaw.exe", "dotnet.exe", "cscript.exe", "wscript.exe",
            "uv.exe", "uvx.exe", "npx.exe", "npm.exe", "yarn.exe", "pnpm.exe", "git.exe", "git-gui.exe",
            "wsl.exe", "msbuild.exe", "escript.exe", "elixir.exe", "iex.bat", "pwsh_ise.exe"
        };

        private void AplicarAutor(WindowRecord rec, int pid)
        {
            rec.AuthorPid = pid;
            rec.AuthorName = "";
            rec.AuthorCmdLine = "";
            if (pid <= 0) { rec.AuthorName = "?"; return; }
            if (Processos != null)
            {
                ProcInfo pi = Processos.Buscar(pid);
                if (pi != null)
                {
                    rec.AuthorName = pi.Name;
                    rec.AuthorCmdLine = pi.CmdLine;
                }
            }
            if (rec.AuthorName.Length == 0)
            {
                // o processo ainda nao passou pelo retrato: consulta dirigida
                // (funciona tambem para processo elevado)
                ProcInfo dir;
                if (NativeProcess.TryGetProcessInfo(pid, out dir) && dir != null && dir.Name.Length > 0)
                {
                    rec.AuthorName = dir.Name;
                    rec.AuthorCmdLine = dir.CmdLine;
                }
                else if (pid == rec.OwnerPid && !string.IsNullOrEmpty(rec.OwnerName))
                {
                    // se o PID escolhido nao esta na tabela, NAO emprestar o nome do
                    // dono: isso misturava nome de um processo com PID de outro.
                    // so reusa o dono quando o PID do autor E o do dono.
                    rec.AuthorName = rec.OwnerName;
                }
                else
                {
                    rec.AuthorName = "?";
                }
            }
        }

        private void MarcarFoco(IntPtr hwnd)
        {
            try
            {
                WindowRecord rec;
                lock (_lock)
                {
                    if (!_janelas.TryGetValue(hwnd, out rec))
                    {
                        if (!EhJanelaRelevante(hwnd)) return;
                        rec = new WindowRecord();
                        rec.Hwnd = hwnd;
                        rec.FirstSeen = DateTime.Now;
                        _janelas[hwnd] = rec;
                    }
                }
                rec.FocusCount++;
                rec.LastFocusAt = DateTime.Now;
                rec.Visible = true;
                rec.HiddenAt = null;
                LerDados(hwnd, rec);
                // foco sem open previo: ainda assim resolve autor e cadeia
                if (rec.AuthorPid <= 0)
                {
                    ResolverAutor(rec);
                    AssociarTerminal(rec);
                    MontarCadeia(rec);
                }
                if (rec.Auxiliar) return;
                Notificar(rec, "focus");
            }
            catch { }
        }

        private void Esconder(IntPtr hwnd)
        {
            try
            {
                WindowRecord rec;
                lock (_lock)
                {
                    if (!_janelas.TryGetValue(hwnd, out rec)) return;
                }
                if (rec.Visible)
                {
                    rec.Visible = false;
                    rec.HiddenAt = DateTime.Now;
                    if (!rec.Auxiliar) Notificar(rec, "hide");
                }
            }
            catch { }
        }

        private void Fechar(IntPtr hwnd)
        {
            try
            {
                // A janela so fecha se ela realmente sumiu. O teste usa a CLASSE
                // da janela e nao IsWindow: medido nesta maquina, IsWindow devolve
                // falso para janela viva de outro processo, enquanto GetClassNameW
                // (que o cmdwatch ja usava para ler o titulo) responde certo.
                if (NomeDaClasse(hwnd).Length > 0) return;

                WindowRecord rec;
                lock (_lock)
                {
                    if (!_janelas.TryGetValue(hwnd, out rec)) return;
                    _janelas.Remove(hwnd);
                }
                if (!rec.ClosedAt.HasValue)
                {
                    rec.ClosedAt = DateTime.Now;
                    rec.Visible = false;
                    if (!rec.Auxiliar) Notificar(rec, "close");
                }
            }
            catch { }
        }

        /// <summary>
        /// Varredura periodica: pega as janelas que ja existiam quando o
        /// monitor abriu, atualiza os dados de quem ficou mais tempo em foco e
        /// descarta as que sumiram sem evento de destruicao.
        /// </summary>
        private void Varrer()
        {
            try
            {
                var vistas = new List<IntPtr>();
                EnumWindows(delegate (IntPtr h, IntPtr l)
                {
                    if (EhJanelaRelevante(h)) vistas.Add(h);
                    return true;
                }, IntPtr.Zero);

                foreach (IntPtr h in vistas) Registrar(h);

                lock (_lock)
                {
                    _lixo.Clear();
                    foreach (var kv in _janelas)
                    {
                        WindowRecord r = kv.Value;
                        if (r.ClosedAt.HasValue) { _lixo.Add(kv.Key); continue; }
                        // janela que sumiu da varredura: ela esta ESCONDIDA, nao
                        // fechada. Quem decide que uma janela fechou e o evento de
                        // destruicao - e IsWindow nao serve para isso aqui (medido:
                        // devolve falso para janela viva de outro processo).
                        // Mas se a janela nao existe mais (a classe nao responde), o
                        // evento de destruicao se perdeu: em rajada ele chega enquanto
                        // a janela ainda esta sendo destruida e Fechar o ignora
                        // (medido: 16 de 100 close). Fecha aqui, senao o registro
                        // fica para sempre em _janelas.
                        if (!vistas.Contains(kv.Key) && NomeDaClasse(kv.Key).Length == 0)
                        {
                            r.ClosedAt = DateTime.Now;
                            r.Visible = false;
                            if (!r.Auxiliar) Notificar(r, "close");
                            _lixo.Add(kv.Key);
                            continue;
                        }
                        if (!vistas.Contains(kv.Key) && !r.Visible)
                        {
                            if (!r.HiddenAt.HasValue)
                            {
                                r.HiddenAt = DateTime.Now;
                                if (!r.Auxiliar) Notificar(r, "hide");
                            }
                        }
                    }
                    foreach (IntPtr h in _lixo) _janelas.Remove(h);
                }
            }
            catch { }
        }

        /// <summary>
        /// Monta a cadeia de ancestrais do autor (pai -> avo...), hidratando a
        /// linha de comando. Usa a tabela viva e o cache de processos
        /// recem-encerrados do Watcher.
        /// </summary>
        private void MontarCadeia(WindowRecord rec)
        {
            rec.Chain.Clear();
            if (Processos == null || rec.AuthorPid <= 0) return;
            try
            {
                List<ProcInfo> cadeia = Processos.CadeiaDe(rec.AuthorPid, 8);
                foreach (ProcInfo p in cadeia) rec.Chain.Add(p);
            }
            catch { }
        }

        /// <summary>
        /// Windows Terminal expoe DUAS janelas top-level por aba: a Cascadia
        /// (titulo, dono WindowsTerminal.exe) e a PseudoConsoleWindow (dono =
        /// processo real). Liga as duas: a Pseudo vira auxiliar; a Cascadia
        /// herda autor e cadeia do processo real.
        /// </summary>
        private void AssociarTerminal(WindowRecord rec)
        {
            try
            {
                if (rec.ClassName == "PseudoConsoleWindow")
                {
                    WindowRecord cascadia = AcharCascadiaProxima(rec);
                    if (cascadia != null)
                    {
                        lock (_lock)
                        {
                            rec.Auxiliar = true;
                            rec.LinkedHwnd = cascadia.Hwnd;
                            // Cascadia herda o autor real
                            if (rec.AuthorPid > 0 &&
                                (cascadia.AuthorPid == cascadia.OwnerPid ||
                                 EhTerminalHost(cascadia.AuthorName)))
                            {
                                cascadia.AuthorPid = rec.AuthorPid;
                                cascadia.AuthorName = rec.AuthorName;
                                cascadia.AuthorCmdLine = rec.AuthorCmdLine;
                                cascadia.AutorPeloConsole = true;
                            }
                        }
                    }
                    return;
                }

                if (rec.ClassName == "CASCADIA_HOSTING_WINDOW_CLASS" || EhTerminalHost(rec.OwnerName))
                {
                    WindowRecord pseudo = AcharPseudoProxima(rec);
                    if (pseudo != null)
                    {
                        lock (_lock)
                        {
                            pseudo.Auxiliar = true;
                            pseudo.LinkedHwnd = rec.Hwnd;
                            if (pseudo.AuthorPid > 0)
                            {
                                rec.AuthorPid = pseudo.AuthorPid;
                                rec.AuthorName = pseudo.AuthorName;
                                rec.AuthorCmdLine = pseudo.AuthorCmdLine;
                                rec.AutorPeloConsole = true;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void PropagarAutorParaLigada(WindowRecord aux)
        {
            if (aux.LinkedHwnd == IntPtr.Zero) return;
            WindowRecord alvo;
            lock (_lock)
            {
                if (!_janelas.TryGetValue(aux.LinkedHwnd, out alvo)) return;
            }
            bool mudou;
            lock (_lock)
            {
                mudou = alvo.AuthorPid != aux.AuthorPid;
                alvo.AuthorPid = aux.AuthorPid;
                alvo.AuthorName = aux.AuthorName;
                alvo.AuthorCmdLine = aux.AuthorCmdLine;
                alvo.AutorPeloConsole = true;
            }
            MontarCadeia(alvo);
            if (mudou) Notificar(alvo, "update");
        }

        private static bool EhTerminalHost(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return false;
            return nome.Equals("WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase)
                || nome.Equals("wt.exe", StringComparison.OrdinalIgnoreCase);
        }

        private WindowRecord AcharCascadiaProxima(WindowRecord pseudo)
        {
            WindowRecord melhor = null;
            double melhorDt = 2.0; // segundos
            lock (_lock)
            {
                // link ja feito e a Cascadia ainda aberta: fica com ele. Sem isso,
                // duas abas abertas juntas (menos de 2 s) podiam trocar de autor a
                // cada nova resolucao.
                WindowRecord ligada;
                if (pseudo.LinkedHwnd != IntPtr.Zero && _janelas.TryGetValue(pseudo.LinkedHwnd, out ligada)
                    && !ligada.ClosedAt.HasValue)
                    return ligada;
                // Cascadias ja ligadas a OUTRA Pseudo ficam de fora
                var ocupadas = new HashSet<IntPtr>();
                foreach (WindowRecord r in _janelas.Values)
                    if (r.Hwnd != pseudo.Hwnd && r.ClassName == "PseudoConsoleWindow" && r.LinkedHwnd != IntPtr.Zero && !r.ClosedAt.HasValue)
                        ocupadas.Add(r.LinkedHwnd);
                foreach (WindowRecord r in _janelas.Values)
                {
                    if (r.Hwnd == pseudo.Hwnd) continue;
                    if (r.ClosedAt.HasValue) continue;
                    if (r.ClassName != "CASCADIA_HOSTING_WINDOW_CLASS") continue;
                    if (ocupadas.Contains(r.Hwnd)) continue;
                    double dt = Math.Abs((r.FirstSeen - pseudo.FirstSeen).TotalSeconds);
                    if (dt < melhorDt) { melhorDt = dt; melhor = r; }
                }
            }
            return melhor;
        }

        private WindowRecord AcharPseudoProxima(WindowRecord cascadia)
        {
            WindowRecord melhor = null;
            double melhorDt = 2.0;
            lock (_lock)
            {
                foreach (WindowRecord r in _janelas.Values)
                {
                    if (r.Hwnd == cascadia.Hwnd) continue;
                    if (r.ClosedAt.HasValue) continue;
                    if (r.ClassName != "PseudoConsoleWindow") continue;
                    if (r.LinkedHwnd == cascadia.Hwnd) return r;              // ja ligada a esta
                    if (r.LinkedHwnd != IntPtr.Zero && _janelas.ContainsKey(r.LinkedHwnd)) continue; // ligada a outra
                    if (EhTerminalHost(r.AuthorName) || EhTerminalHost(r.OwnerName)) continue;
                    double dt = Math.Abs((r.FirstSeen - cascadia.FirstSeen).TotalSeconds);
                    if (dt < melhorDt) { melhorDt = dt; melhor = r; }
                }
            }
            return melhor;
        }

        private void Notificar(WindowRecord rec, string tipo)
        {
            try { if (OnChanged != null) OnChanged(rec, tipo); } catch { }
        }

        // ------------------------------------------------------- API do Windows
        private const int GA_ROOT = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public System.Drawing.Point pt;
        }

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLengthW(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern uint MsgWaitForMultipleObjects(uint nCount, IntPtr[] pHandles,
            bool bWaitAll, uint dwMilliseconds, uint dwWakeMask);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);
    }
}
