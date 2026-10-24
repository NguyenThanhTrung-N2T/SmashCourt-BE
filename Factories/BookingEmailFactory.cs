using System.Globalization;
using SmashCourt_BE.DTOs.Email;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.Deposit;

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

        // Chỉ hiện phần cọc khi khách mới trả một phần, phần còn lại thu tại quầy
        var remaining = invoice?.DepositAmount is > 0 and var deposit
            ? DepositFieldsMapper.RemainingAmount(invoice.FinalTotal, deposit)
            : 0m;

        var paymentMethod = MapPaymentMethod(invoice?.Payments?.FirstOrDefault()?.Method);
        var paymentStatus = MapPaymentStatus(invoice?.PaymentStatus);

        // Build cancel URL - sử dụng frontend base URL từ config hoặc default
        var baseUrl = frontendBaseUrl ?? "https://smashcourt.vn";
        var cancelUrl = $"{baseUrl}/booking/cancel?token={Uri.EscapeDataString(cancelToken)}";

        // Build booking code (sử dụng BookingCode từ DB, fallback 8 ký tự đầu của GUID)
        var bookingCode = string.IsNullOrEmpty(booking.BookingCode)
            ? booking.Id.ToString()[..8].ToUpper()
            : booking.BookingCode;

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
            DepositPaid = remaining > 0 ? FormatCurrency(invoice!.DepositAmount!.Value) : null,
            RemainingAmount = remaining > 0 ? FormatCurrency(remaining) : null,
            CancelUrl = cancelUrl
        };
    }

    /// <summary>
    /// Model cho email luồng duyệt cọc (chờ duyệt, đã duyệt, bị từ chối). Booking cần nạp sẵn Customer, Branch, BookingCourts.Court, Invoice.
    /// Giờ (hạn duyệt, hạn trả cọc) đổi từ UTC sang giờ VN; không đưa lý do của Quản lý vào email.
    /// </summary>
    public static DepositApprovalEmailModel BuildApproval(Booking booking, string frontendBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(booking);

        var email = booking.Customer?.Email;
        var name = booking.Customer?.FullName;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"Missing recipient info for booking {booking.Id}");

        var courtNames = booking.BookingCourts?
            .Where(bc => bc.Court != null)
            .Select(bc => bc.Court.Name)
            .ToList() ?? new List<string>();
        if (!courtNames.Any())
            courtNames.Add("Sân không xác định");

        var firstSlot = booking.BookingCourts?.OrderBy(bc => bc.Date).ThenBy(bc => bc.StartTime).FirstOrDefault();
        var invoice = booking.Invoice;
        var deposit = invoice?.DepositAmount;

        return new DepositApprovalEmailModel
        {
            Email = email,
            Name = name,
            BranchName = booking.Branch?.Name ?? "SmashCourt",
            BranchAddress = booking.Branch?.Address ?? "Địa chỉ không xác định",
            BranchPhone = booking.Branch?.Phone ?? "Liên hệ qua website",
            CourtNames = courtNames,
            BookingDate = booking.BookingDate.ToString("dd/MM/yyyy"),
            StartTime = firstSlot?.StartTime.ToString("HH:mm") ?? "00:00",
            EndTime = firstSlot?.EndTime.ToString("HH:mm") ?? "00:00",
            BookingCode = booking.BookingCode,
            TotalAmount = FormatCurrency(invoice?.FinalTotal ?? 0),
            DepositAmount = deposit is { } d ? FormatCurrency(d) : null,
            RemainingAmount = deposit is { } paid ? FormatCurrency(Math.Max(0, (invoice?.FinalTotal ?? 0) - paid)) : null,
            PayBefore = FormatVietnamTime(invoice?.ExpiresAt),
            ApprovalExpiresAt = FormatVietnamTime(booking.ApprovalExpiresAt),
            BookingsUrl = $"{frontendBaseUrl.TrimEnd('/')}/bookings"
        };
    }

    // Mốc UTC trong DB ⇒ giờ VN dạng "HH:mm dd/MM/yyyy"
    private static string? FormatVietnamTime(DateTime? utc) =>
        utc is { } value ? DateTimeHelper.ToVietnamTime(value).ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture) : null;

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
