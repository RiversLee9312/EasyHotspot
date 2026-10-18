using System.Net;
using System.Net.NetworkInformation;

namespace EasyHotspot.Core;

public static class NetworkDiagnostics
{
    public static string? ActiveTun()
    {
        try {
        var names=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up &&
            new[]{"tunnel","wintun","wireguard","mihomo","sing-box","clash"}.Any(term=>n.Description.Contains(term,StringComparison.OrdinalIgnoreCase)))
            .Where(n=>n.GetIPProperties().UnicastAddresses.Count>0).Select(n=>n.Name).Distinct().ToArray();
        return names.Length==0?null:string.Join(", ",names);
        }catch(NetworkInformationException){return null;}
    }
    public static async Task CheckProxyAsync(OutboundRouter router)
    {
        using var handler=new SocketsHttpHandler{UseProxy=false,ConnectTimeout=TimeSpan.FromSeconds(8),ConnectCallback=async(context,ct)=>await router.ConnectAsync(context.DnsEndPoint.Host,context.DnsEndPoint.Port,ct)};
        using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(12)};
        try{using var response=await http.GetAsync("https://www.microsoft.com/",HttpCompletionOption.ResponseHeadersRead);response.EnsureSuccessStatusCode();}
        catch(Exception ex) when(ex is HttpRequestException or IOException or OperationCanceledException){throw new IOException("Error_ProxyCheckFailed|"+ex.Message.Replace('|',' '),ex);}
    }
    public static async Task CheckProxyAsync(string address)
    {
        // Connect through our own listener, using the same outbound sockets and OS routes as clients.
        // In particular, do not bind outbound sockets to the physical WLAN and bypass a user's TUN.
        using var handler=new SocketsHttpHandler{Proxy=new WebProxy($"http://{IPAddress.Parse(address)}:18877"),UseProxy=true,ConnectTimeout=TimeSpan.FromSeconds(8),MaxResponseHeadersLength=16};
        using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(12),MaxResponseContentBufferSize=4096};
        try {
            using var response=await http.GetAsync("https://www.microsoft.com/",HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
        }catch(Exception ex) when(ex is HttpRequestException or IOException or OperationCanceledException){throw new IOException("Error_ProxyCheckFailed|"+ex.Message.Replace('|',' '),ex);}
    }
}
