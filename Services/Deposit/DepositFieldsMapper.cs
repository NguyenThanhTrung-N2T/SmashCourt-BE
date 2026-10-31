using SmashCourt_BE.Models.Deposit;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Services.Deposit;

/// <summary>
/// Trường cọc trả cho FE, đọc nguyên số đã lưu trên hóa đơn (không tự quy đổi tỷ lệ ra tiền).
/// </summary>
public static class DepositFieldsMapper
{
    // Null khi đơn đang chờ duyệt (chưa có mức cọc) hoặc hóa đơn không có cọc (đơn tại quầy, đơn online cũ)
    public static DepositFields? FromInvoice(BookingStatus status, Invoice? invoice)
    {
        if (status == BookingStatus.PENDING_APPROVAL)
            return null;
        if (invoice is not { DepositSource: { } source, DepositRate: { } rate, DepositAmount: { } amount })
            return null;

        return new DepositFields(rate, amount, RemainingAmount(invoice.FinalTotal, amount), source);
    }

    // Phần thu tại quầy theo tổng hiện tại (gồm dịch vụ thêm sau), không âm
    public static decimal RemainingAmount(decimal finalTotal, decimal depositAmount) => Math.Max(0m, finalTotal - depositAmount);
}
