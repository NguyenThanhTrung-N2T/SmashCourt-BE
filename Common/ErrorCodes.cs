namespace SmashCourt_BE.Common;

/// <summary>
/// Machine-readable error codes — tập trung tại một chỗ, tránh magic string rải rác.
/// FE dùng các code này để xử lý logic theo từng loại lỗi cụ thể.
/// </summary>
public static class ErrorCodes
{
    // ─── Success ─────────────────────────────────────────────────────────
    public const string Success = "SUCCESS";

    // ─── Client errors (4xx) ─────────────────────────────────────────────
    public const string ValidationError  = "VALIDATION_ERROR";
    public const string BadRequest       = "BAD_REQUEST";
    public const string Unauthorized     = "UNAUTHORIZED";
    public const string Forbidden        = "FORBIDDEN";
    public const string NotFound         = "NOT_FOUND";
    public const string Conflict         = "CONFLICT";

    // ─── Business-specific ───────────────────────────────────────────────
    public const string ResourceInUse    = "RESOURCE_IN_USE";
    public const string OtpInvalid       = "OTP_INVALID";
    public const string OtpExpired       = "OTP_EXPIRED";
    public const string OtpLimitExceeded = "OTP_LIMIT_EXCEEDED";
    public const string TokenInvalid     = "TOKEN_INVALID";
    public const string EmailExists      = "EMAIL_EXISTS";
    public const string NameDuplicate    = "NAME_DUPLICATE";
    public const string AccountLocked    = "ACCOUNT_LOCKED";

    // ─── Branch Management ───────────────────────────────────────────────
    public const string BranchNotFound         = "BRANCH_NOT_FOUND";
    public const string UserNotFound           = "USER_NOT_FOUND";
    public const string ManagerNotFound        = "MANAGER_NOT_FOUND";
    public const string ManagerAlreadyExists   = "MANAGER_ALREADY_EXISTS";
    public const string InvalidManagerUser     = "INVALID_MANAGER_USER";
    public const string StaffNotFound          = "STAFF_NOT_FOUND";
    public const string StaffAlreadyExists     = "STAFF_ALREADY_EXISTS";
    public const string InvalidStaffUser       = "INVALID_STAFF_USER";
    public const string InvalidBulkOperation   = "INVALID_BULK_OPERATION";

    // ─── Server errors (5xx) ─────────────────────────────────────────────
    public const string InternalError    = "INTERNAL_ERROR";
}
