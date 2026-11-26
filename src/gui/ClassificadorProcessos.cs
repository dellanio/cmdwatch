using System;
using System.Collections.Generic;

namespace CmdWatch.Gui
{
    /// <summary>
    /// Decide o que e "do Windows" e o que foi "instalado por fora".
    ///
    /// A regra principal e o caminho do executavel: o que roda dentro da pasta
    /// do Windows (C:\Windows) e do Windows. Quando o caminho nao pode ser lido
    /// - acontece com os processos protegidos do sistema (svchost, lsass,
    /// csrss...), cuja ACL nega a consulta - o reconhecimento e pelo nome: a
    /// lista abaixo cobre o nucleo do Windows. O que sobrar (servicos de
    /// fabricante, programas que voce instalou) continua visivel.
    ///
    /// O caminho de um processo nao muda enquanto ele viver, entao a consulta
    /// e feita uma vez por (pid, hora de inicio) e guardada em cache: abrir
    /// 600 processos custa ~15 ms, so na primeira vez.
    /// </summary>
    internal static class ClassificadorProcessos
    {
        /// <summary>Pasta do Windows (normalmente C:\Windows).</summary>
        public static readonly string DirWindows =
            Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        /// <summary>
        /// Componentes do Windows que nao deixam ler o caminho (protegidos
        /// pela ACL do sistema). Medido na maquina: 186 processos de 623 nao
        /// respondem a consulta, e 95 deles sao svchost.exe.
        /// </summary>
        private static readonly HashSet<string> NomesDoWindows =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // nucleo: nao existe caminho legivel para nenhum deles
                "System", "Secure System", "Registry", "Memory Compression",
                "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe",
                "services.exe", "lsass.exe", "LsaIso.exe", "svchost.exe",
                "dwm.exe", "fontdrvhost.exe", "conhost.exe", "dllhost.exe",
                // servicos do sistema
                "spoolsv.exe", "WmiPrvSE.exe", "WUDFHost.exe", "SearchIndexer.exe",
                "unsecapp.exe", "msdtc.exe", "sppsvc.exe", "NgcIso.exe",
                "dasHost.exe", "Dsapi.exe", "AggregatorHost.exe",
                // seguranca
                "SecurityHealthService.exe", "SecurityHealthSystray.exe",
                "MsMpEng.exe", "MpDefenderCoreService.exe", "NisSrv.exe",
                "DefenderAiPlatformHost.exe",
                // recursos do Windows (hypervisor/WSL)
                "vmmemWSL", "vmcompute.exe", "vmwp.exe", "wslservice.exe"
            };

        private sealed class Item
        {
            public DateTime Inicio = DateTime.MinValue;
            public bool EhWindows;
        }

        private static readonly Dictionary<int, Item> _cache = new Dictionary<int, Item>();

        /// <summary>Quantas consultas ao sistema foram feitas (diagnostico).</summary>
        public static int Consultas;

        /// <summary>O caminho do executavel esta dentro da pasta do Windows?</summary>
        public static bool CaminhoDoWindows(string caminho)
        {
            if (string.IsNullOrEmpty(caminho)) return false;
            if (DirWindows.Length == 0) return false;
            return caminho.StartsWith(DirWindows, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// O processo e do Windows? (caminho dentro da pasta do Windows ou
        /// nome de componente protegido do sistema).
        /// </summary>
        public static bool EhWindows(CpuRow r)
        {
            if (r == null) return false;

            Item it;
            if (!_cache.TryGetValue(r.Pid, out it) || it.Inicio != r.StartedAt)
            {
                string caminho;
                bool win;
                if (NativeProcess.TryGetProcessPath(r.Pid, out caminho))
                {
                    Consultas++;
                    win = CaminhoDoWindows(caminho);
                }
                else win = NomesDoWindows.Contains(r.Name ?? "");

                it = new Item();
                it.Inicio = r.StartedAt;
                it.EhWindows = win;
                _cache[r.Pid] = it;
                if (_cache.Count > 4000) _cache.Clear();
            }
            return it.EhWindows;
        }

        /// <summary>
        /// Devolve a lista sem os processos do Windows (mesma ordem de entrada).
        /// A lista original nao e alterada.
        /// </summary>
        public static List<CpuRow> SemWindows(List<CpuRow> todos)
        {
            var kept = new List<CpuRow>();
            if (todos == null) return kept;
            foreach (CpuRow r in todos)
                if (!EhWindows(r)) kept.Add(r);
            return kept;
        }
    }
}
