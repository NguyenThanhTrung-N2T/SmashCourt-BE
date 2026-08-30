using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;
using VNPAY;

namespace SmashCourt_BE.Tests.Services;

public class VnPayServiceTests
{
    [Fact]
    public void VerifyIpn_InvalidSignatureReturnsFalse()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VnPay:HashSecret"] = "test-secret"
        }).Build();
        var service = new VnPayService(configuration, new HttpContextAccessor(), Mock.Of<IVnpayClient>(), Mock.Of<ILogger<VnPayService>>());
        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["vnp_TxnRef"] = "order-1",
            ["vnp_ResponseCode"] = "00",
            ["vnp_SecureHash"] = "invalid"
        });

        var result = service.VerifyIpn(query, out var reference, out var success, out _);

        Assert.False(result);
        Assert.Equal("order-1", reference);
        Assert.True(success);
    }
}
