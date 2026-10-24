using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.ViewModels
{
    /// <summary>
    /// Internal model — bốn trường cọc hiển thị cho FE (DEPOSIT_APPROVAL_API 2.2). Không có giá trị (null) khi đơn chưa có mức cọc
    /// hoặc không áp dụng cọc; xem <c>DepositFieldsHelper</c>.
    /// </summary>
    /// <param name="Rate">Tỷ lệ cọc 0…1</param>
    /// <param name="Amount">Số tiền cọc thu online (a′ và duyệt 0 %: 0)</param>
    /// <param name="RemainingAmount">Phần thu tại quầy: <c>finalTotal hiện tại − Amount</c>, không âm</param>
    /// <param name="Source">Ai quyết định chính sách cọc</param>
    public readonly record struct DepositFields(decimal Rate, decimal Amount, decimal RemainingAmount, DepositSource Source);
}
