using System;
using System.Collections.Generic;
using System.Threading;

namespace CmdWatch
{
    /// <summary>
    /// Uma linha do retrato de CPU: o que a aba "Processos CPU" do painel
    /// mostra. Montada pelo amostrador (que vive no motor) e lida pela
    /// interface.
    /// </summary>
    internal sealed class CpuRow
    {
        public int Pid;
        public int ParentPid;
        public string Name = "";
        /// <summary>Linha de comando (so para os processos do topo: custo por processo).</summary>
        public string CmdLine = "";
        public string Path = "";
        /// <summary>Percentual do total da maquina neste instante.</summary>
        public double Cpu;
        /// <summary>Percentual medio desde o inicio do processo.</summary>
        public double CpuMedia;
        public long MemBytes;
        public int Threads;
        public DateTime StartedAt = DateTime.MinValue;
        public uint SessionId;
        /// <summary>Usuario (SID do token). Vazio quando nao da para ler.</summary>
        public string User = "";
        /// <summary>True quando o processo roda com integridade alta (administrador).</summary>
        public bool Elevado;
    }

    /// <summary>Um retrato completo de CPU, publicado de uma vez (imutavel).</summary>
    internal sealed class CpuSnapshot
    {
        public DateTime Em = DateTime.MinValue;
        /// <summary>Uso total da maquina, em % (0 a 100).</summary>
        public double UsoTotal;
        /// <summary>Nucleos logicos da maquina.</summary>
        public int Nucleos = 1;
        public readonly List<CpuRow> Processos = new List<CpuRow>();
    }

    /// <summary>
    /// Ponte entre o motor e a interface: o amostrador publica um retrato
    /// inteiro e o painel le sempre o ultimo. Sem lock e sem copia - o
    /// retrato e imutavel, entao trocar a referencia basta.
    /// </summary>
    internal static class CpuFeed
    {
        private static volatile CpuSnapshot _atual = new CpuSnapshot();

        public static CpuSnapshot Atual { get { return _atual; } }

        public static void Publicar(CpuSnapshot s) { _atual = s ?? new CpuSnapshot(); }
    }

    /// <summary>
    /// Amostra o uso de CPU comparando dois retratos do kernel.
    ///
    /// O retrato do kernel ja traz o tempo de CPU acumulado de cada processo
    /// (UserTime + KernelTime) e a memoria - entao o percentual sai de graca:
    /// e a diferenca entre dois retratos dividida pelo tempo que passou. O
    /// total da maquina e a soma das diferencas (sem o processo Idle).
    ///
    /// A linha de comando so e lida para os processos do topo: cada leitura
    /// e uma chamada por processo (barata, mas nao gratis em centena).
    /// </summary>
    internal sealed class CpuSampler
    {
        private readonly int _ms;
        private readonly int _nucleos;
        private Thread _thread;
        private volatile bool _running;

        // amostra anterior: pid -> tempo de CPU acumulado (100 ns) e ->
        // instante de nascimento (para saber se o PID foi reusado)
        private readonly Dictionary<int, long> _cpuAnterior = new Dictionary<int, long>();
        private readonly Dictionary<int, DateTime> _nasceuAnterior = new Dictionary<int, DateTime>();
        private DateTime _quando = DateTime.MinValue;

        /// <summary>Quantos processos do topo ganham linha de comando lida.</summary>
        private const int TopoComComando = 40;

        public CpuSampler(int intervaloMs)
        {
            _ms = Math.Max(700, intervaloMs);
            _nucleos = Math.Max(1, Environment.ProcessorCount);
        }

        public void Iniciar()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "cmdwatch-cpu";
            _thread.Start();
        }

        public void Parar() { _running = false; }

        private void Loop()
        {
            while (_running)
            {
                try { Amostrar(); }
                catch { }
                try { Thread.Sleep(_ms); }
                catch { return; }
            }
        }

