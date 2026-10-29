using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Tests.TestData;
using VNPAY;
using VNPAY.Extensions;
using VNPAY.Models;
using VNPAY.Models.Enums;

namespace SmashCourt_BE.Tests.Unit.Helpers;

/// <summary>
/// VnPayHelper (P6-27, plan 6A-6 Task 2): một nơi ký cho cả chiều tạo link lẫn chiều kiểm IPN. Bằng chứng mức 1 của D3: với cùng tập
/// tham số, chuỗi ký, chữ ký và URL phải trùng từng ký tự với thư viện VNPAY.NET 2.1.0 (thư viện chỉ còn ở project test để đối chứng).
/// Giờ gửi VNPay là GMT+7 (T-5).
/// </summary>
[Trait("Category", TestCategories.Payment)]
[Trait("Category", TestCategories.Helper)]
public class VnPayHelperTests
{
    private const string Secret = "TESTSECRET";
    private const string BaseUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";

    // URL do VNPAY.NET 2.1.0 sinh ngày 30/10/2026 với TmnCode TESTTMN1, HashSecret TESTSECRET (khóa giả), IP 203.0.113.7:
    // vector cố định, độc lập với mã của BE, khóa cách ký khi thư viện không còn trong BE
    private const string LibrarySignData =
        "vnp_Amount=5000000&vnp_Command=pay&vnp_CreateDate=20261029173941&vnp_CurrCode=VND&vnp_IpAddr=203.0.113.7&vnp_Locale=vn" +
        "&vnp_OrderInfo=Dat+san+San+Mot%2C+tang+2.&vnp_OrderType=other" +
        "&vnp_ReturnUrl=https%3A%2F%2Fexample.test%2Fapi%2Fpayments%2Fvnpay%2Fcallback%3Fa%3D1%26b%3D2" +
        "&vnp_TmnCode=TESTTMN1&vnp_TxnRef=639288923814173716&vnp_Version=2.1.0";
    private const string LibraryHash =
        "CEED8FDC1AF05CEAB822E0B2261280E2091C7199550027A7789F1EDED7C6CBB3B79B403A108885119494C206C160F066012C26F609C390A4BE20206638DF3194";

