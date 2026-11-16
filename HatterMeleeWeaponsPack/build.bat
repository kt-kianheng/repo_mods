@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo The .NET SDK was not found. Install it from https://dotnet.microsoft.com/download and try again.
  pause
  exit /b 1
)

echo Building HatterMeleeWeaponsPack...
dotnet build "src\HatterMeleeWeaponsPack\HatterMeleeWeaponsPack.csproj" -c Release -nologo %*
if errorlevel 1 (
  echo.
  echo Build failed - see the errors above.
  pause
  exit /b 1
)

echo.
echo Packaging Thunderstore zip...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop';" ^
  "$stage='dist\stage'; if (Test-Path $stage) { Remove-Item $stage -Recurse -Force };" ^
  "New-Item -ItemType Directory -Force $stage | Out-Null;" ^
  "Copy-Item 'src\HatterMeleeWeaponsPack\bin\Release\HatterMeleeWeaponsPack.dll','thunderstore\manifest.json','thunderstore\README.md','thunderstore\CHANGELOG.md','thunderstore\icon.png' $stage;" ^
  "$v=(Get-Content 'thunderstore\manifest.json' -Raw | ConvertFrom-Json).version_number;" ^
  "Compress-Archive -Path \"$stage\*\" -DestinationPath \"dist\HatterMeleeWeaponsPack-$v.zip\" -Force;" ^
  "Remove-Item $stage -Recurse -Force;" ^
  "Write-Host ('Package: dist\HatterMeleeWeaponsPack-' + $v + '.zip')"

echo.
echo Done. Launch R.E.P.O. from r2modman with the "Test mod" profile to test.
pause
