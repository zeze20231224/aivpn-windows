$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=Join-Path $env:RUNNER_TEMP 'aivpn-pinned-source.zip'
Invoke-WebRequest -UseBasicParsing -Uri 'https://aiworkport.com/downloads/windows-auto-0.1.0-rc14/AIVPN-Windows-0.1.0-rc14-source.zip' -OutFile $archive
if((Get-FileHash $archive).Hash -ne '2150296110AEB79AF13EFE27B737A53001A1238BCFAD9343658A92CDE2705E96'){throw 'Pinned dependency bundle changed'}
$allowed=@('webview2.zip','wintun.zip','mihomo.zip','mihomo-source.zip','wintun-source.zip','mihomo.exe','webview-setup.exe','mihomo-release.json')
$dest=Join-Path (Split-Path $PSScriptRoot -Parent) 'vendor'
New-Item -ItemType Directory -Force $dest | Out-Null
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try {
 foreach($e in $zip.Entries){
  $n=$e.FullName.Replace('\','/')
  if(!$n.StartsWith('vendor/')){continue}
  $leaf=$n.Substring(7)
  if($allowed -contains $leaf){[IO.Compression.ZipFileExtensions]::ExtractToFile($e,(Join-Path $dest $leaf),$true)}
 }
}finally{$zip.Dispose()}
foreach($n in $allowed){if(!(Test-Path (Join-Path $dest $n))){throw "Missing dependency: $n"}}
