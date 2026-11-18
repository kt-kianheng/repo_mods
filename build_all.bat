@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

rem Builds the mods listed below (add new mods to the list): each one's build.bat compiles it
rem against the r2modman "Test" profile's BepInEx and REPOLib and writes Builds\<mod_name>-<version>.zip
rem (e.g. Builds\hatter_weapons_pack-1_0_4.zip). Nothing is installed into a profile:
rem import a zip in r2modman (Settings, Import local mod) to test it.

set NOPAUSE=1
rem Every mod's zip goes to the Builds folder here (a mod's own build.bat does the same).
set "HATTER_BUILD_OUT=%~dp0Builds"
set FAILED=
set BUILT=0

for %%M in (TargetDummy HatterWeaponsPack HatterMeleeWeaponsPack HatterValuablesPack HatterMiscellaneousPack HatterEnemiesPack HatterCartsPack) do (
  if exist "%%M\build.bat" (
    echo.
    echo ============================================================
    echo  %%M
    echo ============================================================
    call "%%M\build.bat"
    if errorlevel 1 (
      set FAILED=!FAILED! %%M
    ) else (
      set /a BUILT+=1
    )
  ) else (
    echo.
    echo %%M\build.bat not found - skipped.
    set FAILED=!FAILED! %%M^(missing^)
  )
)

cd /d "%~dp0"
echo.
echo ============================================================
echo  Built: %BUILT% mod(s)
if defined FAILED (
  echo  FAILED:%FAILED%
  echo  Scroll up for the red error lines of each failed mod.
  echo ============================================================
  pause
  exit /b 1
)
echo  All mods built. Zips are in the Builds folder.
echo ============================================================
pause
