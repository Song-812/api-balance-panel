param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\build'),
    [string]$TestRoot = (Join-Path $PSScriptRoot ('..\build\tests-' + [guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
if (!(Test-Path $csc)) { throw '需要 Windows x64 与 .NET Framework 4.8 编译工具。' }
New-Item -ItemType Directory -Path $OutputRoot,$TestRoot -Force | Out-Null
$OutputRoot = (Resolve-Path $OutputRoot).Path
$TestRoot = (Resolve-Path $TestRoot).Path
$systemRefs = @('/r:System.Core.dll','/r:System.Web.Extensions.dll','/r:System.Net.Http.dll','/r:System.Security.dll')
$uiRefs = $systemRefs + @("/r:$framework\PresentationFramework.dll","/r:$framework\PresentationCore.dll","/r:$framework\WindowsBase.dll",'/r:System.Xaml.dll',"/r:$source\lib\Microsoft.Web.WebView2.Core.dll","/r:$source\lib\Microsoft.Web.WebView2.Wpf.dll")
$flags = @('/nologo','/utf8output','/langversion:5','/warnaserror+','/platform:x64','/optimize+')
function Compile([string]$Output,[string[]]$References,[string[]]$Files,[string[]]$Extra) {
    $fullFiles = $Files | ForEach-Object { Join-Path $source $_ }
    & $csc @flags "/out:$Output" @References @Extra @fullFiles
    if ($LASTEXITCODE -ne 0) { throw "编译失败：$Output" }
}
function CopyRuntime([string]$Destination) {
    Copy-Item (Join-Path $source 'lib\*.dll') $Destination -Force
    Copy-Item (Join-Path $source 'lib\*.txt') $Destination -Force
    Copy-Item (Join-Path $source 'BalanceDom.js') $Destination -Force
    Copy-Item (Join-Path $source 'GeminiBalanceDom.js') $Destination -Force
}
function RunTest([string]$Program,[string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "测试失败：$Program" }
}
CopyRuntime $TestRoot
Compile (Join-Path $TestRoot 'CoreTests.exe') $systemRefs @('Core.cs','CoreTests.cs') @('/target:exe','/main:CoreTests')
RunTest (Join-Path $TestRoot 'CoreTests.exe') @($TestRoot)
Compile (Join-Path $TestRoot 'ViewStateTests.exe') @() @('ViewState.cs','BrowserReadState.cs','ViewStateTests.cs') @('/target:exe','/main:ViewStateTests')
RunTest (Join-Path $TestRoot 'ViewStateTests.exe') @()
Compile (Join-Path $TestRoot 'SafeDiagnosticsTests.exe') $systemRefs @('SafeDiagnostics.cs','SafeDiagnosticsTests.cs') @('/target:exe','/main:SafeDiagnosticsTests')
RunTest (Join-Path $TestRoot 'SafeDiagnosticsTests.exe') @($TestRoot)
$uiSources = @('Core.cs','ViewState.cs','BrowserReadState.cs','BrowserHost.cs','BrowserPopups.cs','BrowserNavigation.cs','SafeDiagnostics.cs','BrowserSession.cs','BrowserTargets.cs','PanelPreferences.cs','PanelTheme.cs','CardBoard.cs','SettingsWindow.cs','Ui.cs')
Compile (Join-Path $TestRoot 'UiTests.exe') $uiRefs ($uiSources + @('PanelUiTests.cs')) @('/target:exe','/main:PanelUiTests')
Copy-Item (Join-Path $source 'app.config') (Join-Path $TestRoot 'UiTests.exe.config') -Force
RunTest (Join-Path $TestRoot 'UiTests.exe') @((Join-Path $TestRoot 'ui-preview'))
Compile (Join-Path $TestRoot 'BrowserTests.exe') $uiRefs ($uiSources + @('BrowserTests.cs')) @('/target:exe','/main:BrowserTests')
Copy-Item (Join-Path $source 'app.config') (Join-Path $TestRoot 'BrowserTests.exe.config') -Force
RunTest (Join-Path $TestRoot 'BrowserTests.exe') @((Join-Path $TestRoot 'browser-test-profile'),(Join-Path $source 'BalanceDom.js'))
# 只构建 API 余额面板（本仓库不含 VPN 流量面板）
$folder = Join-Path $OutputRoot 'API余额面板'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$extra = @('/target:winexe','/main:BalancePanel.Program',"/win32manifest:$source\app.manifest","/win32icon:$source\app.ico")
Compile (Join-Path $folder 'API余额面板.exe') $uiRefs ($uiSources + @('Program.cs')) $extra
CopyRuntime $folder
Copy-Item (Join-Path $source 'app.config') (Join-Path $folder 'API余额面板.exe.config') -Force
Write-Output "BUILT $folder"
Write-Output "ALL TESTS PASSED. Test artifacts: $TestRoot"
