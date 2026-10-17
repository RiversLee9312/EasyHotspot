using System.Net.NetworkInformation;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace EasyHotspot.Core;
public record HotspotState(string State, string Ssid, string Password, int Clients, string Address, string Source, string Connectivity, string[] Devices);
public sealed class HotspotController
{
    string source = "—";
    public static IEnumerable<ConnectionProfile> Profiles() => NetworkInformation.GetConnectionProfiles();
    public static NetworkOperatorTetheringManager Manager(ConnectionProfile? profile = null)
    {
        foreach (var p in profile is null ? Profiles() : new[] { profile })
            try { return NetworkOperatorTetheringManager.CreateFromConnectionProfile(p); } catch { }
        throw new InvalidOperationException("Error_AccessHotspot");
    }
    public HotspotState Read()
    {
        var m = Manager(); var c = m.GetCurrentAccessPointConfiguration();
        var devices = m.TetheringOperationalState == TetheringOperationalState.On
            ? m.GetTetheringClients().Select(x => x.HostNames.FirstOrDefault()?.DisplayName ?? x.MacAddress).ToArray() : [];
        var online = Profiles().FirstOrDefault(p => p.IsWlanConnectionProfile && p.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None);
        return new(m.TetheringOperationalState.ToString(), c.Ssid, c.Passphrase, (int)m.ClientCount, FindHotspotAddress(), source,
            online?.GetNetworkConnectivityLevel().ToString() ?? "None", devices);
    }
    public static string FindHotspotAddress() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.Description.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase))
        .SelectMany(n => n.GetIPProperties().UnicastAddresses).FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString() ?? "192.168.137.1";
    public async Task StartAsync(string mode)
    {
        var m = Manager();
        if (m.TetheringOperationalState == TetheringOperationalState.On) throw new InvalidOperationException("Error_AlreadyOn");
        var adapters = NetworkInterface.GetAllNetworkInterfaces();
        var profiles = Profiles().Where(p => p.NetworkAdapter is not null && p.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None).ToList();
        ConnectionProfile? selected;
        if (mode is "offline" or "proxy")
            selected = profiles.Where(p => p.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.LocalAccess)
                .OrderBy(p => p.ProfileName == "vEthernet (Default Switch)" ? 0 : 1)
                .FirstOrDefault(p => adapters.Any(n => Guid.TryParse(n.Id, out var id) && id == p.NetworkAdapter.NetworkAdapterId &&
                    (n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) || n.Name.StartsWith("vEthernet"))));
        else selected = profiles.OrderBy(p => p.IsWlanConnectionProfile ? 0 : 1).FirstOrDefault();
        if (selected is null) throw new InvalidOperationException("Error_LocalAdapter");
        m = Manager(selected);
        var result = await m.StartTetheringAsync();
        if (result.Status != TetheringOperationStatus.Success && result.Status != TetheringOperationStatus.AlreadyOn)
            throw new InvalidOperationException($"Error_StartHotspot|{result.Status}|{result.AdditionalErrorMessage}");
        source = selected.ProfileName;
    }
    public async Task StopAsync()
    {
        var m = Manager(); if (m.TetheringOperationalState == TetheringOperationalState.Off) return;
        var r = await m.StopTetheringAsync();
        if (r.Status != TetheringOperationStatus.Success) throw new InvalidOperationException($"Error_StopHotspot|{r.Status}");
        source = "—";
    }
    public async Task ConfigureAsync(string ssid, string password)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(ssid) is < 1 or > 32) throw new ArgumentException("Error_InvalidSsid");
        if (password.Length is < 8 or > 63 || password.Any(c => c < 32 || c > 126)) throw new ArgumentException("Error_InvalidPassword");
        var m = Manager(); if (m.TetheringOperationalState != TetheringOperationalState.Off) throw new InvalidOperationException("Error_StopBeforeConfigure");
        var c = m.GetCurrentAccessPointConfiguration(); c.Ssid = ssid; c.Passphrase = password;
        await m.ConfigureAccessPointAsync(c);
    }
}
