$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
& "$PSScriptRoot\build.ps1"
if($LASTEXITCODE -ne 0){throw 'Build failed'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Pin($path,$expected){if((Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -ne $expected){throw "Pin mismatch: $path"}}
Pin 'vendor\mihomo-source.zip' '6790545b467fd6e61b6610793f37b64fac7f59b061f222501f6cc3a7a5de7c8b'
Pin 'vendor\webview-setup.exe' '17debf797a6c737959bc588236e897936ffac1af5f7e515e674ab32f9edfe719'
Pin 'build\wintun.dll' 'e5da8447dc2c320edc0fc52fa01885c103de8c118481f683643cacc3220dafce'
Pin 'build\cn-domains.list' '0c4262c30e84c2c585affa3b11a1bb36cc83ba086f4f8ea01921e87927fa85db'
Pin 'build\cn-ip.txt' '59d1cb68dbbfb8ca7db757142a3b536e8fdbfc5f45d381091b084a4c494a6675'
$release=Get-Content vendor\mihomo-release.json -Raw | ConvertFrom-Json
$asset=$release.assets | Where-Object name -eq 'mihomo-windows-amd64-compatible-v1.19.30.zip'
if($asset.digest -ne 'sha256:289fde5e29d37a5b3326480590d8b3551c5bf7f8737290355c19bce74d57a563'){throw 'Upstream asset mismatch'}
$archive=[IO.Compression.ZipFile]::OpenRead((Resolve-Path vendor\mihomo.zip))
try{$stream=$archive.Entries[0].Open();$sha=[Security.Cryptography.SHA256]::Create();try{$digest=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}finally{$stream.Dispose();$sha.Dispose()};if($digest -ne (Get-FileHash build\mihomo.exe).Hash.ToLowerInvariant()){throw 'Engine differs from official archive'}}finally{$archive.Dispose()}
$signatures=@()
foreach($file in @('vendor\webview-setup.exe','build\Microsoft.Web.WebView2.Core.dll','build\Microsoft.Web.WebView2.WinForms.dll','build\WebView2Loader.dll','build\wintun.dll')){
    $sig=Get-AuthenticodeSignature -LiteralPath $file
    $expected=if($file -eq 'build\wintun.dll'){'O=WireGuard LLC'}else{'O=Microsoft Corporation'}
    if($sig.Status -ne 'Valid' -or !$sig.SignerCertificate.Subject.Contains($expected)){throw "Invalid supplier signature: $file"}
    $signatures+=@{file=Split-Path $file -Leaf;status='Valid';signer=$sig.SignerCertificate.Subject}
}
Copy-Item -LiteralPath vendor\webview-setup.exe -Destination build\MicrosoftEdgeWebview2Setup.exe -Force
Copy-Item -LiteralPath README.md,LICENSE -Destination build -Force
$payloadNames=@('AIVPN.exe','AIVPN.exe.config','AIVPN.Service.exe','AIVPN.Service.exe.config','mihomo.exe','wintun.dll','cn-domains.list','cn-ip.txt','RULES-LICENSE','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','WEBVIEW2-LICENSE.txt','WINTUN-LICENSE.txt','MicrosoftEdgeWebview2Setup.exe','README.md','LICENSE')
$manifest=[ordered]@{}
foreach($name in $payloadNames){$manifest[$name]=(Get-FileHash -LiteralPath "build\$name").Hash.ToLowerInvariant()}
[IO.File]::WriteAllText("$PSScriptRoot\build\payload-manifest.json",($manifest|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$payload="$PSScriptRoot\build\payload.zip"
$fs=[IO.File]::Open($payload,[IO.FileMode]::Create)
try{$zip=[IO.Compression.ZipArchive]::new($fs,[IO.Compression.ZipArchiveMode]::Create);try{foreach($name in $payloadNames){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,"$PSScriptRoot\build\$name",$name)|Out-Null}}finally{$zip.Dispose()}}finally{$fs.Dispose()}
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$version='0.1.0-rc14'
if(!(Select-String -LiteralPath Common.cs -SimpleMatch ('Version = "'+$version+'"') -Quiet)){throw 'Product/package version mismatch'}
$setup="$PSScriptRoot\delivery\AIVPN-Windows-$version-Setup.exe"
& $compiler /nologo /utf8output /optimize+ /platform:x64 /target:winexe /main:Aivpn.Setup /win32icon:assets\aivpn.ico /win32manifest:setup.manifest "/out:$setup" /r:System.dll /r:System.Core.dll /r:Microsoft.CSharp.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Security.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /resource:build\payload.zip,payload.zip /resource:build\payload-manifest.json,payload-manifest.json Common.cs Setup.cs
if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
$assembly=[Reflection.Assembly]::LoadFile($setup)
$reader=[IO.StreamReader]::new($assembly.GetManifestResourceStream('payload-manifest.json'))
try{$embedded=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
$zip=[IO.Compression.ZipArchive]::new($assembly.GetManifestResourceStream('payload.zip'),[IO.Compression.ZipArchiveMode]::Read)
try{if($zip.Entries.Count -ne $payloadNames.Count){throw 'Embedded payload count mismatch'};foreach($entry in $zip.Entries){if($payloadNames -notcontains $entry.FullName){throw 'Unexpected payload'};$sha=[Security.Cryptography.SHA256]::Create();$stream=$entry.Open();try{$actual=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}finally{$stream.Dispose();$sha.Dispose()};if($actual -ne $embedded.($entry.FullName)){throw 'Embedded payload hash mismatch'}}}finally{$zip.Dispose()}
$source="$PSScriptRoot\delivery\AIVPN-Windows-$version-source.zip"
$fs=[IO.File]::Open($source,[IO.FileMode]::Create)
try{$zip=[IO.Compression.ZipArchive]::new($fs,[IO.Compression.ZipArchiveMode]::Create);try{
    $sources=@(Get-ChildItem -File *.cs,*.ps1,*.manifest,*.config,README.md,LICENSE)+@(Get-ChildItem -File assets\*)+@(Get-ChildItem -File vendor\*.zip,vendor\mihomo.exe,vendor\webview-setup.exe,vendor\mihomo-release.json)
    foreach($file in $sources){$relative=$file.FullName.Substring($PSScriptRoot.Length+1).Replace('\','/');[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,$relative)|Out-Null}
}finally{$zip.Dispose()}}finally{$fs.Dispose()}
$loginSmoke=if(Test-Path 'artifacts\login-smoke\result.json'){Get-Content 'artifacts\login-smoke\result.json' -Raw|ConvertFrom-Json}else{$null}
$result=[ordered]@{version=$version;status='PACKAGED_TEST_RELEASE';setup_sha256=(Get-FileHash $setup).Hash.ToLowerInvariant();setup_bytes=(Get-Item $setup).Length;source_sha256=(Get-FileHash $source).Hash.ToLowerInvariant();source_bytes=(Get-Item $source).Length;own_code_signature='UNSIGNED';supplier_signatures=$signatures;embedded_payload_verified=$true;offline_tests='NOT_RUN';core_auto_and_global_parse='NOT_RUN';public_login_form=$loginSmoke;prior_rc7_login_and_tun='SHORT_LIVE_CHECK_PASSED';real_account_login='RC14_NOT_VERIFIED';windows_tun='RC14_NOT_VERIFIED';installer_execution='RC14_NOT_EXECUTED';production_changes=$false}
[IO.File]::WriteAllText("$PSScriptRoot\delivery\verification.json",($result|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
$result|ConvertTo-Json -Depth 5





