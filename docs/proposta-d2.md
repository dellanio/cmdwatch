# Proposta D2 (NAO APLICADA - aguardando decisao do usuario)

Medido (maquina parada, 8 .cmd curtos ocultos): padrao 0/8 e 1/8; --interval 50: 8/8 e 4/8; 100: 5/8; 200: 4/8.
O CmdCreationSensor (WMI __InstanceCreationEvent WITHIN 0.1) nao melhorou a captura (0/8 com ele ligado).

Opcao A - remover o sensor WMI (custo sem ganho medido)
  src/Monitor.cs: apagar a classe CmdCreationSensor (linha ~122), o campo _cmdSensor (~184),
  o bloco de Start (~226-231) e o Dispose correspondente.

Opcao B - intervalo parado padrao 500 -> 50 ms
  Program.cs/Options: valor padrao de --interval = 50.
  Custo medido: ~17% de 1 nucleo (~1% da maquina de 16 logicos) contra ~2-7% hoje.

Opcao C - A + B (recomendada se captura de .cmd curto oculto for requisito).
Opcao D - manter e documentar: README/notas dizem que .cmd oculto < ~500 ms pode escapar; usar --interval 50.