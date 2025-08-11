@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

rem ===== Config =====
set "APP_URL=http://localhost:5000"

rem ===== Optional: load .env.local (ignores lines starting with #) =====
if exist ".env.local" (
  for /f "usebackq tokens=1,* delims== eol=#" %%A in (".env.local") do (
    set "%%A=%%B"
  )
)

rem ===== Clean & build =====
dotnet clean || goto :eof
dotnet build || goto :eof

rem ===== Background watcher: open browser once /health returns 200 =====
start "" powershell -NoLogo -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command ^
  "$u='%APP_URL%/health';" ^
  "$opened=$false;" ^
  "for($i=0;$i -lt 120;$i++){" ^
  "  try{ $r=Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 $u; if($r.StatusCode -eq 200){ Start-Process '%APP_URL%'; $opened=$true; break } }catch{}" ^
  "  Start-Sleep -Milliseconds 500" ^
  "}; if(-not $opened){ Start-Process '%APP_URL%' }"

rem ===== Run app with Ctrl+C-safe cleanup =====
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop';" ^
  "try { dotnet run --urls '%APP_URL%' }" ^
  "finally { Write-Host 'Shutting down... cleaning build output'; dotnet clean }"

endlocal
