using System.Net;
using System.Net.Sockets;
using I76.Net;
using Xunit;

namespace I76.Net.Tests;

/// <summary>
/// The LAN search asks every network's own broadcast address as well as 255.255.255.255. Measured on a home network with three
/// real machines (the report: "can't see my network game on lan", either way round): a Mac hosting a game answered a query to
/// its own address and one to 192.168.1.255, from two Windows machines, and none to 255.255.255.255, the only address the search
/// used; yesterday's build searched the same way and missed it the same way.
/// </summary>
public class LanSearchTests
{
    [Theory]
    [InlineData("192.168.1.81", "255.255.255.0", "192.168.1.255")]
    [InlineData("10.5.0.2", "255.255.0.0", "10.5.255.255")]
    [InlineData("172.28.224.1", "255.255.240.0", "172.28.239.255")]
    [InlineData("192.168.1.81", "255.255.255.252", "192.168.1.83")]
    public void ANetworksBroadcastAddressIsItsLastOne(string address, string mask, string expected) =>
        Assert.Equal(IPAddress.Parse(expected), Discovery.SubnetBroadcast(IPAddress.Parse(address), IPAddress.Parse(mask)));

    /// <summary>A VPN's single address (/32), a point-to-point pair (/31), no mask at all and IPv6 have no broadcast address to ask.</summary>
    [Theory]
    [InlineData("100.113.196.47", "255.255.255.255")]
    [InlineData("10.0.0.2", "255.255.255.254")]
    [InlineData("192.168.1.81", "0.0.0.0")]
    [InlineData("192.168.1.81", null)]
    [InlineData("fe80::1", "255.255.255.0")]
    public void AnAddressWithNoNetworkHasNone(string address, string? mask) =>
        Assert.Null(Discovery.SubnetBroadcast(IPAddress.Parse(address), mask is null ? null : IPAddress.Parse(mask)));

    /// <summary>This machine's list holds no loopback, link-local or limited-broadcast address, and nothing twice.</summary>
    [Fact]
    public void ThisMachinesNetworksAreRealOnes()
    {
        var all = Discovery.SubnetBroadcasts();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(all, a =>
        {
            var b = a.GetAddressBytes();
            Assert.Equal(AddressFamily.InterNetwork, a.AddressFamily);
            Assert.False(b[0] == 127 || (b[0] == 169 && b[1] == 254) || a.Equals(IPAddress.Broadcast), a.ToString());
        });
    }

    /// <summary>
    /// A search sends the query to each of its networks' addresses on every searched port, as a plain unconnected message (which
    /// a server answers like the broadcast). Here the "network" is a second loopback address with a socket on it.
    /// </summary>
    [Fact]
    public void ASearchAsksEveryNetworksOwnAddress()
    {
        var net = IPAddress.Parse("127.0.0.2");
        using var there = new UdpClient(new IPEndPoint(net, 0));
        there.Client.ReceiveTimeout = 3000;
        int port = ((IPEndPoint)there.Client.LocalEndPoint!).Port;
        using var browser = new SessionBrowser { Subnets = () => [net] };
        browser.SearchLan(port - 1);   // the second of the ports searched
        var from = new IPEndPoint(IPAddress.Loopback, 0);
        // (on Windows the limited broadcast of a socket on loopback arrives here too, as LiteNetLib's Broadcast, 12: passed over)
        var got = there.Receive(ref from);
        for (int i = 0; i < 4 && (got[0] & 0x1F) != 9; i++) got = there.Receive(ref from);
        Assert.Equal(browser.LocalPort, from.Port);
        Assert.Equal(9, got[0] & 0x1F);   // LiteNetLib's UnconnectedMessage
        Assert.True(Discovery.IsQuery(got.AsSpan(1)));
        Assert.Equal(Discovery.Query(), got[1..]);
    }

    /// <summary>A browser the tests keep on loopback asks no real network (no datagram leaves the machine from a test).</summary>
    [Fact]
    public void ABrowserOnLoopbackAsksNoRealNetwork()
    {
        using var browser = new SessionBrowser();
        Assert.Empty(browser.Subnets());
    }
}
