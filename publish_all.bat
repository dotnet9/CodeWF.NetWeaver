@echo off
setlocal enabledelayedexpansion

set "project_paths=src\CodeWF.NetWeaver.AOTTest SocketTest.Client src\SocketTest.Server"
set "platforms=win-x64 linux-x64"

call "%~dp0publishbase.bat" "%project_paths%" "%platforms%"
