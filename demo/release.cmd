@echo off
echo === Orquestrador de release ===
echo Etapa 1: build
call "%~dp0build.cmd"
echo Etapa 2: deploy
call "%~dp0deploy.cmd"
echo Etapa 3: verificacao
git log -1 --oneline
echo Release concluida
