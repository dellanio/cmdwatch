@echo off
setlocal
echo === Deploy do Portal de Glosa ===

REM --- prepara o ambiente ---
if not exist "%~dp0dist" mkdir "%~dp0dist"
if exist "%~dp0dist\old" rmdir /s /q "%~dp0dist\old"

echo [1/4] Instalando dependencias do front
call npm ci

echo [2/4] Compilando o front
call npm run build

echo [3/4] Publicando a API .NET
dotnet publish src\Api\Api.csproj -c Release -o "%~dp0dist\api"

echo [4/4] Enviando os artefatos para o servidor
robocopy "%~dp0dist\*" \\servidor\glosa /MIR /R:2 /W:2

echo === Deploy concluido ===
endlocal
