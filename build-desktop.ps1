$ErrorActionPreference = 'Stop'
$framework = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$arguments = @('/nologo', '/noconfig', '/nostdlib+', '/codepage:65001', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', "/win32manifest:$PSScriptRoot\desktop\app.manifest", "/win32icon:$PSScriptRoot\CrosshairDesktop.ico", "/out:$PSScriptRoot\CrosshairDesktop.exe")
foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll', 'System.Numerics.dll')) {
    $arguments += "/reference:$(Join-Path $framework $reference)"
}
$arguments += (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'desktop') -Filter '*.cs').FullName
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed' }
Write-Output "Built $PSScriptRoot\CrosshairDesktop.exe"
