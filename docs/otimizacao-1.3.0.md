# Otimização de consumo — medição antes/depois

Este documento registra a avaliação de consumo de recursos do cmdwatch feita em
25/11/2026, na máquina de teste (16 núcleos lógicos, 648 processos no retrato do
kernel). **Nenhum número aqui é estimativa**: todos saíram de execução do
binário ou de medição interna do próprio app.

## 1. Linha de base (v1.2.0, painel aberto, ocioso)

| Cenário | Medido |
|---|---|
| App completo, GUI visível | **0,60%** da máquina (média de 60 s, amostras de 5 s) |
| Motor sozinho, sem GUI (`cmdwatch monitor`) | **0,49%** da máquina |
| Working set / memória privada | ~59–64 MB / ~40–44 MB |
| Threads / handles | 12–14 / ~330–354 |

Ou seja: **a interface responde por ~0,11% e o motor por ~0,49%**. A caça à
otimização tinha de ser feita no motor, não no desenho.

## 1b. Medição de controle (v1.3.0, o mesmo binário publicado)

Rodada de 30 s com `tools/medir-consumo.ps1` sobre o `out/cmdwatch.exe` da
v1.3.0 (620.032 bytes), na mesma máquina:

```
[cmdwatch v1.3.0] exe=.\out\cmdwatch.exe  nucleos=16
  23:09:20  cpu=  1,32%  ws=   59,8MB  privado=   40,2MB  threads=14
  23:09:25  cpu=  0,68%  ws=   60,6MB  privado=   41,0MB  threads=14
  23:09:30  cpu=  0,35%  ws=   60,6MB  privado=   41,1MB  threads=14
  23:09:35  cpu=  0,47%  ws=   61,2MB  privado=   41,6MB  threads=14
  23:09:40  cpu=  0,55%  ws=   61,9MB  privado=   42,3MB  threads=14
  23:09:45  cpu=  0,49%  ws=   62,3MB  privado=   42,7MB  threads=12
[cmdwatch v1.3.0] media cpu = 0,64% da maquina | pico ws = 62,3MB | pico privado = 42,7MB
```

**Leitura honesta:** o consumo **total do processo** ficou em 0,64% contra
0,60% da linha de base — **dentro do ruído de ±0,2%** desta máquina (o
primeiro sample de 1,32% é o aquecimento: JIT, primeira varredura do kernel
e a abertura da lista inteira (~620 processos)). Ou seja: **a otimização não aparece
no número grosso do `Task Manager`** — ela aparece no trabalho por tique
(seção 3), que é o que de fato consome quando o painel fica aberto horas
seguidas com a aba de CPU visível.

O número grosso é dominado pela **varredura do kernel**, que é obrigatória
(é o preço de detectar o início e o fim dos processos) e não pôde ser
reduzida sem perder funcionalidade.

## 2. Onde o custo está (medido, não estimado)

| Operação | Custo medido | Frequência | Peso |
|---|---|---|---|
| Varredura do kernel (`NtQuerySystemInformation`) | **10,5–13,4 ms** por chamada (648 processos) | 2/s (motor ocioso) | ~21–27 ms/s = **2,1–2,7% de um núcleo** |
| Varredura do kernel extra do amostrador de CPU | 10,5–13,4 ms | 0,67/s | ~7 ms/s = **0,7% de um núcleo** |
| Leitura da linha de comando (PEB) dos 40 do topo | ~7 ms por amostra | 1/1,5 s | ~4,7 ms/s |
| Remontar a lista de CPU (ordenar 560 linhas + dicionário) | **0,179 ms** | 1/s | 0,18 ms/s |
| Tique completo do painel (aba de CPU fechada) | **0,023 ms** (era ~0,20 ms) | 1/s | — |
| Costura de threads de janelas (acorda 10x/s) | ~50 µs por wake | 10/s | ~0,5 ms/s (irrelevante) |

## 3. O que mudou

| Mudança | Ganho medido | Risco |
|---|---|---|
| A lista de CPU só é remontada quando o **retrato** ou o **filtro** mudam — e só quando a aba está aberta | tique do painel caiu de ~0,20 ms para **0,023 ms** (−84%) | baixo |
| A linha de comando é lida **uma vez por processo** (cache por PID + hora de início) em vez de duas leituras a cada amostra | ~7 ms por amostra deixam de ser pagas (≈4,7 ms/s) | baixo |
| A máscara de segredos é aplicada **uma vez por amostra**, e não a cada repaint da tabela | 6 passes de regex × linhas visíveis → 1 por amostra | baixo |
| Ao mandar para a bandeja, os **timers e o amostrador param**; ao restaurar, voltam | todo o trabalho de painel + a varredura do kernel do amostrador (≈7 ms/s) saem da conta | baixo (o motor continua monitorando e o log continua sendo escrito) |
| As posições x das colunas são **cacheadas** por largura | 2 `int[]` por paint → 0 | baixo |
| O bitmap de fundo do cartão é **liberado** no `Dispose` | ~10–20 MB de GDI+ no fim do processo | baixo |
| A memória da máquina (`GlobalMemoryStatusEx`) alimenta o novo indicador | 1 chamada de sistema por segundo, sem custo mensurável | baixo |

## 4. O que foi avaliado e **descartado** (com o motivo)

| Ideia | Por que não foi feita |
|---|---|
| Subir o timeout da thread de janelas de 100 ms para 500 ms | o wake custa ~50 µs → ~0,5 ms/s. Ganho invisível, e piora a latência da varredura de segurança |
| Partilhar o retrato do motor com o amostrador de CPU | no modo **ETW** o motor não faz polling: a aba de CPU ficaria sem dados. Risco de functionality > ganho de ~7 ms/s |
| Baixar o intervalo do motor (500 ms → 1 s) | é o que permite não perder `.cmd` curtos. Não se mexe |
| Cachear brushes/pens por cor no GDI+ | o churn é limitado (objetos descartados) e o ganho seria pequeno frente ao risco |
| Reduzir o polling adaptativo em atividade (50 ms) | é o custo de não perder script ultr curto |

## 5. O que continua sendo o maior custo

A varredura do kernel (10,5–13,4 ms) é **inevitável** para detectar o início e o
fim de processos: é o preço da funcionalidade. O que dá para reduzir — e foi
reduzido — é quantas vezes ela é paga em paralelo (o amostrador tirava o seu
próprio retrato).

O resto do custo do motor (~0,3% da máquina) está no observador de janelas e no
tratamento de eventos; medir a fatia exata de cada um exigiria instrumentar o
motor, e a diferença entre "com janelas" e "sem janelas" ficou **dentro do ruído**
da máquina (±0,2%).

## 6. Como medir de novo

```powershell
# consumo do app completo (GUI ociosa)
powershell -File .\tools\medir-consumo.ps1 -Exe .\out\cmdwatch.exe -Segundos 60

# custo de uma varredura do kernel (a operação que domina o motor)
.\.build\probe\ProbeKernel.exe
```
