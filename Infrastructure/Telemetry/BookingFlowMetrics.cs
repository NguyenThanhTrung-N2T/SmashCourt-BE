using System.Diagnostics.Metrics;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Infrastructure.Telemetry;

/// <summary>
/// Counter nghiệp vụ của luồng đặt sân (singleton): nhánh quyết định cọc của đơn online, kết quả thanh toán từ cổng, số đơn job hủy theo lý do.
/// Dùng để thấy xu hướng bất thường (ví dụ tỷ lệ cọc 100 % tăng vọt, IPN sai chữ ký); không chứng minh dữ liệu đúng (việc của kiểm tính đúng).
/// </summary>
public sealed class BookingFlowMetrics
{
    public const string DepositDecisionsInstrument = "smashcourt.booking.deposit_decisions";
    public const string PaymentResultsInstrument = "smashcourt.payment.results";
    public const string ExpirationsInstrument = "smashcourt.booking.expirations";

    public const string PaymentInvalidSignature = "invalid_signature";
    public const string PaymentUnknownTransaction = "unknown_transaction";
    public const string PaymentAmountMismatch = "amount_mismatch";
    public const string PaymentFailed = "failed";
    public const string PaymentStaleFailure = "stale_failure"; // kết quả thất bại của link cũ hoặc gửi lặp
    public const string PaymentSucceeded = "succeeded";
    public const string PaymentAlreadyRecorded = "already_recorded";
    public const string PaymentAfterCancellation = "after_cancellation"; // tiền về sau khi đơn đã hủy, tạo hoàn 100 %
    public const string PaymentExtra = "extra_payment";

    public const string ExpiryPayment = "payment_expired";
    public const string ExpiryApproval = "approval_expired";

    private readonly Counter<long> _depositDecisions;
    private readonly Counter<long> _paymentResults;
    private readonly Counter<long> _expirations;

    public BookingFlowMetrics(IMeterFactory meterFactory)
    {
        // Cùng meter với kiểm tính đúng; meter thuộc IMeterFactory, không dispose ở đây
        var meter = meterFactory.Create(BookingInvariantMetrics.MeterName);
        _depositDecisions = meter.CreateCounter<long>(DepositDecisionsInstrument,
            description: "Số quyết định cọc của đơn online theo nhánh: tadss_deposit, confirm_without_online_payment, need_approval, rejected, full_payment_fallback.");
        _paymentResults = meter.CreateCounter<long>(PaymentResultsInstrument,
            description: "Số kết quả thanh toán từ cổng theo cách BE xử lý, nhãn outcome.");
        _expirations = meter.CreateCounter<long>(ExpirationsInstrument,
            description: "Số đơn job hủy theo lý do: payment_expired (hết hạn thanh toán), approval_expired (hết hạn duyệt).");
    }

    public void RecordDepositDecision(DepositBranch branch) =>
        _depositDecisions.Add(1, new KeyValuePair<string, object?>("branch", BranchLabel(branch)));

    public void RecordPaymentResult(string outcome) =>
        _paymentResults.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordExpiry(string reason) =>
        _expirations.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public static string BranchLabel(DepositBranch branch) => branch switch
    {
        DepositBranch.TadssDeposit => "tadss_deposit",
        DepositBranch.ConfirmWithoutOnlinePayment => "confirm_without_online_payment",
        DepositBranch.NeedApproval => "need_approval",
        DepositBranch.Rejected => "rejected",
        DepositBranch.FullPaymentFallback => "full_payment_fallback",
        // Metric chạy trên luồng đặt sân: nhánh mới chưa có nhãn thì ghi "unknown", không bao giờ ném
        _ => "unknown"
    };
}
