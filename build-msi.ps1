#requires -Version 7.2
[CmdletBinding()]
param([string]$WixPath,[string]$Destination)
$ErrorActionPreference='Stop'
if(-not$WixPath){$WixPath=Join-Path $PSScriptRoot '.tools/wix/wix.exe'}
if(-not$Destination){$Destination=Join-Path $PSScriptRoot 'dist/EasyHotspot-1.2.2-x64.msi'}
$payload=Join-Path $PSScriptRoot 'artifacts/app'
foreach($required in @('EasyHotspot.exe','EasyHotspot.Service.exe','App.xbf','EasyHotspot.pri','Assets/app.ico','Native/WinDivert.dll','Native/WinDivert64.sys','ThirdParty/WinDivert/LICENSE','ThirdParty/WinDivert/WinDivert-2.2.2-source.tar.gz')){if(-not(Test-Path -LiteralPath (Join-Path $payload $required))){throw "Missing published file: $required. Run build.ps1 first."}}
$namespace='http://wixtoolset.org/schemas/v4/wxs'
$doc=[Xml.XmlDocument]::new()
function Node([string]$name,[hashtable]$attributes=@{},[Xml.XmlElement]$parent=$null){$node=$doc.CreateElement($name,$namespace);foreach($key in $attributes.Keys){$node.SetAttribute($key,[string]$attributes[$key])};if($null-ne$parent){[void]$parent.AppendChild($node)};return $node}
$root=Node 'Wix';[void]$doc.AppendChild($root)
$package=Node 'Package' @{Name='EasyHotspot';Manufacturer='EasyHotspot';Version='1.2.2';UpgradeCode='{2B76D1B8-CE68-462F-8E9B-B62F1C2BE89A}';Language='2052';Codepage='936';Scope='perMachine'} $root
$null=Node 'SummaryInformation' @{Description='EasyHotspot WinUI 3 桌面应用';Manufacturer='EasyHotspot';Codepage='936'} $package
$null=Node 'MajorUpgrade' @{DowngradeErrorMessage='已经安装了较新版本的EasyHotspot。'} $package
$null=Node 'MediaTemplate' @{EmbedCab='yes';CompressionLevel='high'} $package
$null=Node 'Launch' @{Condition='Installed OR VersionNT64';Message='EasyHotspot需要 64 位 Windows。'} $package
$null=Node 'Icon' @{Id='AppIcon.exe';SourceFile=(Join-Path $payload 'Assets/app.ico')} $package
$null=Node 'Property' @{Id='ARPPRODUCTICON';Value='AppIcon.exe'} $package
$null=Node 'Property' @{Id='ARPNOMODIFY';Value='1'} $package
$null=Node 'StandardDirectory' @{Id='ProgramMenuFolder'} $package
$programFiles=Node 'StandardDirectory' @{Id='ProgramFiles64Folder'} $package
$install=Node 'Directory' @{Id='INSTALLFOLDER';Name='EasyHotspot'} $programFiles
$feature=Node 'Feature' @{Id='Complete';Title='EasyHotspot';Level='1'} $package
$directories=@{''=$install}
function Id([string]$path){$bytes=[Text.Encoding]::UTF8.GetBytes($path.ToLowerInvariant());return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).Substring(0,24)}
foreach($file in Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName){
 $relative=[IO.Path]::GetRelativePath($payload,$file.FullName)
 $parts=$relative.Split([IO.Path]::DirectorySeparatorChar)
 $dir=$install;$current=''
 for($n=0;$n-lt$parts.Length-1;$n++){$current=if($current){Join-Path $current $parts[$n]}else{$parts[$n]};if(-not$directories.ContainsKey($current)){$directories[$current]=Node 'Directory' @{Id=('d'+(Id $current));Name=$parts[$n]} $dir};$dir=$directories[$current]}
 $fileId=if($relative-eq'EasyHotspot.Service.exe'){'ServiceExe'}elseif($relative-eq'EasyHotspot.exe'){'AppExe'}else{'f'+(Id $relative)}
 $componentId='c'+(Id $relative)
 $component=Node 'Component' @{Id=$componentId;Guid='*';Bitness='always64'} $dir
 $fileNode=Node 'File' @{Id=$fileId;Source=$file.FullName;KeyPath='yes'} $component
 if($fileId-eq'AppExe'){$null=Node 'Shortcut' @{Id='StartMenuShortcut';Directory='ProgramMenuFolder';Name='EasyHotspot';WorkingDirectory='INSTALLFOLDER';Advertise='yes';Icon='AppIcon.exe'} $fileNode}
 if($fileId-eq'ServiceExe'){$null=Node 'ServiceControl' @{Id='RemoveOptionalService';Name='EasyHotspotService';Stop='uninstall';Remove='uninstall';Wait='yes'} $component}
 $null=Node 'ComponentRef' @{Id=$componentId} $feature
}
$marker=Node 'Component' @{Id='InstallMarker';Guid='*';Bitness='always64'} $install
$key=Node 'RegistryKey' @{Root='HKLM';Key='Software\EasyHotspot';ForceDeleteOnUninstall='yes'} $marker
$null=Node 'RegistryValue' @{Name='InstallerVersion';Value='1.2.2';Type='string';KeyPath='yes'} $key
$null=Node 'ComponentRef' @{Id='InstallMarker'} $feature
$null=Node 'CustomAction' @{Id='CleanupOwnedSettings';FileRef='ServiceExe';ExeCommand='--cleanup';Execute='deferred';Impersonate='no';Return='check'} $package
$sequence=Node 'InstallExecuteSequence' @{} $package
$null=Node 'Custom' @{Action='CleanupOwnedSettings';Before='RemoveFiles';Condition='REMOVE~="ALL" AND NOT UPGRADINGPRODUCTCODE'} $sequence
$source=Join-Path $PSScriptRoot 'Installer.wxs'
$doc.Save($source)
& $WixPath build $source -arch x64 -o $Destination
if($LASTEXITCODE-ne0){throw 'MSI build failed.'}
# Some App SDK resource DLLs contain modern LCIDs unsupported by MSI's legacy
# validator, or a multilingual list longer than File.Language's 20 characters.
# Mark only those entries neutral in MSI metadata; shipped binaries stay intact.
$installer=New-Object -ComObject WindowsInstaller.Installer
$database=$installer.OpenDatabase([IO.Path]::GetFullPath($Destination),1)
$view=$database.OpenView('SELECT `File`,`Language` FROM `File`');$view.Execute()
$neutral=[Collections.Generic.List[string]]::new()
while($record=$view.Fetch()){$language=$record.StringData(2);if($language.Length-gt20-or$language-in@('1152','1153','1169')){$neutral.Add($record.StringData(1))}}
$view.Close()
$update=$database.OpenView('UPDATE `File` SET `Language` = ? WHERE `File` = ?')
foreach($fileId in $neutral){$record=$installer.CreateRecord(2);$record.StringData(1)='0';$record.StringData(2)=$fileId;$update.Execute($record)}
$update.Close();$database.Commit()
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($update)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
& $WixPath msi validate $Destination
if($LASTEXITCODE-ne0){throw 'MSI validation failed.'}
