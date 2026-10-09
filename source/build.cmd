@echo off
setlocal
pushd "%~dp0"
set "ZAPRET_CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%ZAPRET_CSC%" set "ZAPRET_CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%ZAPRET_CSC%" (
  echo .NET Framework compiler was not found.
  popd
  pause
  exit /b 1
)
"%ZAPRET_CSC%" /noconfig /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /utf8output /win32manifest:app.manifest /win32icon:zapret.ico /resource:zapret.ico,ZapretGui.Icon /out:ZapretGui.exe /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.ServiceProcess.dll /r:System.Xml.dll /r:System.Xml.Linq.dll ZapretGui.cs Discovery.cs Backend.cs FeaturesWindow.cs Maintenance.cs ZapretService.cs Brand.cs Theme.cs TestWindow.cs TestAdapter.cs Motion.cs HostsGuard.cs HostsWriter.cs CreatorsWindow.cs
set "ZAPRET_BUILD_RESULT=%errorlevel%"
if "%ZAPRET_BUILD_RESULT%"=="0" echo Build complete: ZapretGui.exe
if not "%ZAPRET_BUILD_RESULT%"=="0" echo Build failed. Please send the compiler output.
popd
pause
exit /b %ZAPRET_BUILD_RESULT%
