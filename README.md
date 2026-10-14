# NAT64 Switch

一个用于切换 DNS64/NAT64 的小工具。Windows 提供图形界面和命令行；Android 提供一键 DNS 转发。它不会建立全流量 VPN。

## 下载

解压 Windows 发布包后双击 `NAT64Switch-GUI.cmd` 打开图形界面；也可以双击 `NAT64Switch.exe` 使用命令行菜单。若仓库尚无 Windows Release，可在源码目录双击 `NAT64Switch-GUI.cmd`。

程序为 Windows x64 自包含版本，不需要预先安装 .NET。

## 使用

图形界面中可以：

- 一键切换所选网卡的 IPv6 DNS64 设置，再次点击恢复原设置。
- 查看当前 DNS 状态，并测试公共 DNS64/NAT64 是否可达。
- 操作时通过 Windows UAC 请求管理员权限。

命令行菜单仍提供以下功能：

1. 查看当前网卡、IPv6 地址和 DNS 状态
2. 将自己的网卡切换到 NAT64/DNS64
3. 恢复原来的 DNS 设置
4. 测试 `ipv4only.arpa`、`ipv6.baidu.com` 或自定义域名
5. 查看内置公共 DNS64 服务

也可以在命令行中使用：

```powershell
.\NAT64Switch.exe status
.\NAT64Switch.exe enable --adapter WLAN
.\NAT64Switch.exe enable --adapter WLAN --server 2a00:1098:2b::1
.\NAT64Switch.exe test --domain example.com
.\NAT64Switch.exe disable
.\NAT64Switch.exe disable --dhcp
```

查看完整参数：

```powershell
.\NAT64Switch.exe help
```

## 安全性

- 只修改所选网卡的 IPv6 DNS，不修改 IPv4 DNS。
- 开启前会保存原来的 DNS 配置；关闭时优先恢复原设置。
- 没有后台服务、驱动、遥测或开机启动项。
- 修改 DNS 需要管理员权限，程序会通过 Windows UAC 请求提权。
- `disable --dhcp` 会忽略备份，直接将 IPv6 DNS 恢复为 DHCP 自动获取。

## 注意事项

- 只支持 Windows 10/11 x64。
- GUI 启动器需要系统自带的 Windows PowerShell 和 .NET Framework Windows Forms；Windows 10/11 默认包含。
- 公共 DNS64 服务的可用地址可能变化。可以先用 `servers` 查看内置列表，再通过 `--server` 指定其他 IPv6 DNS64 地址。
- 某些企业网络或安全软件会限制第三方 DNS，使用前请确认符合所在网络的规定。
- `enable` 只负责设置 DNS64 通道，不代表任何运营商或网络管理方的规则。

## Android

使用 Android Studio 打开 `android/` 目录，或在该目录运行 `gradlew.bat assembleDebug`。首次构建需要 JDK 17 或更高版本、Android SDK Platform 35 和 Build Tools 35；Android Studio 的 SDK Manager 会提示安装缺少的组件并接受对应许可。首次开启时 Android 会显示 VPN 授权确认；这是系统要求。应用只把 DNS 查询送到所选 DNS64 服务，其他流量不经过 VPN 隧道。DNS64 服务会收到设备查询的域名；IPv4-only 应用、硬编码 IPv4 地址、应用自带 DoH/DoT 可能绕过系统 DNS。Android 端目前代理 UDP DNS。

Android VPN 会占用系统唯一的 VPN 槽位；开启前请先断开其他 VPN。应用会在状态栏持续显示运行通知，用户可以随时停止。

### 构建 Android APK

安装 Android Studio 后打开 `android/` 并等待 Gradle 同步，或在 `android/` 目录运行：

```powershell
.\gradlew.bat assembleDebug
```

APK 默认生成在 `android/app/build/outputs/apk/debug/app-debug.apk`。调试 APK 仅用于自行安装；公开分发前应生成正式签名版本。

## 从源码构建

需要 .NET 8 SDK 或更高版本。

```powershell
dotnet publish .\NAT64Switch.csproj -c Release -r win-x64 --self-contained true -o .\dist
```

或运行：

```powershell
.\build.ps1
```

生成文件和压缩包位于 `artifacts/`。

## 许可证

MIT
