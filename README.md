# EasyHotspot

A WinUI 3 hotspot manager for Windows 11 x64.

- Offline hotspot using an existing Hyper-V Default Switch.
- HTTP, SOCKS5 and PAC proxy support.
- Uses current Windows routes, including an active TUN, with an in-app connection test.
- Hotspot-scoped DHCP option 252 and DHCPINFORM responses for WPAD clients.
- Optional Windows service for startup.
- 24 interface languages.

Install the MSI and launch EasyHotspot from the Start menu. Remove it through **Settings → Apps → Installed apps**. Startup is disabled by default.

Offline mode requires a supported WLAN adapter and an existing Hyper-V Default Switch. Proxy compatibility depends on the client and network. Campus network device-limit behavior has not been verified.

Enable **Proxy → DHCP auto-discovery** while the hotspot is off, then start a proxy hotspot. Windows still assigns addresses and leases. Discovery runs in a demand-start service independently of automatic startup. Clients must support DHCP WPAD; mobile devices may need a manually configured PAC URL, and applications must support proxies. If Windows Security blocks the component, allow `Native/WinDivert.dll` and `Native/WinDivert64.sys` in the installation directory and retry. The application does not change Windows Security settings.

Uninstall removes the EasyHotspot service, its DHCP driver service, firewall rules and backend settings. The driver is registered as `EasyHotspotDhcpDriver`; other applications' WinDivert services are not removed.

The PC proxy follows Windows routing; it does not bypass a running TUN by binding outbound sockets to the WLAN. A separate VPN on a phone can prevent access to the hotspot proxy. For a Wi-Fi proxy, turn off that phone VPN or configure it to relay through the PC. Android does not automatically apply a DHCP WPAD URL to every application.

Under **Proxy → Export mobile profile**, choose Clash YAML or sing-box JSON. Clash Meta for Android can also import `http://192.168.137.1:18080/mobile-clash.yaml` while connected to a proxy hotspot. Activate that relay profile to use the PC while retaining Android's always-on VPN protection. Switch back to your regular profile when leaving the hotspot. Keep the Wi-Fi connection if Android reports that the network itself has no Internet access.

## Build and test

Requires Windows, .NET SDK 10.0.400, Windows SDK and PowerShell 7.

```powershell
./build.ps1
./test.ps1
```

UI tests require an unlocked interactive desktop.

Localization uses Microsoft's [RESW and MRT Core](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/mrtcore/localize-strings), with `ApplicationLanguages.PrimaryLanguageOverride` applied at startup. Language changes take effect after restarting the app.

Third-party notices are in `ThirdParty/`. Icon generation prompts are in `App/Assets/image-generation.json`.
