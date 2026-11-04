using System;
using System.Text.RegularExpressions;

namespace CmdWatch
{
    /// <summary>
    /// Mascara segredos provaveis em linhas de comando antes de gravar no log
    /// ou mostrar na tela. E conservadora: so troca o VALOR por *** quando o
    /// nome do parametro deixa claro que e segredo (senha, token, chave de
    /// API, Authorization/Bearer, usuario:senha@ em URL, chave AWS, -pSENHA do
    /// mysql). O resto da linha fica igual. Desliga com --no-mask.
    /// </summary>
    internal static class Mascara
    {
        public static bool Ativa = true;
        private const string X = "***";
        private const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly Regex[] Regras =
        {
            // --password valor | --password=valor | -token:valor | /apikey valor
            new Regex(@"((?:^|\s)(?:--?|/)(?:password|passwd|pwd|pass|token|access[-_]?token|refresh[-_]?token|api[-_]?key|apikey|secret|client[-_]?secret)(?:[=:]|\s+))(""[^""]*""|'[^']*'|[^\s""']+)", O),
            // password=valor, token: valor, Password=x; (string de conexao), apikey=...
            new Regex(@"(\b(?:password|passwd|pwd|token|access[-_]?token|refresh[-_]?token|api[-_]?key|apikey|secret|client[-_]?secret)\s*[=:]\s*)(""[^""]*""|'[^']*'|[^\s;&""',]+)", O),
            // Authorization: Bearer xxx / Basic xxx, ou "Bearer xxx" solto
            new Regex(@"(\bauthorization:\s*(?:bearer|basic)\s+|\bbearer\s+)([A-Za-z0-9._~+/=-]{8,})", O),
            // esquema://usuario:senha@host
            new Regex(@"(\b[a-z][a-z0-9+.-]*://[^/\s:@""']+:)([^/\s@""']+)(?=@)", O),
            // mysql/mariadb -pSENHA (grudado; -p sozinho pede a senha)
            new Regex(@"(\b(?:mysql|mysqldump|mysqladmin|mariadb)(?:\.exe)?""?\s(?:.*?\s)?-p)([^\s""']+)", O),
        };
        // chave de acesso AWS (AKIA/ASIA + 16)
        private static readonly Regex Aws = new Regex(@"\b(AKIA|ASIA)[0-9A-Z]{16}\b", RegexOptions.CultureInvariant);

        public static string Aplicar(string s)
        {
            if (!Ativa || string.IsNullOrEmpty(s)) return s ?? "";
            try
            {
                foreach (Regex r in Regras)
                    s = r.Replace(s, m => m.Groups[2].Value == X ? m.Value : m.Groups[1].Value + X);
                s = Aws.Replace(s, m => m.Groups[1].Value + X);
            }
            catch { }
            return s;
        }
    }
}
