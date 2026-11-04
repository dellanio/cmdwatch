using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CmdWatch
{
    /// <summary>
    /// Resultado do encerramento de um processo (e dos filhos que ele deixou).
    /// </summary>
    internal sealed class TerminateResult
    {
        public int Pid;
        /// <summary>Quantos processos foram efetivamente encerrados.</summary>
        public int Killed;
        /// <summary>Verdadeiro quando o processo alvo foi encerrado.</summary>
        public bool Ok;
        /// <summary>Motivo da falha (vazio quando deu certo).</summary>
        public string Error = "";
        /// <summary>True quando o processo ja tinha terminado antes do pedido.</summary>
        public bool AlreadyGone;
    }

    /// <summary>
    /// Encerramento de processos observados pelo painel. Vive no nucleo (nao na
    /// interface) porque a operacao depende so da API do Windows - o mesmo
    /// caminho serve para o painel e para qualquer outro consumidor.
    ///
    /// A arvore de filhos e montada com o proprio snapshot do Toolhelp que o
    /// monitor ja usa: encerrar apenas o cmd.exe deixaria os programas que o
    /// script deixou rodando (um `ping -n 9999`, por exemplo) orfaos.
    /// </summary>
    internal static class ProcessControl
    {
        private const uint PROCESS_TERMINATE = 0x0001;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(IntPtr h,
            out System.Runtime.InteropServices.ComTypes.FILETIME criacao,
            out System.Runtime.InteropServices.ComTypes.FILETIME saida,
            out System.Runtime.InteropServices.ComTypes.FILETIME kernel,
            out System.Runtime.InteropServices.ComTypes.FILETIME usuario);

        /// <summary>Diferenca maxima aceita entre a hora registrada e a real.</summary>
        private static readonly TimeSpan Tolerancia = TimeSpan.FromSeconds(1);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr h);

        private const int ERROR_ACCESS_DENIED = 5;
        private const int ERROR_INVALID_PARAMETER = 87;

        /// <summary>
        /// Encerra o processo e, quando <paramref name="includeChildren"/> e
        /// verdadeiro, todos os descendentes (do mais fundo para o mais alto).
        /// </summary>
        public static TerminateResult Terminate(int pid, bool includeChildren)
        {
            return Terminate(pid, includeChildren, DateTime.MinValue);
        }

        /// <summary>
        /// Igual, mas so encerra se o processo com esse pid nasceu na hora
        /// registrada (<paramref name="inicioRegistrado"/>): um pid
        /// reaproveitado por outro processo nao e tocado. Os filhos tambem sao
        /// conferidos com a hora lida no retrato. DateTime.MinValue = hora
        /// desconhecida (sem conferencia, como antes).
        /// </summary>
        public static TerminateResult Terminate(int pid, bool includeChildren, DateTime inicioRegistrado)
        {
            TerminateResult res = new TerminateResult();
            res.Pid = pid;
            if (pid <= 0)
            {
                res.Error = "pid invalido";
                return res;
            }

            // o alvo primeiro: se o pid ja e de outro processo, nada e encerrado
            // (nem os "filhos" desse outro processo)
            if (inicioRegistrado != DateTime.MinValue)
            {
                DateTime real;
                if (LerInicio(pid, out real) && Math.Abs((real - inicioRegistrado).TotalMilliseconds) > Tolerancia.TotalMilliseconds)
                {
                    res.AlreadyGone = true;
                    res.Error = "o pid " + pid + " agora e de outro processo (iniciado " + real.ToString("HH:mm:ss") +
                                "); nada foi encerrado";
                    return res;
                }
            }

            Dictionary<int, DateTime> inicios = new Dictionary<int, DateTime>();
            List<int> ordem = new List<int>();
            if (includeChildren)
            {
                List<int> filhos = Descendants(pid, inicios);
                for (int i = 0; i < filhos.Count; i++) ordem.Add(filhos[i]);
            }
            ordem.Add(pid);
            inicios[pid] = inicioRegistrado;

            bool erroPrincipal = false;
            for (int i = 0; i < ordem.Count; i++)
            {
                int alvo = ordem[i];
                DateTime esperado;
                if (!inicios.TryGetValue(alvo, out esperado)) esperado = DateTime.MinValue;
                bool ok = TryKill(alvo, esperado, out bool jaMorreu, out string erro);
                if (ok) { res.Killed++; continue; }

                if (alvo == pid)
                {
                    if (jaMorreu) { res.AlreadyGone = true; if (erro.Length > 0) res.Error = erro; continue; }
                    res.Error = erro;
                    erroPrincipal = true;
                }
                // falha em um filho nao impede o encerramento do processo principal
            }

            res.Ok = !erroPrincipal && !res.AlreadyGone && res.Killed > 0;
            return res;
        }

        /// <summary>Hora de inicio real do processo (falso se ele nao existe).</summary>
        private static bool LerInicio(int pid, out DateTime inicio)
        {
            inicio = DateTime.MinValue;
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try { return LerInicio(h, out inicio); }
            finally { try { CloseHandle(h); } catch { } }
        }

        private static bool LerInicio(IntPtr h, out DateTime inicio)
        {
            inicio = DateTime.MinValue;
            System.Runtime.InteropServices.ComTypes.FILETIME ct, et, kt, ut;
            if (!GetProcessTimes(h, out ct, out et, out kt, out ut)) return false;
            long ft = ((long)ct.dwHighDateTime << 32) | (uint)ct.dwLowDateTime;
            inicio = DateTime.FromFileTimeUtc(ft).ToLocalTime();
            return true;
        }

        /// <summary>Encerra um unico processo (sem tocar nos filhos).</summary>
        private static bool TryKill(int pid, DateTime esperado, out bool jaMorreu, out string erro)
        {
            jaMorreu = false;
            erro = "";

            IntPtr h = OpenProcess(PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_INVALID_PARAMETER) { jaMorreu = true; return false; }
                if (err == ERROR_ACCESS_DENIED)
                {
                    erro = "sem permissao para encerrar este processo (precisa de administrador)";
                    return false;
                }
                erro = "nao foi possible abrir o processo (erro " + err + ")";
                return false;
            }

            try
            {
                // confere no MESMO handle que vai encerrar: sem janela entre a
                // conferencia e o TerminateProcess para o pid trocar de dono
                DateTime real;
                if (esperado != DateTime.MinValue && LerInicio(h, out real)
                    && Math.Abs((real - esperado).TotalMilliseconds) > Tolerancia.TotalMilliseconds)
                {
                    jaMorreu = true;
                    erro = "pid reaproveitado por outro processo; nao encerrado";
                    return false;
                }
                bool ok = TerminateProcess(h, 1);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == ERROR_ACCESS_DENIED)
                    {
                        erro = "sem permissao para encerrar este processo (precisa de administrador)";
                        return false;
                    }
                    erro = "falha ao encerrar (erro " + err + ")";
                    return false;
                }
                return true;
            }
            finally { try { CloseHandle(h); } catch { } }
        }

        /// <summary>
        /// Descendentes do processo (filhos, netos, bisnetos) em ordem de
        /// profundidade decrescente - os mais filhos primeiro.
        /// </summary>
        private static List<int> Descendants(int root, Dictionary<int, DateTime> iniciosSaida)
        {
            List<int> resultado = new List<int>();
            try
            {
                List<ProcInfo> todos = NativeProcess.Snapshot();
                if (todos == null || todos.Count == 0) return resultado;

                // hora de inicio de cada pid: um processo so e filho de verdade se
                // nasceu depois do pai. Sem isso, um orfao cujo pai antigo morreu e
                // teve o pid reaproveitado pelo alvo seria encerrado junto.
                Dictionary<int, DateTime> inicio = new Dictionary<int, DateTime>();
                for (int i = 0; i < todos.Count; i++) inicio[todos[i].Pid] = todos[i].StartedAt;
                Dictionary<int, List<int>> filhos = new Dictionary<int, List<int>>();
                for (int i = 0; i < todos.Count; i++)
                {
                    ProcInfo p = todos[i];
                    List<int> lista;
                    if (!filhos.TryGetValue(p.ParentPid, out lista))
                    {
                        lista = new List<int>();
                        filhos[p.ParentPid] = lista;
                    }
                    lista.Add(p.Pid);
                }

                Dictionary<int, int> profundidade = new Dictionary<int, int>();
                Queue<int> fila = new Queue<int>();
                fila.Enqueue(root);
                profundidade[root] = 0;

                while (fila.Count > 0)
                {
                    int atual = fila.Dequeue();
                    List<int> cs;
                    if (!filhos.TryGetValue(atual, out cs)) continue;
                    for (int i = 0; i < cs.Count; i++)
                    {
                        int c = cs[i];
                        if (profundidade.ContainsKey(c)) continue;   // evita ciclo
                        DateTime ti, tp;
                        if (inicio.TryGetValue(c, out ti) && inicio.TryGetValue(atual, out tp)
                            && ti != DateTime.MinValue && tp != DateTime.MinValue && ti < tp)
                            continue;   // pid do pai reaproveitado: nao e descendente
                        profundidade[c] = profundidade[atual] + 1;
                        fila.Enqueue(c);
                    }
                }

                foreach (KeyValuePair<int, int> kv in profundidade)
                {
                    if (kv.Key == root) continue;
                    resultado.Add(kv.Key);
                    DateTime t;
                    if (iniciosSaida != null && inicio.TryGetValue(kv.Key, out t)) iniciosSaida[kv.Key] = t;
                }

                // mais profundos primeiro: o filho morre antes do pai
                resultado.Sort(delegate (int a, int b)
                {
                    int da = profundidade[a], db = profundidade[b];
                    if (da != db) return db.CompareTo(da);
                    return a.CompareTo(b);
                });
            }
            catch { }
            return resultado;
        }
    }
}
