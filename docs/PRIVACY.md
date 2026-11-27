# CMDWatcher — Privacy Policy / Política de Privacidade

Last updated / Última atualização: 2026-11-27

## English

**CMDWatcher collects no personal data and sends nothing anywhere.**

- **No network access.** The app contains no code that connects to the internet, and it has no analytics, telemetry, crash reporting, advertising or accounts.
- **What it reads.** To do its job, CMDWatcher reads information that Windows already exposes on your own computer: running processes, their command lines, window titles and CPU/memory figures. This data is displayed on screen and never leaves your machine.
- **What it writes (local only).**
  - A log file (`cmdwatch-log.jsonl`) with the events it observed. By default it is created next to where you start the app; if that folder is read-only (for example, the Store package), it falls back to `%LOCALAPPDATA%\cmdwatch`. You can export it or open it from the app, and delete it at any time.
  - A small preferences file (`ui.cfg` in `%APPDATA%\cmdwatch`) with your language, theme and layout choices.
- **Secret masking.** Tokens, passwords, API keys and `Authorization` headers are redacted before being shown or saved. Masking is on by default.
- **Sharing.** We do not share, sell or transmit any data. If you copy or export the log and send it to someone, that is your choice.
- **Children.** The app does not collect data from anyone, including children.
- **Changes.** If this policy changes, the new version will be published in this file with a new date.

**Contact:** open an issue at https://github.com/dellanio/cmdwatch/issues or write to dellanio@gmail.com.

## Português

**O CMDWatcher não coleta dados pessoais e não envia nada para lugar nenhum.**

- **Sem acesso à rede.** O app não tem código que se conecte à internet, e não possui análise de uso, telemetria, relatório de falhas, anúncios nem contas.
- **O que ele lê.** Para funcionar, lê informações que o próprio Windows já expõe no seu computador: processos em execução, linhas de comando, títulos de janelas e números de CPU e memória. Esses dados aparecem na tela e não saem da sua máquina.
- **O que ele grava (só local).**
  - Um arquivo de log (`cmdwatch-log.jsonl`) com os eventos observados. Por padrão nasce ao lado de onde você inicia o app; se essa pasta for somente leitura (como no pacote da Store), vai para `%LOCALAPPDATA%\cmdwatch`. Você pode exportá-lo ou abri-lo pelo app e apagá-lo quando quiser.
  - Um pequeno arquivo de preferências (`ui.cfg` em `%APPDATA%\cmdwatch`) com idioma, tema e escolhas de layout.
- **Mascaramento de segredos.** Tokens, senhas, chaves de API e cabeçalhos `Authorization` são ocultados antes de aparecer ou ser gravados. O mascaramento vem ativado por padrão.
- **Compartilhamento.** Não compartilhamos, vendemos nem transmitimos dados. Se você copiar ou exportar o log e enviá-lo a alguém, a escolha é sua.
- **Crianças.** O app não coleta dados de ninguém, inclusive de crianças.
- **Alterações.** Se esta política mudar, a nova versão será publicada neste arquivo com a nova data.

**Contato:** abra uma issue em https://github.com/dellanio/cmdwatch/issues ou escreva para dellanio@gmail.com.
