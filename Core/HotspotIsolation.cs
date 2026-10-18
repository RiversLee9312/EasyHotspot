using System.Net.NetworkInformation;

namespace EasyHotspot.Core;

// Native ICS must not also forward the same packets. Change only the hotspot
// adapter's volatile IPv4 setting, retaining a journal for crash recovery.
public sealed class HotspotIsolation
{
    static string Journal=>Path.Combine(ServiceSetup.DataDirectory,"forwarding-restore.json");
    static string Q(string value)=>"'"+value.Replace("'","''")+"'";
    public async Task AcquireAsync(NetworkInterface adapter)
    {
        await RecoverAsync();
        var id=Guid.Parse(adapter.Id);
        await WindowsNetworkCommand.Run($"$captureStage='isolate hotspot';$nic=Get-NetAdapter -IncludeHidden|Where-Object {{[Guid]$_.InterfaceGuid -eq [Guid]'{id}'}};if(!$nic){{throw 'Hotspot adapter disappeared'}};"+
            "$ip=Get-NetIPInterface -InterfaceIndex $nic.ifIndex -AddressFamily IPv4 -PolicyStore ActiveStore;"+
            $"if($ip.Forwarding -eq 'Enabled'){{New-Item -ItemType Directory -Path {Q(ServiceSetup.DataDirectory)} -Force|Out-Null;"+
            $"$saved=@{{AdapterId='{id}';Forwarding='Enabled'}}|ConvertTo-Json;[IO.File]::WriteAllText({Q(Journal+".tmp")},$saved);Move-Item -LiteralPath {Q(Journal+".tmp")} -Destination {Q(Journal)} -Force;"+
            "Set-NetIPInterface -InterfaceIndex $nic.ifIndex -AddressFamily IPv4 -PolicyStore ActiveStore -Forwarding Disabled}","Error_TransparentIsolation");
    }
    public static Task RecoverAsync()
    {
        if(!File.Exists(Journal))return Task.CompletedTask;
        return WindowsNetworkCommand.Run($"$captureStage='restore hotspot forwarding';$path={Q(Journal)};$saved=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json;"+
            "if($saved.Forwarding -ne 'Enabled'){throw 'Invalid forwarding journal'};"+
            "$id=[Guid]$saved.AdapterId;$nic=Get-NetAdapter -IncludeHidden|Where-Object {[Guid]$_.InterfaceGuid -eq $id};"+
            "if($nic){Set-NetIPInterface -InterfaceIndex $nic.ifIndex -AddressFamily IPv4 -PolicyStore ActiveStore -Forwarding Enabled};Remove-Item -LiteralPath $path","Error_TransparentIsolation");
    }
}
