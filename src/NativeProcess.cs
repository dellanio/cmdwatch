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
        /// Le o command line e o caminho do executavel a partir do PEB.
        /// Em vez de confiar em offsets fixos de RTL_USER_PROCESS_PARAMETERS (que mudam entre
        /// versoes do Windows), varre a regiao procurando a UNICODE_STRING cujo conteudo
        /// comeca com o nome do executavel.
        /// </summary>
        public static bool TryGetProcessStrings(int pid, string exeName, out string commandLine, out string imagePath)
        {
            commandLine = "";
            imagePath = "";
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

                    // argv[0] pode estar entre aspas: "C:\Windows\system32\cmd.exe" /c ...
                    string first = FirstToken(s);
                    string baseName = BatchFile.SafeBaseName(first.Trim('"'));
                    if (exeName != null && exeName.Length > 0 &&
                        baseName.Equals(exeName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (s.Length > first.Length)
                        {
                            // command line com argumentos: vence o mais longo
                            if (s.Length > commandLine.Length) commandLine = s;
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
                return commandLine.Length > 0 || imagePath.Length > 0;
            }
            catch { return false; }
            finally { try { CloseHandle(h); } catch { } }
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
