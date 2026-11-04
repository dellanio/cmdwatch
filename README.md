# cmdwatch

Monitor de execução de scripts `.cmd`/`.bat` para Windows. Responde a quatro perguntas,
em tempo real:

1. **Qual `.cmd` foi aberto?**
2. **Quem chamou** (processo pai, com a cadeia de ancestrais até a raiz).
3. **O que ele está fazendo agora** — qual comando está sendo executado, em qual linha
   de qual arquivo, e o que esse comando significa.
4. **Que janela apareceu na tela** — e **quem está por trás dela** (o autor, achado pelo
   próprio console), quanto tempo ela ficou aberta e se tomou o foco.

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

**[⬇ cmdwatch v1.1.0 — cmdwatch.exe (541 KB)](https://github.com/dellanio/cmdwatch/releases/latest/download/cmdwatch.exe)**

| | |
|---|---|
| Versão | 1.1.0 |
| Tamanho | 541 KB (553.984 bytes) |
| SHA-256 | `20DF02681CD61926082651E2BC270C4CCE4C373BD328AB01F8458ABAB4C6D499` |
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
`--log`, `--quiet`), mais `--fast-interval` e `--no-windows`.

### As quatro áreas

| Área | O que mostra |
|---|---|
| **SESSÕES .CMD** | Uma linha por script `.cmd` executado: PID, script, quem chamou, comandos observados, última linha mapeada, **data/hora de início** e **data/hora de término** (`em curso` enquanto roda) |
| **JANELAS VISÍVEIS** | Uma linha por janela na tela: nome, **autor** (quem está por trás dela), comando do autor, início, **duração**, **foco** e estado (`visível` / `oculta` / `fechada`) |
| **FLUXO DE COMANDOS** | As mensagens do cmdwatch **no mesmo formato do `monitor` do CLI** — cabeçalho, sessão aberta com script/origem/chamador/cadeia/resumo/passos, e cada comando com a linha do script e a explicação |
| **ATIVIDADE .CMD** | Comandos/s (verde) e sessões abertas/s (vermelho) nos últimos 60 s |

### Janelas visíveis (v1.1.0)

O cmdwatch não lista mais só processos: ele **observa as janelas que existem na tela** e
descobre **quem está por trás de cada uma**. É o que faltava para separar o incomoda do
ruído — um `cmd.exe` oculto e um `cmd.exe` que tomou a tela deixarão de ser a mesma coisa.

O que é registrado para cada janela:

| Dado | Como é obtido |
|---|---|
| **Janela** | título e classe da janela (`SetWinEventHook`, eventos `OBJECT_CREATE`/`SHOW`/`HIDE` + `SYSTEM_FOREGROUND`) |
| **Autor** | quem está rodando **dentro daquele console** — não a árvore de processos |
| **Comando** | a linha de comando do autor |
| **Início / Duração** | quando a janela apareceu e quanto tempo ela ficou de pé |
| **Foco** | quantas vezes a janela veio para frente |
| **Estado** | `visível`, `oculta` ou `fechada` |

**Quem e o autor.** No Windows Terminal existem **duas** janelas top-level por aba: a
`CASCADIA_HOSTING_WINDOW_CLASS` (titulo, dono `WindowsTerminal.exe`) e a
`PseudoConsoleWindow` (dono = processo real da sessao). O cmdwatch **associa** as duas: a
Pseudo fica auxiliar (nao conta em dobro) e a Cascadia herda o autor, o comando e a
**cadeia de ancestrais** do processo real. Quando a associacao nao da, ou a janela nao e
de terminal, o autor vem da arvore de processos do dono (com validacao de horario de
criacao contra reuso de PID) e, se ainda houver, da pergunta ao console
(`AttachConsole` + `GetConsoleProcessList`) - util sobretudo no **painel**. No modo CLI o
cmdwatch ja esta anexado a um console, entao essa pergunta fica desligada
(`GetConsoleProcessList != 0`) e o autor vem da arvore / da Pseudo.


Os eventos de janela entram **no mesmo fluxo do CLI** (e no mesmo log JSONL):

