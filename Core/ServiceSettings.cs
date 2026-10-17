using System.Text.Json;
namespace EasyHotspot.Core;
public static class ServiceSettings
{
    public static AppSettings Read(){try{return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(ServiceSetup.DataDirectory,"settings.json")))??new();}catch{return new();}}
    public static void Save(string mode){if(mode is not ("none" or "offline" or "proxy"))throw new ArgumentException("Error_InvalidStartup");Directory.CreateDirectory(ServiceSetup.DataDirectory);File.WriteAllText(Path.Combine(ServiceSetup.DataDirectory,"settings.json"),JsonSerializer.Serialize(new AppSettings(mode)));}
}
