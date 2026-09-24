# NAT64 Switch

一个无需安装、双击即可使用的 Windows 小工具，用于切换网卡的 DNS64 设置并测试 IPv6/NAT64 连通性。

## 下载

从本仓库的 Releases 页面下载 `NAT64Switch-win-x64-v1.0.0.zip`，解压后双击 `NAT64Switch.exe`。

程序为 Windows x64 自包含版本，不需要预先安装 .NET。

## 使用

双击运行后会显示菜单：

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
- 公共 DNS64 服务的可用地址可能变化。可以先用 `servers` 查看内置列表，再通过 `--server` 指定其他 IPv6 DNS64 地址。
- 某些企业网络或安全软件会限制第三方 DNS，使用前请确认符合所在网络的规定。
- `enable` 只负责设置 DNS64 通道，不代表任何运营商或网络管理方的规则。

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
