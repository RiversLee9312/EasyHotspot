# EasyHotspot

A WinUI 3 hotspot manager for Windows 11 x64.

- Offline hotspot using an existing Hyper-V Default Switch.
- HTTP, SOCKS5 and PAC proxy support.
- Optional Windows service for startup.
- 24 interface languages.

Install the MSI and launch EasyHotspot from the Start menu. Remove it through **Settings → Apps → Installed apps**. Startup is disabled by default.

Offline mode requires a supported WLAN adapter and an existing Hyper-V Default Switch. Proxy compatibility depends on the client and network. Campus network device-limit behavior has not been verified.

## Build and test

Requires Windows, .NET SDK 10.0.400, Windows SDK and PowerShell 7.

```powershell
./build.ps1
./test.ps1
```

UI tests require an unlocked interactive desktop.

Localization uses Microsoft's [RESW and MRT Core](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/mrtcore/localize-strings), with `ApplicationLanguages.PrimaryLanguageOverride` applied at startup. Language changes take effect after restarting the app.

Third-party notices are in `ThirdParty/`. Icon generation prompts are in `App/Assets/image-generation.json`.