```
[20:50:18.395] .. JANELA ESCONDIDA  C:\Windows\system32\cmd.exe  (13,1s)
[20:50:18.396] ## JANELA FECHADA  C:\Windows\system32\cmd.exe  (13,1s)
```

> A janela do próprio cmdwatch **não entra** na lista dele.

### Correções da v1.1.1

Quatro defeitos achados no teste de campo da v1.1.0, todos medidos antes de corrigir:

| Defeito | Causa | Correção |
|---|---|---|
| **Processo elevado sumia do log** | a chamada à consulta limitada usava um nome de função inexistente no `ntdll.dll`; o erro era engolido por um `catch` e a leitura caía no PEB — que processo elevado bloqueia | `EntryPoint` correto: a consulta passa a ler a linha de comando de processo elevado (medido: 6 de 6 processos de integridade alta lidos) |
| **A janela mostrava o programa, não quem o chamou** | o autor era escolhido como o cliente de console **mais recente** (o `git.exe` transitório) | o autor passa a ser o **ancestral mais próximo dentro do console, sem contar a raiz**: num console `cmd.exe → python.exe → git.exe`, o autor é o `python.exe` (quem chamou), e o `git.exe` fica registrado como o *programa* em execução |
| **Janela escondida virava "fechada"** | a varredura deduzia "fechada" de "não apareceu na varredura" | só é fechada quando o Windows confirma que a janela **não existe mais**; escondida ela é *oculta*, e ao reaparecer continua a mesma janela |
| **`sessionId` saindo como 59133952** | o campo era lido no offset errado (`0x68` = `UniqueProcessKey`) | corrigido para `0x64`, medido nos 859 registros da máquina |

E uma melhoria de captura: a **janela de console oculta** (o `cmd.exe` que roda com a janela
escondida) dispara evento de criação do mesmo jeito que a visível — mas o filtro de
visibilidade a descartava. Agora ela **acorda o motor** e dispara uma consulta dirigida ao
`cmd.exe`, que é o dono da janela de console. É o que faz um script oculto com console
aparecer mesmo com a máquina parada.

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

### Chamadas entre scripts (v1.0.7)

Quando um script chama outro, o cmdwatch registra a **chamada** com o script chamado, o
nível e a linha exata de onde ela saiu:

```
[19:54:34.496]   +-> t3-interno.cmd (nivel 2)  (chamado na linha 3 de t2-chama-call.cmd)
[19:54:34.496]   | where.exe (PID 35476)
             em: t3-interno.cmd (nivel 2)
             linha 3 do script: where hostname
             -> executa "where hostname"
```

São reconhecidas todas as formas de chamar um script:

| Forma | Como aparece |
|---|---|
| `cmd /c script.cmd` | chamada de nível 1, na linha do chamador |
| `cmd /c "script.cmd"` | idem (com aspas) |
| `cmd /c call script.cmd` | idem |
| `cmd /k script.cmd` | idem |
| `start "" script.cmd` | idem |
| `cmd /c "pasta com espacos\script.cmd"` | idem — caminho com espaços funciona |
| `cmd /c script.cmd ARG1 ARG2` | idem, com os argumentos mapeados |
| `call script.cmd` (mesmo processo) | chamada de nível+1, dentro do script que chamou |
| `.bat` | idem |
| duplo clique no Explorer / PowerShell | sessão própria |

O título do painel *SESSÕES .CMD* mostra o total de chamadas junto com as sessões
(`1 SESSOES · 4 CHAMADAS`).

### Filtrar por sessão

**Clique numa sessão** na área *SESSÕES .CMD* e o *FLUXO DE COMANDOS* passa a mostrar só
aquela sessão (a linha ganha a marca `FOCO`). **Clique de novo** para desmarcar e voltar a
ver todas. `ESC` também limpa o foco.

### Encerrar um script (v1.0.9)

**Botão direito numa sessão** da área *SESSÕES .CMD* abre o menu de contexto, com a opção
**“Fechar”** para **encerrar o script em execução**:

![Menu de contexto da sessão](docs/menu-contexto.png)

