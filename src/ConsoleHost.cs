using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CmdWatch
{
    /// <summary>
    /// Console dos modos de terminal. O executavel e compilado como aplicacao
    /// Windows (winexe): dar duplo clique nele NAO abre console nenhum - so o
    /// painel aparece. Quando o cmdwatch e chamado como comando de terminal
    /// (monitor, analyze, trace), ele se anexa ao console do terminal que o
    /// chamou, de modo que a saida sai exatamente onde o usuario espera.
    /// </summary>
    internal static class ConsoleHost
    {
        private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

        [DllImport("kernel32.dll")] private static extern bool AttachConsole(uint processId);
        [DllImport("kernel32.dll")] private static extern bool AllocConsole();
        [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();

        /// <summary>Garante que haja um console para escrever.</summary>
        public static void Ensure()
        {
            try
            {
                if (GetConsoleWindow() != IntPtr.Zero) return;   // ja tem console

                // anexa ao console de quem chamou; sem terminal (duplo clique
                // com argumentos), abre um console proprio
                if (!AttachConsole(ATTACH_PARENT_PROCESS)) AllocConsole();

                // depois de anexar, os streams precisam ser reabertos: os
                // handles originais foram criados sem console
                Stream stdout = Console.OpenStandardOutput();
                Stream stderr = Console.OpenStandardError();
                Console.SetOut(new StreamWriter(stdout) { AutoFlush = true });
                Console.SetError(new StreamWriter(stderr) { AutoFlush = true });

                Console.OutputEncoding = Encoding.UTF8;
            }
            catch { }
        }
    }
}
