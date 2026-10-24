using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Helpers
{
    /// <summary>
    /// Trường cọc hiển thị cho FE (DEPOSIT_APPROVAL_API 2.2, BE-UT-07), dùng chung cho <c>BookingDto</c>, <c>OnlineBookingResponse</c>
    /// và response thanh toán lại. Hàm thuần: BE không tự quy đổi tỷ lệ ra tiền, chỉ đọc số đã lưu.
    /// </summary>
    public static class DepositFieldsHelper
    {
        /// <summary>
        /// Đọc từ hóa đơn. Trả null khi: đơn đang <c>PENDING_APPROVAL</c> (chưa có mức cọc), không có hóa đơn, hoặc hóa đơn không có
        /// nguồn cọc (đơn tại quầy, đơn online tạo trước Phase 6).
        /// </summary>
        public static DepositFields? FromInvoice(BookingStatus status, Invoice? invoice)
        {
            if (status == BookingStatus.PENDING_APPROVAL)
                return null;
            if (invoice is not { DepositSource: { } source, DepositRate: { } rate, DepositAmount: { } amount })
                return null;

            return new DepositFields(rate, amount, RemainingAmount(invoice.FinalTotal, amount), source);
        }

        /// <summary>
        /// Phần thu tại quầy theo <paramref name="finalTotal"/> hiện tại (gồm dịch vụ thêm sau); không âm khi tổng giảm dưới số đã cọc.
        /// </summary>
        public static decimal RemainingAmount(decimal finalTotal, decimal depositAmount) => Math.Max(0m, finalTotal - depositAmount);
    }
}
