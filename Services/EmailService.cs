using System.Net;
using System.Net.Mail;

namespace SmashCourt_BE.Services;

public class EmailService
{
    private readonly IConfiguration _config;

    public EmailService(IConfiguration config)
    {
        _config = config;
    }

    // Hàm gửi email chung
    private async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        var host = _config["Smtp:Host"] ?? throw new InvalidOperationException("Smtp:Host chưa cấu hình");
        var port = int.Parse(_config["Smtp:Port"] ?? "587");
        var user = _config["Smtp:Username"] ?? throw new InvalidOperationException("Smtp:Username chưa cấu hình");
        var pass = _config["Smtp:Password"] ?? throw new InvalidOperationException("Smtp:Password chưa cấu hình");
        var from = _config["Smtp:From"] ?? user;

        using var client = new SmtpClient(host, port)
        {
            Credentials = new NetworkCredential(user, pass),
            EnableSsl = true
        };

        var message = new MailMessage(from, toEmail, subject, htmlBody)
        {
            IsBodyHtml = true
        };

        await client.SendMailAsync(message);
    }

    public async Task SendOtpRegisterAsync(string toEmail, string fullName, string otpCode)
    {
        var subject = "🔐 Xác thực tài khoản SmashCourt";

        var body = BuildEmailTemplate(
            title: "Xác thực tài khoản",
            fullName: fullName,
            message: "Chào mừng bạn đến với SmashCourt. Vui lòng sử dụng mã xác thực (OTP) bên dưới để hoàn tất việc tạo tài khoản:",
            otpCode: otpCode,
            extraNote: "Nếu bạn không thực hiện thao tác đăng ký này, xin vui lòng bỏ qua email hoặc báo cáo cho bộ phận hỗ trợ."
        );

        await SendAsync(toEmail, subject, body);
    }

    // Gửi email OTP đặt lại mật khẩu
    public async Task SendOtpForgotPasswordAsync(string toEmail, string fullName, string otpCode)
    {
        var subject = "🔐 Đặt lại mật khẩu SmashCourt";

        var body = BuildEmailTemplate(
            title: "Đặt lại mật khẩu",
            fullName: fullName,
            message: "Bạn đang yêu cầu đặt lại mật khẩu cho tài khoản của mình. Vui lòng sử dụng mã OTP bên dưới:",
            otpCode: otpCode,
            extraNote: "Nếu bạn không thực hiện yêu cầu này, hãy bỏ qua email hoặc liên hệ hỗ trợ ngay để bảo vệ tài khoản."
        );

        await SendAsync(toEmail, subject, body);
    }

    // Gửi email OTP xác thực đăng nhập 2FA
    public async Task SendOtp2FAAsync(string toEmail, string fullName, string otpCode)
    {
        var subject = "🔐 Xác thực đăng nhập SmashCourt";

        var body = BuildEmailTemplate(
            title: "Xác thực đăng nhập (2FA)",
            fullName: fullName,
            message: "Bạn đang đăng nhập vào hệ thống. Vui lòng nhập mã OTP bên dưới để tiếp tục:",
            otpCode: otpCode,
            extraNote: "Nếu bạn không thực hiện đăng nhập này, hãy đổi mật khẩu ngay để bảo vệ tài khoản."
        );

        await SendAsync(toEmail, subject, body);
    }

    private string BuildEmailTemplate(string title, string fullName, string message, string otpCode, string extraNote)
    {
        return $"""
    <!DOCTYPE html>
    <html lang="vi">
    <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1.0">
    </head>
    <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f4f7f6; margin: 0; padding: 0;">
        <table width="100%" cellpadding="0" cellspacing="0" style="background-color: #f4f7f6; padding: 40px 0;">
            <tr>
                <td align="center">
                    <table width="100%" cellpadding="0" cellspacing="0" style="max-width: 600px; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 10px 25px rgba(0, 0, 0, 0.05); border: 1px solid #e2e8f0; margin: 0 auto;">
                        <!-- Header -->
                        <tr>
                            <td style="background-color: #1e3a8a; padding: 35px 30px; text-align: center;">
                                <h1 style="color: #ffffff; margin: 0; font-size: 32px; font-weight: 800; letter-spacing: 2px;">SMASHCOURT</h1>
                                <p style="color: #bfdbfe; margin: 8px 0 0 0; font-size: 15px; font-weight: 500;">Nền Tảng Đặt Sân Thể Thao Hàng Đầu</p>
                            </td>
                        </tr>
                        
                        <!-- Body Content -->
                        <tr>
                            <td style="padding: 45px 35px;">
                                <h2 style="color: #1e293b; margin: 0 0 25px 0; font-size: 22px; font-weight: 700;">{title}</h2>
                                <p style="color: #475569; font-size: 16px; line-height: 1.6; margin: 0 0 15px 0;">Xin chào <strong style="color: #0f172a;">{fullName}</strong>,</p>
                                <p style="color: #475569; font-size: 16px; line-height: 1.6; margin: 0 0 35px 0;">{message}</p>
                                
                                <!-- OTP Box -->
                                <table width="100%" cellpadding="0" cellspacing="0">
                                    <tr>
                                        <td align="center">
                                            <div style="background-color: #f8fafc; border: 1px dashed #cbd5e1; border-radius: 12px; padding: 25px 40px; margin: 0 0 35px 0; display: inline-block;">
                                                <p style="margin: 0 0 12px 0; color: #64748b; font-size: 13px; text-transform: uppercase; font-weight: 700; letter-spacing: 1px; text-align: center;">Mã Xác Thực Của Bạn</p>
                                                <div style="font-size: 42px; letter-spacing: 12px; font-weight: 800; color: #2563eb; text-align: center; font-family: 'Courier New', monospace; text-shadow: 1px 1px 0px rgba(37,99,235,0.1);">{otpCode}</div>
                                            </div>
                                        </td>
                                    </tr>
                                </table>
                                
                                <!-- Security Info -->
                                <table width="100%" cellpadding="0" cellspacing="0" style="margin-bottom: 30px; background-color: #eff6ff; border-radius: 8px;">
                                    <tr>
                                        <td style="padding: 20px; border-left: 4px solid #3b82f6; border-radius: 8px;">
                                            <p style="margin: 0 0 10px 0; color: #1e3a8a; font-size: 14px; align-items: center;">
                                                <span style="font-size: 16px;">⏱️</span> Mã xác thực này có hiệu lực trong vòng <strong>5 phút</strong>.
                                            </p>
                                            <p style="margin: 0; color: #1e3a8a; font-size: 14px; align-items: center;">
                                                <span style="font-size: 16px;">🔒</span> Tuyệt đối không chia sẻ mã này cho bất kỳ ai.
                                            </p>
                                        </td>
                                    </tr>
                                </table>

                                <!-- Warning Note -->
                                <p style="color: #64748b; font-size: 14px; line-height: 1.6; margin: 0; padding-top: 20px; border-top: 1px solid #e2e8f0; font-style: italic;">
                                    {extraNote}
                                </p>
                            </td>
                        </tr>
                        
                        <!-- Footer -->
                        <tr>
                            <td style="background-color: #f8fafc; padding: 30px; text-align: center; border-top: 1px solid #e2e8f0;">
                                <table width="100%" cellpadding="0" cellspacing="0">
                                    <tr>
                                        <td align="center" style="padding-bottom: 15px;">
                                            <p style="margin: 0 0 8px 0; color: #64748b; font-size: 13px; font-weight: 500;">Trân trọng,</p>
                                            <p style="margin: 0; color: #0f172a; font-size: 15px; font-weight: 600;">Đội ngũ phát triển SmashCourt</p>
                                        </td>
                                    </tr>
                                    <tr>
                                        <td align="center">
                                            <p style="margin: 0; color: #94a3b8; font-size: 12px; line-height: 1.5;">Email này được tự động tạo và gửi từ hệ thống SmashCourt.<br>Vui lòng không phản hồi lại địa chỉ này.</p>
                                            <p style="margin: 15px 0 0 0; color: #cbd5e1; font-size: 12px;">&copy; {DateTime.UtcNow.Year} SmashCourt. Tất cả các quyền được bảo lưu.</p>
                                        </td>
                                    </tr>
                                </table>
                            </td>
                        </tr>
                    </table>
                </td>
            </tr>
        </table>
    </body>
    </html>
    """;
    }
}