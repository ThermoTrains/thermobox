@echo off

rem
rem Test if it's already running.
rem

tasklist /FI "IMAGENAME eq VisibleLightReader.exe" 2>NUL | find /I /N "VisibleLightReader.exe">NUL
if "%ERRORLEVEL%"=="0" (
  echo VisibleLightReader is already running
  pause
  exit
)
tasklist /FI "IMAGENAME eq Uploader.exe" 2>NUL | find /I /N "Uploader.exe">NUL
if "%ERRORLEVEL%"=="0" (
  echo Uploader is already running
  pause
  exit
)
tasklist /FI "IMAGENAME eq WeatherReader.exe" 2>NUL | find /I /N "WeatherReader.exe">NUL
if "%ERRORLEVEL%"=="0" (
  echo WeatherReader is already running
  pause
  exit
)
tasklist /FI "IMAGENAME eq IRCompressor.exe" 2>NUL | find /I /N "IRCompressor.exe">NUL
if "%ERRORLEVEL%"=="0" (
  echo IRCompressor is already running
  pause
  exit
)
tasklist /FI "IMAGENAME eq IRReader.exe" 2>NUL | find /I /N "IRReader.exe">NUL
if "%ERRORLEVEL%"=="0" (
  echo IRReader is already running
  pause
  exit
)

rem
rem Change to source directory.
rem

cd C:\repos\thermotrains

rem
rem Fetching and pulling newest version of the software.
rem

git pull

rem
rem Install packages that have been updated
rem

nuget restore

rem
rem Get version and git commit hash
rem

FOR /f "delims=" %%i in ('git describe --long --tags') do set VersionNumber=%%i

echo %VersionNumber%

rem
rem Build software.
rem

"C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe"^
 /t:Build^
 /p:Configuration=Release^
 /p:VersionNumber=%VersionNumber%

rem
rem Start all components.
rem

cd src\main\csharp

start /B VisibleLightReader\bin\release\VisibleLightReader.exe

rem
rem Sleep for 4 seconds to let VisibleLightReader find the camera and not block IRReader
rem

ping 127.0.0.1 -n 4 > nul

start /B Uploader\bin\release\Uploader.exe
start /B WeatherReader\bin\release\WeatherReader.exe
start /B IRCompressor\bin\release\IRCompressor.exe
start /B IRReader\bin\release\IRReader.exe
