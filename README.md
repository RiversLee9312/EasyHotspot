# EasyHotspot

[简体中文](README.zh-CN.md) · [Download MSI](https://github.com/RiversLee9312/EasyHotspot/releases/latest) · [MIT License](LICENSE) · [Third-party licenses](THIRD-PARTY-NOTICES.md)

A WinUI 3 hotspot manager for Windows 11 x64.

- Offline hotspot using an existing Hyper-V Default Switch.
- HTTP, SOCKS5 and PAC proxy support.
- Uses current Windows routes, including an active TUN, with an in-app connection test.
- Transparent IPv4 forwarding for hotspot clients, automatically using the PC's system proxy or existing TUN.
- Single instance, recoverable system tray icon and asynchronous controls with progress indicators.
- Hotspot-scoped DHCP option 252 and DHCPINFORM responses for WPAD clients.
- Optional Windows service for startup.
- 24 interface languages.
- About page with version/release links and offline, asynchronous documentation and license reading.

Install the MSI and launch EasyHotspot from the Start menu. Remove it through **Settings → Apps → Installed apps**. Startup is disabled by default.

## Quick start

1. Download the x64 MSI from [Releases](https://github.com/RiversLee9312/EasyHotspot/releases/latest), install it, and open EasyHotspot from the Start menu.
2. Set the hotspot name and password. Choose **Transparent forwarding** to share the PC's current proxy/TUN automatically, and start the hotspot. Approve the administrator prompt when the service is first configured.
3. Connect the phone or another device to the Wi-Fi hotspot, keeping its existing proxy/VPN profile. No PAC setup is needed in transparent mode.
4. Use **Proxy → Check proxy connection** for diagnostics, **Settings** for startup/language, and **About** for bundled documentation and full license texts.

## Modes and compatibility

Offline mode requires a supported WLAN adapter and an existing Hyper-V Default Switch. Proxy compatibility depends on the client and network. Campus network device-limit behavior has not been verified.

Enable **Proxy → DHCP auto-discovery** while the hotspot is off, then start a proxy hotspot. Windows still assigns addresses and leases. Discovery runs in a demand-start service independently of automatic startup. Clients must support DHCP WPAD; mobile devices may need a manually configured PAC URL, and applications must support proxies. If Windows Security blocks the component, allow `Native/WinDivert.dll` and `Native/WinDivert64.sys` in the installation directory and retry. The application does not change Windows Security settings.

Uninstall removes the EasyHotspot service, its DHCP driver service, firewall rules and backend settings. The driver is registered as `EasyHotspotDhcpDriver`; other applications' WinDivert services are not removed.

The PC proxy follows Windows routing; it does not bypass a running TUN by binding outbound sockets to the WLAN. In the explicit proxy mode, a separate VPN on a phone can prevent access to the hotspot proxy. Android does not automatically apply a DHCP WPAD URL to every application.

Choose **Transparent forwarding** to connect devices without setting a PAC URL or changing their proxy/VPN profiles. This mode uses an elevated background service and the existing Windows hotspot. It captures only the hotspot adapter, passes IPv4 TCP/UDP through a gVisor network stack, and sends replies back to the client. It creates no additional TUN adapter. TCP uses the signed-in owner's Windows manual proxy or PAC settings, or follows the current Windows routes when no proxy applies. DNS uses the PC's DNS path, including an existing TUN's DNS. With an HTTP system proxy alone, DNS uses HTTPS through that proxy. UDP uses an existing TUN or a SOCKS5 UDP proxy; a generic HTTP-only proxy cannot forward arbitrary UDP. Explicit proxy failures do not silently fall back to direct connections. PAC rules receive destination IP URLs for captured connections.

Native IPv4 forwarding on the hotspot adapter is temporarily disabled to prevent a second ICS forwarding path. The original value is restored on stop, with a recovery journal for service restart or uninstall. Other adapters and the PC's proxy/TUN configuration are untouched. This mode currently forwards IPv4; it does not provide IPv6 routing.

The OnePlus 13 was tested with its existing Clash Meta VPN running and without changing its profile or Wi-Fi proxy settings. With cellular data disabled during verification, Google HTTPS returned 204, Cloudflare HTTPS returned 200, and an IPv4 HTTP request returned 301 through the hotspot. The PC's existing Meta TUN remained active. Both the Windows system proxy path and the LocalSystem background service were also verified with Google 204 and Cloudflare 200 responses. Temporary system proxy changes were restored after the test. This confirms that combination, not every VPN or WLAN driver.

Closing the window hides it to the tray after confirming the icon is available. **Exit** in the tray stops the hotspot and exits. A second launch restores the existing window. If Explorer is not ready at logon, icon registration retries asynchronously; Explorer restart messages and periodic checks restore a lost icon. High-priority startup remains a Windows service and does not require a tray icon or an interactive user session to run the hotspot.

Under **Proxy → Export mobile profile**, choose Clash YAML or sing-box JSON. Clash Meta for Android can also import `http://192.168.137.1:18080/mobile-clash.yaml` while connected to a proxy hotspot. Activate that relay profile to use the PC while retaining Android's always-on VPN protection. Switch back to your regular profile when leaving the hotspot. Keep the Wi-Fi connection if Android reports that the network itself has no Internet access.

## Build and test

Requires Windows, .NET SDK 10.0.400, Windows SDK and PowerShell 7.2+. The first build requires Internet access for NuGet packages, WiX 5.0.2, the pinned Go SDK 1.27.2, and Go modules. The build verifies the Go SDK's SHA-256 and builds the packet stack from `PacketStack/`. Go modules are pinned in `go.mod` and `go.sum`.

```powershell
./build.ps1
./test.ps1
```

UI tests require an unlocked interactive desktop.

The build creates `dist/EasyHotspot-1.3.1-x64.msi` and a static MSI verification report. Intermediate publish outputs are under `artifacts/`; reusable tool caches are under `.tools/`. Neither these directories nor `bin/obj` belong in source control. Release downloads include `SHA256SUMS.txt` for the MSI; GitHub's source archives contain the corresponding application source and build scripts.

Localization uses Microsoft's [RESW and MRT Core](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/mrtcore/localize-strings), with `ApplicationLanguages.PrimaryLanguageOverride` applied at startup. Language changes take effect after restarting the app.

Third-party notices are in `ThirdParty/`. Icon generation prompts are in `App/Assets/image-generation.json`.

## License

EasyHotspot's own code and documentation are licensed under [MIT](LICENSE), copyright © 2026 RiversLee9312 and contributors. Third-party components retain their respective licenses, including MIT, BSD, Apache-2.0, and WinDivert's LGPLv3. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for attribution, full texts, and WinDivert's included corresponding source archive and library replacement rights.

The MSI installs the project license and the complete `ThirdParty/` directory. Read them offline under **About → Documentation and licenses**.
