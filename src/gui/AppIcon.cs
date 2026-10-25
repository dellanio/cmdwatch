using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace CmdWatch.Gui
{
    /// <summary>
    /// Icone do produto. O arquivo .ico fica embutido no exe (build.ps1 usa
    /// /resource) e e carregado em runtime para a janela e para a bandeja.
    /// O .ico tem varias resolucoes; cada chamada devolve o quadro mais
    /// proximo do tamanho pedido.
    /// </summary>
    internal static class AppIcon
    {
        private const string ResourceName = "cmdwatch.ico";
        private static byte[] _bytes;
        private static bool _loaded;

        private static byte[] Bytes
        {
            get
            {
                if (_loaded) return _bytes;
                _loaded = true;
                try
                {
                    Assembly asm = Assembly.GetExecutingAssembly();
                    using (Stream s = asm.GetManifestResourceStream(ResourceName))
                    {
                        if (s == null) return null;
                        using (MemoryStream ms = new MemoryStream())
                        {
                            byte[] buf = new byte[8192];
                            int n;
                            while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                            _bytes = ms.ToArray();
                        }
                    }
                }
                catch { _bytes = null; }
                return _bytes;
            }
        }

        /// <summary>Devolve o icone no tamanho pedido, ou null se o recurso faltar.</summary>
        public static Icon Get(int size)
        {
            byte[] b = Bytes;
            if (b == null || b.Length == 0) return null;
            try
            {
                using (MemoryStream ms = new MemoryStream(b))
                {
                    // o recurso embutido tem varios quadros (16/24/32/48): o
                    // .NET escolhe o quadro que casa com o tamanho pedido
                    return new Icon(ms, new Size(size, size));
                }
            }
            catch { return null; }
        }
    }
}