        /// <summary>Uma amostra completa. Publica o retrato (ou devolve null).</summary>
        private void Amostrar()
        {
            List<ProcInfo> snap = NativeProcess.SnapshotFast();
            if (snap == null || snap.Count == 0) return;

            DateTime agora = DateTime.Now;
            double elapsed = _quando == DateTime.MinValue ? 0 : (agora - _quando).TotalSeconds;
            if (elapsed < 0.05) elapsed = 0;

            var cpuNovo = new Dictionary<int, long>();
            var nasceuNovo = new Dictionary<int, DateTime>();
            var linhas = new List<CpuRow>();
            double soma = 0;          // segundos de CPU consumidos no intervalo

            foreach (ProcInfo p in snap)
            {
                cpuNovo[p.Pid] = p.CpuTime;
                nasceuNovo[p.Pid] = p.StartedAt;

                double pct = 0;
                long anterior;
                DateTime nasceuAntes;
                bool reuso = false;
                if (_cpuAnterior.TryGetValue(p.Pid, out anterior))
                {
                    if (_nasceuAnterior.TryGetValue(p.Pid, out nasceuAntes) &&
                        p.StartedAt != DateTime.MinValue && nasceuAntes != DateTime.MinValue &&
                        p.StartedAt != nasceuAntes)
                        reuso = true;   // o Windows reusou o PID
                    if (!reuso && p.CpuTime >= anterior && elapsed > 0)
                    {
                        double segundos = (p.CpuTime - anterior) / 10000000.0;
                        pct = segundos / elapsed * 100.0 / _nucleos;
                        if (pct < 0) pct = 0;
                        if (pct > 100) pct = 100;
                        // o Idle (pid 0) e o tempo ocioso: nao entra no total
                        if (p.Pid != 0) soma += segundos;
                    }
                }

                CpuRow r = new CpuRow();
                r.Pid = p.Pid;
                r.ParentPid = p.ParentPid;
                r.Name = p.Name;
                r.Cpu = pct;
                r.MemBytes = p.MemBytes;
                r.Threads = p.Threads;
                r.StartedAt = p.StartedAt;
                r.SessionId = p.SessionId;

                // media desde o inicio do processo
                if (p.StartedAt != DateTime.MinValue && p.CpuTime > 0)
                {
                    double vida = (agora - p.StartedAt).TotalSeconds;
                    if (vida > 1)
                    {
                        r.CpuMedia = (p.CpuTime / 10000000.0) / vida * 100.0 / _nucleos;
                        if (r.CpuMedia < 0) r.CpuMedia = 0;
                    }
                }
                linhas.Add(r);
            }

            _cpuAnterior.Clear();
            foreach (var kv in cpuNovo) _cpuAnterior[kv.Key] = kv.Value;
            _nasceuAnterior.Clear();
            foreach (var kv in nasceuNovo) _nasceuAnterior[kv.Key] = kv.Value;
            _quando = agora;

            // ordena pelo consumo (o topo primeiro) e le a linha de comando dos
            // primeiros: e o que a lista mostra
            linhas.Sort(delegate (CpuRow a, CpuRow b)
            {
                if (b.Cpu != a.Cpu) return b.Cpu.CompareTo(a.Cpu);
                return b.MemBytes.CompareTo(a.MemBytes);
            });
            for (int i = 0; i < linhas.Count && i < TopoComComando; i++)
            {
                CpuRow r = linhas[i];
                string cmd;
                if (NativeProcess.TryGetCommandLineLimited(r.Pid, out cmd) && !string.IsNullOrEmpty(cmd))
                {
                    r.CmdLine = cmd;
                    string path;
                    if (NativeProcess.TryGetProcessStrings(r.Pid, r.Name, out cmd, out path)) r.Path = path;
                }
            }

            var snapFinal = new CpuSnapshot();
            snapFinal.Em = agora;
            snapFinal.Nucleos = _nucleos;
            snapFinal.UsoTotal = soma > 0 ? Math.Min(100, soma / elapsed * 100.0 / _nucleos) : 0;
            snapFinal.Processos.AddRange(linhas);
            CpuFeed.Publicar(snapFinal);
        }
    }
}
