#requires -Version 7.2
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$output=Join-Path $PSScriptRoot 'Core/Native/EasyHotspot.PacketStack.exe'
$inputs=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'PacketStack') -File)+@(Get-Item -LiteralPath $PSCommandPath)
if((Test-Path -LiteralPath $output) -and -not($inputs | Where-Object LastWriteTimeUtc -gt (Get-Item -LiteralPath $output).LastWriteTimeUtc)){return}
$go=Join-Path $PSScriptRoot '.tools/go/bin/go.exe'
if(-not(Test-Path -LiteralPath $go)){
    $archive=Join-Path $PSScriptRoot '.tools/go1.27.2.windows-amd64.zip'
    New-Item -ItemType Directory -Path (Split-Path $archive) -Force | Out-Null
    if(-not(Test-Path -LiteralPath $archive)){Invoke-WebRequest 'https://go.dev/dl/go1.27.2.windows-amd64.zip' -OutFile $archive}
    if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '1314008898bd40df77af4b014f777f08873dbdfbcd3d92308728ee03304fe04f'){throw 'Go SDK checksum mismatch.'}
    Expand-Archive -LiteralPath $archive -DestinationPath (Split-Path $archive) -Force
}
Push-Location (Join-Path $PSScriptRoot 'PacketStack')
$previousGoEnvironment=@{}
foreach($name in @('GOOS','GOARCH','CGO_ENABLED','GOTOOLCHAIN')){$previousGoEnvironment[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try{
    $env:GOOS='windows';$env:GOARCH='amd64';$env:CGO_ENABLED='0';$env:GOTOOLCHAIN='local'
    & $go build -trimpath -ldflags '-s -w' -o $output .
    if($LASTEXITCODE -ne 0){throw 'PacketStack build failed.'}
}finally{foreach($name in $previousGoEnvironment.Keys){[Environment]::SetEnvironmentVariable($name,$previousGoEnvironment[$name],'Process')};Pop-Location}
