@echo off
setlocal

set ROOT=%~dp0
set MSBUILD=C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe
if not exist "%MSBUILD%" set MSBUILD=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe

taskkill /im OZON-PILOT.exe /f >nul 2>nul
taskkill /im OZON-PILOT-Updater.exe /f >nul 2>nul
taskkill /im LitchiOzonRecovery.exe /f >nul 2>nul
taskkill /im LitchiAutoUpdate.exe /f >nul 2>nul

if not exist "%MSBUILD%" (
  echo MSBuild not found.
  exit /b 1
)

echo Building updater...
"%MSBUILD%" "%ROOT%src\LitchiAutoUpdate\LitchiAutoUpdate.csproj" /t:Build /p:Configuration=Debug /verbosity:minimal
if errorlevel 1 exit /b 1

echo Building main app...
"%MSBUILD%" "%ROOT%src\LitchiOzonRecovery\LitchiOzonRecovery.csproj" /t:Build /p:Configuration=Debug /verbosity:minimal
if errorlevel 1 exit /b 1

set DIST=%ROOT%dist\OZON-PILOT
if exist "%DIST%" rmdir /s /q "%DIST%"
mkdir "%DIST%"

echo Copying build output...
xcopy "%ROOT%src\LitchiOzonRecovery\bin\Debug\OZON-PILOT.exe" "%DIST%\" /y /i /q >nul
xcopy "%ROOT%src\LitchiOzonRecovery\bin\Debug\OZON-PILOT.pdb" "%DIST%\" /y /i /q >nul
xcopy "%ROOT%src\LitchiOzonRecovery\bin\Debug\Microsoft.Web.WebView2.Core.dll" "%DIST%\" /y /i /q >nul
xcopy "%ROOT%src\LitchiOzonRecovery\bin\Debug\Microsoft.Web.WebView2.WinForms.dll" "%DIST%\" /y /i /q >nul
xcopy "%ROOT%baseline\runtimes\win-x64\native\WebView2Loader.dll" "%ROOT%src\LitchiOzonRecovery\bin\Debug\runtimes\win-x64\native\" /y /i /q >nul
xcopy "%ROOT%baseline\runtimes\win-x86\native\WebView2Loader.dll" "%ROOT%src\LitchiOzonRecovery\bin\Debug\runtimes\win-x86\native\" /y /i /q >nul
xcopy "%ROOT%baseline\runtimes\win-x64\native\WebView2Loader.dll" "%ROOT%src\LitchiOzonRecovery\bin\Debug\" /y /i /q >nul
xcopy "%ROOT%src\LitchiOzonRecovery\bin\Debug\Newtonsoft.Json.dll" "%DIST%\" /y /i /q >nul
xcopy "%ROOT%src\LitchiOzonRecovery\bin\Debug\NPOI*.dll" "%DIST%\" /y /i /q >nul
xcopy "%ROOT%src\LitchiOzonRecovery\bin\Debug\System.Data.SQLite.dll" "%DIST%\" /y /i /q >nul
xcopy "%ROOT%baseline\x64\SQLite.Interop.dll" "%ROOT%src\LitchiOzonRecovery\bin\Debug\x64\" /y /i /q >nul
xcopy "%ROOT%baseline\x86\SQLite.Interop.dll" "%ROOT%src\LitchiOzonRecovery\bin\Debug\x86\" /y /i /q >nul
xcopy "%ROOT%src\LitchiAutoUpdate\bin\Debug\OZON-PILOT-Updater.exe" "%DIST%\" /y /i /q >nul
xcopy "%ROOT%src\LitchiAutoUpdate\bin\Debug\OZON-PILOT-Updater.pdb" "%DIST%\" /y /i /q >nul
xcopy "%ROOT%src\LitchiAutoUpdate\bin\Debug\ICSharpCode.SharpZipLib.dll" "%DIST%\" /y /i /q >nul
xcopy "%ROOT%src\LitchiOzonRecovery\bin\Debug\zh-Hans\*.*" "%DIST%\zh-Hans\" /y /i /q >nul
xcopy "%ROOT%baseline\runtimes\win-x64\native\WebView2Loader.dll" "%DIST%\runtimes\win-x64\native\" /y /i /q >nul
xcopy "%ROOT%baseline\runtimes\win-x86\native\WebView2Loader.dll" "%DIST%\runtimes\win-x86\native\" /y /i /q >nul
xcopy "%ROOT%baseline\runtimes\win-x64\native\WebView2Loader.dll" "%DIST%\" /y /i /q >nul
xcopy "%ROOT%baseline\x64\SQLite.Interop.dll" "%DIST%\x64\" /y /i /q >nul
xcopy "%ROOT%baseline\x86\SQLite.Interop.dll" "%DIST%\x86\" /y /i /q >nul
xcopy "%ROOT%baseline\*.*" "%DIST%\" /e /i /y /q >nul

if exist "%DIST%\mscorlib.dll" del /q "%DIST%\mscorlib.dll"
if exist "%DIST%\normidna.nlp" del /q "%DIST%\normidna.nlp"
if exist "%DIST%\normnfc.nlp" del /q "%DIST%\normnfc.nlp"
if exist "%DIST%\normnfd.nlp" del /q "%DIST%\normnfd.nlp"
if exist "%DIST%\normnfkc.nlp" del /q "%DIST%\normnfkc.nlp"
if exist "%DIST%\normnfkd.nlp" del /q "%DIST%\normnfkd.nlp"

echo Build complete.
echo Output: %DIST%
endlocal
