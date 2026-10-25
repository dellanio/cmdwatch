# cmdwatch

Monitor de execução de scripts `.cmd`/`.bat` para Windows. Responde a três perguntas,
em tempo real:

1. **Qual `.cmd` foi aberto?**
2. **Quem chamou** (processo pai, com a cadeia de ancestrais até a raiz).
3. **O que ele está fazendo agora** — qual comando está sendo executado, em qual linha
   de qual arquivo, e o que esse comando significa.

```
[13:50:53.272] >> SESSÃO .CMD ABERTA
           script : C:\projetos\glosa\release.cmd
           origem : Explorer do Windows (abertura por duplo clique ou pelo menu de contexto)
           chamador: explorer.exe (PID 10312)
           cadeia : explorer.exe(10312) -> MiniMax Code.exe(1468)
           resumo : "=== Orquestrador de release ===", publica/implanta artefatos, e chama outros scripts
           passos : 9 comandos mapeados no arquivo

[13:50:53.438]   | node.exe (PID 10648)
             em: build.cmd (nivel 1)
             linha 3 do script: call npm run build
             -> roda o script "build" definido no package.json

[13:50:54.116]   | node.exe (PID 16944)
             em: deploy.cmd (nivel 1)
             linha 10 do script: call npm ci
             -> instala as dependências de forma reprodutível a partir do package-lock.json

[13:50:56.716] ## SESSÃO ENCERRADA  release.cmd  (3,5s, 7 comandos executados)
```

---

## Download

Executável pronto para Windows 10/11 — arquivo único, sem DLLs e sem runtime extra
(precisa apenas do .NET Framework 4.8, já presente no sistema):

