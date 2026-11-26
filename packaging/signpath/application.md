# Rascunho da aplicação — SignPath Foundation

Conteúdo pronto para preencher o formulário de <https://signpath.org/apply>.
As respostas vão em inglês (é o idioma da Fundação); as notas de contexto estão
em português.

> **Quem submete:** o formulário exige a conta do **dono do repositório**
> (`dellanio`). É o passo que o agente não dá sozinho — ele precisa da sua
> identidade no GitHub e da sua assinatura nos termos da Fundação.

## Dados do projeto

| Campo | Resposta |
|---|---|
| Repository | https://github.com/dellanio/cmdwatch |
| Project name | `cmdwatch` |
| Homepage | https://github.com/dellanio/cmdwatch |
| Download / release page | https://github.com/dellanio/cmdwatch/releases/latest |
| Latest release | https://github.com/dellanio/cmdwatch/releases/tag/v1.3.2 |
| License | MIT (`LICENSE` no repositório) |
| Platform | Windows (x64, .NET Framework 4.8) |
| Product name in signed files | `cmdwatch` |
| Package managers | winget (PR #489709) e Scoop (bucket `dellanio/scoop-bucket`) |

## Descrição curta (para o formulário)

> cmdwatch is a single-file Windows program that shows what a `.cmd`/`.bat`
> script actually does: it traces the processes each script step spawns, the
> command line each one runs, and the windows they open, in real time and in a
> static-analysis mode that explains a script without running it. It is written
> in C# on the .NET Framework with WinForms and custom GDI+ drawing, has no
> third-party runtime dependency, makes no network requests, and ships as a
> portable executable (no installer, no system changes).

## Respostas ponto a ponto (termos da Fundação)

| Requisito dos termos | Situação do cmdwatch | Como foi verificado |
|---|---|---|
| Licença OSI, sem dual-licensing | MIT,License única | arquivo `LICENSE` |
| Sem código proprietário | Só código próprio; dependências: apenas System Libraries do .NET Framework | `src/` não referencia nenhuma biblioteca de terceiros |
| Sem malware | Ferramenta observadora de execução de scripts; nada é baixado, executado ou instalado em nome do usuário | inspeção do código |
| Projeto mantido | Corrigido e publicado ativamente (v1.3.2 em 2026-11-26) | histórico de commits e releases |
| **Já publicado na forma a ser assinada** | Binário único `cmdwatch.exe` em release | v1.3.2 |
| Funcionalidade documentada | README com descrição, capturas e modos de uso | `README.md` / `README.pt-BR.md` |
| **Política de assinatura na home page** | Seção “Code signing policy” com a frase exigida, papéis e privacidade | README (en e pt-BR) |
| **Metadata do arquivo** | `ProductName=cmdwatch`, `ProductVersion=<versão>` gravados pelo build e verificados no CI | `build.ps1` + `.github/workflows/build.yml` |
| **Build verificável** | Build público em runner hospedado do GitHub; binário assinado sai do mesmo pipeline | `.github/workflows/release.yml` |
| **Aprovação manual por release** | Um Approver aprova cada pedido no painel do SignPath | `release-signing` policy |
| MFA para todos do time | 2FA do GitHub ativado na conta `dellanio` (único membro) | confirmar antes de submeter |
| Sem transferência de dados | Zero código de rede em `src/`; só grava log local e `ui.cfg` | `grep` por `HttpWebRequest|WebClient|Socket|HttpClient` → 0 ocorrências |
| Sem alteração de sistema | Não escreve no registro, não instala serviço, não cria tarefa agendada | `grep` por `Registry.SetValue|ServiceController|schtasks` → 0 ocorrências de escrita |
| Desinstalação | Portable: apagar `cmdwatch.exe` (e, se quiser, `%APPDATA%\cmdwatch`) | seção de desinstalação no README |

## Papéis do time (exigido pela Fundação)

| Papel | Membro |
|---|---|
| Authors — commitam sem revisão adicional | [dellanio](https://github.com/dellanio) (único mantenedor e dono do repositório) |
| Reviewers — revisam mudanças de quem não é committer | [dellanio](https://github.com/dellanio) |
| Approvers — aprovam cada pedido de assinatura | [dellanio](https://github.com/dellanio) |

## Ponto honesto para declarar (cláusula “no hacking tools”)

O cmdwatch **observa** a execução de scripts: mostra processos filhos, linha de
comando e janelas abertas. Ele **não** varre o sistema em busca de
vulnerabilidades, não testa portas, não tenta burlar controles de acesso e não
identifica fraquezas — é uma ferramenta de observabilidade para quem
desenvolve e depura scripts `.cmd`/`.bat`. É justo registrar isso na aplicação
para que a Fundação avalie com o contexto certo.

## Risco conhecido: reputação pública

O cmdwatch foi criado em **2026-10-25** e está em ~1 mês de vida. Hoje o
repositório tem 0 stars, 0 forks e 1 contribuidor. A Fundação já recusou um
projeto parecido (gphotos-takeout-organizer, 22/09/2026) por falta de
"established public trust" (stars, forks, contribuidores, artigos externos,
discussões independentes).

Duas leituras possíveis:

1. **Aplicar agora** — a Fundação convida a re candidatar depois que o projeto
   tiver reconhecimento mais amplo. O custo é um formulário perdido.
2. **Aplicar depois** — publicar mais, documentar, receber issues de terceiros
   e só então candidatar, com chance maior de aprovação na primeira tentativa.

O repositório já está **pronto para a aplicação** de qualquer jeito (política de
assinatura publicada, metadata no binário, CI verificável), então a decisão é
sobre **quando** submeter, não sobre o quê.

## Passos manuais depois da aprovação

1. Instalar o app do SignPath no repositório: <https://github.com/apps/signpath>
2. No painel do SignPath: criar o projeto, a signing policy
   (`release-signing`, com aprovação manual) e a artifact configuration —
   prontos em [`signing-policy.md`](signing-policy.md) e
   [`artifact-configuration.xml`](artifact-configuration.xml).
3. Conectar o Trusted Build System “GitHub.com” ao projeto.
4. No GitHub, criar o secret `SIGNPATH_API_TOKEN` e as variáveis
   `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG` e
   `SIGNPATH_SIGNING_POLICY_SLUG`.
5. Publicar uma tag (`v1.3.3`) → o CI compila, assina e publica a release
   com o `.exe` assinado.
