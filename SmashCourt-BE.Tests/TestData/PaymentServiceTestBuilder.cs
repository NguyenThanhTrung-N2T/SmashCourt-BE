using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Payments;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Tests.TestData;

internal sealed class PaymentServiceTestBuilder
{
    /// <summary>"Bây giờ" mặc định: 20/11/2026 09:00 giờ VN.</summary>
    public static readonly DateTime DefaultNow = new(2026, 11, 20, 2, 0, 0, DateTimeKind.Utc);

    public Mock<IPaymentRepository> Payments { get; } = new();
    public Mock<IBookingRepository> Bookings { get; } = new();
    public Mock<IPaymentGateway> Gateway { get; } = new();
    public Mock<IPaymentResultService> PaymentResults { get; } = new();
    public BookingPaymentSettings Settings { get; } = new();
    public FakeTimeProvider Time { get; } = new(DefaultNow);

    /// <summary>Các yêu cầu tạo link cổng đã nhận, theo thứ tự.</summary>
    public List<GatewayPaymentRequest> LinkRequests { get; } = [];

    /// <summary>Số tiền các link cổng đã tạo, theo thứ tự.</summary>
    public List<decimal> LinkAmounts => LinkRequests.Select(r => r.Amount).ToList();

    public PaymentServiceTestBuilder()
    {
        Gateway.Setup(x => x.Method).Returns(PaymentTxMethod.VNPAY);
        Gateway.Setup(x => x.CreatePaymentLink(It.IsAny<GatewayPaymentRequest>()))
            .Returns((GatewayPaymentRequest request) =>
            {
                LinkRequests.Add(request);
                return new GatewayPaymentLink("https://vnpay.test/retry", $"RETRY-{LinkRequests.Count}");
            });
        // Mặc định đơn vẫn còn PENDING khi ghi
        Bookings.Setup(x => x.ExtendPaymentWindowAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(true);
    }

    public PaymentService Build()
    {
        var settings = new Mock<IOptionsMonitor<BookingPaymentSettings>>();
        settings.Setup(x => x.CurrentValue).Returns(Settings);
        var gateways = new PaymentGatewayResolver([Gateway.Object]);

        return new PaymentService(
            Payments.Object,
            Bookings.Object,
            gateways,
            new OnlinePaymentStarter(gateways, Payments.Object),
            PaymentResults.Object,
            Mock.Of<ILogger<PaymentService>>(),
            settings.Object,
            Time);
    }
}
