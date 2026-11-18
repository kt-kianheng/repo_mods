@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo The .NET SDK was not found. Install it from https://dotnet.microsoft.com/download and try again.
  if not defined NOPAUSE pause
  exit /b 1
)

echo Building HatterEnemiesPack...
dotnet build "src\HatterEnemiesPack\HatterEnemiesPack.csproj" -c Release -nologo %*
if errorlevel 1 (
  echo.
  echo Build failed - see the errors above.
  if not defined NOPAUSE pause
  exit /b 1
)

echo.
echo Packaging Thunderstore zip...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop';" ^
  "$out=$env:HATTER_BUILD_OUT; if (-not $out) { $out='..\Builds' }; New-Item -ItemType Directory -Force $out | Out-Null;" ^
  "$stage=Join-Path $env:TEMP 'HatterEnemiesPack_stage'; if (Test-Path $stage) { Remove-Item $stage -Recurse -Force };" ^
  "New-Item -ItemType Directory -Force $stage | Out-Null;" ^
  "Copy-Item 'src\HatterEnemiesPack\bin\Release\HatterEnemiesPack.dll','thunderstore\manifest.json','thunderstore\README.md','thunderstore\CHANGELOG.md','thunderstore\icon.png' $stage;" ^
  "$v=(Get-Content 'thunderstore\manifest.json' -Raw | ConvertFrom-Json).version_number;" ^
  "$zip=Join-Path $out ('hatter_enemies_pack-' + $v.Replace('.','_') + '.zip');" ^
  "Compress-Archive -Path \"$stage\*\" -DestinationPath $zip -Force;" ^
  "Remove-Item $stage -Recurse -Force;" ^
  "Write-Host ('Package: ' + $zip)"

echo.
echo Done. To test, import the zip in r2modman: Settings, Import local mod.
if not defined NOPAUSE pause