**[⬇ cmdwatch v1.0.5 — cmdwatch.exe (499 KB)](https://github.com/dellanio/cmdwatch/releases/latest/download/cmdwatch.exe)**

| | |
|---|---|
| Versão | 1.0.5 |
| Tamanho | 499 KB (510.464 bytes) |
| SHA-256 | `3AC7F573210E230701F44B8413972F8419140F5C02FFB011DD3A6B40F86A1532` |
| Página da release | [releases/latest](https://github.com/dellanio/cmdwatch/releases/latest) |

Depois de baixar:

```powershell
cmdwatch.exe gui        # painel grafico do fluxo de execucao
cmdwatch.exe monitor    # saida no terminal
cmdwatch.exe --help
```

> A release também traz o arquivo `cmdwatch.exe.sha256` para conferência do hash.

---

## Painel gráfico (Windows)

O mesmo motor, agora com uma interface ao vivo para acompanhar a execução dos scripts:

![Painel do cmdwatch](docs/dashboard.png)

**Dá para abrir com duplo clique no `cmdwatch.exe`** (sem argumento nenhum) ou por terminal:

```powershell
cmdwatch.exe            # duplo clique no .exe faz o mesmo
cmdwatch.exe gui        # idêntico, só explícito
```

O executável é uma **aplicação Windows** (`/target:winexe`): ao dar duplo clique, **não
aparece nenhuma janela de console** — só o painel. Os modos de terminal (`monitor`,
`analyze`, `trace`) se anexam ao console de quem os chamou, então a saída continua
saindo exatamente onde você espera:

```powershell
cmdwatch.exe monitor    # a saída sai no terminal de onde você chamou
```

O painel é **WinForms nativo dentro do mesmo executável** — sem runtime extra, sem navegador,
sem Electron. Aceita as mesmas opções do `monitor` (`--filter`, `--interval`, `--engine`,
`--log`, `--quiet`).

### As três áreas

| Área | O que mostra |
|---|---|
| **SESSÕES .CMD** | Uma linha por script `.cmd` executado: PID, script, quem chamou, comandos observados, última linha mapeada, **data/hora de início** e **data/hora de término** (`em curso` enquanto roda) |
| **FLUXO DE COMANDOS** | As mensagens do cmdwatch **no mesmo formato do `monitor` do CLI** — cabeçalho, sessão aberta com script/origem/chamador/cadeia/resumo/passos, e cada comando com a linha do script e a explicação |
| **ATIVIDADE .CMD** | Comandos/s (verde) e sessões abertas/s (vermelho) nos últimos 60 s |

### O fluxo fala a língua do terminal

O *FLUXO DE COMANDOS* é a **mesma saída do `cmdwatch monitor`**, linha a linha — o que
você lê no terminal é exatamente o que aparece no painel:

```
[13:50:53.272] >> SESSÃO .CMD ABERTA
           script : C:\projetos\glosa\release.cmd
           origem : Explorer do Windows (abertura por duplo clique ou pelo menu de contexto)
           chamador: explorer.exe (PID 10312) -> C:\WINDOWS\Explorer.EXE
           cadeia : explorer.exe(10312) -> MiniMax Code.exe(1468)
           resumo : "=== Orquestrador de release ===", publica/implanta artefatos, e chama outros scripts
           passos : 9 comandos mapeados no arquivo

[13:50:53.438]   | node.exe (PID 10648)
             em: build.cmd (nivel 1)
             linha 3 do script: call npm run build
             -> roda o script "build" definido no package.json

[13:50:56.716] ## SESSÃO ENCERRADA  release.cmd  (3,5s, 7 comandos executados)
```

O formato é gerado por um módulo só (`src/HumanFormat.cs`), usado tanto pelo CLI
quanto pelo painel — os dois não podem divergir.

As linhas **não quebram** (como no terminal): o fluxo rola na horizontal quando a
mensagem é mais larga que o painel. A rolagem é desenhada à mão, mas no **estilo do
Windows**: polegar arredondado cinza sobre trilho invisível, nas cores do tema do
sistema (claro ou escuro), em vez das barras claras do Windows — que destoariam do
tema escuro do painel.

### Filtrar por sessão

**Clique numa sessão** na área *SESSÕES .CMD* e o *FLUXO DE COMANDOS* passa a mostrar só
aquela sessão (a linha ganha a marca `FOCO`). **Clique de novo** para desmarcar e voltar a
ver todas. `ESC` também limpa o foco.

### Minimizar para a bandeja

O checkbox **“Enviar ao relógio do Windows e continuar monitorando”** no rodapé manda a
janela para a área de notificação e o cmdwatch **continua monitorando em segundo plano**
(ícone na bandeja: clique para restaurar, botão direito para sair).

Atalhos: `ESPAÇO` pausa · `F5` limpa o fluxo · `ESC` limpa o foco / sai.
No fluxo, a roda do mouse rola na vertical e `SHIFT`+roda na horizontal.

### Idioma da interface (PT-BR / EN)

O rodapé tem um seletor segmentado **IDIOMA · PT-BR | EN**. A troca é imediata: títulos das
áreas, cabeçalhos das colunas, rótulos do gráfico, dica do rodapé, menu da bandeja e as
linhas do fluxo são redesenhados no idioma escolhido — sem reiniciar o painel.

A preferência fica salva em `%APPDATA%\cmdwatch\ui.cfg` e volta no próximo uso.

> O que troca de idioma é a **interface**. As *explicações dos comandos* (`npm ci` →
> “instala as dependências…”) vêm do dicionário compartilhado com a CLI e continuam em
> português — são o conteúdo do produto, não o texto da interface.

### Ícone do produto

O cmdwatch tem ícone próprio: na barra de título, na barra de tarefas, na bandeja e no
Explorer. É um `>` ciano com cursor verde e o ponto de “monitorando”, desenhado por
código (`tools\Generate-Icon.ps1`) em 7 resoluções (16 a 256 px) — sem depender de
imagem ou fonte externa.

O ícone do `.exe` fica nos recursos do PE (`/win32icon`); uma versão compacta
(16/24/32/48 px) vai embutida no assembly para a janela e a bandeja carregarem em
runtime.

### Ver o painel em ação

```powershell
demo\gui-demo.cmd
```

O script dispara uma sequência de comandos (incluindo um `call` de outro `.cmd`) para o
painel ter o que mostrar.

> A interface é desenhada à mão em GDI+ (painéis arredondados com gradiente, gráficos com
> halo neon, fundo em cinza escuro e marcações de escala), sem nenhum controle de terceiros.
> Os títulos das seções usam **Bahnschrift SemiCondensed** (a DIN técnica do Windows) em
> maiúscula com espaçamento aberto, gradiente vertical e halo neon — com **Agency FB**,
> Consolas e a matriz de pixels 5×7 como fallbacks.
> O fluxo de comandos também é desenhado à mão (`NeonFlow`), com rolagem própria — sem
> `RichTextBox` e sem as barras de rolagem do sistema, que ficariam claras no tema escuro.

---

## Existe software pronto pra isso?

Sim, existem ferramentas **genéricas** de monitoramento de processo, mas nenhuma entrega
a parte "explicar o que o .cmd está fazendo":

| Ferramenta | O que faz | Limitação pro nosso caso |
|---|---|---|
| **Sysinternals Process Monitor** | Captura criação de processo, linha de comando e árvore de processos em tempo real | Exige admin; não sabe nada sobre `.cmd`; você tem que filtrar e interpretar manualmente |
| **Sysmon (Event ID 1)** | Mesmo, via Event Log, com `ParentCommandLine` | Idem; é telemetria genérica de segurança |
| **Event Log 4688** | Criação de processo auditada pelo Windows | Precisa habilitar "Audit Process Creation"; e **não** mostra qual linha do `.cmd` está rodando |
| **WMI `Win32_ProcessStartTrace`** | Evento de criação de processo via WMI | Exige admin; **não** traz a linha de comando (precisa de segunda consulta) |
| **ETW `Microsoft-Windows-Kernel-Process`** | Precisão de Event Tracing: não perde processo nenhum | Exige admin em Windows 11 |

Ou seja: as ferramentas existentes dão **o processo e a linha de comando**, mas o "que comando do
`.cmd` está rodando" e o "o que isso significa" ficam por conta do humano. **O `cmdwatch`
fecha essa lacuna** — e ainda roda **sem precisar de administrador**.

---

## Como funciona

O `cmdwatch` é um executável único (.NET Framework 4.8, **zero dependências**),
compilado a partir de C# em `src/`.

É uma aplicação **Windows** (`winexe`) com **dois modos**: o painel gráfico roda sem
console nenhum, e os comandos de terminal (`monitor`, `analyze`, `trace`) se anexam ao
console do terminal que os chamou (`AttachConsole` + `AllocConsole`, em
`src/ConsoleHost.cs`). Por isso o mesmo `.exe` serve para as duas coisas sem abrir
janela extra.

### Motor de captura

| Motor | Quando usar | Como funciona |
|---|---|---|
| `polling` (padrão) | Sempre, **sem admin** | `CreateToolhelp32Snapshot` (~8 ms por varredura) + leitura do *command line* direto do PEB do processo |
| `wmi` | Com admin | Evento `Win32_ProcessStartTrace` (detecção instantânea) + WMI para a linha de comando |

O `auto` escolhe `wmi` quando o processo roda elevado e `polling` caso contrário.

Por que não WMI em tudo: uma consulta a `Win32_Process` no WMI leva **170–300 ms** nesta
máquina — scripts `.cmd` curtos terminariam antes de serem vistos. A enumeração via
Toolhelp + PEB leva ~8 ms e pega inclusive processos que viveram menos de 50 ms.

> A leitura do *command line* pelo PEB não usa offsets fixos de `RTL_USER_PROCESS_PARAMETERS`
> (que mudam entre versões do Windows): o código varre a região procurando a `UNICODE_STRING`
> cujo conteúdo começa com o executável do processo.

### Atribuição: quem chamou

Todo `.cmd` roda dentro de um `cmd.exe`. O `cmdwatch` monitora a criação de `cmd.exe`
cuja linha de comando referencia um `.cmd`/`.bat` e reconstrói:

* o **processo pai** (explorer.exe, powershell.exe, Code.exe, outro script, serviço…),
* a **cadeia de ancestrais** até a raiz (até 6 níveis),
* o **usuário** e a sessão do Windows.

### Explicação: o que está rodando

Para cada processo filho do `cmd.exe`, o `cmdwatch`:

1. Lê o *command line* real do processo.
2. Compara com as linhas de todos os scripts alcançáveis a partir do `.cmd` original
   (incluindo os chamados por `call`, recursivamente).
3. Escolhe a melhor combinação (nome do executável + sufixo de argumentos + similaridade).
4. Explica o comando em português, com um dicionário de ~125 ferramentas
   (`npm`, `dotnet`, `docker`, `kubectl`, `git`, `robocopy`, `terraform`, …) e regras
   específicas para os casos compostos (`npm run build`, `git commit -m`, `docker build -t`…).

#### Detalhe importante: `call` roda no mesmo processo

Quando um `.cmd` faz `call outro.cmd`, o Windows **não cria um processo novo** — o script
chamado roda dentro do mesmo `cmd.exe`. Por isso não existe evento observável para
"entrou no build.cmd". O `cmdwatch` resolve isso reconstruindo a **árvore de chamadas**
a partir do conteúdo dos arquivos e procurando em qual script o comando observado
melhor se encaixa:

```
release.cmd  ->  call "%~dp0build.cmd"   ->  build.cmd  ->  call npm run build
```

Por isso a saída mostra `em: build.cmd (nivel 1)` e a linha exata dentro do `build.cmd`.

---

## Uso

### Monitorar o sistema inteiro

```powershell
cmdwatch.exe monitor
```

Opções:

| Opção | Padrão | Descrição |
|---|---|---|
| `--log <arquivo>` | `cmdwatch-log.jsonl` | Log estruturado (JSONL) |
| `--interval <ms>` | `50` | Intervalo de amostragem do motor `polling` |
| `--engine <auto\|polling\|wmi>` | `auto` | Motor de captura |
| `--duration <seg>` | `0` (infinito) | Encerra após N segundos |
| `--filter <texto>` | — | Só reporta `.cmd` cujo caminho contenha o texto |
| `--no-children` | — | Não reporta os comandos internos do script |
| `--json` | — | Saída JSONL no stdout |
| `--quiet` | — | Não escreve arquivo de log |
| `--verbose` | — | Despeja todo processo detectado (diagnóstico) |

### Painel gráfico

```powershell
cmdwatch.exe gui
```

Painel ao vivo do fluxo de execução de scripts — veja [Painel gráfico](#painel-gráfico-windows).
Sem argumento nenhum (`cmdwatch.exe`), ou por duplo clique no arquivo, o painel é o que abre.

### Explicar um script (sem executar)

```powershell
cmdwatch.exe analyze deploy.cmd
```

```
ARQUIVO   : C:\projetos\glosa\deploy.cmd
RESUMO    : "=== Deploy do Portal de Glosa ===", publica/implanta artefatos
CATEGORIA : publicação
TAMANHO   : 23 linhas, 15 passos executáveis
VARIÁVEIS : PATH, NODE_ENV

PASSOS:
  linha    1  @echo off
               -> exibe uma mensagem na tela
  linha   10  call npm ci
               -> instala as dependências de forma reprodutível a partir do package-lock.json
  ...
```

### Executar e capturar a sequência exata de comandos

```powershell
cmdwatch.exe trace deploy.cmd
```

```
[0,00s] echo === Deploy do Portal de Glosa ===
          -> exibe uma mensagem na tela   [echo ← linha 3]
[0,04s] call npm ci
          -> instala as dependências de forma reprodutível a partir do package-lock.json   [npm ← linha 10]
[2,31s] dotnet publish src\Api\Api.csproj -c Release -o dist\api
          -> compila e publica o projeto .NET para o diretório de saída   [dotnet ← linha 16]
```

O `trace` cria uma **cópia instrumentada** do script na mesma pasta (com `echo on` no lugar
de `echo off`), executa a cópia e captura o que o `cmd.exe` realmente executou, linha a
linha, com timestamp. ⚠️ Ele **executa o script** — use em ambiente controlado.

---

## Saída estruturada (JSONL)

Uma linha JSON por evento:

```jsonc
{"ts":"2026-10-25T13:50:53.272","type":"batch_open","cmdPid":5400,
 "script":"C:\\projetos\\glosa\\release.cmd","scriptName":"release.cmd",
 "caller":{"pid":10312,"name":"explorer.exe","cmdLine":"C:\\WINDOWS\\Explorer.EXE",
           "description":"Explorer do Windows (abertura por duplo clique...)"},
 "chain":[{"pid":10312,"name":"explorer.exe"}],
 "analysis":{"purpose":"publica/implanta artefatos","category":"publicação","executableSteps":9}}

{"ts":"2026-10-25T13:50:53.438","type":"batch_step","cmdPid":5400,
 "process":{"pid":10648,"ppid":5400,"name":"node.exe","cmdLine":"...npm-cli.js\" run build"},
 "step":{"line":3,"raw":"call npm run build","meaning":"roda o script \"build\" definido no package.json"}}

{"ts":"2026-10-25T13:50:56.716","type":"batch_close","cmdPid":5400,"durationMs":3444,"stepCount":7}
```

Tipos: `batch_open`, `batch_step`, `batch_close`.

---

## Limitações conhecidas

* **Processos que duram menos de ~50 ms** podem escapar do motor `polling`. Com admin,
  use `--engine wmi` para não perder nenhum.
* **Linha de comando inacessível** em processos de outro usuário ou elevados — o evento é
  registrado, mas sem a linha de comando (o `cmdwatch` não tem permissão para ler o PEB alheio).
* **Comandos internos do `cmd.exe`** (`echo`, `cd`, `if`, `for`, `goto`) não geram processo
  filho, então não são observáveis pelo monitor passivo. Para ver a sequência completa de
  um script específico, use o modo `trace`.
* O `cmdwatch` **não bloqueia nem altera** nada: é somente leitura de estado do sistema.

---

## Compilando

```powershell
.\build.ps1
```

O script usa o compilador Roslyn (NuGet `Microsoft.Net.Compilers.Toolset`). Se ele não
estiver em `.build\roslyn`, o `build.ps1` baixa automaticamente — ou use o
`csc.exe` do .NET Framework:

```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:exe /out:out\cmdwatch.exe `
  /r:System.Management.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  src\Core.cs src\Monitor.cs src\NativeProcess.cs src\Program.cs src\gui\*.cs
```

⚠️ O `csc.exe` do framework é o compilador **C# 5**; o código usa recursos mais novos.
Use o Roslyn (o `build.ps1` resolve isso).

O `build.ps1` também embute o ícone do produto: `/win32icon` para os recursos do `.exe`
(Explorer, barra de tarefas) e `/resource` para os recursos gerenciados (janela e
bandeja). Se `assets\cmdwatch.ico` não existir, o build avisa e sai sem ícone — regere com:

```powershell
.\tools\Generate-Icon.ps1
```

Saída: `out/cmdwatch.exe` — um arquivo, sem DLLs, sem runtime extra. Roda em qualquer
Windows 10/11 com .NET Framework 4.8 (já presente no sistema).

## Estrutura

```
cmdwatch/
├── src/
│   ├── Core.cs          parser de .cmd, dicionário de explicações, JSON
│   ├── Monitor.cs       engines de captura, atribuição, sessões, árvore de chamadas
│   ├── NativeProcess.cs Toolhelp + leitura de PEB
│   ├── HumanFormat.cs   formato das mensagens (CLI e painel usam o mesmo)
│   ├── Lang.cs          dicionário PT-BR/EN e persistência da preferência
│   ├── ConsoleHost.cs   console dos modos de terminal (winexe)
│   ├── Program.cs       CLI, saída humana, modos analyze e trace
│   └── gui/             painel WinForms (dashboard ao vivo)
│       ├── Theme.cs         paleta cinza + matriz de pixels 5×7 + fonte dos títulos
│       ├── ScrollTheme.cs   cores da barra de rolagem conforme o tema do Windows
│       ├── TechTitle.cs     renderizador dos títulos (tracking, gradiente, halo neon)
│       ├── Widgets.cs       painéis, tabela de sessões, gráfico, fluxo e seletor de idioma
│       ├── AppIcon.cs       carrega o ícone embutido (janela e bandeja)
│       ├── DashboardForm.cs layout, estado e consumo dos eventos
│       └── GuiRunner.cs     entrada da GUI e modo escuro da barra de título
├── assets/              ícones do produto (gerados por tools\Generate-Icon.ps1)
├── tools/               geração do ícone e captura de tela do painel
├── demo/                scripts de demonstração
├── docs/dashboard.png   captura do painel
├── build.ps1
└── out/cmdwatch.exe     <- o executável (arquivo único, sem DLLs)
```

## Estendendo

* **Mais comandos conhecidos**: edite o dicionário `Exe` em `src/Core.cs` (~125 entradas).
* **Regras compostas**: o método `Explain.Describe` tem blocos por ferramenta
  (`npm`, `git`, `dotnet`, `docker`, `kubectl`, `terraform`, …).
* **Explicação por LLM**: o ponto de extensão natural é `Explain.DescribeObserved`, que é
  chamado para todo comando não reconhecido. Ele pode ser trocado por uma chamada a um
  modelo (o runtime local já expõe um endpoint compatível com a API da Anthropic em
  `~/.minimax/config.yaml`).
