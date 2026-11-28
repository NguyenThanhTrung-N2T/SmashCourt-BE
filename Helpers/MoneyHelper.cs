namespace SmashCourt_BE.Helpers;

/// <summary>
/// Làm tròn tiền VND về đồng nguyên, dùng cho mọi phép chia theo tỷ lệ / phần trăm ra tiền.
/// Nửa đồng làm tròn lên (AwayFromZero), cùng cách TADSS quy đổi cọc và cùng Math.round của FE với số dương.
/// </summary>
public static class MoneyHelper
{
    public static decimal RoundVnd(decimal amount) =>
        Math.Round(amount, 0, MidpointRounding.AwayFromZero);

    // VNPay và thu tiền mặt chỉ nhận số đồng nguyên, nên giá nhập vào cũng phải là số đồng nguyên
    public static bool IsWholeVnd(decimal amount) => amount == decimal.Truncate(amount);
}
