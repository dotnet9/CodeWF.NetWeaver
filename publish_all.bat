@echo off
setlocal enabledelayedexpansion

set "project_paths=src\CodeWF.NetWeaver.AOTTest src\SocketTest.Client src\SocketTest.Server"
set "platforms=win-x64"

call "%~dp0publishbase.bat" "%project_paths%" "%platforms%"
