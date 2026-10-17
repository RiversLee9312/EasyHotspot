#requires -Version 7.2
[CmdletBinding()]
param([string]$Destination,[string]$WixPath)
$ErrorActionPreference='Stop'
if(-not $Destination){$Destination=Join-Path $PSScriptRoot 'dist'}
$Destination=[IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Push-Location $PSScriptRoot
try {
    if(-not $WixPath){
        $toolDirectory=Join-Path $PSScriptRoot '.tools/wix'
        $WixPath=Join-Path $toolDirectory 'wix.exe'
        if(-not(Test-Path -LiteralPath $WixPath)){
            dotnet tool install wix --version 5.0.2 --tool-path $toolDirectory
            if($LASTEXITCODE-ne0){throw 'WiX installation failed.'}
        }
    }
    dotnet publish App/App.csproj -c Release -p:Platform=x64 -o artifacts/app
    if($LASTEXITCODE-ne0){throw 'App publishing failed.'}
    dotnet publish Service/Service.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o artifacts/service
    if($LASTEXITCODE-ne0){throw 'Service publishing failed.'}
    Copy-Item -Path artifacts/service/* -Destination artifacts/app -Recurse -Force
    Copy-Item -LiteralPath README-zh-CN.txt -Destination artifacts/app -Force
    Copy-Item -LiteralPath ThirdParty -Destination artifacts/app -Recurse -Force
    & ./build-msi.ps1 -WixPath $WixPath -Destination (Join-Path $Destination 'EasyHotspot-1.2.0-x64.msi')
    & ./verify-msi.ps1 -Path (Join-Path $Destination 'EasyHotspot-1.2.0-x64.msi') -Report (Join-Path $Destination 'MSI-verification.txt')
    Copy-Item -LiteralPath README-zh-CN.txt -Destination $Destination -Force
} finally {Pop-Location}
