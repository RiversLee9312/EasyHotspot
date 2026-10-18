EasyHotspot

Windows 11 x64 热点工具。

安装：双击 MSI，完成管理员授权后，从开始菜单打开 EasyHotspot。
卸载：设置 → 应用 → 安装的应用 → EasyHotspot → 卸载。

离线热点需要 WLAN 和现有的 Hyper-V Default Switch。
代理模式提供 HTTP、SOCKS5 和 PAC；手机需要设置代理或导入 Clash / sing-box 中继配置。
选择“透明转发”模式后，连接热点即可使用，无需设置 PAC 或更改手机现有的 Clash 配置。
透明转发通过管理员后台服务运行，自动读取当前用户的 Windows 系统代理或 PAC；未配置代理时沿 Windows 当前路由使用已有 TUN。
不会创建额外的 TUN 网卡，也不会修改电脑上 Clash 等工具的配置或路由。
支持 IPv4 TCP、UDP 和 DNS。通用的纯 HTTP 代理无法承载任意 UDP；UDP 需已有 TUN 或支持 UDP 的 SOCKS5 / 混合代理。
DNS 使用电脑已有的 DNS 路径；仅有 HTTP 系统代理时通过该代理发送 DNS over HTTPS。
PAC 按捕获连接的目标 IP 地址匹配。当前不提供 IPv6 转发。
运行时会临时关闭热点网卡原生的 IPv4 转发，防止 ICS 重复转发；停止时还原，服务重启和卸载也会恢复遗留状态。
电脑端转发沿 Windows 当前路由运行，兼容 TUN。可在“代理”页面检测电脑端代理连接。
在显式代理模式下，手机端其他 VPN 可能阻止访问热点代理；可改用透明转发。
DHCP 自动发现在“代理”页面启用，仅在代理热点运行时发布 PAC 地址。
客户端须支持 DHCP WPAD；Android、iPhone 和 iPad 可能仍需手动设置 PAC。
若 Windows 安全中心拦截组件，请放行安装目录 Native 下的 WinDivert.dll 和 WinDivert64.sys，再重试。
校园网共享效果取决于网络限制，尚未验证。

高优先级开机自启通过 Windows 服务实现，默认关闭，启用时需要管理员授权。
开启后可选择自动开启热点，或仅运行后台服务。
后台服务无需登录桌面或托盘图标也能运行热点。
关闭主窗口时会隐藏到托盘；从托盘选择“退出”会停止热点并退出。重复启动会唤起已有窗口。
Explorer 尚未就绪时，托盘图标会异步重试创建；Explorer 重启或图标丢失后也会自动恢复。
界面长操作异步执行，等待时显示加载动画，不影响切换页面。
DHCP 自动发现使用按需启动的后台服务，与开机自启单独设置。
卸载会删除应用服务、DHCP 驱动服务、应用防火墙规则和后台设置。

语言可在设置中更改，重启应用后生效。

关于页面提供版本、项目仓库、最新版本链接，以及离线文档和开源许可证查看器。
本项目自身代码采用 MIT 许可证；第三方组件保留各自的许可证。
完整许可见安装目录 LICENSE、THIRD-PARTY-NOTICES.md 和 ThirdParty 文件夹。
源代码：https://github.com/RiversLee9312/EasyHotspot
版本下载：https://github.com/RiversLee9312/EasyHotspot/releases/latest

实测：一加 13 保持原有 Clash Meta VPN 运行且不改配置，临时关闭蜂窝数据后，Google HTTPS 返回 204、Cloudflare HTTPS 返回 200、IPv4 HTTP 返回 301；电脑已有 Meta TUN 保持运行。
该结果对应本次设备和网络环境，不代表所有手机 VPN 或 WLAN 驱动均已验证。

构建：安装 .NET SDK 10.0.400、Windows SDK 和 PowerShell 7 后运行 build.ps1。首次构建会下载并校验固定版本的 Go SDK，从源码生成数据包转发组件。
测试：运行 test.ps1；UI 测试需要已解锁的桌面。
