using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Nat64Switch;

internal static class ConnectivityTester
{
    public static async Task<DomainCheck> CheckAsync(
        string domain,
        bool connect,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            var addresses = await Dns.GetHostAddressesAsync(domain, timeoutSource.Token);

            IPAddress? selected = addresses.FirstOrDefault(address =>
                address.AddressFamily == AddressFamily.InterNetworkV6);
            IPAddress? connected = null;
            TimeSpan? connectTime = null;

            if (connect && selected is not null)
            {
                var stopwatch = Stopwatch.StartNew();
                using var client = new TcpClient(AddressFamily.InterNetworkV6);
                await client.ConnectAsync(selected, 443, timeoutSource.Token);
                stopwatch.Stop();
                connected = selected;
                connectTime = stopwatch.Elapsed;
            }

            return new DomainCheck(domain, addresses, connected, connectTime, null);
        }
        catch (Exception ex)
        {
            return new DomainCheck(domain, Array.Empty<IPAddress>(), null, null, ex.Message);
        }
    }
}
