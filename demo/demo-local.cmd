@echo off
echo === Verificacao local do ambiente ===
where cmd
where git
echo Listando arquivos da pasta
dir /b
echo Verificando o git
git --version
echo Esperando 1 segundo
timeout /t 1 /nobreak > nul
echo Fim da verificacao
