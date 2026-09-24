using System.Net;
using System.Net.NetworkInformation;

namespace Nat64Switch;

internal static class AdapterCatalog
{
    public static IReadOnlyList<AdapterInfo> GetAll()
    {
        var result = new List<AdapterInfo>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var properties = networkInterface.GetIPProperties();
            var ipv6Addresses = properties.UnicastAddresses
                .Select(item => item.Address)
                .Where(IsGlobalIpv6)
                .Distinct()
                .ToArray();
            var dnsServers = properties.DnsAddresses
                .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                .Distinct()
                .ToArray();
            var gateways = properties.GatewayAddresses
                .Select(item => item.Address)
                .Where(IsUsableGateway)
                .Distinct()
                .ToArray();

            result.Add(new AdapterInfo(
                networkInterface.Name,
                networkInterface.Description,
                networkInterface.NetworkInterfaceType,
                networkInterface.OperationalStatus,
                ipv6Addresses,
                dnsServers,
                gateways));
        }

        return result
            .OrderByDescending(item => item.Status == OperationalStatus.Up)
            .ThenByDescending(item => item.HasGlobalIpv6)
            .ThenByDescending(item => item.Gateways.Count > 0)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static AdapterInfo? Find(string name)
    {
        return GetAll().FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Description, name, StringComparison.OrdinalIgnoreCase));
    }

    public static AdapterInfo? AutoSelect()
    {
        var candidates = GetAll()
            .Where(item => item.Status == OperationalStatus.Up)
            .Where(item => item.HasGlobalIpv6 || item.Gateways.Count > 0)
            .ToArray();

        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        var preferred = candidates
            .Where(item => item.HasGlobalIpv6 && item.Gateways.Count > 0)
            .ToArray();

        return preferred.Length == 1 ? preferred[0] : null;
    }

    private static bool IsGlobalIpv6(IPAddress address)
    {
        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
            !address.IsIPv6LinkLocal &&
            !address.IsIPv6Multicast &&
            !IPAddress.IsLoopback(address);
    }

    private static bool IsUsableGateway(IPAddress address)
    {
        return !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any);
    }
}
