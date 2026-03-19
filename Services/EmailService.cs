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

    // Gửi email OTP xác nhận đăng ký tài khoản 
    public async Task SendOtpRegisterAsync(string toEmail, string fullName, string otpCode)
    {
        var subject = "🔐 Xác thực tài khoản SmashCourt";

        var body = $"""
    <div style="font-family: Arial, sans-serif; background-color: #f4f6f8; padding: 20px;">
        <div style="max-width: 600px; margin: auto; background: white; border-radius: 8px; padding: 30px;">

            <!-- Header -->
            <div style="text-align: center; margin-bottom: 20px;">
                <h1 style="color: #2563eb; margin: 0;">SmashCourt</h1>
                <p style="color: #6b7280;">Hệ thống đặt sân cầu lông</p>
            </div>

            <!-- Greeting -->
            <p>Xin chào <strong>{fullName}</strong>,</p>

            <p>Bạn đang thực hiện đăng ký tài khoản. Vui lòng sử dụng mã OTP bên dưới để xác thực:</p>

            <!-- OTP -->
            <div style="text-align: center; margin: 30px 0;">
                <span style="
                    font-size: 36px;
                    letter-spacing: 10px;
                    font-weight: bold;
                    color: #2563eb;
                ">
                    {otpCode}
                </span>
            </div>

            <!-- Info -->
            <p>⏳ Mã OTP có hiệu lực trong <strong>5 phút</strong>.</p>
            <p>🔒 Không chia sẻ mã này với bất kỳ ai để đảm bảo an toàn tài khoản.</p>

            <!-- Warning -->
            <div style="background: #fff3cd; padding: 10px; border-radius: 6px; margin: 20px 0;">
                <p style="margin: 0; color: #856404;">
                    Nếu bạn không thực hiện yêu cầu này, vui lòng bỏ qua email này hoặc liên hệ hỗ trợ.
                </p>
            </div>

            <!-- Footer -->
            <hr style="margin: 30px 0;" />

            <p style="font-size: 12px; color: #6b7280;">
                Email này được gửi tự động, vui lòng không trả lời.
            </p>

            <p style="font-size: 12px; color: #6b7280;">
                © {DateTime.UtcNow.Year} SmashCourt. All rights reserved.
            </p>

        </div>
    </div>
    """;

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
    <div style="font-family: Arial, sans-serif; background-color: #f4f6f8; padding: 20px;">
        <div style="max-width: 600px; margin: auto; background: white; border-radius: 10px; padding: 30px;">

            <!-- Header -->
            <div style="text-align: center; margin-bottom: 20px;">
                <h1 style="color: #2563eb; margin: 0;">SmashCourt</h1>
                <p style="color: #6b7280; margin: 5px 0;">Hệ thống đặt sân cầu lông</p>
            </div>

            <!-- Title -->
            <h2 style="color: #111827;">{title}</h2>

            <!-- Greeting -->
            <p>Xin chào <strong>{fullName}</strong>,</p>

            <!-- Message -->
            <p>{message}</p>

            <!-- OTP -->
            <div style="text-align: center; margin: 30px 0;">
                <span style="
                    font-size: 36px;
                    letter-spacing: 10px;
                    font-weight: bold;
                    color: #2563eb;
                ">
                    {otpCode}
                </span>
            </div>

            <!-- Info -->
            <p>⏳ Mã OTP có hiệu lực trong <strong>5 phút</strong>.</p>
            <p>🔒 Không chia sẻ mã này với bất kỳ ai.</p>

            <!-- Warning -->
            <div style="background: #fff3cd; padding: 12px; border-radius: 6px; margin: 20px 0;">
                <p style="margin: 0; color: #856404;">
                    {extraNote}
                </p>
            </div>

            <!-- Footer -->
            <hr style="margin: 30px 0;" />

            <p style="font-size: 12px; color: #6b7280;">
                Email này được gửi tự động, vui lòng không trả lời.
            </p>

            <p style="font-size: 12px; color: #6b7280;">
                © {DateTime.UtcNow.Year} SmashCourt. All rights reserved.
            </p>

        </div>
    </div>
    """;
    }
}