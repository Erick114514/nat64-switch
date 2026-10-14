using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace Nat64Switch;

internal static class App
{
    private const string Version = "1.1.0";

    private static readonly Dns64Server[] Dns64Servers =
    {
        new("2a00:1098:2b::1", "nat64.net", "阿姆斯特丹"),
        new("2a01:4f8:c2c:123f::1", "nat64.net", "纽伦堡"),
        new("2a01:4f9:c010:3f02::1", "nat64.net", "赫尔辛基")
    };

    public static int Run(string[] args)
    {
        var commandLine = CommandLine.Parse(args);
        if (commandLine.HasFlag("version") ||
            string.Equals(commandLine.Command, "version", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"NAT64 Switch {Version}");
            return 0;
        }

        if (commandLine.HasFlag("help") ||
            string.Equals(commandLine.Command, "help", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
            return 0;
        }

        return commandLine.Command.ToLowerInvariant() switch
        {
            "menu" => RunInteractive(),
            "status" => RunStatus(commandLine),
            "servers" => RunServers(),
            "enable" => RunEnable(commandLine),
            "disable" => RunDisable(commandLine),
            "test" => RunTest(commandLine),
            "selftest" => RunSelfTest(),
            _ => UnknownCommand(commandLine.Command)
        };
    }

    private static int RunInteractive()
    {
        Console.WriteLine($"NAT64 Switch {Version}");
        Console.WriteLine(new string('-', 52));

        var adapter = SelectAdapterInteractive();
        if (adapter is null)
        {
            Console.WriteLine("没有找到可用网卡。");
            return 3;
        }

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine($"当前网卡：{adapter.ShortLabel}");
            Console.WriteLine("1. 查看状态");
            Console.WriteLine("2. 开启 NAT64");
            Console.WriteLine("3. 关闭 NAT64");
            Console.WriteLine("4. 测试 IPv6 / NAT64");
            Console.WriteLine("5. 查看 DNS64 服务");
            Console.WriteLine("6. 更换网卡");
            Console.WriteLine("0. 退出");
            Console.Write("请选择：");

            var choice = Console.ReadLine()?.Trim();
            Console.WriteLine();

            switch (choice)
            {
                case "1":
                    PrintStatus(adapter);
                    break;
                case "2":
                    if (EnableInteractive(adapter))
                    {
                        return 0;
                    }
                    break;
                case "3":
                    if (DisableInteractive(adapter))
                    {
                        return 0;
                    }
                    break;
                case "4":
                    TestAllAsync(adapter, null, TimeSpan.FromSeconds(5))
                        .GetAwaiter()
                        .GetResult();
                    break;
                case "5":
                    PrintServers();
                    break;
                case "6":
                    adapter = SelectAdapterInteractive();
                    if (adapter is null)
                    {
                        return 3;
                    }
                    break;
                case "0":
                    return 0;
                default:
                    Console.WriteLine("请输入有效选项。");
                    break;
            }
        }
    }

    private static int RunStatus(CommandLine commandLine)
    {
        var adapter = ResolveAdapter(commandLine.Get("--adapter"), interactive: false);
        if (adapter is null)
        {
            return AdapterSelectionFailed();
        }

        if (commandLine.HasFlag("json"))
        {
            var state = PowerShellService.GetDnsState(adapter.Name);
            var payload = new StatusPayload(
                adapter.Name,
                adapter.Description,
                adapter.Status.ToString(),
                adapter.Ipv6Addresses.Select(item => item.ToString()).ToArray(),
                state.Servers.ToArray(),
                state.Dhcp,
                AdminService.IsAdministrator);
            Console.WriteLine(JsonSerializer.Serialize(
                payload,
                AppJsonContext.Default.StatusPayload));
            return 0;
        }

        PrintStatus(adapter);
        return 0;
    }

    private static int RunServers()
    {
        PrintServers();
        return 0;
    }

    private static int RunEnable(CommandLine commandLine)
    {
        var adapter = ResolveAdapter(commandLine.Get("--adapter"), interactive: false);
        if (adapter is null)
        {
            return AdapterSelectionFailed();
        }

        var server = commandLine.Get("--server") ?? Dns64Servers[0].Address;
        if (!IsIpv6Address(server))
        {
            throw new ArgumentException($"DNS64 地址无效：{server}");
        }

        if (commandLine.HasFlag("dry-run"))
        {
            PrintDryRunEnable(adapter, server);
            return 0;
        }

        if (!AdminService.IsAdministrator)
        {
            Console.WriteLine("开启 NAT64 需要管理员权限，正在请求提权...");
            var elevatedArgs = new[]
            {
                "enable",
                "--adapter", adapter.Name,
                "--server", server
            };
            return AdminService.RelaunchElevated(elevatedArgs) ? 0 : 1;
        }

        Enable(adapter, server);
        return 0;
    }

