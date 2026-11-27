# Microsoft Store listing — CMDWatcher

Texto para colar no Partner Center. Os valores marcados com [PREENCHER]
dependem de você ou da conta.

## Dados gerais

| Campo | Valor |
|---|---|
| Nome do produto | CMDWatcher |
| Categoria | Developer tools |
| Preço | US$ 4,99 (faixa de preço mais próxima de US$ 5; confirme na tabela do Partner Center) |
| Teste gratuito | Nenhum. A versão portátil gratuita continua no GitHub. |
| Mercados | Todos os disponíveis (sugestão) |
| Idiomas da ficha | English (United States) e Português (Brasil) |
| URL da política de privacidade | https://github.com/dellanio/cmdwatch/blob/main/docs/PRIVACY.md |
| Site | https://github.com/dellanio/cmdwatch |
| Suporte | https://github.com/dellanio/cmdwatch/issues |
| Faixa etária (IARC) | Sem violência, sem conteúdo adulto, sem compras, sem chat. Responda "não" a tudo; resultado esperado: 3+ / Livre. |
| Recursos restritos | `runFullTrust` (necessário para observar processos e janelas de outros aplicativos). Justificativa abaixo. |

## English (United States)

**Short description (max 100 chars)**
See every .cmd/.bat session, command and window on your PC in real time.

**Description**
CMDWatcher is a real-time monitor for what runs on your Windows PC. It shows
which scripts are running, which commands they launch, which windows open and
who opened them, and how much CPU each process is using. All in one dashboard.

- Live view of .cmd/.bat sessions, with the process chain behind each one
- Command stream with timestamps, windows opened and closed, and who launched them
- CPU and memory per process, with a live activity chart
- Safe end-session: confirm before closing a session or a window
- Secret masking: tokens, passwords and API keys are hidden by default
- Pin on top, 40x40 grid, export or open the log, minimize to the tray
- English and Portuguese (Brazil)
- Runs as a standard user. No administrator rights needed.

Private by design: CMDWatcher has no network access, no telemetry and no
account. Everything stays on your computer.

**What's new in this version**
Redesigned industrial interface with a collapsible sidebar, a global search bar
and a stat strip. New app icon. English is now the default language.

**Search terms (up to 7)**
process monitor, cmd, batch, command line, scripts, task, developer

**Notes for certification**
CMDWatcher declares the `runFullTrust` capability because it reads the list of
running processes, their command lines and window titles through standard
Win32 APIs (CreateToolhelp32Snapshot, NtQuerySystemInformation, EnumWindows)
and receives window events through SetWinEventHook in out-of-context mode.
It does not inject code into, or modify the memory of, other processes. It can end a process
only when the user explicitly confirms it in the app. It makes no network
connections. Run it, open a Command Prompt and start a .cmd file to see the
session appear.

## Português (Brasil)

**Descrição curta (máx. 100 caracteres)**
Veja em tempo real cada sessão .cmd/.bat, comando e janela do seu PC.

**Descrição**
O CMDWatcher é um monitor em tempo real do que roda no seu PC com Windows.
Mostra quais scripts estão em execução, quais comandos eles disparam, quais
janelas abrem e quem as abriu, e quanto de CPU cada processo usa. Tudo em um
só painel.

- Visão ao vivo das sessões .cmd/.bat, com a cadeia de processos de cada uma
- Fluxo de comandos com horário, janelas abertas e fechadas e quem as iniciou
- CPU e memória por processo, com gráfico de atividade ao vivo
- Encerramento seguro: pede confirmação antes de fechar uma sessão ou janela
- Mascaramento de segredos: tokens, senhas e chaves de API ficam ocultos por padrão
- Fixar no topo, grade 40x40, exportar ou abrir o log, minimizar para a bandeja
- Inglês e português (Brasil)
- Roda como usuário comum. Não precisa de administrador.

Privado por princípio: o CMDWatcher não acessa a rede, não tem telemetria e
não pede conta. Tudo fica no seu computador.

**Novidades desta versão**
Interface industrial redesenhada, com barra lateral retrátil, busca global e
faixa de indicadores. Novo ícone do app. O inglês agora é o idioma padrão.

**Termos de pesquisa**
monitor de processos, cmd, batch, linha de comando, scripts, tarefas, desenvolvedor

## Imagens da loja

Capturas (1440x880) já existem em `docs/`: `dashboard.png`, `processos-cpu.png`,
`ocultar-windows.png`, `recolhido.png`, `menu-contexto.png`, `confirmacao.png`.
A Store pede ao menos 1 captura; use de 4 a 6, em inglês para a ficha en-us.
As atuais estão em português: gere versões em inglês com `tools/captura-readme.ps1`
trocando `lang=pt-br` por `lang=en` antes de enviar. [PREENCHER]
Ícone da loja (1:1, 300x300 ou maior): `assets/icon-preview.png` (512x512).
