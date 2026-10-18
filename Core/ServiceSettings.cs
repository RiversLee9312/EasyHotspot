using System.Text.Json;
namespace EasyHotspot.Core;
public static class ServiceSettings
{
    static readonly object gate=new();
    public static AppSettings Read(){try{return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(ServiceSetup.DataDirectory,"settings.json")))??new();}catch{return new();}}
    public static void Save(string mode){if(mode is not ("none" or "offline" or "proxy" or "transparent"))throw new ArgumentException("Error_InvalidStartup");lock(gate)Write(Read() with{StartupMode=mode});}
    public static void SaveDhcp(bool enabled){lock(gate)Write(Read() with{DhcpDiscovery=enabled});}
    static void Write(AppSettings settings){Directory.CreateDirectory(ServiceSetup.DataDirectory);var path=Path.Combine(ServiceSetup.DataDirectory,"settings.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(settings));File.Move(path+".tmp",path,true);}
}
