using System.Net;
using System.Net.Sockets;

namespace EasyHotspot.Core;

public static class MobileProfiles
{
    public static string Clash(string address)
    {
        var ip=IPAddress.Parse(address);if(ip.AddressFamily!=AddressFamily.InterNetwork)throw new ArgumentException(nameof(address));
        return $$"""
mixed-port: 7890
allow-lan: false
mode: rule
log-level: warning
ipv6: false
dns:
  enable: true
  ipv6: false
  enhanced-mode: fake-ip
  fake-ip-range: 198.18.0.1/16
  default-nameserver:
    - 223.5.5.5
  nameserver:
    - 'https://223.5.5.5/dns-query#EasyHotspot'
proxies:
  - name: EasyHotspot
    type: socks5
    server: {{ip}}
    port: 18877
    udp: true
rules:
  - MATCH,EasyHotspot

""";
    }
}
