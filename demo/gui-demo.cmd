@echo off
echo === demonstracao do painel cmdwatch ===
echo.
echo etapa 1 - verificando o ambiente
where cmd
where git
git --version
ping -n 2 127.0.0.1 > nul
echo.
echo etapa 2 - listando arquivos
dir /b
ping -n 2 127.0.0.1 > nul
echo.
echo etapa 3 - chamando outro script
call "%~dp0demo-local.cmd"
ping -n 3 127.0.0.1 > nul
echo.
echo etapa 4 - verificando o repositorio git
git status
ping -n 8 127.0.0.1 > nul
echo.
echo === fim da demonstracao ===
