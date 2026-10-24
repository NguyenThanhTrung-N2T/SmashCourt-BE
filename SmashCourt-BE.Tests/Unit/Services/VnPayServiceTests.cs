using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services;
using VNPAY;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// Cổng VNPay chuẩn hóa kết quả: kiểm chữ ký HMAC-SHA512, quy đổi vnp_Amount (× 100) ra VND, mã 00 là thành công.
/// </summary>
public class VnPayServiceTests
{
    private const string Secret = "test-secret";

    private static VnPayService CreateService()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VnPay:HashSecret"] = Secret
        }).Build();
        return new VnPayService(configuration, new HttpContextAccessor(), Mock.Of<IVnpayClient>(), Mock.Of<ILogger<VnPayService>>());
    }

    // Ký đúng cách VNPay: các khóa vnp_ theo thứ tự ordinal, giá trị URL-encode, HMAC-SHA512 bằng HashSecret
    private static QueryCollection Signed(Dictionary<string, string> values)
    {
        var data = string.Join("&", values
            .Where(kv => kv.Key.StartsWith("vnp_") && !string.IsNullOrEmpty(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={WebUtility.UrlEncode(kv.Value)}"));
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(Secret));
        var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();

        var query = values.ToDictionary(kv => kv.Key, kv => new StringValues(kv.Value));
        query["vnp_SecureHash"] = hash;
        return new QueryCollection(query);
    }

    private static Dictionary<string, string> Values(string responseCode = "00", string? amount = "6000000") => new()
    {
        ["vnp_TxnRef"] = "order-1",
        ["vnp_ResponseCode"] = responseCode,
        ["vnp_Amount"] = amount ?? string.Empty,
        ["vnp_OrderInfo"] = "Dat san San Mot"
    };

    [Fact]
    public void Method_IsVnPay()
    {
        Assert.Equal(PaymentTxMethod.VNPAY, CreateService().Method);
    }

    [Fact]
    public void ReadResult_SignedSuccess_IsValidAndConvertsTheAmount()
    {
        var result = CreateService().ReadResult(Signed(Values()));

        Assert.True(result.IsSignatureValid);
        Assert.True(result.IsSuccess);
        Assert.Equal(60_000m, result.Amount);
        Assert.Equal("order-1", result.TransactionRef);
        Assert.Equal("00", result.ResponseCode);
        Assert.Equal(IpnProvider.VNPAY, result.Provider);
    }

    [Theory]
    [InlineData("24")]
    [InlineData("51")]
    public void ReadResult_SignedNonZeroCode_IsFailure(string responseCode)
    {
        var result = CreateService().ReadResult(Signed(Values(responseCode)));

        Assert.True(result.IsSignatureValid);
        Assert.False(result.IsSuccess);
        Assert.Equal(responseCode, result.ResponseCode);
    }

    [Fact]
    public void ReadResult_InvalidHash_IsNotValid()
    {
        var values = Values().ToDictionary(kv => kv.Key, kv => new StringValues(kv.Value));
        values["vnp_SecureHash"] = "invalid";

        var result = CreateService().ReadResult(new QueryCollection(values));

        Assert.False(result.IsSignatureValid);
        Assert.Equal("order-1", result.TransactionRef);
    }

    [Fact]
    public void ReadResult_TamperedAmountAfterSigning_IsNotValid()
    {
        var signed = new Dictionary<string, StringValues>(Signed(Values())) { ["vnp_Amount"] = "100" };

        Assert.False(CreateService().ReadResult(new QueryCollection(signed)).IsSignatureValid);
    }

    [Fact]
    public void ReadResult_MissingAmount_IsNull()
    {
        var result = CreateService().ReadResult(Signed(Values(amount: null)));

        Assert.Null(result.Amount);
    }
}
