using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Payments;

/// <summary>
/// Kết quả giao dịch một cổng gửi về, đã chuẩn hóa: nghiệp vụ thanh toán chỉ đọc kiểu này, không đọc tham số riêng của cổng.
/// </summary>
/// <param name="Amount">Số tiền VND đã quy đổi từ đơn vị của cổng; null khi cổng không gửi hoặc không đọc được</param>
/// <param name="ResponseCode">Mã kết quả gốc của cổng, chỉ để log và hiển thị thông báo</param>
public sealed record GatewayPaymentResult(
    IpnProvider Provider,
    string TransactionRef,
    bool IsSignatureValid,
    bool IsSuccess,
    decimal? Amount,
    string ResponseCode,
    string RawPayload);
