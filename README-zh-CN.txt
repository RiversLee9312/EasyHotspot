EasyHotspot

Windows 11 x64 热点工具。

安装：双击 MSI，完成管理员授权后，从开始菜单打开 EasyHotspot。
卸载：设置 → 应用 → 安装的应用 → EasyHotspot → 卸载。

离线热点需要 WLAN 和现有的 Hyper-V Default Switch。
代理模式提供 HTTP、SOCKS5 和 PAC；手机需要设置代理或导入导出的 sing-box 配置。
DHCP 自动发现在“代理”页面启用，仅在代理热点运行时发布 PAC 地址。
客户端须支持 DHCP WPAD；Android、iPhone 和 iPad 可能仍需手动设置 PAC。
若 Windows 安全中心拦截组件，请放行安装目录 Native 下的 WinDivert.dll 和 WinDivert64.sys，再重试。
校园网共享效果取决于网络限制，尚未验证。

高优先级开机自启通过 Windows 服务实现，默认关闭，启用时需要管理员授权。
开启后可选择自动开启热点，或仅运行后台服务。
DHCP 自动发现使用按需启动的后台服务，与开机自启单独设置。
卸载会删除应用服务、DHCP 驱动服务、应用防火墙规则和后台设置。

语言可在设置中更改，重启应用后生效。

构建：安装 .NET SDK 10.0.400、Windows SDK 和 PowerShell 7 后运行 build.ps1。
测试：运行 test.ps1；UI 测试需要已解锁的桌面。
