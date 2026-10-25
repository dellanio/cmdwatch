using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CmdWatch.Gui
{
    /// <summary>Entrada da interface grafica do cmdwatch.</summary>
    internal static class GuiRunner
    {
        public static int Run(Options o)
        {
            ConsoleHider.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool hideConsole = ConsoleHider.ShouldHideConsole();
            using (DashboardForm form = new DashboardForm(o))
            {
                if (hideConsole)
                    form.Shown += delegate { ConsoleHider.HideConsole(); };
                Application.Run(form);
            }
            return 0;
        }
    }

    /// <summary>
    /// Esconde o console quando ele pertence exclusively ao cmdwatch (execucao por
    /// duplo clique). Se o processo foi lancado a partir de um terminal existente,
    /// o console do usuario e preservado.
    /// </summary>
    internal static class ConsoleHider
    {
        [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
        [DllImport("kernel32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("kernel32.dll")] private static extern uint GetConsoleProcessList(uint[] list, uint count);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        private const int SW_HIDE = 0;

        public static void EnableDpiAwareness()
        {
            try { SetProcessDPIAware(); } catch { }
        }

        public static bool ShouldHideConsole()
        {
            try
            {
                if (GetConsoleWindow() == IntPtr.Zero) return false;
                uint[] buf = new uint[16];
                uint count = GetConsoleProcessList(buf, (uint)buf.Length);
                return count <= 1;
            }
            catch { return false; }
        }

        public static void HideConsole()
        {
            try
            {
                IntPtr h = GetConsoleWindow();
                if (h != IntPtr.Zero) ShowWindow(h, SW_HIDE);
            }
            catch { }
        }
    }
}
