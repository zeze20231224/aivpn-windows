$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $PSScriptRoot 'build'
$delivery = Join-Path $PSScriptRoot 'delivery'
New-Item -ItemType Directory -Force -Path $out,$delivery | Out-Null
function Assert-Hash($file,$expected) { if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw "Hash mismatch: $file" } }
Assert-Hash 'vendor\webview2.zip' 'f492bbf547d0da329553b6727435b677579b1e9f91cc9e4a1ad029366d5f23d0'
Assert-Hash 'vendor\wintun.zip' '07c256185d6ee3652e09fa55c0b673e2624b565e02c4b9091c79ca7d2f24ef51'
if(!(Test-Path 'vendor\webview2\lib\net462\Microsoft.Web.WebView2.Core.dll')){Expand-Archive -LiteralPath vendor\webview2.zip -DestinationPath vendor\webview2}
if(!(Test-Path 'vendor\wintun\wintun\bin\amd64\wintun.dll')){Expand-Archive -LiteralPath vendor\wintun.zip -DestinationPath vendor\wintun}
$engine = 'vendor\mihomo.exe'
Assert-Hash 'vendor\mihomo.zip' '289fde5e29d37a5b3326480590d8b3551c5bf7f8737290355c19bce74d57a563'
Assert-Hash $engine '6ac25fcb26afe8e1bea24b6e6e80805bf884a33232d12e2d78dfa0b6c529ac14'
Copy-Item -LiteralPath $engine -Destination "$out\mihomo.exe" -Force
Copy-Item -LiteralPath 'vendor\wintun\wintun\bin\amd64\wintun.dll' -Destination $out -Force
foreach($file in @('cn-domains.list','cn-ip.txt','RULES-LICENSE')) { Copy-Item -LiteralPath "assets\$file" -Destination $out -Force }
foreach($file in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')) { Copy-Item -LiteralPath "vendor\webview2\lib\net462\$file" -Destination $out -Force }
Copy-Item -LiteralPath 'vendor\webview2\runtimes\win-x64\native\WebView2Loader.dll' -Destination $out -Force
Copy-Item -LiteralPath 'vendor\webview2\LICENSE.txt' -Destination "$out\WEBVIEW2-LICENSE.txt" -Force
Copy-Item -LiteralPath 'vendor\wintun\wintun\LICENSE.txt' -Destination "$out\WINTUN-LICENSE.txt" -Force
Copy-Item -LiteralPath 'AIVPN.exe.config' -Destination $out -Force
Copy-Item -LiteralPath 'AIVPN.exe.config' -Destination "$out\AIVPN.Service.exe.config" -Force
$refs = @('/r:System.dll','/r:System.Core.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.Security.dll','/r:System.Management.dll','/r:System.ServiceProcess.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll')
$options=@('/nologo','/optimize+','/platform:x64','/utf8output','/warn:4')
& $compiler @options @refs /target:exe /main:Aivpn.BrandExport "/out:$out\BrandExport.exe" Branding.cs
if($LASTEXITCODE -ne 0){throw 'Brand asset compiler failed'}
& "$out\BrandExport.exe" "$PSScriptRoot\assets"
if($LASTEXITCODE -ne 0){throw 'Brand export failed'}
& $compiler @options @refs /target:winexe /main:Aivpn.MainWindow /win32icon:assets\aivpn.ico /resource:assets\aivpn.ico,aivpn.ico /win32manifest:app.manifest "/out:$out\AIVPN.exe" "/r:$out\Microsoft.Web.WebView2.Core.dll" "/r:$out\Microsoft.Web.WebView2.WinForms.dll" Common.cs App.cs Branding.cs Service.cs BuildPins.cs
if($LASTEXITCODE -ne 0){throw 'Client build failed'}
& $compiler @options @refs /target:exe /main:Aivpn.TunnelService "/out:$out\AIVPN.Service.exe" Common.cs Service.cs BuildPins.cs
if($LASTEXITCODE -ne 0){throw 'Service build failed'}
Write-Host 'Client and service compiled; tests not run.'
