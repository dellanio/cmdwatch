using System;
using System.IO;
using System.Text;

namespace CmdWatch
{
    /// <summary>
    /// Resultado da abertura do log: onde ele foi de fato aberto, se teve que
    /// mudar de lugar e o que deu errado.
    /// </summary>
    internal sealed class LogOpen
    {
        /// <summary>Escritor pronto (null quando o log ficou desligado ou falhou).</summary>
        public StreamWriter Writer;

        /// <summary>Caminho real do arquivo de log.</summary>
        public string Caminho = "";

        /// <summary>True quando o log foi para outro lugar porque o pedido nao cabia.</summary>
        public bool Movido;

        /// <summary>Motivo da falha (vazio quando deu certo).</summary>
        public string Erro = "";

        /// <summary>
        /// Aviso para o usuario, ja no idioma escolhido. Vazio quando esta
        /// tudo em ordem.
        /// </summary>
        public string Aviso()
        {
            if (Erro.Length > 0) return Lang.F("logFalhou", Erro);
            if (Movido) return Lang.F("logMovido", Caminho);
            return "";
        }
    }

    /// <summary>
    /// Decide onde o log JSONL pode ser gravado.
    ///
    /// O caminho padrao (cmdwatch-log.jsonl) e RELATIVO: ele nasce ao lado do
    /// script que o usuario executou. Isso e o comportamento de sempre e
    /// continua assim sempre que o diretorio atual permitir escrita.
    ///
    /// Mas o diretorio atual nem sempre permite: um instalador em "C:\Program
    /// Files", um pacote MSIX em "C:\Program Files\WindowsApps", uma pasta de
    /// rede ou um diretório removido no meio da sessao. Antes, a falha era
    /// engolida em silencio - o painel continuava funcionando, a tela de log
    /// abria vazia e ninguem entendia por que.
    ///
    /// Agora: caminho absoluto e intencao do usuario, entao respeitado (e o
    /// erro e reportado); caminho relativo que nao cabe cai para
    /// %LOCALAPPDATA%\cmdwatch e o usuario e avisado de onde o log foi parar.
    /// </summary>
    internal static class LogFile
    {
        /// <summary>Nome do arquivo de log usado quando nada e informado.</summary>
        public const string NomePadrao = "cmdwatch-log.jsonl";

        /// <summary>
        /// Abre o log para acrescer. Nunca lanca: devolve o resultado.
        /// </summary>
        public static LogOpen Abrir(string pedido)
        {
            LogOpen r = new LogOpen();
            if (pedido == null || pedido.Trim().Length == 0) return r;   // log desligado

            // caminho absoluto: a intencao do usuario manda - nao redireciona
            if (Path.IsPathRooted(pedido))
            {
                try
                {
                    r.Writer = Criar(pedido);
                    r.Caminho = pedido;
                    return r;
                }
                catch (Exception ex)
                {
                    r.Erro = ex.Message;
                    return r;
                }
            }

            // caminho relativo: primeiro ao lado do script (comportamento de sempre)
            try
            {
                string atual = Environment.CurrentDirectory;
                if (!string.IsNullOrEmpty(atual))
                {
                    string noAtual = Path.Combine(atual, pedido);
                    r.Writer = Criar(noAtual);
                    r.Caminho = noAtual;
                    return r;
                }
            }
            catch (Exception)
            {
                // sem escrita no diretorio atual (ou diretorio inexistente):
                // segue para a pasta de dados do usuario
            }

            // reserva: %LOCALAPPDATA%\cmdwatch (dentro do MSIX, essa pasta ja
            // e a pasta privada do pacote, que e gravavel)
            try
            {
                string baseLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(baseLocal))
                    baseLocal = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string pasta = Path.Combine(baseLocal, "cmdwatch");
                string reserva = Path.Combine(pasta, pedido);
                r.Writer = Criar(reserva);
                r.Caminho = reserva;
                r.Movido = true;
                return r;
            }
            catch (Exception ex)
            {
                r.Erro = ex.Message;
                return r;
            }
        }

        /// <summary>Cria o arquivo (e a pasta) e devolve o escritor.</summary>
        private static StreamWriter Criar(string caminho)
        {
            string dir = Path.GetDirectoryName(caminho);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            // sem BOM: JSONL com BOM quebra leitores estritos (jq e afins)
            return new StreamWriter(caminho, true, new UTF8Encoding(false)) { AutoFlush = true };
        }
    }
}