    private static IVnpayClient LibraryClient(string callbackUrl)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = http });
        services.AddVnpayClient(c =>
        {
            c.TmnCode = "TESTTMN1";
            c.HashSecret = Secret;
            c.CallbackUrl = callbackUrl;
        });
        return services.BuildServiceProvider().GetRequiredService<IVnpayClient>();
    }

    [Theory]
    [InlineData("Dat san San Mot", "https://example.test/api/payments/vnpay/callback", BankCode.ANY)]
    [InlineData("Dat san San Mot, tang 2.", "https://example.test/api/payments/vnpay/callback?a=1&b=2", BankCode.ANY)]
    [InlineData("Thanh toan lai San A, San B", "http://localhost:5179/api/payments/vnpay/callback", BankCode.VNBANK)]
    public void BuildPaymentUrl_SameParametersAsLibrary_ProducesIdenticalUrl(string orderInfo, string callbackUrl, BankCode bankCode)
    {
        var library = LibraryClient(callbackUrl).CreatePaymentUrl(50_000, orderInfo, bankCode);
        var parameters = library.Parameters.ToDictionary(kv => kv.Key, kv => kv.Value);

        var url = VnPayHelper.BuildPaymentUrl(BaseUrl, parameters, Secret);

        Assert.Equal(library.Url, url);
    }

    [Fact]
    public void Sign_KnownLibraryVector_MatchesTheLibraryHash()
    {
        Assert.Equal(LibraryHash, VnPayHelper.Sign(LibrarySignData, Secret));
    }

    [Fact]
    public void BuildSignData_KnownLibraryVector_MatchesTheLibraryQuery()
    {
        var parameters = new Dictionary<string, string>
        {
            ["vnp_Version"] = "2.1.0",
            ["vnp_TxnRef"] = "639288923814173716",
            ["vnp_TmnCode"] = "TESTTMN1",
            ["vnp_ReturnUrl"] = "https://example.test/api/payments/vnpay/callback?a=1&b=2",
            ["vnp_OrderType"] = "other",
            ["vnp_OrderInfo"] = "Dat san San Mot, tang 2.",
            ["vnp_Locale"] = "vn",
            ["vnp_IpAddr"] = "203.0.113.7",
            ["vnp_CurrCode"] = "VND",
            ["vnp_CreateDate"] = "20261029173941",
            ["vnp_Command"] = "pay",
            ["vnp_Amount"] = "5000000"
        };

        Assert.Equal(LibrarySignData, VnPayHelper.BuildSignData(parameters));
        Assert.Equal($"{BaseUrl}?{LibrarySignData}&vnp_SecureHash={LibraryHash}", VnPayHelper.BuildPaymentUrl(BaseUrl, parameters, Secret));
    }

    [Fact]
    public void Sign_IsUppercaseHexOfHmacSha512()
    {
        var hash = VnPayHelper.Sign("vnp_Amount=100", Secret);

        Assert.Equal(128, hash.Length);
        Assert.Matches("^[0-9A-F]{128}$", hash);
        Assert.NotEqual(hash, VnPayHelper.Sign("vnp_Amount=100", Secret + "x"));
        Assert.NotEqual(hash, VnPayHelper.Sign("vnp_Amount=101", Secret));
    }

    [Fact]
    public void BuildSignData_SkipsEmptyValues()
    {
        // Giống VnPayLibrary: tham số rỗng không nằm trong chuỗi ký (IPN có thể gửi vnp_BankTranNo rỗng)
        var data = VnPayHelper.BuildSignData(new Dictionary<string, string>
        {
            ["vnp_Amount"] = "100",
            ["vnp_BankCode"] = "",
            ["vnp_Locale"] = "vn"
        });

        Assert.Equal("vnp_Amount=100&vnp_Locale=vn", data);
    }

    [Fact]
    public void BuildSignData_SortsKeysOrdinally()
    {
        // Ordinal: chữ hoa (0x41–0x5A) đứng trước gạch dưới (0x5F) và chữ thường; không theo văn hóa
        var data = VnPayHelper.BuildSignData(new Dictionary<string, string>
        {
            ["vnp_amount"] = "3",
            ["vnp_Amount"] = "1",
            ["vnp_Amount_"] = "2"
        });

        Assert.Equal("vnp_Amount=1&vnp_Amount_=2&vnp_amount=3", data);
    }

    [Fact]
    public void BuildSignData_FormEncodesValues()
    {
        var data = VnPayHelper.BuildSignData(new Dictionary<string, string>
        {
            ["vnp_OrderInfo"] = "a b,c/d:e?f&g=h+i.j-k_l",
            ["vnp_Text"] = "Đặt sân"
        });

        Assert.Equal("vnp_OrderInfo=a+b%2Cc%2Fd%3Ae%3Ff%26g%3Dh%2Bi.j-k_l&vnp_Text=%C4%90%E1%BA%B7t+s%C3%A2n", data);
    }

    [Fact]
    public void BuildPaymentUrl_EmptyValuesAreLeftOutOfTheUrl()
    {
        var url = VnPayHelper.BuildPaymentUrl(BaseUrl, new Dictionary<string, string>
        {
            ["vnp_Amount"] = "100",
            ["vnp_BankCode"] = ""
        }, Secret);

        Assert.Equal($"{BaseUrl}?vnp_Amount=100&vnp_SecureHash={VnPayHelper.Sign("vnp_Amount=100", Secret)}", url);
    }

    [Theory]
    [Trait("Catalog", "BE-UT-12")]
    [InlineData("2026-11-20T16:59:59Z", "20261120235959")] // 23:59:59 giờ VN
    [InlineData("2026-11-20T17:00:00Z", "20261121000000")] // 00:00:00 giờ VN ngày sau
    [InlineData("2026-12-31T17:00:00Z", "20270101000000")] // qua năm
    [InlineData("2026-11-20T03:00:00.9999999Z", "20261120100000")] // cắt phần lẻ giây, không làm tròn lên
    public void FormatVietnamTime_ConvertsUtcToGmtPlus7(string utc, string expected)
    {
        var value = DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

        Assert.Equal(expected, VnPayHelper.FormatVietnamTime(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
    }

    [Theory]
    [Trait("Catalog", "BE-UT-12")]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void FormatVietnamTime_NonUtc_Throws(DateTimeKind kind)
    {
        Assert.Throws<ArgumentException>(() =>
            VnPayHelper.FormatVietnamTime(DateTime.SpecifyKind(new DateTime(2026, 11, 20, 3, 0, 0), kind)));
    }
}
