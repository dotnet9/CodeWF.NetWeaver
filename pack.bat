@echo off
setlocal

pushd "%~dp0"

set "CONFIGURATION=Release"
set "ARTIFACTS_DIR=%CD%\artifacts"
set "PACKAGES_DIR=%ARTIFACTS_DIR%\packages"

if exist "%PACKAGES_DIR%" rmdir /s /q "%PACKAGES_DIR%"
mkdir "%PACKAGES_DIR%"

echo [1/3] Restoring solution...
dotnet restore CodeWF.NetWeaver.slnx
if errorlevel 1 goto :error

echo [2/3] Building solution...
dotnet build CodeWF.NetWeaver.slnx -c %CONFIGURATION% --no-restore
if errorlevel 1 goto :error

echo [3/3] Packing libraries...
for %%P in (
    "src\CodeWF.NetWeaver\CodeWF.NetWeaver.csproj"
    "src\CodeWF.NetWrapper\CodeWF.NetWrapper.csproj"
) do (
    dotnet pack %%~P -c %CONFIGURATION% -o "%PACKAGES_DIR%"
    if errorlevel 1 goto :error
)

echo.
echo Packages are available in:
echo %PACKAGES_DIR%

popd
exit /b 0

:error
set "EXIT_CODE=%ERRORLEVEL%"
echo.
echo Pack failed with exit code %EXIT_CODE%.
popd
exit /b %EXIT_CODE%