O item mostra qual script é o alvo (`demo-longo.cmd  (PID 22212)`), e a escolha pede
confirmação antes de encerrar qualquer coisa:

![Confirmação do encerramento](docs/confirmacao.png)

O que acontece ao confirmar:

* o `cmd.exe` que hospeda o script é **encerrado** — junto com os **processos filhos** que
  ele deixou rodando (o `ping.exe`, o `conhost.exe`…); sem isso, um script travado num
  `ping -n 9999` continuaria com o filho vivo depois do `cmd.exe` morrer;
* o cmdwatch **continua monitorando**: a sessão aparece com o horário de *FIM* preenchido e
  o fluxo registra o `SESSÃO ENCERRADA`;
* o rodapé confirma em verde: `Script encerrado: demo-longo.cmd`.

Em sessão já encerrada o item *Fechar* fica esmaecido (e o `ESC` cancela sem encerrar
nada). Encerrar um processo que roda como administrador exige rodar o cmdwatch como
administrador — o painel avisa o motivo caso não consiga.

### Sessões que já estavam rodando (v1.0.10)

O painel passa a mostrar **os scripts `.cmd` que já estavam em execução quando ele abriu** —
inclusive o horário real de início. Antes, esses scripts eram descartados em silêncio.

E o script chamado por **caminho relativo** (`cmd /c build.cmd`) passou a ser resolvido:
o cmdwatch usa agora o **diretório de trabalho do próprio `cmd.exe`** (lido do PEB), e não
o diretório de onde o cmdwatch foi aberto.

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

### Versão no rodapé (v1.0.8)

O canto inferior direito do painel mostra a versão do cmdwatch em uso (`cmdwatch 1.0.8`),
para dar para conferir de imediato qual build está rodando — útil quando há mais de uma
cópia do `.exe` em circulação. É a mesma constante usada pela CLI, então o rodapé e a
saída de `cmdwatch --version` nunca divergem entre si.

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
| `polling` (padrão) | Sempre, **sem admin** | Retrato do sistema via `NtQuerySystemInformation` (13–19 ms, buffer reaproveitado) + leitura do *command line* por `NtQueryInformationProcess` |
| `wmi` | Com admin | Evento `Win32_ProcessStartTrace` (detecção instantânea) + WMI para a linha de comando |

O `auto` escolhe `wmi` quando o processo roda elevado e `polling` caso contrário.

Por que não WMI em tudo: uma consulta a `Win32_Process` no WMI leva **170–300 ms** nesta
máquina — scripts `.cmd` curtos terminariam antes de serem vistos. E o próprio
`Win32_ProcessStartTrace` **exige admin** (sem elevação ele responde "Acesso negado"), então
o modo automático continua sendo `polling`.

> A leitura do *command line* pelo PEB não usa offsets fixos de `RTL_USER_PROCESS_PARAMETERS`
> (que mudam entre versões do Windows): o código varre a região procurando a `UNICODE_STRING`
> cujo conteúdo começa com o executável do processo.

#### Linha de comando sem permissão de memória (v1.1.0)

Antes, o cmdwatch lia a linha de comando **dentro da memória do processo** (`PROCESS_VM_READ`).
Um script que roda **elevado** barra essa leitura — e o script sumia do log. Foi o que
acontecia com a tarefa `Higiene-Remocao`, que abria janela e nunca aparecia.

Agora a leitura principal usa a informação do **kernel**, que pede apenas
`PROCESS_QUERY_LIMITED_INFORMATION`:

| | Antes | Agora |
|---|---|---|
| Consulta | memória do processo (`PROCESS_VM_READ`) | `NtQueryInformationProcess` classe **60** (`ProcessCommandLineInformation`) |
| Permissão | lê a memória do processo | `PROCESS_QUERY_LIMITED_INFORMATION` |
| Processo elevado | **invisível** | **visível**, com a linha de comando |

Medido nesta máquina: **628 processos lidos com sucesso** contra 177 falhas — e as falhas
são exatamente os processos protegidos do SYSTEM. O PEB continua sendo lido só para o
diretório de trabalho e a imagem do processo (o que um processo elevado não expõe).

