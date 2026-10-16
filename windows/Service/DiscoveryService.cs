using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClipboardBridge;

public sealed class DiscoveryService(PairingHub pairing) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 45872));
        while (!stoppingToken.IsCancellationRequested)
        {
            var packet = await udp.ReceiveAsync(stoppingToken);
            try
            {
                var reply = pairing.Discovery(packet.Buffer, packet.RemoteEndPoint.Address);
                if (reply is not null) await udp.SendAsync(reply, packet.RemoteEndPoint, stoppingToken);
            }
            catch (Exception e) when (e is JsonException or FormatException or InvalidDataException or CryptographicException or KeyNotFoundException or InvalidOperationException or ArgumentException or NullReferenceException) { /* Ignore untrusted or incompatible datagrams; never log keys or payloads. */ }
        }
    }
}
