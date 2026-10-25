using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CmdWatch.Gui
{
    /// <summary>Entrada da interface grafica do cmdwatch.</summary>
    internal static class GuiRunner
    {
        [DllImport("kernel32.dll")] private static extern bool SetProcessDPIAware();

        public static int Run(Options o)
        {
            try { SetProcessDPIAware(); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // idioma escolhido na ultima execucao (padrao: PT-BR)
            Lang.Load();

            using (DashboardForm form = new DashboardForm(o))
            {
                Application.Run(form);
            }
            return 0;
        }
    }
}
