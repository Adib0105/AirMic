using System.Net;
using AirMic.Core.Networking;
using AirMic.Core.Security;

namespace AirMic.Tests;

public sealed class PairingAndNetworkTests
{
    [Fact]
    public void ValidPinSucceedsAndInvalidPinFails()
    {
        var pins = new PairingPinService();
        Assert.False(pins.TryValidate("192.168.1.5", "00000x", out _));
        Assert.True(pins.TryValidate("192.168.1.5", pins.CurrentPin, out _));
    }

    [Fact]
    public void PinAttemptsAreRateLimited()
    {
        var pins = new PairingPinService();
        TimeSpan retry = default;
        for (var i = 0; i < 5; i++) Assert.False(pins.TryValidate("192.168.1.5", "not-pin", out retry));
        Assert.True(retry > TimeSpan.Zero);
        Assert.False(pins.TryValidate("192.168.1.5", pins.CurrentPin, out retry));
        Assert.True(retry > TimeSpan.Zero);
    }

    [Theory]
    [InlineData("192.168.1.4", true)]
    [InlineData("10.4.5.6", true)]
    [InlineData("172.20.1.1", true)]
    [InlineData("8.8.8.8", false)]
    public void LanPolicyRejectsPublicAddresses(string text, bool expected) =>
        Assert.Equal(expected, NetworkPolicy.IsTrustedLan(IPAddress.Parse(text)));
}