#### Intervalo adaptativo (v1.1.0)

O retrato de processos saía a cada **50 ms** e consumia ~29% de um núcleo por instância. Agora
o cmdwatch alterna entre dois regimes:

| Regime | Quando | Intervalo |
|---|---|---|
| **rápido** | nos 10 s seguintes a qualquer atividade | 50 ms |
| **lento** | parado | **500 ms** |

E, quando **aparece uma janela**, o motor acorda na hora: o evento de janela dispara uma
consulta dirigida ao processo dono da janela (em vez de um retrato global) e o motor retoma
imediatamente.

**Custo medido nesta máquina (~600–870 processos):**

| Situação | CPU de um núcleo |
|---|---|
| **parado** (regime lento, 500 ms) | **~2–3%** |
| **com atividade** (regime rápido, 50 ms) | **~14–18%** |
| painel, com atividade | ~8–14% |

O número depende do regime — os "3%" da v1.1.0 eram a medição de **máquina parada**, e a
nota de release não dizia isso. Com scripts rodando o motor fica a 50 ms e o custo sobe para
~14–18%: o retrato do sistema custa **7–20 ms por chamada** (o kernel varre todos os
processos), e é o mesmo motor do v1.0.10 — que gastava ~29% porque varria mais caro
(Toolhelp, ~34 ms) e sem parar.

O que mudou em relação ao v1.0.10 foi o **preço** do retrato e a **razão** para acelerar, não
a capacidade de ver scripts: o retrato ficou mais barato (`NtQuerySystemInformation` com
buffer reaproveitado) e o motor só acelera quando há motivo.

