using SmashCourt_BE.DTOs.Email;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Factories;

/// <summary>
/// Factory để map từ Booking entity sang BookingEmailModel DTO
/// Tách biệt logic mapping khỏi EmailService để dễ test và maintain
/// </summary>
public static class BookingEmailFactory
{
    /// <summary>
    /// Build BookingEmailModel từ Booking entity
    /// </summary>
    /// <param name="booking">Booking entity với tất cả navigation properties đã load</param>
    /// <param name="cancelToken">Raw cancel token (chưa hash) để tạo cancel URL</param>
    /// <param name="frontendBaseUrl">Frontend base URL (e.g., https://smashcourt.vn hoặc http://localhost:3000)</param>
    /// <returns>BookingEmailModel ready để gửi email</returns>
    public static BookingEmailModel Build(Booking booking, string cancelToken, string? frontendBaseUrl = null)
    {
        // Validate input
        if (booking == null)
            throw new ArgumentNullException(nameof(booking));
        if (string.IsNullOrEmpty(cancelToken))
            throw new ArgumentNullException(nameof(cancelToken));

        // Extract recipient info (customer hoặc guest)
        var email = booking.Customer?.Email ?? booking.GuestEmail;
        var name = booking.Customer?.FullName ?? booking.GuestName;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(name))
            throw new InvalidOperationException($"Missing recipient info for booking {booking.Id}");

        // Extract branch info với fallback
        var branchName = booking.Branch?.Name ?? "SmashCourt";
        var branchAddress = booking.Branch?.Address ?? "Địa chỉ không xác định";
        var branchPhone = booking.Branch?.Phone ?? "Liên hệ qua website";

        // Extract court names
        var courtNames = booking.BookingCourts?
            .Where(bc => bc.Court != null)
            .Select(bc => bc.Court.Name)
            .ToList() ?? new List<string>();

        if (!courtNames.Any())
            courtNames.Add("Sân không xác định");

        // Extract time info
        var firstSlot = booking.BookingCourts?.FirstOrDefault();
        var startTime = firstSlot?.StartTime.ToString("HH:mm") ?? "00:00";
        var endTime = firstSlot?.EndTime.ToString("HH:mm") ?? "00:00";
        var bookingDate = booking.BookingDate.ToString("dd/MM/yyyy");

        // Extract payment info
        var invoice = booking.Invoice;
        var totalAmount = FormatCurrency(invoice?.FinalTotal ?? 0);
        var courtFee = FormatCurrency(invoice?.CourtFee ?? 0);
        
        // Chỉ hiển thị discount nếu > 0
        var loyaltyDiscount = invoice?.LoyaltyDiscountAmount > 0
            ? "-" + FormatCurrency(invoice.LoyaltyDiscountAmount)
            : null;
        var promotionDiscount = invoice?.PromotionDiscountAmount > 0
            ? "-" + FormatCurrency(invoice.PromotionDiscountAmount)
            : null;

        var paymentMethod = MapPaymentMethod(invoice?.Payments?.FirstOrDefault()?.Method);
        var paymentStatus = MapPaymentStatus(invoice?.PaymentStatus);

        // Build cancel URL - sử dụng frontend base URL từ config hoặc default
        var baseUrl = frontendBaseUrl ?? "https://smashcourt.vn";
        var cancelUrl = $"{baseUrl}/booking/cancel?token={Uri.EscapeDataString(cancelToken)}";

        // Build booking code (8 ký tự đầu của GUID)
        var bookingCode = booking.Id.ToString()[..8].ToUpper();

        return new BookingEmailModel
        {
            Email = email,
            Name = name,
            BranchName = branchName,
            BranchAddress = branchAddress,
            BranchPhone = branchPhone,
            CourtNames = courtNames,
            BookingDate = bookingDate,
            StartTime = startTime,
            EndTime = endTime,
            BookingCode = bookingCode,
            TotalAmount = totalAmount,
            CourtFee = courtFee,
            LoyaltyDiscount = loyaltyDiscount,
            PromotionDiscount = promotionDiscount,
            PaymentMethod = paymentMethod,
            PaymentStatus = paymentStatus,
            CancelUrl = cancelUrl
        };
    }

    /// <summary>
    /// Format số tiền thành chuỗi VND với dấu phân cách hàng nghìn
    /// </summary>
    private static string FormatCurrency(decimal amount)
    {
        return amount.ToString("N0") + " VND";
    }

    /// <summary>
    /// Map PaymentTxMethod enum sang label hiển thị
    /// </summary>
    private static string MapPaymentMethod(PaymentTxMethod? method)
    {
        return method switch
        {
            PaymentTxMethod.VNPAY => "VNPay",
            PaymentTxMethod.CASH => "Tiền mặt",
            PaymentTxMethod.MOMO => "MoMo",
            _ => "Chưa thanh toán"
        };
    }

    /// <summary>
    /// Map InvoicePaymentStatus enum sang label hiển thị
    /// </summary>
    private static string MapPaymentStatus(InvoicePaymentStatus? status)
    {
        return status switch
        {
            InvoicePaymentStatus.PARTIALLY_PAID => "Đã thanh toán",
            InvoicePaymentStatus.PAID => "Đã thanh toán đầy đủ",
            InvoicePaymentStatus.UNPAID => "Chưa thanh toán",
            InvoicePaymentStatus.REFUNDED => "Đã hoàn tiền",
            _ => "Không xác định"
        };
    }
}
