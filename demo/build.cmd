@echo off
echo === Build local ===
call npm run build
echo Copiando assets
xcopy "%~dp0src\assets\*" "%~dp0dist\assets\" /E /Y /Q
echo Build ok
