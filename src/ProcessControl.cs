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
            TerminateResult res = new TerminateResult();
            res.Pid = pid;
            if (pid <= 0)
            {
                res.Error = "pid invalido";
                return res;
            }

            List<int> ordem = new List<int>();
            if (includeChildren)
            {
                List<int> filhos = Descendants(pid);
                for (int i = 0; i < filhos.Count; i++) ordem.Add(filhos[i]);
            }
            ordem.Add(pid);

            bool erroPrincipal = false;
            for (int i = 0; i < ordem.Count; i++)
            {
                int alvo = ordem[i];
                bool ok = TryKill(alvo, out bool jaMorreu, out string erro);
                if (ok) { res.Killed++; continue; }

                if (alvo == pid)
                {
                    if (jaMorreu) { res.AlreadyGone = true; continue; }
                    res.Error = erro;
                    erroPrincipal = true;
                }
                // falha em um filho nao impede o encerramento do processo principal
            }

            res.Ok = !erroPrincipal && res.Killed > 0;
            if (res.AlreadyGone && res.Error.Length == 0) res.Ok = false;
            return res;
        }

        /// <summary>Encerra um unico processo (sem tocar nos filhos).</summary>
        private static bool TryKill(int pid, out bool jaMorreu, out string erro)
        {
            jaMorreu = false;
            erro = "";

            IntPtr h = OpenProcess(PROCESS_TERMINATE, false, pid);
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
        private static List<int> Descendants(int root)
        {
            List<int> resultado = new List<int>();
            try
            {
                List<ProcInfo> todos = NativeProcess.Snapshot();
                if (todos == null || todos.Count == 0) return resultado;

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
                        profundidade[c] = profundidade[atual] + 1;
                        fila.Enqueue(c);
                    }
                }

                foreach (KeyValuePair<int, int> kv in profundidade)
                {
                    if (kv.Key == root) continue;
                    resultado.Add(kv.Key);
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
