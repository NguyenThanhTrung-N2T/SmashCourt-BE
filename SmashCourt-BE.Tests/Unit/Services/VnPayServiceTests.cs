using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Payments;
using SmashCourt_BE.Services;
using VNPAY;
using VNPAY.Extensions;
using VNPAY.Models.Enums;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// Cổng VNPay chuẩn hóa kết quả: kiểm chữ ký HMAC-SHA512, quy đổi vnp_Amount (× 100) ra VND, mã 00 là thành công.
/// Tạo link (P6-27, plan 6A-6 Task 3): BE tự dựng URL đã ký, có vnp_ExpireDate, hai mốc theo GMT+7, vnp_TxnRef do BE sinh;
/// các tham số chung trùng thư viện VNPAY.NET 2.1.0 (bằng chứng mức 2 của D3).
/// </summary>
public class VnPayServiceTests
{
    private const string Secret = "test-secret";
    private const string TmnCode = "TESTTMN1";
    private const string CallbackUrl = "https://example.test/api/payments/vnpay/callback?a=1&b=2";
    private const string ClientIp = "203.0.113.7";
    private static readonly DateTime Created = new(2026, 11, 20, 16, 58, 0, DateTimeKind.Utc); // 23:58 giờ VN

    private static VnPayService CreateService()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VnPay:HashSecret"] = Secret,
            ["VnPay:TmnCode"] = TmnCode,
            ["VnPay:CallbackUrl"] = CallbackUrl
        }).Build();
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(ClientIp);
        return new VnPayService(configuration, new HttpContextAccessor { HttpContext = http }, Mock.Of<ILogger<VnPayService>>());
    }

    private static GatewayPaymentRequest Request(decimal amount = 60_000m, DateTime? created = null, DateTime? expires = null) =>
        new("booking-1", amount, "Dat san San Mot", created ?? Created, expires ?? (created ?? Created).AddMinutes(8));

    // Tham số của URL ở dạng đã mã hóa, đúng như trên URL (khóa → giá trị chưa giải mã)
    private static Dictionary<string, string> RawQuery(string url) =>
        new Uri(url).Query.TrimStart('?').Split('&')
            .Select(part => part.Split('=', 2))
            .ToDictionary(kv => kv[0], kv => kv[1]);

    private static QueryCollection DecodedQuery(string url) =>
        new(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(url).Query));

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
    public void ReadResult_SignedWithSharedHelperInUppercase_IsValid()
    {
        // Chiều IPN và chiều tạo link ký bằng cùng VnPayHelper; VNPay so chữ ký không phân biệt hoa thường nên hex hoa cũng đúng
        var values = Values();
        var query = values.ToDictionary(kv => kv.Key, kv => new StringValues(kv.Value));
        query["vnp_SecureHash"] = SmashCourt_BE.Helpers.VnPayHelper.Sign(SmashCourt_BE.Helpers.VnPayHelper.BuildSignData(values), Secret);

        Assert.True(CreateService().ReadResult(new QueryCollection(query)).IsSignatureValid);
    }

    [Fact]
    public void ReadResult_SecureHashTypeAndNonVnpKeysAreNotSigned()
    {
        // vnp_SecureHashType và tham số lạ (không tiền tố vnp_) không nằm trong chuỗi ký, như VnPayLibrary
        var signed = new Dictionary<string, StringValues>(Signed(Values()))
        {
            ["vnp_SecureHashType"] = "HmacSHA512",
            ["utm_source"] = "mail"
        };

        Assert.True(CreateService().ReadResult(new QueryCollection(signed)).IsSignatureValid);
    }

    [Fact]
    public void ReadResult_EmptyValueIsNotSigned()
    {
        // VNPay gửi vnp_BankTranNo rỗng khi khách hủy: giá trị rỗng không nằm trong chuỗi ký
        var values = Values();
        var signed = new Dictionary<string, StringValues>(Signed(values)) { ["vnp_BankTranNo"] = "" };

        Assert.True(CreateService().ReadResult(new QueryCollection(signed)).IsSignatureValid);
    }

    [Fact]
    public void ReadResult_MissingAmount_IsNull()
    {
        var result = CreateService().ReadResult(Signed(Values(amount: null)));

        Assert.Null(result.Amount);
    }

    // ───────────────────────── Tạo link (P6-27, T-5) ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public void CreatePaymentLink_SendsCreateAndExpireDateInVietnamTime()
    {
        // Tạo 16:58 UTC = 23:58 giờ VN; hạn 17:06 UTC = 00:06 giờ VN ngày hôm sau
        var link = CreateService().CreatePaymentLink(Request(expires: Created.AddMinutes(8)));

        var query = RawQuery(link.Url);
        Assert.Equal("20261120235800", query["vnp_CreateDate"]);
        Assert.Equal("20261121000600", query["vnp_ExpireDate"]);
    }

    [Fact]
    public void CreatePaymentLink_AmountIsVndTimesHundred()
    {
        var link = CreateService().CreatePaymentLink(Request(amount: 60_000m));

        Assert.Equal("6000000", RawQuery(link.Url)["vnp_Amount"]);
    }

    [Fact]
    public void CreatePaymentLink_TxnRefIsGeneratedByTheBackendAndReturned()
    {
        var service = CreateService();

        var links = Enumerable.Range(0, 100).Select(_ => service.CreatePaymentLink(Request())).ToList();

        Assert.All(links, l => Assert.Matches("^[0-9a-f]{32}$", l.TransactionRef));
        Assert.All(links, l => Assert.Equal(l.TransactionRef, RawQuery(l.Url)["vnp_TxnRef"]));
        Assert.Equal(100, links.Select(l => l.TransactionRef).Distinct().Count());
    }

    [Fact]
    public void CreatePaymentLink_UsesTheConfiguredBaseUrlAndCallback()
    {
        var link = CreateService().CreatePaymentLink(Request());

        Assert.StartsWith("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?", link.Url);
        Assert.Equal(CallbackUrl, DecodedQuery(link.Url)["vnp_ReturnUrl"].ToString());
        Assert.Equal(TmnCode, DecodedQuery(link.Url)["vnp_TmnCode"].ToString());
    }

    [Fact]
    public void CreatePaymentLink_SignatureVerifiesWithTheSameGatewayAndBreaksWhenTheDeadlineIsChanged()
    {
        var service = CreateService();
        var link = service.CreatePaymentLink(Request());
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(link.Url).Query);

        Assert.True(service.ReadResult(new QueryCollection(query)).IsSignatureValid);

        // Kéo dài hạn link trên URL ⇒ chữ ký không còn khớp (VNPay từ chối)
        query["vnp_ExpireDate"] = "20991231235959";
        Assert.False(service.ReadResult(new QueryCollection(query)).IsSignatureValid);
    }

    [Fact]
    public void CreatePaymentLink_CommonParametersMatchTheLibrary()
    {
        // Bằng chứng mức 2 của D3: cùng cấu hình, IP, số tiền và nội dung ⇒ mọi tham số chung trùng thư viện ở dạng đã mã hóa;
        // khác có chủ đích: vnp_CreateDate (GMT+7), vnp_TxnRef (BE sinh), vnp_ExpireDate (mới), chữ ký
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(ClientIp);
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = http });
        services.AddVnpayClient(c =>
        {
            c.TmnCode = TmnCode;
            c.HashSecret = Secret;
            c.CallbackUrl = CallbackUrl;
        });
        var library = RawQuery(services.BuildServiceProvider().GetRequiredService<IVnpayClient>()
            .CreatePaymentUrl(60_000, "Dat san San Mot", BankCode.ANY).Url);

        var ours = RawQuery(CreateService().CreatePaymentLink(Request(amount: 60_000m)).Url);

        string[] differ = ["vnp_CreateDate", "vnp_TxnRef", "vnp_ExpireDate", "vnp_SecureHash"];
        Assert.Equal(library.Keys.Append("vnp_ExpireDate").Order(StringComparer.Ordinal), ours.Keys.Order(StringComparer.Ordinal));
        foreach (var key in library.Keys.Except(differ))
            Assert.True(library[key] == ours[key], $"{key}: thư viện {library[key]}, BE {ours[key]}");
        Assert.Equal(library["vnp_SecureHash"].Length, ours["vnp_SecureHash"].Length);
    }

    [Fact]
    public void CreatePaymentLink_ExpiryNotAfterCreation_Throws()
    {
        var service = CreateService();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.CreatePaymentLink(Request(expires: Created)));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.CreatePaymentLink(Request(expires: Created.AddSeconds(-1))));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void CreatePaymentLink_NonUtcTimes_Throw(DateTimeKind kind)
    {
        var service = CreateService();
        var notUtc = DateTime.SpecifyKind(Created, kind);

        Assert.Throws<ArgumentException>(() => service.CreatePaymentLink(Request(created: notUtc, expires: Created.AddMinutes(8))));
        Assert.Throws<ArgumentException>(() => service.CreatePaymentLink(Request(expires: DateTime.SpecifyKind(Created.AddMinutes(8), kind))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000.5)]
    public void CreatePaymentLink_AmountMustBeAPositiveWholeVnd(double amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateService().CreatePaymentLink(Request(amount: (decimal)amount)));
    }
}
