# EasyHotspot

A WinUI 3 hotspot manager for Windows 11 x64.

- Offline hotspot using an existing Hyper-V Default Switch.
- HTTP, SOCKS5 and PAC proxy support.
- Hotspot-scoped DHCP option 252 and DHCPINFORM responses for WPAD clients.
- Optional Windows service for startup.
- 24 interface languages.

Install the MSI and launch EasyHotspot from the Start menu. Remove it through **Settings → Apps → Installed apps**. Startup is disabled by default.

Offline mode requires a supported WLAN adapter and an existing Hyper-V Default Switch. Proxy compatibility depends on the client and network. Campus network device-limit behavior has not been verified.

Enable **Proxy → DHCP auto-discovery** while the hotspot is off, then start a proxy hotspot. Windows still assigns addresses and leases. Discovery runs in a demand-start service independently of automatic startup. Clients must support DHCP WPAD; mobile devices may need a manually configured PAC URL, and applications must support proxies. If Windows Security blocks the component, allow `Native/WinDivert.dll` and `Native/WinDivert64.sys` in the installation directory and retry. The application does not change Windows Security settings.

Uninstall removes the EasyHotspot service, its DHCP driver service, firewall rules and backend settings. The driver is registered as `EasyHotspotDhcpDriver`; other applications' WinDivert services are not removed.

## Build and test

Requires Windows, .NET SDK 10.0.400, Windows SDK and PowerShell 7.

```powershell
./build.ps1
./test.ps1
```

UI tests require an unlocked interactive desktop.

Localization uses Microsoft's [RESW and MRT Core](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/mrtcore/localize-strings), with `ApplicationLanguages.PrimaryLanguageOverride` applied at startup. Language changes take effect after restarting the app.

Third-party notices are in `ThirdParty/`. Icon generation prompts are in `App/Assets/image-generation.json`.
