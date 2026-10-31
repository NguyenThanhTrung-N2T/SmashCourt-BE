namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>Kết quả thực tế của booking báo về TADSS (contract v2 mục 3.7).</summary>
public enum TadssOutcome
{
    COMPLETED,
    NO_SHOW,
    CANCELLED_BY_CUSTOMER,
    CANCELLED_BY_STAFF,
    PAYMENT_EXPIRED,
    APPROVAL_EXPIRED,
    REJECTED
}
