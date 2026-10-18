using Microsoft.Win32;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace EasyHotspot.Core;

public record ProxySettings(bool Enabled=false,string? Server=null,string? Bypass=null,string? Pac=null,bool AutoDetect=false)
{
    // A service's HKCU belongs to LocalSystem, not to the person running Clash.
    public static ProxySettings Read(string sid)
    {
        using var key=Registry.Users.OpenSubKey(sid+@"\Software\Microsoft\Windows\CurrentVersion\Internet Settings");
        if(key is null)return new();
        using var connections=key.OpenSubKey("Connections");
        var flags=connections?.GetValue("DefaultConnectionSettings") as byte[];
        return new(key.GetValue("ProxyEnable") is int enabled && enabled!=0,key.GetValue("ProxyServer") as string,
            key.GetValue("ProxyOverride") as string,flags is {Length:>=12} && (flags[8]&4)==0?null:key.GetValue("AutoConfigURL") as string,flags is {Length:>=12} && (flags[8]&8)!=0);
    }

    public Uri? Manual(Uri destination)
    {
        if(!Enabled || string.IsNullOrWhiteSpace(Server))return null;
        if(IsBypassed(destination.Host,Bypass))return null;
        var entries=Server.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var selected=entries.FirstOrDefault(x=>x.StartsWith(destination.Scheme+"=",StringComparison.OrdinalIgnoreCase))
            ??entries.FirstOrDefault(x=>!x.Contains('='))
            ??entries.FirstOrDefault(x=>x.StartsWith("socks=",StringComparison.OrdinalIgnoreCase));
        if(selected is null)throw new IOException("Error_UpstreamProtocol");
        bool socks=selected.StartsWith("socks=",StringComparison.OrdinalIgnoreCase);
        var value=selected.Contains('=')?selected[(selected.IndexOf('=')+1)..]:selected;
        if(!value.Contains("://"))value=(socks?"socks5://":"http://")+value;
        if(!Uri.TryCreate(value,UriKind.Absolute,out var proxy) || proxy.Scheme is not ("http" or "https" or "socks" or "socks5"))throw new IOException("Error_UpstreamProtocol");
        return proxy;
    }

    public static bool IsBypassed(string host,string? list)=>list?.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Any(pattern=>
        pattern.Equals("<local>",StringComparison.OrdinalIgnoreCase)?!host.Contains('.') && !host.Contains(':'):
        Regex.IsMatch(host,"^"+Regex.Escape(pattern).Replace("\\*",".*")+"$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50)))==true;
}

public sealed class SystemProxyResolver
{
    readonly Func<ProxySettings> read;
    readonly SemaphoreSlim pacGate=new(1,1);
    readonly Dictionary<string,(DateTime Expiry,Uri? Proxy)> cache=new();
    ProxySettings? previous;
    public SystemProxyResolver(string sid):this(()=>ProxySettings.Read(sid)){}
    public SystemProxyResolver(Func<ProxySettings> read)=>this.read=read;
    public ProxySettings Settings=>read();
    public async Task<Uri?> ResolveAsync(string host,int port,CancellationToken ct)
    {
        var destination=new UriBuilder(port==443?"https":"http",host,port).Uri;
        var settings=read();
        if(settings.Enabled && !string.IsNullOrWhiteSpace(settings.Server) && string.IsNullOrWhiteSpace(settings.Pac))return settings.Manual(destination);
        if(string.IsNullOrWhiteSpace(settings.Pac) && !settings.AutoDetect)return settings.Manual(destination);
        await pacGate.WaitAsync(ct);
        try {
            if(previous!=settings){cache.Clear();previous=settings;}
            if(cache.TryGetValue(destination.AbsoluteUri,out var item) && item.Expiry>DateTime.UtcNow)return item.Proxy;
            // WinHTTP evaluates the user's PAC; no scripting engine or configuration rewriting.
            var proxy=await Task.Run(()=>PacProxy(destination,settings),ct);
            if(cache.Count>=1024)cache.Clear();
            cache[destination.AbsoluteUri]=(DateTime.UtcNow.AddSeconds(30),proxy);
            return proxy;
        } finally {pacGate.Release();}
    }
    static Uri? PacProxy(Uri destination,ProxySettings settings)
    {
        var session=WinHttpOpen("EasyHotspot/1.3",1,null,null,0);
        if(session==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            WinHttpSetTimeouts(session,3000,3000,3000,5000);
            var options=new AutoProxyOptions{Flags=string.IsNullOrWhiteSpace(settings.Pac)?1u:2u,DetectFlags=3,Url=settings.Pac};
            if(!WinHttpGetProxyForUrl(session,destination.AbsoluteUri,ref options,out var result)) {
                var error=Marshal.GetLastWin32Error();
                // A failed WPAD discovery without an explicit PAC follows the user's manual setting.
                if(string.IsNullOrWhiteSpace(settings.Pac) && error==12180)return settings.Manual(destination);
                throw new IOException("Error_UpstreamPac|"+error);
            }
            try {
                if(result.AccessType==1)return null;
                var server=Marshal.PtrToStringUni(result.Proxy);
                return new ProxySettings(true,server,Marshal.PtrToStringUni(result.Bypass)).Manual(destination);
            }finally{if(result.Proxy!=IntPtr.Zero)GlobalFree(result.Proxy);if(result.Bypass!=IntPtr.Zero)GlobalFree(result.Bypass);}
        }finally{WinHttpCloseHandle(session);}
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct AutoProxyOptions{public uint Flags,DetectFlags;[MarshalAs(UnmanagedType.LPWStr)]public string? Url;public IntPtr Reserved;public uint ReservedFlags;[MarshalAs(UnmanagedType.Bool)]public bool AutoLogon;}
    [StructLayout(LayoutKind.Sequential)] struct ProxyInfo{public uint AccessType;public IntPtr Proxy,Bypass;}
    [DllImport("winhttp.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr WinHttpOpen(string agent,uint access,string? proxy,string? bypass,uint flags);
    [DllImport("winhttp.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool WinHttpSetTimeouts(IntPtr session,int resolve,int connect,int send,int receive);
    [DllImport("winhttp.dll",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool WinHttpGetProxyForUrl(IntPtr session,string url,ref AutoProxyOptions options,out ProxyInfo result);
    [DllImport("winhttp.dll")][return:MarshalAs(UnmanagedType.Bool)]static extern bool WinHttpCloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")]static extern IntPtr GlobalFree(IntPtr handle);
}
