# EasyHotspot

[English](README.md) · [下载 MSI](https://github.com/RiversLee9312/EasyHotspot/releases/latest) · [MIT 许可证](LICENSE) · [第三方开源许可](THIRD-PARTY-NOTICES.md)

适用于 **Windows 11 x64** 的原生 WinUI 3 热点管理工具，让连接电脑热点的设备使用电脑已有的系统代理或 TUN 代理。

## 功能

- **透明转发**：自动使用电脑现有的系统代理、PAC 或 TUN，无需在手机上输入 PAC 地址，也无需替换已有的 Clash 配置；不创建额外的 TUN 网卡。
- **显式代理**：提供 HTTP、SOCKS5、PAC、可选的 DHCP WPAD 自动发现，以及 Clash / sing-box 中继配置导出。
- **离线热点**：使用已有的 Hyper-V Default Switch 创建离线热点，需要系统及无线网卡支持。
- **桌面集成**：单实例运行，重复启动唤起原窗口；托盘图标在登录或 Explorer 重启后自动重试恢复；长操作异步执行并显示加载动画。
- **可选开机服务**：高优先级自启可在未登录桌面时运行，默认关闭。
- **24 种界面语言**，支持从右向左的布局。
- **关于页面**：查看版本、源代码及下载链接，离线阅读使用说明和完整的开源许可证。

## 安装与使用

1. 从 [Releases](https://github.com/RiversLee9312/EasyHotspot/releases/latest) 下载 x64 MSI，安装后从开始菜单打开 EasyHotspot。
2. 设置热点名称和密码，选择 **透明转发** 并启动热点；首次配置后台服务时同意 Windows 管理员授权。
3. 让手机等设备连接该热点，保留原来的代理 / VPN 配置，无需配置 Wi-Fi PAC。
4. 在 **代理 → 检测代理连接** 中诊断连接，在 **设置** 中调整自启及语言，在 **关于** 中查看说明和许可证。

关闭窗口时，应用确认托盘图标可用后隐藏到托盘；重复启动会唤起原窗口。从托盘选择 **退出** 会停止热点并退出。

卸载入口：**设置 → 应用 → 安装的应用 → EasyHotspot**。卸载会移除本应用的服务、驱动服务、规则及后台设置，不会删除其他应用的 WinDivert 服务或更改其他代理工具的配置。

## 代理行为与限制

透明转发通过管理员后台服务捕获热点网卡的数据包，交给 gVisor 网络栈处理，支持 **IPv4 TCP、UDP 和 DNS**。TCP 读取当前登录用户的 Windows 手动代理或 PAC；没有匹配的显式代理时，沿 Windows 当前路由使用已有 TUN。显式代理失败时会报告错误，不会悄悄改为直连。PAC 使用捕获连接的目标 IP 地址进行匹配。

DNS 使用电脑已有的 DNS 路径；仅有 HTTP 系统代理时，通过该代理发送 DNS over HTTPS。任意 UDP 转发需要已有 TUN 或支持 UDP 的 SOCKS5 代理，纯 HTTP 代理无法承载任意 UDP。目前没有实现 IPv6 转发。

运行期间会临时关闭热点网卡原生的 IPv4 转发，避免 ICS 重复转发；停止时还原，并在服务重启或卸载时恢复遗留状态。不会修改电脑上的 Clash 配置，也不会添加新的 TUN 路由。

已使用一加 13 实测：保持其原有 Clash Meta VPN 运行，不改配置或 Wi-Fi 代理设置，关闭蜂窝数据后，Google HTTPS 返回 204，Cloudflare HTTPS 返回 200，电脑原有 Meta TUN 保持运行。系统代理及 LocalSystem 后台服务路径也已验证。该结果对应这套设备和网络环境，其他 WLAN 驱动或 VPN 配置仍可能有差异，校园网设备数限制尚未验证。

### 显式代理与 DHCP 自动发现

默认热点网段下，HTTP / SOCKS5 端口为 `18877`，PAC 地址为 `http://192.168.137.1:18080/wpad.dat`。启用 **代理 → DHCP 自动发现** 后，会向支持 DHCP WPAD 的客户端发布 PAC 地址，地址和租约仍由 Windows 分配。

Android、iOS 不会把 DHCP WPAD 应用于所有应用；显式代理模式可能仍需手动设置 PAC，手机上的其他 VPN 也可能阻止访问热点代理。可以在 **代理 → 导出手机配置** 中导出中继配置，或在显式代理热点下向 Clash Meta 导入 `http://192.168.137.1:18080/mobile-clash.yaml`。这些中继配置是可选的，透明转发模式无需替换已有配置。

如果 Windows 安全中心拦截所需组件，请检查安装目录 `Native/WinDivert.dll` 和 `Native/WinDivert64.sys` 后再决定是否允许并重试。应用不会修改 Windows 安全中心设置。

## 构建与测试

需要 Windows、PowerShell 7.2+、**.NET SDK 10.0.400** 和 Windows SDK。首次构建需要联网下载 NuGet 包、WiX 5.0.2、固定版本 Go SDK 1.27.2 和 Go 模块。Go SDK 下载会校验 SHA-256，模块版本及校验值记录在 `PacketStack/go.mod` 和 `go.sum`。

```powershell
./test.ps1
./build.ps1
```

UI 测试需要已解锁的交互式桌面。构建输出为 `dist/EasyHotspot-1.3.2-x64.msi`，同时生成 MSI 静态校验报告。发布中间文件位于 `artifacts/`，工具缓存位于 `.tools/`，这些目录及 `bin/obj` 不属于源码。

语言资源使用微软 RESW / MRT Core，切换语言后重启应用生效。图标生成记录位于 `App/Assets/image-generation.json`。

## 许可证

EasyHotspot 自身代码与文档采用 [MIT 许可证](LICENSE)，版权所有 © 2026 RiversLee9312 及贡献者。第三方组件保留原许可证，包括 Apache-2.0、BSD、MIT 和 WinDivert 的 LGPLv3，详见 [第三方开源声明](THIRD-PARTY-NOTICES.md) 及 [ThirdParty/](ThirdParty/) 中的完整许可、声明和 WinDivert 对应源码归档。

安装包包含这些文档，可在 **关于 → 文档与开源许可证** 中离线阅读。Release 的源码归档包含应用源码与构建脚本，MSI 同时提供 `SHA256SUMS.txt` 校验文件。
