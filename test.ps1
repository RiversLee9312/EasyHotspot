#requires -Version 7.2
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    dotnet test --project Tests/Tests.csproj --report-trx --results-directory TestResults/core --minimum-expected-tests 50
    if($LASTEXITCODE-ne0){throw 'Core tests failed.'}
    dotnet test --project UITests/UITests.csproj -p:Platform=x64 --report-trx --results-directory TestResults/ui --minimum-expected-tests 15
    if($LASTEXITCODE-ne0){throw 'WinUI tests failed.'}
} finally {Pop-Location}
