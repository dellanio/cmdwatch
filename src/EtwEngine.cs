using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CmdWatch
{
    /// <summary>
    /// Motor por ETW (eventos de processo do kernel). Exige administrador.
    ///
    /// Por que existe: o polling perde o .cmd oculto e curto (~25 ms) com a
    /// maquina parada (medido: 0/8 no padrao) e o Win32_ProcessStartTrace do
    /// motor wmi entrega so o PID - a linha de comando ainda precisa ser lida
    /// do processo, que ja morreu. O evento de inicio de processo do kernel
    /// traz pid, pai, sessao, imagem E a linha de comando dentro do proprio
    /// evento: nada se perde, por mais curto que o processo seja.
    ///
    /// Sessao propria em modo "system logger" (Windows 8+), com nome proprio:
    /// nao disputa a sessao unica "NT Kernel Logger" com outras ferramentas.
    /// Sem dependencia nova: so advapi32 (StartTrace/OpenTrace/ProcessTrace).
    /// </summary>
    internal sealed class EtwEngine : IEngine
    {
        public const string NomeSessao = "CMDWatcher-Processos";
        public string Name { get { return "etw"; } }

        private IntPtr _props = IntPtr.Zero;
        private IntPtr _logfile = IntPtr.Zero;
        private IntPtr _nome = IntPtr.Zero;
        private ulong _sessao;
        private ulong _trace = INVALID_TRACE;
        private Thread _thread;
        private volatile bool _running;
        private Action<ProcInfo> _onStart;
        private Action<int> _onExit;
        private EventRecordCallback _callback;   // mantido vivo: o kernel guarda o ponteiro

        /// <summary>Ultimo erro de Iniciar (para o aviso de fallback).</summary>
        public string Erro = "";

        /// <summary>
        /// Abre a sessao ETW. Falso (com Erro preenchido) quando nao ha
        /// permissao, o processo e 32 bits ou o Windows recusa: quem chamou
        /// cai para outro motor.
        /// </summary>
        public bool Iniciar()
        {
            try
            {
                if (IntPtr.Size != 8) { Erro = "motor etw so em processo 64 bits"; return false; }
                PararSessaoAntiga();
                _props = MontarPropriedades();
                int rc = StartTraceW(out _sessao, NomeSessao, _props);
                if (rc != 0) { Erro = "StartTrace falhou (erro " + rc + ")"; Liberar(); return false; }

                _callback = AoEvento;
                _nome = Marshal.StringToHGlobalUni(NomeSessao);
                _logfile = Marshal.AllocHGlobal(LOGFILE_SIZE);
                Zerar(_logfile, LOGFILE_SIZE);
                Marshal.WriteIntPtr(_logfile, 8, _nome);                         // LoggerName
                Marshal.WriteInt32(_logfile, 28, PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD);
                Marshal.WriteIntPtr(_logfile, 424, Marshal.GetFunctionPointerForDelegate(_callback));
                _trace = OpenTraceW(_logfile);
                if (_trace == INVALID_TRACE)
                {
                    Erro = "OpenTrace falhou (erro " + Marshal.GetLastWin32Error() + ")";
                    PararSessao(); Liberar(); return false;
                }
                return true;
            }
            catch (Exception ex) { Erro = ex.GetType().Name + ": " + ex.Message; PararSessao(); Liberar(); return false; }
        }

        public void Start(Action<ProcInfo> onStart, Action<int> onExit)
        {
            _onStart = onStart;
            _onExit = onExit;
            if (_trace == INVALID_TRACE && !Iniciar())
                throw new InvalidOperationException(Erro);
            _running = true;
            _thread = new Thread(() =>
            {
                try { ProcessTrace(new[] { _trace }, 1, IntPtr.Zero, IntPtr.Zero); }
                catch (Exception ex) { Console.Error.WriteLine("[!] etw: " + ex.Message); }
            });
            _thread.IsBackground = true;
            _thread.Name = "cmdwatch-etw";
            _thread.Start();
        }

        // EVENT_RECORD (x64): EventHeader.ProviderId @24, Version @42, Opcode @45,
        // TimeStamp @16, UserDataLength @86, UserData @96
        private void AoEvento(IntPtr rec)
        {
            if (!_running) return;
            try
            {
                Guid prov = LerGuid(rec, 24);
                if (prov != ProcessGuid) return;
                byte opcode = Marshal.ReadByte(rec, 45);
                if (opcode != 1 && opcode != 2) return;     // 1 = inicio, 2 = fim (3/4 = rundown)
                byte versao = Marshal.ReadByte(rec, 42);
                int len = (ushort)Marshal.ReadInt16(rec, 86);
                IntPtr ud = Marshal.ReadIntPtr(rec, 96);
                if (ud == IntPtr.Zero || len < 24) return;

                int pid = Marshal.ReadInt32(ud, 8);
                if (opcode == 2) { if (_onExit != null) _onExit(pid); return; }

                var pi = new ProcInfo();
                pi.Pid = pid;
                pi.ParentPid = Marshal.ReadInt32(ud, 12);
                pi.SessionId = unchecked((uint)Marshal.ReadInt32(ud, 16));
                long ft = Marshal.ReadInt64(rec, 16);      // ClientContext = 2: hora do sistema (FILETIME)
                pi.StartedAt = ft > 0 ? DateTime.FromFileTime(ft) : DateTime.Now;

                int off = 32;                               // UniqueProcessKey, Pid, Ppid, Session, ExitStatus, DirectoryTableBase
                if (versao >= 4) off += 4;                  // Flags
                off = PularSid(ud, off, len);
                if (off < 0) return;
                int fimNome;
                string imagem = LerAnsi(ud, off, len, out fimNome);
                string cmd = fimNome < len ? LerUnicode(ud, fimNome, len) : "";
                pi.Name = imagem;
                pi.CmdLine = cmd;
                if (_onStart != null) _onStart(pi);
            }
            catch (Exception ex) { if (PollingEngine.Verbose) Console.Error.WriteLine("[etw] " + ex.Message); }
        }

        private static int PularSid(IntPtr ud, int off, int len)
        {
            if (off + 4 > len) return -1;
            if (Marshal.ReadInt32(ud, off) == 0) return off + 4;
            int baseSid = off + 2 * IntPtr.Size;            // TOKEN_USER (ponteiro + atributos)
            if (baseSid + 8 > len) return -1;
            int n = Marshal.ReadByte(ud, baseSid + 1);
            return baseSid + 8 + 4 * n;
        }

        private static string LerAnsi(IntPtr p, int off, int len, out int fim)
        {
            int i = off;
            while (i < len && Marshal.ReadByte(p, i) != 0) i++;
            var b = new byte[i - off];
            Marshal.Copy(IntPtr.Add(p, off), b, 0, b.Length);
            fim = i + 1;
            return Encoding.Default.GetString(b);
        }

        private static string LerUnicode(IntPtr p, int off, int len)
        {
            var sb = new StringBuilder();
            for (int i = off; i + 1 < len; i += 2)
            {
                char c = (char)Marshal.ReadInt16(p, i);
                if (c == '\0') break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static Guid LerGuid(IntPtr p, int off)
        {
            var b = new byte[16];
            Marshal.Copy(IntPtr.Add(p, off), b, 0, 16);
            return new Guid(b);
        }

        public void Stop()
        {
            _running = false;
            PararSessao();
            try { if (_trace != INVALID_TRACE) CloseTrace(_trace); } catch { }
            _trace = INVALID_TRACE;
        }

        public void Dispose()
        {
            Stop();
            try { if (_thread != null) _thread.Join(2000); } catch { }
            Liberar();
        }

        // ------------------------------------------------------- sessao
        // EVENT_TRACE_PROPERTIES (x64): Wnode 0-47 (BufferSize@0, Guid@24,
        // ClientContext@40, Flags@44), BufferSize@48, Min@52, Max@56,
        // LogFileMode@64, FlushTimer@68, EnableFlags@72, LogFileNameOffset@112,
        // LoggerNameOffset@116. Total 120 + espaco para o nome.
        private const int PROPS_SIZE = 120;
        private const int PROPS_TOTAL = PROPS_SIZE + 1024 * 2;

        private static IntPtr MontarPropriedades()
        {
            IntPtr p = Marshal.AllocHGlobal(PROPS_TOTAL);
            Zerar(p, PROPS_TOTAL);
            Marshal.WriteInt32(p, 0, PROPS_TOTAL);
            byte[] g = SessaoGuid.ToByteArray();
            Marshal.Copy(g, 0, IntPtr.Add(p, 24), 16);
            Marshal.WriteInt32(p, 40, 2);                                  // hora do sistema
            Marshal.WriteInt32(p, 44, WNODE_FLAG_TRACED_GUID);
            Marshal.WriteInt32(p, 48, 64);                                 // KB por buffer
            Marshal.WriteInt32(p, 52, 4);
            Marshal.WriteInt32(p, 56, 16);
            Marshal.WriteInt32(p, 64, EVENT_TRACE_REAL_TIME_MODE | EVENT_TRACE_SYSTEM_LOGGER_MODE);
            Marshal.WriteInt32(p, 68, 1);                                  // flush a cada 1 s
            Marshal.WriteInt32(p, 72, EVENT_TRACE_FLAG_PROCESS);
            Marshal.WriteInt32(p, 116, PROPS_SIZE);
            return p;
        }

        /// <summary>Uma sessao com o NOSSO nome que sobrou de um cmdwatch encerrado a forca.</summary>
        private static void PararSessaoAntiga()
        {
            IntPtr p = MontarPropriedades();
            try { ControlTraceW(0, NomeSessao, p, EVENT_TRACE_CONTROL_STOP); }
            catch { /* nao havia sessao antiga */ }
            finally { Marshal.FreeHGlobal(p); }
        }

        private void PararSessao()
        {
            if (_sessao == 0 || _props == IntPtr.Zero) return;
            try { ControlTraceW(_sessao, NomeSessao, _props, EVENT_TRACE_CONTROL_STOP); }
            catch (Exception ex) { if (PollingEngine.Verbose) Console.Error.WriteLine("[etw] stop: " + ex.Message); }
            _sessao = 0;
        }

        private void Liberar()
        {
            if (_props != IntPtr.Zero) { Marshal.FreeHGlobal(_props); _props = IntPtr.Zero; }
            if (_logfile != IntPtr.Zero) { Marshal.FreeHGlobal(_logfile); _logfile = IntPtr.Zero; }
            if (_nome != IntPtr.Zero) { Marshal.FreeHGlobal(_nome); _nome = IntPtr.Zero; }
        }

        private static void Zerar(IntPtr p, int n)
        {
            for (int i = 0; i < n; i += 4) Marshal.WriteInt32(p, i, 0);
        }

        // ------------------------------------------------------- API
        private static readonly Guid ProcessGuid = new Guid("3d6fa8d0-fe05-11d0-9dda-00c04fd7ba7c");
        private static readonly Guid SessaoGuid = new Guid("6b1c2f4e-7d3a-4e5b-9c8d-0a1b2c3d4e5f");
        private const ulong INVALID_TRACE = 0xFFFFFFFFFFFFFFFF;
        private const int LOGFILE_SIZE = 448;                      // EVENT_TRACE_LOGFILEW x64
        private const int WNODE_FLAG_TRACED_GUID = 0x00020000;
        private const int EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
        private const int EVENT_TRACE_SYSTEM_LOGGER_MODE = 0x02000000;
        private const int EVENT_TRACE_FLAG_PROCESS = 0x00000001;
        private const int EVENT_TRACE_CONTROL_STOP = 1;
        private const int PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
        private const int PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void EventRecordCallback(IntPtr eventRecord);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int StartTraceW(out ulong handle, string name, IntPtr props);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int ControlTraceW(ulong handle, string name, IntPtr props, int control);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern ulong OpenTraceW(IntPtr logfile);
        [DllImport("advapi32.dll")]
        private static extern int ProcessTrace(ulong[] handles, int count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll")]
        private static extern int CloseTrace(ulong handle);
    }

    /// <summary>
    /// Escolha do motor, igual no monitor e no painel.
    ///   auto  : administrador -> etw (se a sessao abrir; senao wmi); sem admin -> polling
    ///   etw   : exige administrador; sem ele, ou se a sessao falhar, polling (com aviso)
    ///   wmi   : exige administrador; sem ele, polling
    ///   polling
    /// </summary>
    internal static class EngineFactory
    {
        public static IEngine Criar(string pedido, int intervalo, bool admin, out string nome, out string aviso)
        {
            aviso = "";
            // Teste sem admin (tools\test-defeitos.ps1): entrega um EtwEngine nao iniciado para
            // exercitar o caminho de quem chama; o Start falha com o erro do StartTrace.
            if (pedido == "etw" && Environment.GetEnvironmentVariable("CMDWATCH_TESTE_ETW_SEM_ADMIN") == "1")
            { nome = "etw"; return new EtwEngine(); }
            string e = string.IsNullOrEmpty(pedido) ? "auto" : pedido;
            if ((e == "etw" || e == "wmi") && !admin)
            {
                aviso = "motor " + e + " exige administrador; usando 'polling'.";
                e = "polling";
            }
            if (e == "auto" || e == "etw")
            {
                if (admin)
                {
                    var etw = new EtwEngine();
                    if (etw.Iniciar()) { nome = "etw"; return etw; }
                    aviso = "motor etw indisponivel (" + etw.Erro + "); usando '" + (e == "auto" ? "wmi" : "polling") + "'.";
                    etw.Dispose();
                    e = e == "auto" ? "wmi" : "polling";
                }
                else e = "polling";
            }
            if (e == "wmi") { nome = "wmi"; return new WmiEngine(); }
            nome = "polling";
            return new PollingEngine(intervalo);
        }
    }
}
