@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

rem Optional: load .env.local (simple parser; ignores lines starting with #)
if exist ".env.local" (
  for /f "usebackq tokens=1,2 delims== eol=#" %%A in (".env.local") do (
    set "%%A=%%B"
  )
)

dotnet clean || goto :eof
dotnet build || goto :eof
dotnet run --urls http://localhost:5000
