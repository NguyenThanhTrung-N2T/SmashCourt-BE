using Npgsql;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Extensions;

public static class NpgsqlEnumMappingExtensions
{
    // Map enum C# sang enum type của PostgreSQL. Dùng chung cho app (Program.cs) và integration test
    // để test không bao giờ lệch ánh xạ với app thật.
    public static NpgsqlDataSourceBuilder MapSmashCourtEnums(this NpgsqlDataSourceBuilder builder)
    {
        // UpperCaseEnumTranslator giữ nguyên tên enum khi map sang PostgreSQL
        var translator = new UpperCaseEnumTranslator();

        builder.MapEnum<UserRole>("user_role", translator);
        builder.MapEnum<UserStatus>("user_status", translator);
        builder.MapEnum<OtpType>("otp_type", translator);
        builder.MapEnum<BranchStatus>("branch_status", translator);
        builder.MapEnum<CourtTypeStatus>("court_type_status", translator);
        builder.MapEnum<CourtStatus>("court_status", translator);
        builder.MapEnum<UserBranchRole>("user_branch_role", translator);
        builder.MapEnum<DayType>("day_type", translator);
        builder.MapEnum<ServiceStatus>("service_status", translator);
        builder.MapEnum<BranchServiceStatus>("branch_service_status", translator);
        builder.MapEnum<LoyaltyTransactionType>("loyalty_transaction_type", translator);
        builder.MapEnum<PromotionStatus>("promotion_status", translator);
        builder.MapEnum<DiscountTypeEnum>("discount_type_enum", translator);
        builder.MapEnum<BookingStatus>("booking_status", translator);
        builder.MapEnum<BookingSource>("booking_source", translator);
        builder.MapEnum<CancelSourceEnum>("cancel_source_enum", translator);
        builder.MapEnum<InvoicePaymentStatus>("invoice_payment_status", translator);
        builder.MapEnum<PaymentTiming>("payment_timing", translator);
        builder.MapEnum<PaymentTxStatus>("payment_tx_status", translator);
        builder.MapEnum<PaymentTxMethod>("payment_tx_method", translator);
        builder.MapEnum<RefundStatus>("refund_status", translator);
        builder.MapEnum<IpnProvider>("ipn_provider", translator);

        // Phase 6 — cọc TADSS
        builder.MapEnum<DepositSource>("deposit_source", translator);
        builder.MapEnum<ApprovalResult>("approval_result", translator);
        builder.MapEnum<BookingCancelReason>("booking_cancel_reason", translator);

        return builder;
    }
}
