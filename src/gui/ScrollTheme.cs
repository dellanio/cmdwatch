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
        public static readonly Color Track;
        public static readonly Color Thumb;
        public static readonly Color ThumbActive;
        public static readonly Color ThumbHot;
        public static readonly bool Dark;

        static ScrollTheme()
        {
            Dark = Theme.Dark;
            if (Dark)
            {
                Track = Color.FromArgb(0x11, 0x11, 0x11);
                Thumb = Color.FromArgb(0x2F, 0x2F, 0x2F);
                ThumbHot = Theme.Teal;
                ThumbActive = Theme.AcidHover;
            }
            else
            {
                Track = Color.FromArgb(0xEE, 0xEE, 0xEA);
                Thumb = Color.FromArgb(0xB8, 0xB8, 0xB2);
                ThumbHot = Theme.Teal;
                ThumbActive = Theme.Teal;
            }
        }
    }
}
