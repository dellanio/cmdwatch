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

                if (eventType == EVENT_OBJECT_CREATE || eventType == EVENT_OBJECT_SHOW)
                {
                    if (!EhJanelaRelevante(hwnd)) return;
                    Registrar(hwnd);
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

                bool mudou = novo
                    || !tituloAntes.Equals(rec.Title, StringComparison.Ordinal)
                    || donoAntes != rec.OwnerPid
                    || visivelAntes != rec.Visible;

                bool recalcular = novo || mudou || notificarSempre ||
                    (DateTime.Now - rec.UltimaResolucao) > TimeSpan.FromSeconds(5);

                if (!recalcular) return;

                rec.Visible = true;
                rec.HiddenAt = null;
                rec.UltimaResolucao = DateTime.Now;

                int autorAntes = rec.AuthorPid;
                ResolverAutor(rec);

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

                int melhor = 0;
                DateTime maisNovo = DateTime.MinValue;
                foreach (int p in clients)
                {
                    if (p == rec.OwnerPid) continue;
                    DateTime inicio = DateTime.MinValue;
                    if (Processos != null)
                    {
                        ProcInfo pi = Processos.Buscar(p);
                        if (pi != null)
                        {
                            if (EhHospedeiroConsole(pi.Name)) continue;
                            if (pi.StartedAt != DateTime.MinValue) inicio = pi.StartedAt;
                        }
                    }
                    if (inicio > maisNovo) { maisNovo = inicio; melhor = p; }
                }

                int escolhido = melhor != 0 ? melhor : rec.OwnerPid;
                AplicarAutor(rec, escolhido);
                rec.AutorPeloConsole = escolhido != rec.OwnerPid;
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

            // (3) janela normal: o proprio dono e o autor
            AplicarAutor(rec, rec.OwnerPid);
        }

        /// <summary>
        /// Processos que costumam ter um console e lancar programas dentro
        /// dele. So para eles a arvore de filhos serve para descobrir o autor.
        /// </summary>
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
                ProcInfo dono = Processos != null ? Processos.Buscar(rec.OwnerPid) : null;
                rec.AuthorName = dono != null && dono.Name.Length > 0 ? dono.Name : "?";
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
                    Notificar(rec, "hide");
                }
            }
            catch { }
        }

        private void Fechar(IntPtr hwnd)
        {
            try
            {
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
                    Notificar(rec, "close");
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
                        if (!vistas.Contains(kv.Key) && !r.Visible)
                        {
                            r.ClosedAt = DateTime.Now;
                            Notificar(r, "close");
                            _lixo.Add(kv.Key);
                        }
                    }
                    foreach (IntPtr h in _lixo) _janelas.Remove(h);
                }
            }
            catch { }
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
