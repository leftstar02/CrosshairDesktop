$ErrorActionPreference = 'Stop'
$framework = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$root = Split-Path $PSScriptRoot
$arguments = @('/nologo', '/noconfig', '/nostdlib+', '/codepage:65001', '/target:exe', '/platform:x64', '/main:CrosshairDesktop.DesktopTests', "/out:$PSScriptRoot\DesktopTests.exe")
foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll', 'System.Numerics.dll')) {
    $arguments += "/reference:$(Join-Path $framework $reference)"
}
$arguments += (Get-ChildItem -LiteralPath (Join-Path $root 'desktop') -Filter '*.cs').FullName
$arguments += (Join-Path $PSScriptRoot 'DesktopTests.cs')
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Desktop tests build failed' }
& (Join-Path $PSScriptRoot 'DesktopTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Desktop tests failed' }
