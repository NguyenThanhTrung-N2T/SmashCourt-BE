namespace SmashCourt_BE.Models.Enums
{
    public enum OtpType
    {
        EMAIL_VERIFY = 0,
        FORGOT_PASSWORD = 1,
        TWO_FA = 2,           // OTP cho login 2FA
        ENABLE_2FA = 3,       // OTP cho bật 2FA
        DISABLE_2FA = 4       // OTP cho tắt 2FA
    }
}
