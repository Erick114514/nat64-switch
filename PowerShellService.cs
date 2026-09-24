using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Nat64Switch;

internal static class PowerShellService
{
    public static DnsState GetDnsState(string adapterName)
    {
        const string script = """
            $ErrorActionPreference = 'Stop'
            $dns = @(Get-DnsClientServerAddress -InterfaceAlias $env:NAT64_ADAPTER -AddressFamily IPv6 -ErrorAction Stop).ServerAddresses | Where-Object { $_ }
            $iface = Get-NetIPInterface -InterfaceAlias $env:NAT64_ADAPTER -AddressFamily IPv6 -ErrorAction Stop
            [pscustomobject]@{
                Servers = @($dns)
                Dhcp = $iface.Dhcp.ToString()
            } | ConvertTo-Json -Compress -Depth 3
            """;

        var json = Execute(script, new Dictionary<string, string?>
        {
            ["NAT64_ADAPTER"] = adapterName
        });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var servers = root.TryGetProperty("Servers", out var serversElement) &&
            serversElement.ValueKind == JsonValueKind.Array
            ? serversElement.EnumerateArray()
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToArray()
            : Array.Empty<string>();
        var dhcp = root.TryGetProperty("Dhcp", out var dhcpElement)
            ? dhcpElement.GetString() ?? "Unknown"
            : "Unknown";

        return new DnsState(servers, dhcp);
    }

    public static void SetDnsServers(string adapterName, IEnumerable<string> servers)
    {
        var serverList = servers
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        if (serverList.Length == 0)
        {
            throw new InvalidOperationException("没有可写入的 DNS 地址。");
        }

        const string script = """
            $ErrorActionPreference = 'Stop'
            $servers = @($env:NAT64_SERVERS -split '\|' | Where-Object { $_ })
            Set-DnsClientServerAddress -InterfaceAlias $env:NAT64_ADAPTER -AddressFamily IPv6 -ServerAddresses $servers -ErrorAction Stop
            """;

        Execute(script, new Dictionary<string, string?>
        {
            ["NAT64_ADAPTER"] = adapterName,
            ["NAT64_SERVERS"] = string.Join("|", serverList)
        });
    }

    public static void ResetDnsServers(string adapterName)
    {
        const string script = """
            $ErrorActionPreference = 'Stop'
            Set-DnsClientServerAddress -InterfaceAlias $env:NAT64_ADAPTER -AddressFamily IPv6 -ResetServerAddresses -ErrorAction Stop
            """;

        Execute(script, new Dictionary<string, string?>
        {
            ["NAT64_ADAPTER"] = adapterName
        });
    }

    public static void ClearDnsCache()
    {
        const string script = """
            $ErrorActionPreference = 'Stop'
            Clear-DnsClientCache -ErrorAction Stop
            """;

        Execute(script, new Dictionary<string, string?>());
    }

    private static string Execute(string script, IReadOnlyDictionary<string, string?> environment)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = GetPowerShellPath(),
            Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value ?? string.Empty;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Windows PowerShell。");
        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(error) ? output : error;
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(details)
                    ? $"PowerShell 命令失败，退出码 {process.ExitCode}。"
                    : details);
        }

        return output;
    }

    private static string GetPowerShellPath()
    {
        var windowsPowerShell = Path.Combine(
            Environment.SystemDirectory,
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        return File.Exists(windowsPowerShell) ? windowsPowerShell : "pwsh.exe";
    }
}
