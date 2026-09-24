using System.Net;
using System.Net.NetworkInformation;

namespace Nat64Switch;

internal sealed record AdapterInfo(
    string Name,
    string Description,
    NetworkInterfaceType Type,
    OperationalStatus Status,
    IReadOnlyList<IPAddress> Ipv6Addresses,
    IReadOnlyList<IPAddress> DnsServers,
    IReadOnlyList<IPAddress> Gateways)
{
    public bool HasGlobalIpv6 => Ipv6Addresses.Count > 0;

    public string TypeLabel => Type switch
    {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => "以太网",
        _ => "其他"
    };

    public string ShortLabel => $"{Name} ({TypeLabel}, {Status})";
}

internal sealed record DnsState(IReadOnlyList<string> Servers, string Dhcp);

internal sealed record Dns64Server(string Address, string Provider, string Location);

internal sealed record DomainCheck(
    string Domain,
    IReadOnlyList<IPAddress> Addresses,
    IPAddress? ConnectedAddress,
    TimeSpan? ConnectTime,
    string? Error);

internal sealed record StatusPayload(
    string Adapter,
    string Description,
    string Status,
    string[] Ipv6,
    string[] Dns,
    string Dhcp,
    bool Administrator);
