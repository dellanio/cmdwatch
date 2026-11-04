using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace CmdWatch
{
    /// <summary>
    /// Enumeração de processos via Toolhelp (API documentada do kernel32, ~8ms por snapshot)
    /// e leitura do command line direto do PEB (sem WMI, sem admin).
    /// </summary>
    internal static class NativeProcess
    {
        private const uint TH32CS_SNAPPROCESS = 0x00000002;

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        /// <summary>
        /// Classe de informacao do NtQueryInformationProcess que devolve a linha
        /// de comando do processo. Funciona com handle aberto SO com
        /// PROCESS_QUERY_LIMITED_INFORMATION - inclusive para processos
        /// ELEVADOS, que bloqueiam a leitura da memoria (PROCESS_VM_READ).
        /// </summary>
        private const int ProcessCommandLineInformation = 60;

        /// <summary>
        /// Classe de informacao que devolve a lista de processos do sistema num
        /// unico buffer. E o retrato rapido: uma chamada so, sem percorrer
        /// processos um a um (o Toolhelp leva ~34ms com 800 processos).
        /// </summary>
        private const int SystemProcessInformation = 5;

        private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32 entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr baseAddress, byte[] buffer, int size, out IntPtr bytesRead);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr processHandle, int informationClass,
            ref PROCESS_BASIC_INFORMATION information, int informationLength, out int returnLength);

        // O EntryPoint e OBRIGATORIO aqui. Sem ele, o runtime procura no ntdll.dll
        // uma funcao chamada literalmente "NtQueryInformationProcessRaw" - que nao
        // existe - e a chamada LANCA EntryPointNotFoundException. Como o erro era
        // engolido por um catch, a consulta limitada NUNCA rodou: o cmdwatch seguia
        // lendo a linha de comando pelo PEB, que um processo ELEVADO bloqueia.
        // Era esse o motivo de o script elevado nao aparecer no log.
        [DllImport("ntdll.dll", EntryPoint = "NtQueryInformationProcess")]
        private static extern int NtQueryInformationProcessRaw(IntPtr processHandle, int informationClass,
            IntPtr information, int informationLength, out int returnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int systemInformationClass, IntPtr systemInformation,
            int systemInformationLength, out int returnLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags,
            StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(IntPtr hProcess,
            out System.Runtime.InteropServices.ComTypes.FILETIME lpCreationTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME lpExitTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            public int ExitStatus;
            public IntPtr PebBaseAddress;
            public IntPtr AffinityMask;
            public IntPtr BasePriority;
            public IntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
        }

        public static bool Is64Bit { get { return IntPtr.Size == 8; } }

        /// <summary>Enumera todos os processos do sistema (Toolhelp).</summary>
        public static List<ProcInfo> Snapshot()
        {
            var list = new List<ProcInfo>();
            IntPtr snap = IntPtr.Zero;
            try
            {
                snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
                if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return list;

                var e = new PROCESSENTRY32();
                e.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
                if (!Process32FirstW(snap, ref e)) return list;

                do
                {
                    ProcInfo pi = new ProcInfo();
                    pi.Pid = (int)e.th32ProcessID;
                    pi.ParentPid = (int)e.th32ParentProcessID;
                    pi.Name = e.szExeFile ?? "";
                    list.Add(pi);
                } while (Process32NextW(snap, ref e));
            }
            catch { }
            finally { try { if (snap != IntPtr.Zero) CloseHandle(snap); } catch { } }
            return list;
        }

        /// <summary>
        /// Retrato rapido dos processos: uma unica chamada ao kernel devolve
        /// nome, pid, pid do pai, sessao e HORA DE CRIACAO de cada processo.
        ///
        /// O Toolhelp (Snapshot) leva ~34ms com 800 processos; este caminho leva
        /// ~11ms. Como o retrato era o que comia o nucleo inteiro (o painel
        /// tirava um retrato a cada 50ms), a troca e o que derruba o custo.
        ///
        /// A hora de criacao e um bonus: o Toolhelp NAO expoe quando o processo
        /// nasceu, e o monitor so descobria isso abrindo o processo.
        ///
        /// O buffer e reaproveitado entre as chamadas: alocar e liberar 1MB a
        /// cada retrato custava mais que a propria consulta (52ms contra 11ms).
        /// </summary>
        public static List<ProcInfo> SnapshotFast()
        {
            // o buffer estatico e compartilhado: Lookup (thread de janelas) e o
            // motor de polling podem chamar ao mesmo tempo. Sem a trava, um
            // lado libera enquanto o outro ainda le - leitura corrompida ou queda.
            lock (_bufferLock)
            {
            var list = new List<ProcInfo>();
            IntPtr buf = _buffer;
            int cap = _bufferCap;
            try
            {
                int ret;
                int status = -1;

                for (int tentativa = 0; tentativa < 3; tentativa++)
                {
                    if (buf == IntPtr.Zero)
                    {
                        cap = Math.Max(cap, 4 * 1024 * 1024);
                        buf = Marshal.AllocHGlobal(cap);
                        _buffer = buf;
                        _bufferCap = cap;
                    }
                    var swq = System.Diagnostics.Stopwatch.StartNew();
                    status = NtQuerySystemInformation(SystemProcessInformation, buf, cap, out ret);
                    UltimaConsultaMs = swq.Elapsed.TotalMilliseconds;
                    UltimoStatus = status;
                    UltimoTamanho = ret;
                    if (status != STATUS_INFO_LENGTH_MISMATCH) break;
                    // o buffer nao serviu: cresce e tenta de novo
                    Marshal.FreeHGlobal(buf);
                    buf = IntPtr.Zero;
                    _buffer = IntPtr.Zero;
                    cap = Math.Max(cap * 2, ret + 65536);
                }
                if (status != 0 || buf == IntPtr.Zero) return null;

                var swl = System.Diagnostics.Stopwatch.StartNew();

                // o layout do registro mudou entre versoes do Windows: nos
                // builds recentes (e no Windows 11) os dois primeiros campos
                // (NextEntryOffset e NumberOfThreads) sao de 32 bits, o que
                // desloca todo o resto. Descobre qual dos dois vale pelo
                // CreateTime, que tem um valor reconhecivel.
                //
                // SessionId e ULONG (32 bits). No layout moderno (quatroBytes)
                // ele fica em 0x64 (depois de HandleCount em 0x60). Ler 8 bytes
                // em 0x68 pegava UniqueProcessKey e gerava ids absurdos
                // (ex.: 59133952, 2699013802) no lugar da sessao real (1).
                bool quatroBytes = PareceFileTime(ReadLong(buf, 0x20));
                int offNext = 0;
                int offCreate = quatroBytes ? 0x20 : 0x28;
                int offName = quatroBytes ? 0x38 : 0x40;
                int offNamePtr = offName + 8;
                int offPid = quatroBytes ? 0x50 : 0x58;
                int offPpid = quatroBytes ? 0x58 : 0x60;
                int offSession = quatroBytes ? 0x64 : 0x6C;

                long offset = 0;
                while (true)
                {
                    // layout medido do SYSTEM_PROCESS_INFORMATION (Win10/11 x64):
                    //   0x00 NextEntryOffset (4 ou 8 bytes)
                    //   0x04 NumberOfThreads
                    //   0x08 WorkingSetPrivateSize
                    //   0x18 CycleTime
                    //   0x20 CreateTime (FILETIME)
                    //   0x28 UserTime / 0x30 KernelTime
                    //   0x38 UNICODE_STRING ImageName (Length, MaxLen, Buffer)
                    //   0x50 UniqueProcessId / 0x58 pai / 0x60 handles / 0x64 sessao
                    long next = quatroBytes
                        ? (uint)Marshal.ReadInt32(buf, (int)(offset + offNext))
                        : ReadLong(buf, offset + offNext);

                    int pid = (int)ReadLong(buf, offset + offPid);
                    if (pid > 0)
                    {
                        ProcInfo pi = new ProcInfo();
                        pi.Pid = pid;
                        pi.ParentPid = (int)ReadLong(buf, offset + offPpid);
                        pi.SessionId = unchecked((uint)Marshal.ReadInt32(buf, (int)(offset + offSession)));

                        // ImageName: o ponteiro do texto aponta para DENTRO do
                        // buffer devolvido - mas so e copiado se estiver
                        // dentro dele (um ponteiro fora causaria violacao)
                        int nameLen = (int)ReadUShort(buf, offset + offName);
                        long namePtr = ReadLong(buf, offset + offNamePtr);
                        if (nameLen > 0 && nameLen < 1024 && namePtr != 0 &&
                            namePtr >= buf.ToInt64() && namePtr < buf.ToInt64() + cap)
                        {
                            pi.Name = LerUniExterno(namePtr, nameLen);
                        }

                        // CreateTime: o instante real em que o processo nasceu
                        long ft = ReadLong(buf, offset + offCreate);
                        if (ft > 0) pi.StartedAt = DateTime.FromFileTimeUtc(ft).ToLocalTime();

                        list.Add(pi);
                    }

                    if (next <= 0) break;
                    offset += next;
                }
                UltimaLeituraMs = swl.Elapsed.TotalMilliseconds;
            }
            catch { return null; }
            finally { /* o buffer e reaproveitado: nao e liberado aqui */ }
            return list.Count > 0 ? list : null;
            } // lock _bufferLock
        }

        // diagnostico do ultimo retrato
        public static double UltimaConsultaMs;
        public static double UltimaLeituraMs;
        public static int UltimoStatus;
        public static int UltimoTamanho;

        // buffer reaproveitado entre os retratos (ver SnapshotFast)
        private static readonly object _bufferLock = new object();
        private static IntPtr _buffer = IntPtr.Zero;
        private static int _bufferCap = 0;

        private static int ReadInt(IntPtr p, long off)
        {
            return unchecked((int)Marshal.ReadInt64(p, (int)off));
        }

        private static long ReadLong(IntPtr p, long off)
        {
            return Marshal.ReadInt64(p, (int)off);
        }

        private static ushort ReadUShort(IntPtr p, long off)
        {
            return unchecked((ushort)Marshal.ReadInt16(p, (int)off));
        }

        /// <summary>Um FILETIME plausivel (100ns desde 1601, entre 2000 e 2100).</summary>
        private static bool PareceFileTime(long v)
        {
            return v > 125000000000000000L && v < 200000000000000000L;
        }

        private static string LerUniExterno(long address, int bytes)
        {
            try
            {
                byte[] b = new byte[bytes];
                Marshal.Copy(new IntPtr(address), b, 0, bytes);
                return Encoding.Unicode.GetString(b);
            }
            catch { return ""; }
        }

        /// <summary>
        /// Le a linha de comando de um processo SEM PROCESS_VM_READ.
        ///
        /// O PEB (a leitura antiga) exige PROCESS_VM_READ, e um processo ELEVADO
        /// nega esse acesso: e por isso que um .cmd rodando como administrador
        /// sumia do log. A consulta ProcessCommandLineInformation devolve a mesma
        /// linha de comando usando apenas PROCESS_QUERY_LIMITED_INFORMATION, que
        /// o Windows concede mesmo para processos de integridade maior.
        /// </summary>
        /// <summary>
        /// Ultimo erro da consulta limitada (vazio = deu certo). Existe para que
        /// uma falha NAO possa ficar escondida dentro de um catch: foi
        /// exatamente um catch mudo em cima de um EntryPoint errado que deixou
        /// o cmdwatch cego para processo elevado por uma versao inteira.
        /// </summary>
        public static string UltimoErroLimitado = "";

        public static bool TryGetCommandLineLimited(int pid, out string commandLine)
        {
            commandLine = "";
            UltimoErroLimitado = "";
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero)
            {
                UltimoErroLimitado = "OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) falhou: erro " + Marshal.GetLastWin32Error();
                return false;
            }
            IntPtr buf = IntPtr.Zero;
            try
            {
                int cap = 64 * 1024;
                buf = Marshal.AllocHGlobal(cap);
                int ret;
                int status = NtQueryInformationProcessRaw(h, ProcessCommandLineInformation, buf, cap, out ret);
                if (status != 0)
                {
                    UltimoErroLimitado = "NtQueryInformationProcess(60) status=0x" + status.ToString("X8");
                    return false;
                }

                ushort len = ReadUShort(buf, 0);
                if (len == 0)
                {
                    UltimoErroLimitado = "NtQueryInformationProcess(60) devolveu linha de comando vazia";
                    return false;
                }

                // o ponteiro do texto costuma apontar para dentro do proprio
                // buffer devolvido: calculamos o deslocamento em vez de presumir
                long strPtr = Is64Bit ? ReadLong(buf, 8) : (uint)Marshal.ReadInt32(buf, 4);
                int header = Is64Bit ? 16 : 8;
                long off = strPtr - (buf.ToInt64() + header);

                int start = header;
                if (off >= 0 && off + len <= cap) start = header + (int)off;

                byte[] b = new byte[len];
                Marshal.Copy(buf + start, b, 0, len);
                commandLine = Encoding.Unicode.GetString(b);
                return commandLine.Length > 0;
            }
            catch (Exception ex)
            {
                // nunca engolir: um catch mudo aqui ja escondeu uma falha de
                // EntryPoint por uma versao inteira
                UltimoErroLimitado = "consulta limitada: " + ex.GetType().Name + " - " + ex.Message;
                return false;
            }
            finally
            {
                try { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); } catch { }
                try { CloseHandle(h); } catch { }
            }
        }

        /// <summary>
        /// Consulta DIRIGIDA a um processo: nome, pai, linha de comando e hora
        /// de inicio, sem retrato geral do sistema.
        ///
        /// Serve para o observador de janelas: quando uma janela aparece, o
        /// dono dela ja e o processo - nao e preciso varrer os 800 processos do
        /// sistema para saber quem e.
        /// </summary>
        public static bool TryGetProcessInfo(int pid, out ProcInfo info)
        {
            info = new ProcInfo();
            info.Pid = pid;

            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                // caminho (e nome) do executavel
                var sb = new StringBuilder(1024);
                int len = 1024;
                if (QueryFullProcessImageNameW(h, 0, sb, ref len))
                {
                    info.Path = sb.ToString();
                    try { info.Name = System.IO.Path.GetFileName(info.Path); } catch { }
                }

                // pai
                PROCESS_BASIC_INFORMATION pbi = new PROCESS_BASIC_INFORMATION();
                int ret;
                if (NtQueryInformationProcess(h, 0, ref pbi, Marshal.SizeOf(pbi), out ret) == 0)
                    info.ParentPid = (int)pbi.InheritedFromUniqueProcessId.ToInt64();

                // hora de inicio
                System.Runtime.InteropServices.ComTypes.FILETIME ct, et, kt, ut;
                if (GetProcessTimes(h, out ct, out et, out kt, out ut))
                {
                    long ft = ((long)ct.dwHighDateTime << 32) | (uint)ct.dwLowDateTime;
                    info.StartedAt = DateTime.FromFileTimeUtc(ft).ToLocalTime();
                }

                // linha de comando (funciona para processo elevado)
                string cl;
                if (TryGetCommandLineLimited(pid, out cl)) info.CmdLine = cl;

                if (info.Name.Length == 0 && info.CmdLine.Length > 0)
                {
                    string baseName = System.IO.Path.GetFileName(info.CmdLine.Trim().Trim('"'));
                    int sp = baseName.IndexOf(' ');
                    if (sp > 0) baseName = baseName.Substring(0, sp);
                    info.Name = baseName;
                }

                info.Hydrated = false;   // ainda falta a leitura do PEB (diretorio de trabalho)
                return info.Name.Length > 0 || info.CmdLine.Length > 0;
            }
            catch { return false; }
            finally { try { CloseHandle(h); } catch { } }
        }

        /// <summary>
        /// Le o command line e o caminho do executavel a partir do PEB.
        /// Em vez de confiar em offsets fixos de RTL_USER_PROCESS_PARAMETERS (que mudam entre
        /// versoes do Windows), varre a regiao procurando a UNICODE_STRING cujo conteudo
        /// comeca com o nome do executavel.
        /// </summary>
        public static bool TryGetProcessStrings(int pid, string exeName, out string commandLine, out string imagePath)
        {
            string workingDir;
            return TryGetProcessStrings(pid, exeName, out commandLine, out imagePath, out workingDir);
        }

        /// <summary>
        /// Como acima, mas tambem devolve o diretorio de trabalho do processo
        /// (CURDIR dentro do RTL_USER_PROCESS_PARAMETERS). O diretorio de trabalho
        /// e o que permite resolver um script chamado por caminho RELATIVO:
        /// "cmd /c build.cmd" e relativo ao diretorio do cmd.exe, nao ao do monitor.
        /// </summary>
        public static bool TryGetProcessStrings(int pid, string exeName, out string commandLine, out string imagePath, out string workingDir)
        {
            commandLine = "";
            imagePath = "";
            workingDir = "";

            // (1) linha de comando pela consulta limitada: funciona mesmo com o
            //     processo elevado (nao precisa de PROCESS_VM_READ) e devolve a
            //     linha CRUA, sem as heuristicas do PEB.
            string crua;
            if (TryGetCommandLineLimited(pid, out crua) && crua.Length > 0) commandLine = crua;

            // (2) PEB: traz o diretorio de trabalho (CURDIR) e o caminho real do
            //     executavel. Exige VM_READ - para processo elevado nao da, e ai
            //     a linha de comando do passo (1) e a que vale.
            string pebCl; string pebPath; string pebDir;
            if (TryViaPeb(pid, exeName, out pebCl, out pebPath, out pebDir))
            {
                if (commandLine.Length == 0) commandLine = pebCl;
                if (pebPath.Length > 0) imagePath = pebPath;
                if (pebDir.Length > 0) workingDir = pebDir;
            }

            return commandLine.Length > 0 || imagePath.Length > 0 || workingDir.Length > 0;
        }

        /// <summary>Leitura pelo PEB (precisa de PROCESS_VM_READ).</summary>
        private static bool TryViaPeb(int pid, string exeName, out string commandLine, out string imagePath, out string workingDir)
        {
            commandLine = "";
            imagePath = "";
            workingDir = "";
            IntPtr h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
            if (h == IntPtr.Zero)
                h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                PROCESS_BASIC_INFORMATION pbi = new PROCESS_BASIC_INFORMATION();
                int retLen;
                if (NtQueryInformationProcess(h, 0, ref pbi, Marshal.SizeOf(pbi), out retLen) != 0) return false;
                if (pbi.PebBaseAddress == IntPtr.Zero) return false;

                IntPtr procParams;
                if (!ReadPointer(h, IntPtr.Add(pbi.PebBaseAddress, Is64Bit ? 0x20 : 0x10), out procParams)) return false;
                if (procParams == IntPtr.Zero) return false;

                int region = 0x200;
                byte[] buf = new byte[region];
                IntPtr read;
                if (!ReadProcessMemory(h, procParams, buf, region, out read)) return false;
                int usable = (int)read.ToInt64();

                bool achouComArgumentos = false;

                for (int i = 0; i + 16 <= usable; i += 2)
                {
                    ushort len = BitConverter.ToUInt16(buf, i);
                    if (len == 0 || len > 32768) continue;
                    ushort maxLen = BitConverter.ToUInt16(buf, i + 2);
                    if (maxLen < len) continue;
                    IntPtr strPtr = Is64Bit
                        ? new IntPtr(BitConverter.ToInt64(buf, i + 8))
                        : new IntPtr(BitConverter.ToInt32(buf, i + 4));
                    if (strPtr == IntPtr.Zero) continue;

                    string s = ReadString(h, strPtr, len);
                    if (s.Length == 0) continue;

                    // diretorio de trabalho (CURDIR): a UNICODE_STRING crua de uma
                    // pasta que existe e termina em barra. Aparece antes do caminho
                    // do executavel na regiao - e o texto NAO pode ser aparado (o
                    // Trim() comeria a barra final).
                    if (workingDir.Length == 0)
                    {
                        string cru = ReadStringRaw(h, strPtr, len);
                        if (EhDiretorio(cru)) workingDir = cru;
                    }

                    // argv[0] pode estar entre aspas: "C:\Windows\system32\cmd.exe" /c ...
                    // e pode NAO ter a extensao: o cmd.exe lanca o programa com
                    // argv[0] = "where" (sem o .exe). A comparacao ignora a extensao.
                    string first = FirstToken(s);
                    string baseName = SemExtensao(BatchFile.SafeBaseName(first.Trim('"')));
                    string alvo = SemExtensao(exeName ?? "");
                    if (alvo.Length > 0 &&
                        baseName.Equals(alvo, StringComparison.OrdinalIgnoreCase))
                    {
                        if (s.Length > first.Length)
                        {
                            // linha de comando COM argumentos: vale sempre mais que o
                            // caminho do executavel, mesmo sendo mais curta
                            // (ex.: 'where  cmd' vs 'C:\Windows\System32\where.exe')
                            if (!achouComArgumentos || s.Length > commandLine.Length)
                            {
                                commandLine = s;
                                achouComArgumentos = true;
                            }
                        }
                        else
                        {
                            // so o caminho do executavel (ImagePathName)
                            if (imagePath.Length == 0) imagePath = s;
                            if (commandLine.Length == 0) commandLine = s;
                        }
                    }
                    else if (imagePath.Length == 0 && s.IndexOf(' ') < 0 && exeName != null &&
                             s.EndsWith(exeName, StringComparison.OrdinalIgnoreCase))
                    {
                        imagePath = s;
                    }
                }
                return commandLine.Length > 0 || imagePath.Length > 0 || workingDir.Length > 0;
            }
            catch { return false; }
            finally { try { CloseHandle(h); } catch { } }
        }

        /// <summary>
        /// O texto e o caminho de um diretorio de verdade: raiz de drive
        /// (C:\) ou UNC (\\server\share\), terminado em barra e existente.
        /// </summary>
        private static bool EhDiretorio(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 3) return false;
            if (!s.EndsWith("\\", StringComparison.Ordinal)) return false;

            bool raiz = s.Length == 3 && s[1] == ':';
            bool drive = s.Length > 3 && s[1] == ':' && s[2] == '\\';
            bool unc = s.StartsWith("\\\\", StringComparison.Ordinal);
            if (!raiz && !drive && !unc) return false;

            try { return System.IO.Directory.Exists(s); }
            catch { return false; }
        }

        /// <summary>Nome do arquivo sem a extensao de executavel.</summary>
        private static string SemExtensao(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return "";
            foreach (string ext in new[] { ".exe", ".com", ".bat", ".cmd" })
                if (nome.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return nome.Substring(0, nome.Length - ext.Length);
            return nome;
        }

        private static string FirstToken(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            if (s.Length == 0) return "";
            if (s[0] == '"')
            {
                int e = s.IndexOf('"', 1);
                return e > 0 ? s.Substring(0, e + 1) : s;
            }
            int sp = s.IndexOfAny(new[] { ' ', '\t' });
            return sp > 0 ? s.Substring(0, sp) : s;
        }

        private static string ReadString(IntPtr h, IntPtr addr, ushort chars)        {
            try
            {
                byte[] b = new byte[chars * 2];
                IntPtr read;
                if (!ReadProcessMemory(h, addr, b, b.Length, out read)) return "";
                string s = Encoding.Unicode.GetString(b);
                int z = s.IndexOf('\0');
                if (z >= 0) s = s.Substring(0, z);
                return s.Trim();
            }
            catch { return ""; }
        }

        /// <summary>
        /// Igual ao ReadString, mas sem aparar: a barra final do diretorio de
        /// trabalho (CURDIR) faz parte do caminho.
        /// </summary>
        private static string ReadStringRaw(IntPtr h, IntPtr addr, ushort chars)
        {
            try
            {
                byte[] b = new byte[chars * 2];
                IntPtr read;
                if (!ReadProcessMemory(h, addr, b, b.Length, out read)) return "";
                string s = Encoding.Unicode.GetString(b);
                int z = s.IndexOf('\0');
                if (z >= 0) s = s.Substring(0, z);
                return s;
            }
            catch { return ""; }
        }

        private static bool ReadPointer(IntPtr h, IntPtr address, out IntPtr value)
        {
            value = IntPtr.Zero;
            byte[] buf = new byte[IntPtr.Size];
            IntPtr read;
            if (!ReadProcessMemory(h, address, buf, buf.Length, out read)) return false;
            value = Is64Bit ? new IntPtr(BitConverter.ToInt64(buf, 0)) : new IntPtr(BitConverter.ToInt32(buf, 0));
            return true;
        }
    }
}
