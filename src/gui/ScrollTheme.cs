using System;
using System.Drawing;

namespace CmdWatch.Gui
{
    /// <summary>
    /// Cores da barra de rolagem conforme o **tema do Windows** (claro/escuro).
    /// A barra desenhada a mao usa a paleta do sistema - polegar arredondado
    /// cinza sobre trilho invisivel, como no Windows 11 - em vez de uma cor
    /// inventada pelo painel.
    /// </summary>
    internal static class ScrollTheme
    {
        public static readonly Color Thumb;
        public static readonly Color ThumbActive;
        public static readonly Color ThumbHot;
        public static readonly bool Dark;

        static ScrollTheme()
        {
            Dark = IsWindowsDark();
            if (Dark)
            {
                // Windows 11 escuro
                Thumb = Color.FromArgb(0x4D, 0x4D, 0x4D);
                ThumbHot = Color.FromArgb(0x5C, 0x5C, 0x5C);
                ThumbActive = Color.FromArgb(0x6E, 0x6E, 0x6E);
            }
            else
            {
                // Windows 11 claro
                Thumb = Color.FromArgb(0xC8, 0xC8, 0xC8);
                ThumbHot = Color.FromArgb(0xD6, 0xD6, 0xD6);
                ThumbActive = Color.FromArgb(0xB4, 0xB4, 0xB4);
            }
        }

        /// <summary>Le o tema de aplicativos do Windows (0 = escuro).</summary>
        private static bool IsWindowsDark()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser
                    .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k == null) return true;
                    object v = k.GetValue("AppsUseLightTheme");
                    if (v == null) return true;
                    return Convert.ToInt32(v) == 0;
                }
            }
            catch { return true; }
        }
    }
}