    private static int RunDisable(CommandLine commandLine)
    {
        var adapter = ResolveAdapter(commandLine.Get("--adapter"), interactive: false);
        if (adapter is null)
        {
            return AdapterSelectionFailed();
        }

        if (commandLine.HasFlag("dry-run"))
        {
            PrintDryRunDisable(adapter, commandLine.HasFlag("dhcp"));
            return 0;
        }

        if (!AdminService.IsAdministrator)
        {
            Console.WriteLine("恢复 DNS 设置需要管理员权限，正在请求提权...");
            var elevatedArgs = new List<string> { "disable", "--adapter", adapter.Name };
            if (commandLine.HasFlag("dhcp"))
            {
                elevatedArgs.Add("--dhcp");
            }

            return AdminService.RelaunchElevated(elevatedArgs) ? 0 : 1;
        }

        Disable(adapter, commandLine.HasFlag("dhcp"));
        return 0;
    }

    private static int RunTest(CommandLine commandLine)
    {
        var adapter = ResolveAdapter(commandLine.Get("--adapter"), interactive: false);
        if (adapter is null)
        {
            return AdapterSelectionFailed();
        }

        var timeout = TimeSpan.FromSeconds(5);
        if (commandLine.Get("--timeout") is { } timeoutValue)
        {
            if (!double.TryParse(timeoutValue, out var seconds) || seconds <= 0 || seconds > 60)
            {
                throw new ArgumentException("--timeout 必须是 1 到 60 之间的秒数。");
            }

            timeout = TimeSpan.FromSeconds(seconds);
        }

        TestAllAsync(adapter, commandLine.Get("--domain"), timeout)
            .GetAwaiter()
            .GetResult();
        return 0;
    }

    private static int RunSelfTest()
    {
        var payloadJson = JsonSerializer.Serialize(
            new StatusPayload("test", "test", "Up", new[] { "2001:db8::1" }, new[] { "2001:db8::53" }, "Enabled", false),
            AppJsonContext.Default.StatusPayload);
        var payload = JsonSerializer.Deserialize(
            payloadJson,
            AppJsonContext.Default.StatusPayload);

        var stateJson = JsonSerializer.Serialize(
            new Dictionary<string, BackupState>
            {
                ["test"] = new(new[] { "2001:db8::53" }, "Disabled", DateTimeOffset.UnixEpoch)
            },
            AppJsonContext.Default.DictionaryStringBackupState);
        var state = JsonSerializer.Deserialize(
            stateJson,
            AppJsonContext.Default.DictionaryStringBackupState);

        var command = CommandLine.Parse(new[] { "status", "--adapter", "WLAN" });
        var passed = payload?.Adapter == "test" &&
            state is not null &&
            state.TryGetValue("test", out var backup) &&
            backup.Servers.SequenceEqual(new[] { "2001:db8::53" }) &&
            command.Command == "status" &&
            command.Get("--adapter") == "WLAN";

        Console.WriteLine(passed ? "self-test: ok" : "self-test: failed");
        return passed ? 0 : 1;
    }

    private static void Enable(AdapterInfo adapter, string server)
    {
        var current = PowerShellService.GetDnsState(adapter.Name);
        StateStore.Save(adapter.Name, current);

        Console.WriteLine($"正在将 {adapter.Name} 的 IPv6 DNS 设置为 {server}...");
        PowerShellService.SetDnsServers(adapter.Name, new[] { server });
        PowerShellService.ClearDnsCache();
        Console.WriteLine("设置完成。");
        Console.WriteLine();

        TestAllAsync(adapter, null, TimeSpan.FromSeconds(5))
            .GetAwaiter()
            .GetResult();
    }

    private static void Disable(AdapterInfo adapter, bool forceDhcp)
    {
        var backup = StateStore.Take(adapter.Name);
        if (forceDhcp)
        {
            backup = null;
        }

        if (backup is not null &&
            !string.Equals(backup.Dhcp, "Enabled", StringComparison.OrdinalIgnoreCase) &&
            backup.Servers is { Length: > 0 })
        {
            Console.WriteLine($"正在恢复 {adapter.Name} 原来的 IPv6 DNS...");
            PowerShellService.SetDnsServers(adapter.Name, backup.Servers);
        }
        else
        {
            Console.WriteLine($"正在将 {adapter.Name} 的 IPv6 DNS 恢复为自动获取...");
            PowerShellService.ResetDnsServers(adapter.Name);
        }

        PowerShellService.ClearDnsCache();
        Console.WriteLine("恢复完成。");
    }

