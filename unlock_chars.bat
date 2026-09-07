@echo off
setlocal

set "KEY=HKCU\Software\Orsoniks\Gunsaw"

reg add "%KEY%" /v "charUnlocked0_h3259890428"  /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v "charUnlocked1_h3259890429"  /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v "charUnlocked2_h3259890430"  /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v "charUnlocked3_h3259890431"  /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v "charUnlocked4_h3259890424"  /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v "charUnlocked5_h3259890425"  /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v "charUnlocked6_h3259890426"  /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v "charUnlocked7_h3259890427"  /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v "charUnlocked8_h3259890420"  /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v "charUnlocked9_h3259890421"  /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v "charUnlocked10_h202201773" /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v "charUnlocked11_h202201772" /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v "charUnlocked12_h202201775" /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v "charUnlocked13_h202201774" /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v "charUnlocked14_h202201769" /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v "charUnlocked15_h202201768" /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v "charUnlocked16_h202201771" /t REG_DWORD /d 1 /f >nul

reg add "%KEY%" /v "progress_h3727189976" /t REG_DWORD /d 17 /f >nul

echo Done. Gunsaw registry values were added.
pause
