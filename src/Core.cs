using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CmdWatch
{
    // ---------------------------------------------------------------- JSON
    internal static class J
    {
        public static string Esc(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32 || c > 126) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string Str(string s) { return s == null ? "null" : "\"" + Esc(s) + "\""; }
        public static string Arr(IEnumerable<string> items)
        {
            var sb = new StringBuilder("[");
            bool first = true;
            if (items != null)
            {
                foreach (string it in items) { if (!first) sb.Append(","); sb.Append(Str(it)); first = false; }
            }
            sb.Append("]");
            return sb.ToString();
        }
        public static string Num(long n) { return n.ToString(CultureInfo.InvariantCulture); }
        public static string Num(double n) { return n.ToString("0.###", CultureInfo.InvariantCulture); }
        public static string Bool(bool b) { return b ? "true" : "false"; }
        public static string Ts(DateTime t) { return "\"" + t.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) + "\""; }
    }

    // ------------------------------------------------------- Modelo do .cmd
    internal sealed class BatchStep
    {
        public int Line;
        public string Raw = "";
        public string Command = "";     // texto do comando normalizado
        public string Exe = "";         // token do executavel
        public string ExeKey = "";      // chave lowercase (sem extensao)
        public bool IsLabel;
        public string Label = "";
        public bool IsComment;
        public bool IsCall;             // 'call' para outro script/label
        public string CallTarget = "";
        public bool IsControl;          // if/for/goto/set/echo...
        public string Meaning = "";     // explicacao em PT-BR
    }

    internal sealed class BatchFile
    {
        public string Path = "";
        public string Name = "";
        public string Purpose = "";
        public string Category = "";
        public readonly List<BatchStep> Steps = new List<BatchStep>();
        public readonly List<string> Labels = new List<string>();
        public readonly List<string> Variables = new List<string>();
        public int TotalLines;
        public int ExecutableSteps;
        public string Banner = "";
        public bool Exists;

        /// <summary>Encontra a linha do .cmd que melhor corresponde a um comando observado.</summary>
        public BatchStep Match(string observedCmdLine, out double confidence)
        {
            confidence = 0;
            BatchStep best = null;
            string obsExe = ExeKeyOf(observedCmdLine);
            string obsNorm = Normalize(observedCmdLine);
            var obsTokens = CommandTokens(observedCmdLine);

            foreach (BatchStep st in Steps)
            {
                if (st.IsLabel || st.IsComment) continue;
                double score = 0;

                if (st.ExeKey.Length > 0 && st.ExeKey == obsExe) score += 3.0;
                else if (obsExe.Length > 0 && (st.ExeKey.EndsWith(obsExe) || obsExe.EndsWith(st.ExeKey))) score += 1.0;

                string stNorm = Normalize(st.Command);
                if (stNorm.Length > 0 && stNorm == obsNorm) score += 4.0;
                else score += Jaccard(stNorm, obsNorm) * 1.0;

                // sufixo comum de tokens: casa 'npm run build' com 'cmd.exe /c CALL node ... npm-cli.js run build'
                int suffix = CommonSuffixLength(CommandTokens(st.Command), obsTokens);
                if (suffix >= 2) score += 2.5;
                else if (suffix == 1) score += 1.5;

                // comando de script (.cmd/.bat) observado casa com 'call x.cmd'
                if (st.IsCall && st.CallTarget.Length > 0 && obsExe.Length > 0)
                {
                    string tgt = ExeKeyOf(st.CallTarget);
                    if (tgt == obsExe) score += 3.5;
                }

                if (score > confidence) { confidence = score; best = st; }
            }
            return best;
        }

        /// <summary>Tokens significativos de um comando (sem caminho, sem switch do cmd, sem 'call').</summary>
        public static List<string> CommandTokens(string cmdLine)
        {
            var res = new List<string>();
            if (string.IsNullOrEmpty(cmdLine)) return res;

            string s = cmdLine.Trim();

            // remove o proprio executavel inicial (ex.: "C:\Windows\system32\cmd.exe" ou node.exe)
            string first0 = FirstToken(s);
            if (first0.Length > 0 &&
                (first0.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                 first0.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                 first0.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
            {
                s = s.Substring(first0.Length).Trim();
            }

            // remove switches do cmd (/c, /k, /d, /s ...)
            while (s.Length > 0 && s[0] == '/')
            {
                int sp = s.IndexOf(' ');
                if (sp < 0) { s = ""; break; }
                string sw = s.Substring(0, sp);
                if (sw.Length > 3) break; // switch com argumento -> para
                s = s.Substring(sp + 1).Trim();
            }

            // remove 'call'
            if (s.StartsWith("call ", StringComparison.OrdinalIgnoreCase)) s = s.Substring(5).Trim();

            foreach (string tok in TokenizeCommand(s))
            {
                string t = tok.Trim('"');
                if (t.Length == 0) continue;
                string baseName = SafeBaseName(t);
                if (baseName.Length == 0) baseName = t;
                foreach (string ext in new[] { ".exe", ".cmd", ".bat", ".com", ".ps1", ".js", ".py" })
                    if (baseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    { baseName = baseName.Substring(0, baseName.Length - ext.Length); break; }
                res.Add(baseName.ToLowerInvariant());
            }
            return res;
        }

        public static List<string> TokenizeCommand(string s)
        {
            var res = new List<string>();
            if (string.IsNullOrEmpty(s)) return res;
            int i = 0;
            while (i < s.Length)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i >= s.Length) break;
                var sb = new StringBuilder();
                if (s[i] == '"')
                {
                    i++;
                    while (i < s.Length)
                    {
                        if (s[i] == '"') { i++; break; }
                        sb.Append(s[i]); i++;
                    }
                }
                else
                {
                    while (i < s.Length && !char.IsWhiteSpace(s[i])) { sb.Append(s[i]); i++; }
                }
                res.Add(sb.ToString());
            }
            return res;
        }

        private static int CommonSuffixLength(List<string> a, List<string> b)
        {
            int n = 0;
            while (n < a.Count && n < b.Count &&
                   a[a.Count - 1 - n].Equals(b[b.Count - 1 - n], StringComparison.OrdinalIgnoreCase))
                n++;
            return n;
        }

        /// <summary>Nome do arquivo sem caminho, tolerando caracteres invalidos do Windows.</summary>
        public static string SafeBaseName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                int cut = path.LastIndexOfAny(new[] { '\\', '/' });
                return cut >= 0 ? path.Substring(cut + 1) : path;
            }
            catch { return path; }
        }

        public static string ExeKeyOf(string cmdLine)
        {
            if (string.IsNullOrEmpty(cmdLine)) return "";
            string tok = FirstToken(cmdLine);
            if (tok.Length == 0) return "";
            string name = SafeBaseName(tok.Trim('"')).ToLowerInvariant();
            foreach (string ext in new[] { ".exe", ".cmd", ".bat", ".com", ".ps1", ".vbs", ".js", ".py" })
                if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) { name = name.Substring(0, name.Length - ext.Length); break; }
            return name;
        }

        public static string FirstToken(string cmdLine)
        {
            if (cmdLine == null) return "";
            cmdLine = cmdLine.Trim();
            if (cmdLine.Length == 0) return "";
            if (cmdLine[0] == '"')
            {
                int end = cmdLine.IndexOf('"', 1);
                return end > 0 ? cmdLine.Substring(0, end + 1) : cmdLine;
            }
            int sp = cmdLine.IndexOfAny(new[] { ' ', '\t' });
            return sp > 0 ? cmdLine.Substring(0, sp) : cmdLine;
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
            {
                if (c == ' ' || c == '\t' || c == '"' || c == '\'') continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static double Jaccard(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return 0;
            var sa = new HashSet<string>(a.Split(' '));
            var sb2 = new HashSet<string>(b.Split(' '));
            int inter = 0;
            foreach (string x in sa) if (sb2.Contains(x)) inter++;
            return (double)inter / (sa.Count + sb2.Count - inter);
        }
    }

    // ---------------------------------------------------------- Parser .cmd
    internal static class BatchParser
    {
        private static readonly Regex RxLabel = new Regex(@"^:([A-Za-z0-9_.\-]+)\s*$", RegexOptions.Compiled);
        private static readonly Regex RxRem = new Regex(@"^\s*(rem\b|::)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RxCall = new Regex(@"^\s*call\s+(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RxSet = new Regex(@"^\s*set\s+(""?)([A-Za-z_][A-Za-z0-9_]*)\1\s*=", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RxEcho = new Regex(@"^\s*@?echo\s+(.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string[] ControlExes = new[]
        {
            "if", "else", "for", "goto", "call", "set", "echo", "rem", "exit", "pause", "title", "cd", "chcp", "pushd", "popd", "shift", "setlocal", "endlocal", "start", "verify", "errorlevel", "exist", "equ", "neq", "gtr", "lss", "geq", "leq", "md", "rd", "del", "copy", "move", "ren", "type", "cls"
        };

        public static BatchFile Parse(string path)
        {
            var bf = new BatchFile { Path = path, Name = Path.GetFileName(path) };
            if (!File.Exists(path)) return bf;
            bf.Exists = true;

            string[] lines;
            try { lines = ReadLinesSmart(path); }
            catch { return bf; }

            bf.TotalLines = lines.Length;

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i] ?? "";
                string t = raw.Trim();

                if (t.Length == 0) continue;

                if (RxRem.IsMatch(t)) { bf.Steps.Add(new BatchStep { Line = i + 1, Raw = raw, IsComment = true }); continue; }

                var mLab = RxLabel.Match(t);
                if (mLab.Success)
                {
                    bf.Labels.Add(mLab.Groups[1].Value);
                    bf.Steps.Add(new BatchStep { Line = i + 1, Raw = raw, IsLabel = true, Label = mLab.Groups[1].Value });
                    continue;
                }

                string cmd = t;
                if (cmd.StartsWith("@")) cmd = cmd.Substring(1).Trim();

                var mSet = RxSet.Match(cmd);
                if (mSet.Success) bf.Variables.Add(mSet.Groups[2].Value);

                bool isCall = false; string callTarget = "";
                var mCall = RxCall.Match(cmd);
                if (mCall.Success)
                {
                    isCall = true;
                    callTarget = ExtractCallTarget(mCall.Groups[1].Value);
                    // mantem o comando completo (args) para o casamento de tokens
                    cmd = mCall.Groups[1].Value.Trim();
                }

                var mEcho = RxEcho.Match(cmd);
                if (mEcho.Success && bf.Banner.Length == 0)
                {
                    string banner = mEcho.Groups[1].Value.Trim().Trim('"').Trim();
                    if (banner.Length > 3 && banner.Length < 160 && !banner.StartsWith("off") && !banner.StartsWith("on"))
                        bf.Banner = banner;
                }

                string exeTok = BatchFile.FirstToken(cmd);
                string exeKey = BatchFile.ExeKeyOf(exeTok);
                bool isControl = ControlExes.Contains(exeKey);

                var st = new BatchStep
                {
                    Line = i + 1,
                    Raw = raw,
                    Command = cmd,
                    Exe = exeTok.Trim('"'),
                    ExeKey = exeKey,
                    IsCall = isCall,
                    CallTarget = callTarget,
                    IsControl = isControl
                };
                st.Meaning = Explain.Describe(st);
                bf.Steps.Add(st);
            }

            bf.ExecutableSteps = bf.Steps.Count(s => !s.IsComment && !s.IsLabel);
            bf.Category = Explain.Categorize(bf);
            bf.Purpose = Explain.Purpose(bf);
            return bf;
        }

        /// <summary>Extrai o alvo de um 'call': caminho do script chamado (sem aspas).</summary>
        public static string ExtractCallTarget(string rest)
        {
            if (rest == null) return "";
            string s = rest.Trim();
            if (s.Length == 0) return "";
            if (s[0] == '"')
            {
                int e = s.IndexOf('"', 1);
                return e > 0 ? s.Substring(1, e - 1) : s.Substring(1);
            }
            int sp = s.IndexOfAny(new[] { ' ', '\t' });
            return sp > 0 ? s.Substring(0, sp) : s;
        }

        public static string[] ReadLinesSmart(string path)        {
            byte[] raw = File.ReadAllBytes(path);
            if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
                return Encoding.UTF8.GetString(raw, 3, raw.Length - 3).Replace("\r\n", "\n").Split('\n');

            try
            {
                var strict = new UTF8Encoding(false, true);
                string s = strict.GetString(raw);
                return s.Replace("\r\n", "\n").Split('\n');
            }
            catch { }
            return Encoding.Default.GetString(raw).Replace("\r\n", "\n").Split('\n');
        }
    }

    // -------------------------------------------------- Motor de explicacao
    internal static class Explain
    {
        private static readonly Dictionary<string, string> Exe = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "npm", "gerencia pacotes Node.js" },
            { "npx", "executa um binário do pacote Node.js" },
            { "yarn", "gerencia pacotes e dependências com Yarn" },
            { "pnpm", "gerencia pacotes e dependências com pnpm" },
            { "node", "executa código JavaScript/TypeScript com Node.js" },
            { "tsc", "compila TypeScript para JavaScript" },
            { "eslint", "analisa o código em busca de erros e más práticas" },
            { "prettier", "formata o código segundo um padrão de estilo" },
            { "jest", "executa a suíte de testes unitários" },
            { "vitest", "executa a suíte de testes com Vitest" },
            { "mocha", "executa a suíte de testes com Mocha" },
            { "webpack", "empacota os assets da aplicação" },
            { "vite", "sube o servidor de desenvolvimento e empacota o front" },
            { "next", "executa o framework Next.js (dev/build/start)" },
            { "ng", "executa a CLI do Angular" },
            { "nest", "executa a CLI do NestJS" },
            { "dotnet", "compila, publica e roda aplicações .NET" },
            { "msbuild", "compila projetos MSBuild" },
            { "nuget", "gerencia pacotes NuGet" },
            { "mvn", "compila e empacota projetos Maven" },
            { "gradle", "compila e empacota projetos Gradle" },
            { "java", "executa a JVM" },
            { "javac", "compila código Java" },
            { "python", "executa um script Python" },
            { "pip", "instala e gerencia pacotes Python" },
            { "pip3", "instala e gerencia pacotes Python" },
            { "pytest", "executa os testes Python" },
            { "poetry", "gerencia dependências de projetos Python" },
            { "uv", "gerencia pacotes e ambientes Python" },
            { "conda", "gerencia ambientes e pacotes Python" },
            { "go", "compila ou executa código Go" },
            { "gofmt", "formata código Go" },
            { "cargo", "compila projetos Rust" },
            { "rustc", "compila código Rust" },
            { "php", "executa código PHP" },
            { "composer", "gerencia dependências PHP" },
            { "ruby", "executa código Ruby" },
            { "bundle", "gerencia gems do Ruby" },
            { "git", "opera no repositório Git" },
            { "gh", "interage com o GitHub via linha de comando" },
            { "svn", "opera no repositório Subversion" },
            { "hg", "opera no repositório Mercurial" },
            { "docker", "gerencia containers Docker" },
            { "docker-compose", "sobe os serviços definidos em um compose" },
            { "podman", "gerencia containers Podman" },
            { "kubectl", "gerencia clusters Kubernetes" },
            { "helm", "gerencia charts do Kubernetes" },
            { "terraform", "provisiona infraestrutura na nuvem" },
            { "ansible", "automatiza configuração de servidores" },
            { "vagrant", "gerencia máquinas virtuais" },
            { "aws", "interage com a AWS via CLI" },
            { "az", "interage com o Azure via CLI" },
            { "gcloud", "interage com o Google Cloud via CLI" },
            { "gsutil", "opera objetos no Google Cloud Storage" },
            { "curl", "baixa ou envia dados via HTTP" },
            { "wget", "baixa arquivos da web" },
            { "ssh", "abre uma conexão shell segura em outro host" },
            { "scp", "copia arquivos entre hosts via SSH" },
            { "sftp", "transfere arquivos via SFTP" },
            { "telnet", "abre uma conexão de telnet" },
            { "ping", "testa a conectividade com um host" },
            { "nslookup", "resolve nomes DNS" },
            { "tracert", "mostra o caminho de rota até um host" },
            { "ipconfig", "mostra a configuração de rede" },
            { "ifconfig", "mostra interfaces de rede" },
            { "netsh", "configura rede e firewall do Windows" },
            { "powershell", "executa comandos no PowerShell" },
            { "pwsh", "executa comandos no PowerShell 7" },
            { "cmd", "executa comandos no interpretador do Windows" },
            { "wscript", "executa scripts de console em Windows Script Host" },
            { "cscript", "executa scripts de console em Windows Script Host" },
            { "sqlcmd", "executa consultas no Microsoft SQL Server" },
            { "psql", "cliente de linha de comando do PostgreSQL" },
            { "mysql", "cliente de linha de comando do MySQL" },
            { "mongo", "cliente do MongoDB" },
            { "redis-cli", "cliente do Redis" },
            { "make", "executa um makefile" },
            { "cmake", "gera e compila com o CMake" },
            { "ninja", "executa builds com o Ninja" },
            { "robocopy", "copia diretórios de forma resiliente" },
            { "xcopy", "copia arquivos e diretórios" },
            { "copy", "copia arquivos" },
            { "move", "move ou renomeia arquivos" },
            { "del", "apaga arquivos" },
            { "rmdir", "remove diretórios" },
            { "mkdir", "cria diretórios" },
            { "ren", "renomeia arquivos" },
            { "type", "exibe o conteúdo de um arquivo de texto" },
            { "findstr", "procura texto em arquivos" },
            { "find", "procura arquivos por nome" },
            { "attrib", "altera atributos de arquivos" },
            { "icacls", "gerencia permissões de arquivos" },
            { "takeown", "assume a posse de arquivos" },
            { "schtasks", "agenda tarefas do Windows" },
            { "sc", "gerencia serviços do Windows" },
            { "net", "comandos de rede do Windows" },
            { "netstat", "mostra conexões de rede ativas" },
            { "tasklist", "lista processos em execução" },
            { "taskkill", "encerra processos" },
            { "wmic", "consulta informações de hardware e sistema" },
            { "reg", "manipula o registro do Windows" },
            { "chkdsk", "verifica e repara o disco" },
            { "diskpart", "gerencia partições e discos" },
            { "cipher", "criptografa ou descriptografa arquivos" },
            { "certutil", "manipula certificados e hashes" },
            { "msiexec", "instala ou remove pacotes MSI" },
            { "winget", "gerencia pacotes do Windows" },
            { "choco", "gerencia pacotes com Chocolatey" },
            { "scoop", "gerencia pacotes com Scoop" },
            { "systeminfo", "mostra informações do sistema" },
            { "hostname", "mostra o nome do computador" },
            { "whoami", "mostra o usuário atual" },
            { "ver", "mostra a versão do Windows" },
            { "timeout", "pausa a execução por N segundos" },
            { "azcopy", "copia dados para o Azure Storage" },
            { "busybox", "executa utilitários Unix compactos" },
            { "grep", "filtra texto por padrão" },
            { "sed", "edita texto de forma não interativa" },
            { "awk", "processa texto colunar" },
            { "curl.exe", "baixa ou envia dados via HTTP" },
            { "code", "abre o Visual Studio Code" },
            { "notepad", "abre o Bloco de Notas" },
            { "explorer", "abre o Explorador de Arquivos" },
            { "mmc", "abre o Console de Gerenciamento" },
        };

        private static readonly string[] BuildWords = { "build", "compile", "compila", "empacota", "bundle", "publish", "package", "gerar", "dist" };

        /// <summary>Explicacao em PT-BR de uma linha do .cmd.</summary>
        public static string Describe(BatchStep st)
        {
            if (st == null) return "";
            if (st.IsLabel) return "rótulo de salto (:{0})".Replace("{0}", st.Label);
            if (st.IsComment) return "comentário";

            string cmd = st.Command ?? "";
            string key = st.ExeKey ?? "";
            string args = ArgsOf(cmd, st.Exe);

            // Casos compostos mais frequentes -------------------------------------------------
            if (key == "npm" && args.StartsWith("run", StringComparison.OrdinalIgnoreCase))
                return "roda o script \"" + AfterWord(args, "run") + "\" definido no package.json";
            if (key == "npm" && args.StartsWith("install", StringComparison.OrdinalIgnoreCase))
                return "instala as dependências do projeto Node.js";
            if (key == "npm" && args.StartsWith("ci", StringComparison.OrdinalIgnoreCase))
                return "instala as dependências de forma reprodutível a partir do package-lock.json";
            if (key == "npm" && args.StartsWith("publish", StringComparison.OrdinalIgnoreCase))
                return "publica o pacote no registro npm";
            if (key == "npm" && args.StartsWith("test", StringComparison.OrdinalIgnoreCase))
                return "roda a suíte de testes do projeto";
            if (key == "pnpm" && args.StartsWith("install", StringComparison.OrdinalIgnoreCase))
                return "instala as dependências do projeto com pnpm";
            if (key == "yarn" && args.StartsWith("install", StringComparison.OrdinalIgnoreCase))
                return "instala as dependências do projeto com Yarn";

            if (key == "git")
            {
                string sub = FirstWord(args);
                if (sub == "commit") return "cria um commit com as alterações" + Quoted(args);
                if (sub == "push") return "envia os commits para o repositório remoto";
                if (sub == "pull") return "traz as alterações do repositório remoto";
                if (sub == "clone") return "clona um repositório" + Quoted(args);
                if (sub == "checkout") return "troca de branch ou restaura arquivos";
                if (sub == "merge") return "integra alterações de outra branch";
                if (sub == "rebase") return "reaplica commits em cima de outra branch";
                if (sub == "status") return "mostra o estado atual do repositório";
                if (sub == "add") return "adiciona arquivos ao staging";
                if (sub == "tag") return "cria ou lista tags";
                if (sub == "diff") return "mostra as diferenças entre commits ou arquivos";
                if (sub == "fetch") return "baixa as referências do remoto sem integrar";
                if (sub == "stash") return "guarda ou restaura alterações em andamento";
            }

            if (key == "dotnet")
            {
                string sub = FirstWord(args);
                if (sub == "build") return "compila o projeto .NET";
                if (sub == "publish") return "compila e publica o projeto .NET para o diretório de saída";
                if (sub == "test") return "roda os testes do projeto .NET";
                if (sub == "restore") return "restaura os pacotes NuGet do projeto";
                if (sub == "run") return "executa o projeto .NET";
                if (sub == "ef") return "executa um comando do Entity Framework (migrações de banco)";
                if (sub == "clean") return "limpa os artefatos de build";
            }

            if (key == "docker")
            {
                string sub = FirstWord(args);
                if (sub == "build") return "constrói uma imagem Docker a partir do Dockerfile do contexto atual";
                if (sub == "push") return "envia a imagem para um registro Docker";
                if (sub == "pull") return "baixa uma imagem Docker";
                if (sub == "run") return "sobe um container Docker" + Quoted(args);
                if (sub == "compose") return "opera os serviços definidos no docker-compose";
                if (sub == "ps") return "lista os containers em execução";
                if (sub == "stop" || sub == "rm" || sub == "rmi") return "remove containers ou imagens Docker";
                if (sub == "logs") return "mostra os logs do container";
                if (sub == "exec") return "executa um comando dentro de um container em execução";
            }

            if (key == "kubectl")
            {
                string sub = FirstWord(args);
                if (sub == "apply") return "aplica manifestos no cluster Kubernetes";
                if (sub == "get") return "lista recursos do cluster Kubernetes";
                if (sub == "rollout") return "gerencia o rollout de deployments";
                if (sub == "logs") return "mostra os logs dos pods";
                if (sub == "delete") return "remove recursos do cluster Kubernetes";
            }

            if (key == "terraform")
            {
                string sub = FirstWord(args);
                if (sub == "plan") return "simula as mudanças de infraestrutura";
                if (sub == "apply") return "aplica as mudanças de infraestrutura";
                if (sub == "init") return "inicializa o diretório do Terraform";
                if (sub == "destroy") return "destrói a infraestrutura gerenciada";
            }

            if (key == "mvn")
            {
                string sub = FirstWord(args);
                if (sub == "clean") return "limpa os artefatos de build do Maven";
                if (sub == "install" || sub == "package") return "compila e empacota o projeto Maven";
                if (sub == "test") return "roda os testes do projeto Maven";
            }

            if (key == "gradle")
            {
                string sub = FirstWord(args);
                if (sub == "build") return "compila e empacota o projeto Gradle";
                if (sub == "test") return "roda os testes do projeto Gradle";
                if (sub == "clean") return "limpa os artefatos de build do Gradle";
            }

            if (key == "cargo")
            {
                string sub = FirstWord(args);
                if (sub == "build") return "compila o projeto Rust";
                if (sub == "test") return "roda os testes do projeto Rust";
                if (sub == "publish") return "publica o crate no crates.io";
                if (sub == "run") return "executa o projeto Rust";
            }

            if (key == "pip" || key == "pip3")
                return args.StartsWith("install", StringComparison.OrdinalIgnoreCase)
                    ? "instala pacotes Python"
                    : "opera o gerenciador de pacotes Python";

            if (key == "curl" || key == "wget")
                return "baixa ou envia dados via HTTP a partir da rede";

            if (key == "powershell" || key == "pwsh")
                return "executa um script ou comando no PowerShell";

            if (key == "robocopy") return "copia diretórios de forma resiliente, com log e retry";
            if (key == "xcopy" || key == "copy") return "copia arquivos ou diretórios";
            if (key == "move") return "move ou renomeia arquivos";
            if (key == "del") return "apaga arquivos";
            if (key == "mkdir") return "cria diretórios";
            if (key == "rmdir") return "remove diretórios";
            if (key == "certutil") return "gera hashes ou gerencia certificados";
            if (key == "icacls") return "ajusta permissões de arquivos";
            if (key == "taskkill") return "encerra processos em execução";
            if (key == "tasklist") return "lista os processos em execução";
            if (key == "netstat") return "mostra as conexões de rede ativas";
            if (key == "schtasks") return "cria ou consulta tarefas agendadas";

            // Chamada a outro script
            if (st.IsCall)
            {
                string tgt = st.CallTarget ?? "";
                if (tgt.StartsWith(":", StringComparison.Ordinal)) return "salta para o rótulo \"" + tgt.TrimStart(':') + "\" deste script";
                if (tgt.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || tgt.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                    return "executa outro script: " + tgt;
                return "chama \"" + tgt + "\"";
            }

            // Controles de fluxo
            if (st.IsControl)
            {
                switch (key)
                {
                    case "if": return "avalia uma condição e decide o próximo passo";
                    case "for": return "repete um comando para cada item de uma lista";
                    case "goto": return "salta para um rótulo do script";
                    case "set": return "define uma variável de ambiente";
                    case "echo": return "exibe uma mensagem na tela";
                    case "title": return "define o título da janela do console";
                    case "cd": return "muda o diretório atual";
                    case "pushd": return "guarda o diretório atual e muda para outro";
                    case "popd": return "volta ao diretório guardado";
                    case "exit": return "encerra o script";
                    case "pause": return "aguarda o usuário pressionar uma tecla";
                    case "start": return "inicia um processo em uma nova janela";
                    case "chcp": return "muda o conjunto de caracteres do console";
                    case "setlocal": return "isola as alterações de variáveis";
                    case "endlocal": return "descarta as alterações de variáveis isoladas";
                }
            }

            // Dicionário genérico
            string generic;
            if (Exe.TryGetValue(key, out generic))
                return generic + (args.Length > 0 ? " (" + Truncate(args, 80) + ")" : "");

            if (key.Length == 0) return "executa: " + Truncate(cmd, 90);
            return "executa \"" + Truncate(cmd, 90) + "\"";
        }

        /// <summary>
        /// Explicação de um comando observado no sistema. Quando o processo observado é um
        /// cmd.exe intermediário (ex.: cmd.exe /c CALL node ... npm-cli.js run build),
        /// desembrulha para o comando efetivo.
        /// </summary>
        public static string DescribeObserved(string observedCmdLine)
        {
            if (string.IsNullOrEmpty(observedCmdLine)) return "processo encerrado antes de ler a linha de comando";
            string s = observedCmdLine.Trim();

            bool isCmdWrapper = s.IndexOf("cmd.exe", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                (s.IndexOf("/c", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 s.IndexOf("/k", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isCmdWrapper)
            {
                var toks = BatchFile.CommandTokens(s);
                if (toks.Count > 0)
                {
                    string inner = string.Join(" ", toks.ToArray());
                    string key = toks[0];
                    if (key == "npm" || key == "npx" || key == "yarn" || key == "pnpm")
                    {
                        string tail = string.Join(" ", toks.Skip(1).ToArray());
                        if (tail.StartsWith("run", StringComparison.OrdinalIgnoreCase))
                            return "executa o script npm \"" + tail.Substring(3).Trim() + "\"";
                        return "executa o npm (" + inner + ")";
                    }
                    if (key == "node" || key == "node.exe")
                    {
                        // node .../npm-cli.js <args>
                        string tail = string.Join(" ", toks.Skip(1).ToArray());
                        if (tail.Contains("npm-cli.js"))
                            return "executa o npm (" + tail.Substring(tail.IndexOf("npm-cli.js", StringComparison.OrdinalIgnoreCase) + "npm-cli.js".Length).Trim() + ")";
                        return "executa o Node.js (" + inner + ")";
                    }
                    return "executa via cmd: " + inner;
                }
            }

            var t2 = BatchFile.CommandTokens(s);
            string exeKey = t2.Count > 0 ? t2[0] : "";
            string args2 = t2.Count > 1 ? string.Join(" ", t2.Skip(1).ToArray()) : "";
            return Describe(new BatchStep { Command = s, Exe = BatchFile.FirstToken(s), ExeKey = exeKey.Length > 0 ? exeKey : BatchFile.ExeKeyOf(s), });
        }

        public static string Categorize(BatchFile bf)
        {
            // usa o comando completo (com argumentos), nao so o nome do executavel
            var text = string.Join(" ", bf.Steps.ConvertAll(s => (s.Command ?? "") + " " + (s.CallTarget ?? "")).ToArray()).ToLowerInvariant();
            if (text.Contains("docker") || text.Contains("kubectl") || text.Contains("helm")) return "containers";
            if (text.Contains("terraform") || text.Contains("ansible")) return "infraestrutura";
            if (text.Contains("publish") || text.Contains("deploy") || text.Contains("release")) return "publicação";
            if (text.Contains("test") || text.Contains("pytest") || text.Contains("jest") || text.Contains("vitest")) return "testes";
            if (text.Contains("install") || text.Contains("pip ") || text.Contains("npm ci")) return "instalação";
            if (text.Contains("build") || text.Contains("compile") || text.Contains("mvn") || text.Contains("gradle")) return "build";
            if (text.Contains("backup") || text.Contains("robocopy") || text.Contains("xcopy")) return "backup";
            foreach (string sub in new[] { " commit", " push", " pull", " clone", " checkout", " merge", " rebase", " tag ", " stash", " log ", " diff", " fetch" })
                if (text.Contains("git" + sub)) return "controle de versão";
            return "scripts";
        }

        public static string Purpose(BatchFile bf)
        {
            if (!bf.Exists) return "arquivo não encontrado";
            var parts = new List<string>();

            if (bf.Banner.Length > 0) parts.Add("\"" + bf.Banner + "\"");

            string cat = bf.Category;
            if (cat == "containers") parts.Add("opera containers");
            else if (cat == "infraestrutura") parts.Add("provisiona/gerencia infraestrutura");
            else if (cat == "publicação") parts.Add("publica/implanta artefatos");
            else if (cat == "testes") parts.Add("roda testes");
            else if (cat == "instalação") parts.Add("instala dependências");
            else if (cat == "backup") parts.Add("faz backup/cópia de arquivos");
            else if (cat == "controle de versão") parts.Add("opera no Git");
            else if (cat == "build") parts.Add("compila/empacota o projeto");
            else parts.Add("executa um conjunto de comandos");

            string main = DominantAction(bf);
            if (main.Length > 0) parts.Add(main);

            if (bf.Steps.Any(s => s.IsCall && s.CallTarget != null &&
                                   (s.CallTarget.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                                    s.CallTarget.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))))
                parts.Add("e chama outros scripts");

            return string.Join(", ", parts.ToArray());
        }

        private static string DominantAction(BatchFile bf)
        {
            foreach (BatchStep st in bf.Steps)
            {
                if (st.IsComment || st.IsLabel) continue;
                string k = st.ExeKey;
                if (k == "npm" && st.Command.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "o passo principal é \"" + Truncate(st.Command, 50) + "\"";
                if (k == "dotnet" || k == "mvn" || k == "gradle" || k == "cargo" || k == "go")
                    return "o passo principal é \"" + Truncate(st.Command, 50) + "\"";
                if (k == "docker" || k == "kubectl")
                    return "o passo principal é \"" + Truncate(st.Command, 50) + "\"";
                if (k == "robocopy" || k == "xcopy")
                    return "o passo principal é a cópia de arquivos";
            }
            return "";
        }

        public static string ArgsOf(string cmd, string exe)
        {
            if (string.IsNullOrEmpty(cmd)) return "";
            if (!string.IsNullOrEmpty(exe))
            {
                int idx = cmd.IndexOf(exe, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    string rest = cmd.Substring(idx + exe.Length).Trim();
                    if (rest.StartsWith("\"")) { int e = rest.IndexOf('"', 1); if (e > 0) rest = rest.Substring(e + 1).Trim(); }
                    return rest;
                }
            }
            return "";
        }

        private static string FirstWord(string args)
        {
            if (string.IsNullOrEmpty(args)) return "";
            args = args.Trim();
            int sp = args.IndexOfAny(new[] { ' ', '\t' });
            return (sp > 0 ? args.Substring(0, sp) : args).ToLowerInvariant();
        }

        private static string AfterWord(string args, string word)
        {
            if (string.IsNullOrEmpty(args)) return "";
            var parts = args.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
                if (parts[i].Equals(word, StringComparison.OrdinalIgnoreCase))
                    return parts[i + 1].Trim('"');
            return "";
        }

        private static string Quoted(string args)
        {
            int q = args.IndexOf('"');
            if (q < 0) return "";
            int q2 = args.IndexOf('"', q + 1);
            if (q2 < 0) return "";
            return " com a mensagem \"" + args.Substring(q + 1, q2 - q - 1) + "\"";
        }

        public static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}