> **Escopo da regressão (corrigido):** a bateria de 11 formas de chamada (12 chamadas,
> resultado idêntico ao da v1.0.10) cobria scripts **visíveis e longos**. Ela **não** cobria
> script `.cmd` **oculto e curto**, que no regime lento (500 ms) escapa — ver
> [Limitações conhecidas](#limitações-conhecidas).

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
| `--interval <ms>` | `500` | Intervalo de amostragem do motor `polling` **parado** |
| `--fast-interval <ms>` | `50` | Intervalo **com atividade** (nos 10 s seguintes a qualquer evento) |
| `--engine <auto\|polling\|wmi>` | `auto` | Motor de captura |
| `--duration <seg>` | `0` (infinito) | Encerra após N segundos |
| `--filter <texto>` | — | Só reporta `.cmd` cujo caminho contenha o texto |
| `--no-children` | — | Não reporta os comandos internos do script |
| `--no-windows` | — | Só scripts: não observa as janelas da tela |
| `--json` | — | Saída JSONL no stdout |
| `--quiet` | — | Não escreve arquivo de log |
| `--verbose` | — | Despeja todo processo detectado (diagnóstico) |

O monitor também observa as **janelas**: cada janela que abre, ganha foco ou fecha vira um
evento (`window_open` / `window_focus` / `window_hide` / `window_close`), com a duração e o
autor — quem está por trás da janela, perguntando ao console.

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

Tipos: `batch_open`, `batch_step`, `batch_close`, `batch_call`.

Os eventos de janela usam o mesmo formato, com um bloco `window`:

```jsonc
{"ts":"2026-11-03T20:50:05.120","type":"window_open",
 "window":{"hwnd":"0x1F0A2C","title":"C:\\Windows\\system32\\cmd.exe","class":"ConsoleWindowClass",
           "ownerPid":51740,"owner":"PING.EXE","ownerCmdLine":"ping -n 8 127.0.0.1",
           "authorPid":24284,"author":"cmd.exe","authorCmdLine":"cmd /c \"...\\gui-demo.cmd\"",
           "byConsole":true,"consoleClients":2,
           "firstSeen":"2026-11-03T20:50:05.120","durationMs":13150,"focusCount":0,
           "visible":true,"closed":false}}
```

Tipos de janela: `window_open`, `window_focus`, `window_hide`, `window_close`.

> `byConsole` diz se o autor foi achado **perguntando ao console** ou pela árvore / Pseudo.
> `chain` no `window_open` lista os ancestrais do autor (como no `batch_open`), inclusive processos já encerrados (cache recente).

---

## Limitações conhecidas

* **Scripts `.cmd` ocultos e curtos (~25 ms) com a máquina parada** podem escapar do motor
  `polling`: no intervalo lento (500 ms) a taxa medida ficou em **0/6 a 2/8**; com
  `--interval 50` sobe (tipicamente 4/4 a 8/8), ao custo de mais CPU. O evento WMI
  `__InstanceCreationEvent WITHIN …` **também é polling** e perde processos mais curtos
  que o WITHIN; `Win32_ProcessStartTrace` (motor `wmi`) exige administrador. Sem admin,
  captura de scripts ocultos sub-intervalo **não é garantida**.
* **Linha de comando inacessível** apenas em processos **protegidos** (SYSTEM) — a consulta
  limitada lê a linha de comando de processo elevado sem qualquer elevação do cmdwatch.
  Para processos de outro usuário, o evento é registrado, mas sem a linha de comando.
* **A atribuição pelo console só funciona no painel.** No modo CLI o cmdwatch já está
  anexado a um console, e `AttachConsole` só funciona em processo **sem** console próprio —
  então o autor vem da árvore de processos. Com `--no-windows` a observação de janelas é
  desligada por completo.
* **Comandos internos do `cmd.exe`** (`echo`, `cd`, `if`, `for`, `goto`) não geram processo
  filho, então não são observáveis pelo monitor passivo. Para ver a sequência completa de
  um script específico, use o modo `trace`.
* **`call` dentro do mesmo processo**: o Windows não cria processo novo, então o cmdwatch
  reconstrói essa chamada lendo o texto do script que chamou — se o script chamado estiver
  atrás de uma variável que o cmdwatch não consegue resolver, a chamada aparece sem o
  nível exato.
* **`cmd.exe` que não é script** fica de fora das *sessões*: o cmdwatch monitora scripts
  `.cmd`/`.bat`. Um `cmd /c npx …` ou `cmd /c vite …` (servidor de desenvolvimento, MCP,
  `npx`…) **não vira sessão** — mas a **janela** dele aparece na lista *JANELAS VISÍVEIS*,
  com o autor e a duração.
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
  src\*.cs src\gui\*.cs
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
│   ├── NativeProcess.cs Toolhelp, retrato rápido do sistema e leitura de PEB/linha de comando
│   ├── WindowWatcher.cs observação das janelas da tela (eventos, duração, foco, autor)
│   ├── HumanFormat.cs   formato das mensagens (CLI e painel usam o mesmo)
│   ├── Lang.cs          dicionário PT-BR/EN e persistência da preferência
│   ├── ConsoleHost.cs   console dos modos de terminal (winexe)
│   ├── ProcessControl.cs encerramento de processos (e da árvore de filhos)
│   ├── Program.cs       CLI, saída humana, modos analyze e trace
│   └── gui/             painel WinForms (dashboard ao vivo)
│       ├── Theme.cs         paleta cinza + matriz de pixels 5×7 + fonte dos títulos
│       ├── ScrollTheme.cs   cores da barra de rolagem conforme o tema do Windows
│       ├── TechTitle.cs     renderizador dos títulos (tracking, gradiente, halo neon)
│       ├── Widgets.cs       painéis, tabela de sessões, tabela de janelas, gráfico, fluxo e seletor de idioma
│       ├── NeonMenu.cs      menu de contexto no tema (renderer do ToolStrip)
│       ├── NeonDialog.cs    diálogo de confirmação no tema
│       ├── AppIcon.cs       carrega o ícone embutido (janela e bandeja)
│       ├── DashboardForm.cs layout, estado e consumo dos eventos
│       └── GuiRunner.cs     entrada da GUI e modo escuro da barra de título
├── assets/              ícones do produto (gerados por tools\Generate-Icon.ps1)
├── tools/               geração do ícone, captura de tela e testes do painel
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
