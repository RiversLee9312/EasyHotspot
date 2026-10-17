#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path,[string]$Report)
$ErrorActionPreference='Stop'
$installer=New-Object -ComObject WindowsInstaller.Installer
$database=$installer.OpenDatabase((Resolve-Path -LiteralPath $Path).Path,0)
$checks=[Collections.Generic.List[string]]::new()
function Check([bool]$condition,[string]$label){if(-not$condition){throw "Package verification failed: $label"};$checks.Add("PASS: $label")}
function Query([string]$sql,[string[]]$columns){
    $view=$database.OpenView($sql)
    $rows=[Collections.Generic.List[object]]::new()
    try{[void]$view.Execute();while($record=$view.Fetch()){$row=[ordered]@{};for($i=0;$i-lt$columns.Count;$i++){$row[$columns[$i]]=$record.StringData($i+1)};$rows.Add([pscustomobject]$row)}}
    finally{[void]$view.Close();[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)}
    $rows.ToArray()
}
try {
    $properties=@{};Query 'SELECT `Property`,`Value` FROM `Property`' @('Name','Value') | ForEach-Object {$properties[$_.Name]=$_.Value}
    Check ($properties.ProductName-eq'EasyHotspot') 'Product name'
    Check ($properties.ProductVersion-eq'1.2.2') 'Product version'
    Check ($properties.ALLUSERS-eq'1') 'Per-machine installation'
    $summary=$database.SummaryInformation(0)
    Check ($summary.Property(7)-like'x64;*') 'x64 package template'
    $files=@(Query 'SELECT `File`,`FileName`,`FileSize` FROM `File`' @('Id','Name','Size'))
    foreach($name in @('EasyHotspot.exe','EasyHotspot.Service.exe','App.xbf','EasyHotspot.pri','app.ico','Mobile-Relay.json','WinDivert.dll','WinDivert64.sys','WinDivert-2.2.2-source.tar.gz')){
        Check (@($files | Where-Object {($_.Name-split'\|')[-1]-eq$name}).Count-eq1) "Required payload: $name"
    }
    $tables=@(Query 'SELECT `Name` FROM `_Tables`' @('Name'))
    Check (@($tables | Where-Object Name -eq ServiceInstall).Count-eq0) 'Service is opt-in; MSI does not register it'
    $service=@(Query 'SELECT `Name`,`Event`,`Wait` FROM `ServiceControl`' @('Name','Event','Wait'))
    Check (@($service | Where-Object {$_.Name-eq'EasyHotspotService'-and([int]$_.Event-band160)-eq160-and$_.Wait-eq'1'}).Count-eq1) 'Optional service stopped and removed on uninstall'
    $actions=@(Query 'SELECT `Action`,`Type`,`Target` FROM `CustomAction`' @('Name','Type','Target'))
    Check (@($actions | Where-Object {$_.Name-eq'CleanupOwnedSettings'-and$_.Target-eq'--cleanup'-and([int]$_.Type-band3072)-eq3072}).Count-eq1) 'Elevated deferred cleanup action'
    $sequence=@(Query 'SELECT `Action`,`Condition`,`Sequence` FROM `InstallExecuteSequence`' @('Name','Condition','Sequence'))
    $cleanup=$sequence | Where-Object Name -eq CleanupOwnedSettings
    $remove=$sequence | Where-Object Name -eq RemoveFiles
    Check ($cleanup.Condition-eq'REMOVE~="ALL" AND NOT UPGRADINGPRODUCTCODE'-and[int]$cleanup.Sequence-lt[int]$remove.Sequence) 'Cleanup runs before removal, only on final uninstall'
    $shortcuts=@(Query 'SELECT `Name`,`Target` FROM `Shortcut`' @('Name','Target'))
    Check (@($shortcuts | Where-Object {$_.Name-like'*EasyHotspot*'}).Count-eq1) 'Start menu shortcut'
    [xml]$source=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Installer.wxs') -Raw
    $declared=@($source.SelectNodes('//*[local-name()="File"]'))
    Check ($files.Count-eq$declared.Count) 'Complete file table'
    $byId=@{};foreach($file in $files){$byId[$file.Id]=$file}
    foreach($file in $declared){if([long]$byId[$file.Id].Size-ne(Get-Item -LiteralPath $file.Source).Length){throw "Payload size mismatch: $($file.Source)"}}
    $checks.Add("PASS: All $($files.Count) payload file sizes match published files")
    $checks.Add('Scope: static MSI validation. Runtime installation tests are reported separately.')
    $text=$checks-join[Environment]::NewLine
    if($Report){[IO.File]::WriteAllText([IO.Path]::GetFullPath($Report),$text)}
    $text
} finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