    private static async Task TestAllAsync(
        AdapterInfo adapter,
        string? customDomain,
        TimeSpan timeout)
    {
        PrintStatus(adapter);
        Console.WriteLine();

        var checks = new List<(string Domain, bool Connect)>
        {
            ("ipv4only.arpa", false),
            ("ipv6.baidu.com", true)
        };

        if (!string.IsNullOrWhiteSpace(customDomain))
        {
            checks.Add((customDomain, true));
        }

        foreach (var check in checks)
        {
            var result = await ConnectivityTester.CheckAsync(
                check.Domain,
                check.Connect,
                timeout,
                CancellationToken.None);
            PrintDomainCheck(result);
        }
    }

    private static void PrintStatus(AdapterInfo adapter)
    {
        DnsState state;
        try
        {
            state = PowerShellService.GetDnsState(adapter.Name);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"读取 DNS 状态失败：{ex.Message}");
            state = new DnsState(Array.Empty<string>(), "Unknown");
        }

        Console.WriteLine($"网卡：{adapter.Name}");
        Console.WriteLine($"描述：{adapter.Description}");
        Console.WriteLine($"状态：{adapter.Status} ({adapter.TypeLabel})");
        Console.WriteLine($"IPv6：{FormatAddresses(adapter.Ipv6Addresses)}");
        Console.WriteLine($"IPv6 DNS：{(state.Servers.Count == 0 ? "自动/未配置" : string.Join(", ", state.Servers))}");
        Console.WriteLine($"DHCP：{state.Dhcp}");
        Console.WriteLine($"管理员：{(AdminService.IsAdministrator ? "是" : "否")}");
    }

    private static void PrintDomainCheck(DomainCheck result)
    {
        Console.WriteLine($"[测试] {result.Domain}");

        if (result.Error is not null)
        {
            Console.WriteLine($"  失败：{result.Error}");
            return;
        }

        var ipv6 = result.Addresses
            .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            .ToArray();
        var ipv4 = result.Addresses
            .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .ToArray();

        Console.WriteLine($"  IPv6：{(ipv6.Length == 0 ? "无" : string.Join(", ", ipv6.Select(item => item.ToString())))}");
        Console.WriteLine($"  IPv4：{(ipv4.Length == 0 ? "无" : string.Join(", ", ipv4.Select(item => item.ToString())))}");

        if (result.ConnectedAddress is not null)
        {
            Console.WriteLine(
                $"  443：连通到 {result.ConnectedAddress}，耗时 {result.ConnectTime?.TotalMilliseconds:F0} ms");
        }
        else if (ipv6.Length > 0)
        {
            Console.WriteLine("  443：未测试");
        }
        else
        {
            Console.WriteLine("  443：没有可用的 IPv6 地址");
        }
    }

    private static void PrintServers()
    {
        Console.WriteLine("内置 DNS64 服务：");
        for (var index = 0; index < Dns64Servers.Length; index++)
        {
            var server = Dns64Servers[index];
            Console.WriteLine(
                $"  {index + 1}. {server.Address}  {server.Provider} / {server.Location}");
        }
    }

    private static void PrintDryRunEnable(AdapterInfo adapter, string server)
    {
        Console.WriteLine("[dry-run] 将执行：");
        Console.WriteLine($"  Set-DnsClientServerAddress -InterfaceAlias \"{adapter.Name}\" -AddressFamily IPv6 -ServerAddresses \"{server}\"");
        Console.WriteLine("  Clear-DnsClientCache");
    }

    private static void PrintDryRunDisable(AdapterInfo adapter, bool forceDhcp)
    {
        Console.WriteLine("[dry-run] 将执行：");
        if (forceDhcp)
        {
            Console.WriteLine($"  Set-DnsClientServerAddress -InterfaceAlias \"{adapter.Name}\" -AddressFamily IPv6 -ResetServerAddresses");
        }
        else
        {
            Console.WriteLine($"  恢复 {adapter.Name} 上一次保存的 IPv6 DNS；没有备份时恢复为自动获取");
        }

        Console.WriteLine("  Clear-DnsClientCache");
    }

    private static bool EnableInteractive(AdapterInfo adapter)
    {
        PrintServers();
        Console.Write("选择 DNS64 服务编号（直接 Enter 使用 1，输入 0 取消）：");
        var value = Console.ReadLine()?.Trim();
        if (value == "0")
        {
            return false;
        }

        var index = string.IsNullOrWhiteSpace(value) ? 0 : int.TryParse(value, out var parsed) ? parsed - 1 : -1;
        if (index < 0 || index >= Dns64Servers.Length)
        {
            Console.WriteLine("选择无效。");
            return false;
        }

        var server = Dns64Servers[index].Address;
        if (!AdminService.IsAdministrator)
        {
            Console.Write("该操作需要管理员权限，是否重新打开并继续？[Y/n] ");
            var answer = Console.ReadLine()?.Trim();
            if (string.Equals(answer, "n", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(answer, "no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return AdminService.RelaunchElevated(new[]
            {
                "enable",
                "--adapter", adapter.Name,
                "--server", server
            });
        }

        Enable(adapter, server);
        return false;
    }

    private static bool DisableInteractive(AdapterInfo adapter)
    {
        if (!AdminService.IsAdministrator)
        {
            Console.Write("该操作需要管理员权限，是否重新打开并继续？[Y/n] ");
            var answer = Console.ReadLine()?.Trim();
            if (string.Equals(answer, "n", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(answer, "no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return AdminService.RelaunchElevated(new[]
            {
                "disable",
                "--adapter", adapter.Name
            });
        }

        Disable(adapter, forceDhcp: false);
        return false;
    }

    private static AdapterInfo? SelectAdapterInteractive()
    {
        var auto = AdapterCatalog.AutoSelect();
        if (auto is not null)
        {
            return auto;
        }

        var adapters = AdapterCatalog.GetAll()
            .Where(item => item.Status == OperationalStatus.Up)
            .ToArray();
        if (adapters.Length == 0)
        {
            return null;
        }

        Console.WriteLine("请选择网卡：");
        for (var index = 0; index < adapters.Length; index++)
        {
            Console.WriteLine($"  {index + 1}. {adapters[index].ShortLabel}");
        }

        Console.Write("编号：");
        if (!int.TryParse(Console.ReadLine(), out var selected) ||
            selected < 1 ||
            selected > adapters.Length)
        {
            return null;
        }

        return adapters[selected - 1];
    }

    private static AdapterInfo? ResolveAdapter(string? name, bool interactive)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return AdapterCatalog.Find(name);
        }

        var auto = AdapterCatalog.AutoSelect();
        if (auto is not null)
        {
            return auto;
        }

        if (!interactive)
        {
            return null;
        }

        return SelectAdapterInteractive();
    }

    private static int AdapterSelectionFailed()
    {
        Console.WriteLine("未能自动确定网卡，请使用 --adapter 指定。");
        Console.WriteLine();
        Console.WriteLine("可用网卡：");
        foreach (var adapter in AdapterCatalog.GetAll())
        {
            Console.WriteLine($"  {adapter.ShortLabel}");
        }

        return 3;
    }

    private static bool IsIpv6Address(string value)
    {
        return IPAddress.TryParse(value, out var address) &&
            address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
    }

    private static string FormatAddresses(IReadOnlyList<IPAddress> addresses)
    {
        return addresses.Count == 0 ? "无" : string.Join(", ", addresses);
    }

    private static int UnknownCommand(string command)
    {
        Console.WriteLine($"未知命令：{command}");
        Console.WriteLine("运行 `NAT64Switch.exe help` 查看用法。");
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine($"NAT64 Switch {Version}");
        Console.WriteLine();
        Console.WriteLine("用法：");
        Console.WriteLine("  NAT64Switch.exe                         打开交互菜单");
        Console.WriteLine("  NAT64Switch.exe status [--adapter NAME] [--json]");
        Console.WriteLine("  NAT64Switch.exe enable [--adapter NAME] [--server IPv6]");
        Console.WriteLine("  NAT64Switch.exe disable [--adapter NAME] [--dhcp]");
        Console.WriteLine("  NAT64Switch.exe test [--adapter NAME] [--domain DOMAIN]");
        Console.WriteLine("  NAT64Switch.exe servers");
        Console.WriteLine("  NAT64Switch.exe selftest");
        Console.WriteLine();
        Console.WriteLine("示例：");
        Console.WriteLine("  NAT64Switch.exe status");
        Console.WriteLine("  NAT64Switch.exe enable --adapter WLAN");
        Console.WriteLine("  NAT64Switch.exe disable");
        Console.WriteLine("  NAT64Switch.exe enable --dry-run");
    }
}
