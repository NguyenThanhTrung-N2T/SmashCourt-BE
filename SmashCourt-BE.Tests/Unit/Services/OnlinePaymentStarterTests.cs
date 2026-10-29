using Moq;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Payments;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// Chọn cổng theo phương thức và tạo giao dịch online: link lấy từ đúng cổng, Payment PENDING ghi đúng phương thức và mã giao dịch.
/// Cổng nhận hạn link (P6-27); cổng không hỗ trợ hạn link thì lỗi lan ra, không có Payment (R27, plan 6A-6 Task 3).
/// </summary>
[Trait("Category", TestCategories.Payment)]
public class OnlinePaymentStarterTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 2, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LinkExpiresAt = Now.AddMinutes(8);

    private static Mock<IPaymentGateway> Gateway(PaymentTxMethod method, string url)
    {
        var gateway = new Mock<IPaymentGateway>();
        gateway.Setup(x => x.Method).Returns(method);
        gateway.Setup(x => x.CreatePaymentLink(It.IsAny<GatewayPaymentRequest>()))
            .Returns(new GatewayPaymentLink(url, $"{method}-REF"));
        return gateway;
    }

    [Fact]
    public void Resolver_PicksTheGatewayOfTheMethod()
    {
        var vnPay = Gateway(PaymentTxMethod.VNPAY, "https://vnpay");
        var momo = Gateway(PaymentTxMethod.MOMO, "https://momo");
        var resolver = new PaymentGatewayResolver([vnPay.Object, momo.Object]);

        Assert.Same(momo.Object, resolver.Get(PaymentTxMethod.MOMO));
        Assert.Same(vnPay.Object, resolver.Get(PaymentTxMethod.VNPAY));
    }

    [Fact]
    public void Resolver_MethodWithoutGateway_IsAConfigurationError()
    {
        var resolver = new PaymentGatewayResolver([Gateway(PaymentTxMethod.VNPAY, "https://vnpay").Object]);

        var error = Assert.Throws<InvalidOperationException>(() => resolver.Get(PaymentTxMethod.MOMO));
        Assert.Contains("MOMO", error.Message);
    }

    [Fact]
    public void Resolver_TwoGatewaysForOneMethod_FailsWhenBuiltInsteadOfPickingOne()
    {
        // Resolver là Scoped: lỗi đăng ký trùng lộ ra ở request đầu tiên cần thanh toán, không âm thầm chọn một cổng
        var first = Gateway(PaymentTxMethod.VNPAY, "https://vnpay-1").Object;
        var second = Gateway(PaymentTxMethod.VNPAY, "https://vnpay-2").Object;

        Assert.Throws<ArgumentException>(() => new PaymentGatewayResolver([first, second]));
    }

    [Fact]
    public async Task Start_CreatesTheLinkAndAPendingPaymentForThatGateway()
    {
        var momo = Gateway(PaymentTxMethod.MOMO, "https://momo/pay");
        var payments = new Mock<IPaymentRepository>();
        var starter = new OnlinePaymentStarter(
            new PaymentGatewayResolver([Gateway(PaymentTxMethod.VNPAY, "https://vnpay").Object, momo.Object]), payments.Object);
        var invoiceId = Guid.NewGuid();

        var url = await starter.StartAsync(invoiceId, "booking-1", 60_000m, "Dat san", PaymentTxMethod.MOMO, Now, LinkExpiresAt);

        Assert.Equal("https://momo/pay", url);
        momo.Verify(x => x.CreatePaymentLink(new GatewayPaymentRequest("booking-1", 60_000m, "Dat san", Now, LinkExpiresAt)), Times.Once);
        payments.Verify(x => x.CreateAsync(It.Is<Payment>(p =>
            p.InvoiceId == invoiceId && p.Method == PaymentTxMethod.MOMO && p.Amount == 60_000m
            && p.Status == PaymentTxStatus.PENDING && p.TransactionRef == "MOMO-REF" && p.CreatedAt == Now)), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Start_NonPositiveAmount_IsRejectedBeforeCallingTheGateway(int amount)
    {
        var vnPay = Gateway(PaymentTxMethod.VNPAY, "https://vnpay");
        var starter = new OnlinePaymentStarter(new PaymentGatewayResolver([vnPay.Object]), Mock.Of<IPaymentRepository>());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => starter.StartAsync(Guid.NewGuid(), "booking-1", amount, "Dat san", PaymentTxMethod.VNPAY, Now, LinkExpiresAt));
        vnPay.Verify(x => x.CreatePaymentLink(It.IsAny<GatewayPaymentRequest>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Start_LinkExpiryNotAfterNow_IsRejectedBeforeCallingTheGateway(int secondsAfterNow)
    {
        // Caller đã qua PaymentHoldPolicy.TryGetPaymentLinkExpiry; tới đây mà hạn link không sau now là lỗi lập trình
        var vnPay = Gateway(PaymentTxMethod.VNPAY, "https://vnpay");
        var payments = new Mock<IPaymentRepository>();
        var starter = new OnlinePaymentStarter(new PaymentGatewayResolver([vnPay.Object]), payments.Object);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => starter.StartAsync(
            Guid.NewGuid(), "booking-1", 60_000m, "Dat san", PaymentTxMethod.VNPAY, Now, Now.AddSeconds(secondsAfterNow)));
        vnPay.Verify(x => x.CreatePaymentLink(It.IsAny<GatewayPaymentRequest>()), Times.Never);
        payments.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
    }

    [Fact]
    public async Task Start_GatewayThatCannotHonourTheExpiry_FailsWithoutAPayment()
    {
        // R27: không có link nào không hạn; lỗi cấu hình lan ra (ExceptionMiddleware ⇒ 500), transaction của caller rollback
        var gateway = new Mock<IPaymentGateway>();
        gateway.Setup(x => x.Method).Returns(PaymentTxMethod.MOMO);
        gateway.Setup(x => x.CreatePaymentLink(It.IsAny<GatewayPaymentRequest>()))
            .Throws(new NotSupportedException("Cổng không hỗ trợ hạn link"));
        var payments = new Mock<IPaymentRepository>();
        var starter = new OnlinePaymentStarter(new PaymentGatewayResolver([gateway.Object]), payments.Object);

        await Assert.ThrowsAsync<NotSupportedException>(() => starter.StartAsync(
            Guid.NewGuid(), "booking-1", 60_000m, "Dat san", PaymentTxMethod.MOMO, Now, LinkExpiresAt));
        payments.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
    }
}
