using System.Net;
using System.Net.Mail;
using SmashCourt_BE.DTOs.Email;

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

    /// <summary>
    /// Gửi email OTP đăng ký tài khoản mới
    /// </summary>
    /// <param name="toEmail">Email người nhận</param>
    /// <param name="fullName">Tên người nhận</param>
    /// <param name="otpCode">Mã OTP 6 chữ số</param>
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

    /// <summary>
    /// Gửi email OTP đặt lại mật khẩu (forgot password)
    /// </summary>
    /// <param name="toEmail">Email người nhận</param>
    /// <param name="fullName">Tên người nhận</param>
    /// <param name="otpCode">Mã OTP 6 chữ số</param>
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

    /// <summary>
    /// Gửi email OTP xác thực đăng nhập 2FA
    /// </summary>
    /// <param name="toEmail">Email người nhận</param>
    /// <param name="fullName">Tên người nhận</param>
    /// <param name="otpCode">Mã OTP 6 chữ số</param>
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

    /// <summary>
    /// Gửi email xác nhận đặt sân (NEW VERSION - dùng DTO và template file)
    /// Có đầy đủ thông tin: branch, payment, không hiển thị cancel token code
    /// </summary>
    public async Task SendBookingConfirmationAsync(BookingEmailModel model)
    {
        var subject = "✅ Xác nhận đặt sân - SmashCourt";

        // Đọc template từ file
        var templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates", "BookingConfirmation.html");
        
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Email template not found: {templatePath}");
        }
        
        var template = await File.ReadAllTextAsync(templatePath);

        // Replace placeholders với data từ model
        var body = template
            .Replace("{{Name}}", model.Name)
            .Replace("{{BranchName}}", model.BranchName)
            .Replace("{{BranchAddress}}", model.BranchAddress)
            .Replace("{{BranchPhone}}", model.BranchPhone)
            .Replace("{{Courts}}", string.Join(", ", model.CourtNames))
            .Replace("{{Date}}", model.BookingDate)
            .Replace("{{Time}}", $"{model.StartTime} – {model.EndTime}")
            .Replace("{{BookingCode}}", model.BookingCode)
            .Replace("{{CourtFee}}", model.CourtFee)
            .Replace("{{TotalAmount}}", model.TotalAmount)
            .Replace("{{PaymentMethod}}", model.PaymentMethod)
            .Replace("{{PaymentStatus}}", model.PaymentStatus)
            .Replace("{{CancelUrl}}", model.CancelUrl)
            .Replace("{{Year}}", DateTime.UtcNow.Year.ToString());

        // Xử lý conditional rendering cho discounts
        body = ProcessConditionalSection(body, "LoyaltyDiscount", model.LoyaltyDiscount);
        body = ProcessConditionalSection(body, "PromotionDiscount", model.PromotionDiscount);

        await SendAsync(model.Email, subject, body);
    }

    /// <summary>
    /// Xử lý conditional section trong template ({{#if ...}} ... {{/if}})
    /// </summary>
    private static string ProcessConditionalSection(string template, string sectionName, string? value)
    {
        var startTag = $"{{{{#if {sectionName}}}}}";
        var endTag = "{{/if}}";
        var placeholder = $"{{{{{sectionName}}}}}";

        var startIndex = template.IndexOf(startTag);
        if (startIndex < 0) return template;

        var endIndex = template.IndexOf(endTag, startIndex);
        if (endIndex < 0) return template;

        if (!string.IsNullOrEmpty(value))
        {
            // Có giá trị: remove tags và replace placeholder
            var section = template.Substring(startIndex, endIndex - startIndex + endTag.Length);
            var content = section.Replace(startTag, "").Replace(endTag, "").Replace(placeholder, value);
            return template.Remove(startIndex, endIndex - startIndex + endTag.Length).Insert(startIndex, content);
        }
        else
        {
            // Không có giá trị: remove toàn bộ section
            return template.Remove(startIndex, endIndex - startIndex + endTag.Length);
        }
    }

    /// <summary>
    /// Gửi email xác nhận đặt sân kèm link hủy (OLD VERSION - deprecated)
    /// </summary>
    /// <param name="toEmail">Email người nhận</param>
    /// <param name="fullName">Tên người nhận</param>
    /// <param name="bookingId">Booking ID</param>
    /// <param name="cancelToken">Token để hủy booking</param>
    /// <param name="courtName">Tên sân</param>
    /// <param name="bookingDate">Ngày đặt sân</param>
    /// <param name="startTime">Giờ bắt đầu</param>
    /// <param name="endTime">Giờ kết thúc</param>
    public async Task SendBookingConfirmationAsync(
        string toEmail, string fullName, Guid bookingId, string cancelToken,
        string courtName, DateOnly bookingDate, TimeOnly startTime, TimeOnly endTime)
    {
        var subject = "✅ Xác nhận đặt sân - SmashCourt";
        var cancelUrl = $"https://smashcourt.vn/cancel?token={Uri.EscapeDataString(cancelToken)}";
        var dateStr = bookingDate.ToString("dd/MM/yyyy");
        var timeStr = $"{startTime:HH:mm} – {endTime:HH:mm}";

        var body = $"""
    <!DOCTYPE html>
    <html lang="vi">
    <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1.0"></head>
    <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f4f7f6; margin: 0; padding: 0;">
        <table width="100%" cellpadding="0" cellspacing="0" style="background-color: #f4f7f6; padding: 40px 0;">
            <tr><td align="center">
                <table width="100%" cellpadding="0" cellspacing="0" style="max-width: 600px; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 10px 25px rgba(0,0,0,0.05); border: 1px solid #e2e8f0; margin: 0 auto;">
                    <tr><td style="background-color: #1e3a8a; padding: 35px 30px; text-align: center;">
                        <h1 style="color: #ffffff; margin: 0; font-size: 32px; font-weight: 800; letter-spacing: 2px;">SMASHCOURT</h1>
                        <p style="color: #bfdbfe; margin: 8px 0 0 0; font-size: 15px;">Nền Tảng Đặt Sân Thể Thao Hàng Đầu</p>
                    </td></tr>
                    <tr><td style="padding: 45px 35px;">
                        <h2 style="color: #16a34a; margin: 0 0 20px 0; font-size: 22px; font-weight: 700;">✅ Đặt sân thành công!</h2>
                        <p style="color: #475569; font-size: 16px; line-height: 1.6; margin: 0 0 25px 0;">Xin chào <strong style="color: #0f172a;">{fullName}</strong>, booking của bạn đã được xác nhận.</p>
                        <table width="100%" cellpadding="0" cellspacing="0" style="background-color: #f8fafc; border-radius: 10px; margin-bottom: 30px; border: 1px solid #e2e8f0;">
                            <tr><td style="padding: 25px;">
                                <p style="margin: 0 0 12px 0; color: #64748b; font-size: 13px; text-transform: uppercase; font-weight: 700; letter-spacing: 1px;">Chi tiết đặt sân</p>
                                <table width="100%"><tr>
                                    <td style="color: #64748b; font-size: 14px; padding: 6px 0; vertical-align: top;">🏘️ Sân:</td>
                                    <td style="color: #0f172a; font-size: 14px; font-weight: 600; text-align: right; word-wrap: break-word; word-break: break-word;">{courtName}</td>
                                </tr><tr>
                                    <td style="color: #64748b; font-size: 14px; padding: 6px 0;">📅 Ngày:</td>
                                    <td style="color: #0f172a; font-size: 14px; font-weight: 600; text-align: right;">{dateStr}</td>
                                </tr><tr>
                                    <td style="color: #64748b; font-size: 14px; padding: 6px 0;">⏰ Giờ:</td>
                                    <td style="color: #0f172a; font-size: 14px; font-weight: 600; text-align: right;">{timeStr}</td>
                                </tr><tr>
                                    <td style="color: #64748b; font-size: 14px; padding: 6px 0;">🔑 Mã hủy:</td>
                                    <td style="color: #2563eb; font-size: 13px; font-weight: 600; text-align: right; font-family: monospace;">{cancelToken[..8].ToUpper()}</td>
                                </tr></table>
                            </td></tr>
                        </table>
                        <table width="100%" cellpadding="0" cellspacing="0" style="background-color: #fef2f2; border-radius: 8px; margin-bottom: 25px; border-left: 4px solid #ef4444;">
                            <tr><td style="padding: 20px;">
                                <p style="margin: 0 0 10px 0; color: #991b1b; font-size: 14px; font-weight: 700;">⚠️ Cần hủy lịch?</p>
                                <p style="margin: 0 0 15px 0; color: #7f1d1d; font-size: 14px; line-height: 1.5;">Nếu bạn cần hủy, vui lòng nhấn vào nút bên dưới. Link hủy chỉ có hiệu lực trong 24 giờ hoặc trước giờ chơi.</p>
                                <a href="{cancelUrl}" style="display: inline-block; background-color: #dc2626; color: #ffffff; text-decoration: none; padding: 12px 24px; border-radius: 8px; font-size: 14px; font-weight: 600;">Hủy đặt sân</a>
                            </td></tr>
                        </table>
                    </td></tr>
                    <tr><td style="background-color: #f8fafc; padding: 25px 30px; text-align: center; border-top: 1px solid #e2e8f0;">
                        <p style="margin: 0 0 5px 0; color: #64748b; font-size: 13px;">Trân trọng,</p>
                        <p style="margin: 0; color: #0f172a; font-size: 15px; font-weight: 600;">Đội ngũ phát triển SmashCourt</p>
                        <p style="margin: 12px 0 0 0; color: #94a3b8; font-size: 12px;">&copy; {DateTime.UtcNow.Year} SmashCourt. Tất cả các quyền được bảo lưu.</p>
                    </td></tr>
                </table>
            </td></tr>
        </table>
    </body></html>
    """;

        await SendAsync(toEmail, subject, body);
    }

    /// <summary>
    /// Gửi email xác nhận hủy đặt sân thành công
    /// Hiển thị: Số tiền hoàn + Thông tin chi nhánh (để nhận tiền mặt) + Hướng dẫn
    /// </summary>
    /// <param name="toEmail">Email người nhận</param>
    /// <param name="fullName">Tên người nhận</param>
    /// <param name="bookingId">Booking ID (không hiển thị trong email)</param>
    /// <param name="branchName">Tên chi nhánh</param>
    /// <param name="branchAddress">Địa chỉ chi nhánh</param>
    /// <param name="branchPhone">Số điện thoại chi nhánh (optional)</param>
    /// <param name="refundAmount">Số tiền hoàn (0 = không hoàn tiền)</param>
    public async Task SendCancelConfirmationAsync(
        string toEmail, string fullName, Guid bookingId, string branchName, 
        string branchAddress, string? branchPhone, decimal refundAmount)
    {
        var subject = "❌ Xác nhận hủy đặt sân - SmashCourt";

        // Conditional rendering: Có tiền hoàn vs Không hoàn tiền
        var refundMessage = refundAmount > 0
            ? $"""
                <p style="color: #475569; font-size: 16px; line-height: 1.8; margin: 0 0 25px 0;">
                    Yêu cầu hủy đặt sân của bạn đã được xử lý thành công.<br/>
                    Số tiền hoàn: <strong style="color: #16a34a; font-size: 18px;">{refundAmount.ToString("N0")} VNĐ</strong>
                </p>
                <table width="100%" cellpadding="0" cellspacing="0" style="background: linear-gradient(135deg, #f0fdf4 0%, #dcfce7 100%); border-radius: 12px; margin-bottom: 25px; border: 2px solid #16a34a; box-shadow: 0 2px 8px rgba(22, 101, 52, 0.1);">
                    <tr><td style="padding: 25px;">
                        <p style="margin: 0 0 15px 0; color: #166534; font-size: 16px; font-weight: 700;">
                            📍 Cách nhận tiền hoàn
                        </p>
                        <table width="100%" cellpadding="0" cellspacing="0" style="margin-bottom: 15px;">
                            <tr>
                                <td style="padding: 8px 0; vertical-align: top; width: 30px;">
                                    <span style="font-size: 18px;">🏢</span>
                                </td>
                                <td style="padding: 8px 0; vertical-align: top;">
                                    <p style="margin: 0; color: #15803d; font-size: 14px; line-height: 1.6;">
                                        <strong style="display: block; margin-bottom: 4px; color: #166534;">Chi nhánh:</strong>
                                        {branchName}
                                    </p>
                                </td>
                            </tr>
                            <tr>
                                <td style="padding: 8px 0; vertical-align: top;">
                                    <span style="font-size: 18px;">📍</span>
                                </td>
                                <td style="padding: 8px 0; vertical-align: top;">
                                    <p style="margin: 0; color: #15803d; font-size: 14px; line-height: 1.6;">
                                        <strong style="display: block; margin-bottom: 4px; color: #166534;">Địa chỉ:</strong>
                                        {branchAddress}
                                    </p>
                                </td>
                            </tr>
                            {(!string.IsNullOrEmpty(branchPhone) ? $"""
                            <tr>
                                <td style="padding: 8px 0; vertical-align: top;">
                                    <span style="font-size: 18px;">📞</span>
                                </td>
                                <td style="padding: 8px 0; vertical-align: top;">
                                    <p style="margin: 0; color: #15803d; font-size: 14px; line-height: 1.6;">
                                        <strong style="display: block; margin-bottom: 4px; color: #166534;">Điện thoại:</strong>
                                        {branchPhone}
                                    </p>
                                </td>
                            </tr>
                            """ : "")}
                        </table>
                        <div style="background-color: #ffffff; border-radius: 8px; padding: 15px; border-left: 4px solid #16a34a;">
                            <p style="margin: 0; color: #166534; font-size: 14px; line-height: 1.6;">
                                <strong>💡 Lưu ý quan trọng:</strong><br/>
                                • Mang theo CMND/CCCD để xác nhận danh tính<br/>
                                • Vui lòng đến chi nhánh trong giờ làm việc<br/>
                                • Liên hệ trước nếu cần hỗ trợ
                            </p>
                        </div>
                    </td></tr>
                </table>
                """
            : $"""
                <p style="color: #475569; font-size: 16px; line-height: 1.8; margin: 0 0 25px 0;">
                    Yêu cầu hủy đặt sân của bạn đã được xử lý thành công.
                </p>
                <table width="100%" cellpadding="0" cellspacing="0" style="background: linear-gradient(135deg, #fef3c7 0%, #fde68a 100%); border-radius: 12px; margin-bottom: 25px; border: 2px solid #f59e0b; box-shadow: 0 2px 8px rgba(245, 158, 11, 0.1);">
                    <tr><td style="padding: 25px;">
                        <p style="margin: 0; color: #92400e; font-size: 15px; line-height: 1.8; text-align: center;">
                            <strong style="font-size: 16px;">⚠️ Thông báo</strong><br/><br/>
                            Đơn này không được hoàn tiền do hủy quá gần giờ chơi.<br/>
                            Cảm ơn bạn đã thông báo trước cho chúng tôi.
                        </p>
                    </td></tr>
                </table>
                """;

        var body = $"""
    <!DOCTYPE html>
    <html lang="vi">
    <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1.0"></head>
    <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f4f7f6; margin: 0; padding: 0;">
        <table width="100%" cellpadding="0" cellspacing="0" style="background-color: #f4f7f6; padding: 40px 0;">
            <tr><td align="center">
                <table width="100%" cellpadding="0" cellspacing="0" style="max-width: 600px; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 10px 25px rgba(0,0,0,0.05); border: 1px solid #e2e8f0; margin: 0 auto;">
                    <tr><td style="background-color: #1e3a8a; padding: 35px 30px; text-align: center;">
                        <h1 style="color: #ffffff; margin: 0; font-size: 32px; font-weight: 800; letter-spacing: 2px;">SMASHCOURT</h1>
                        <p style="color: #bfdbfe; margin: 8px 0 0 0; font-size: 15px;">Nền Tảng Đặt Sân Thể Thao Hàng Đầu</p>
                    </td></tr>
                    <tr><td style="padding: 45px 35px;">
                        <h2 style="color: #dc2626; margin: 0 0 20px 0; font-size: 24px; font-weight: 700; text-align: center;">❌ Đặt sân đã được hủy</h2>
                        <p style="color: #475569; font-size: 16px; line-height: 1.8; margin: 0 0 30px 0;">
                            Xin chào <strong style="color: #0f172a;">{fullName}</strong>,
                        </p>
                        {refundMessage}
                        <table width="100%" cellpadding="0" cellspacing="0" style="background-color: #eff6ff; border-radius: 8px; margin-bottom: 25px; border-left: 4px solid #3b82f6;">
                            <tr><td style="padding: 20px;">
                                <p style="margin: 0; color: #1e3a8a; font-size: 14px; line-height: 1.6;">
                                    💡 <strong>Lưu ý:</strong> Nếu đây không phải do bạn thực hiện, vui lòng liên hệ hỗ trợ ngay để được giải quyết.
                                </p>
                            </td></tr>
                        </table>
                    </td></tr>
                    <tr><td style="background-color: #f8fafc; padding: 25px 30px; text-align: center; border-top: 1px solid #e2e8f0;">
                        <p style="margin: 0 0 5px 0; color: #64748b; font-size: 13px;">Trân trọng,</p>
                        <p style="margin: 0; color: #0f172a; font-size: 15px; font-weight: 600;">Đội ngũ phát triển SmashCourt</p>
                        <p style="margin: 12px 0 0 0; color: #94a3b8; font-size: 12px;">&copy; {DateTime.UtcNow.Year} SmashCourt. Tất cả các quyền được bảo lưu.</p>
                    </td></tr>
                </table>
            </td></tr>
        </table>
    </body></html>
    """;

        await SendAsync(toEmail, subject, body);
    }

    /// <summary>
    /// Gửi email chúc mừng lên hạng loyalty
    /// </summary>
    /// <param name="toEmail">Email người nhận</param>
    /// <param name="fullName">Tên người nhận</param>
    /// <param name="newTierName">Tên hạng mới</param>
    public async Task SendTierUpgradeAsync(
        string toEmail, string fullName, string newTierName)
    {
        var subject = "🏆 Chúc mừng! Bạn đã lên hạng - SmashCourt";

        var body = $"""
    <!DOCTYPE html>
    <html lang="vi">
    <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1.0"></head>
    <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f4f7f6; margin: 0; padding: 0;">
        <table width="100%" cellpadding="0" cellspacing="0" style="background-color: #f4f7f6; padding: 40px 0;">
            <tr><td align="center">
                <table width="100%" cellpadding="0" cellspacing="0" style="max-width: 600px; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 10px 25px rgba(0,0,0,0.05); border: 1px solid #e2e8f0; margin: 0 auto;">
                    <tr><td style="background-color: #1e3a8a; padding: 35px 30px; text-align: center;">
                        <h1 style="color: #ffffff; margin: 0; font-size: 32px; font-weight: 800; letter-spacing: 2px;">SMASHCOURT</h1>
                        <p style="color: #bfdbfe; margin: 8px 0 0 0; font-size: 15px;">Nền Tảng Đặt Sân Thể Thao Hàng Đầu</p>
                    </td></tr>
                    <tr><td style="padding: 45px 35px; text-align: center;">
                        <div style="font-size: 64px; margin-bottom: 20px;">🏆</div>
                        <h2 style="color: #d97706; margin: 0 0 15px 0; font-size: 24px; font-weight: 800;">Chúc mừng lên hạng!</h2>
                        <p style="color: #475569; font-size: 16px; line-height: 1.6; margin: 0 0 20px 0;">Xin chào <strong style="color: #0f172a;">{fullName}</strong>,</p>
                        <p style="color: #475569; font-size: 16px; line-height: 1.6; margin: 0 0 30px 0;">Chúc mừng! Bạn đã đạt đủ điểm tích lũy để nâng cấp hạng thành viên.</p>
                        <div style="background: linear-gradient(135deg, #fef3c7, #fde68a); border-radius: 12px; padding: 25px; margin-bottom: 30px; border: 2px solid #f59e0b;">
                            <p style="margin: 0 0 8px 0; color: #92400e; font-size: 13px; text-transform: uppercase; font-weight: 700; letter-spacing: 1px;">Hạng thành viên mới</p>
                            <p style="margin: 0; color: #78350f; font-size: 28px; font-weight: 800;">{newTierName}</p>
                        </div>
                        <p style="color: #64748b; font-size: 14px; line-height: 1.6;">Tiếp tục đặt sân để tích lũy nhiều ưu đãi hơn!</p>
                    </td></tr>
                    <tr><td style="background-color: #f8fafc; padding: 25px 30px; text-align: center; border-top: 1px solid #e2e8f0;">
                        <p style="margin: 0 0 5px 0; color: #64748b; font-size: 13px;">Trân trọng,</p>
                        <p style="margin: 0; color: #0f172a; font-size: 15px; font-weight: 600;">Đội ngũ phát triển SmashCourt</p>
                        <p style="margin: 12px 0 0 0; color: #94a3b8; font-size: 12px;">&copy; {DateTime.UtcNow.Year} SmashCourt. Tất cả các quyền được bảo lưu.</p>
                    </td></tr>
                </table>
            </td></tr>
        </table>
    </body></html>
    """;

        await SendAsync(toEmail, subject, body);
    }

    /// <summary>
    /// Gửi email xác nhận hoàn tiền thành công (sau khi staff confirm refund)
    /// Hiển thị: Số tiền hoàn + Thông tin chi nhánh (để nhận tiền mặt) + Hướng dẫn + Warning (không chuyển khoản)
    /// </summary>
    /// <param name="toEmail">Email người nhận</param>
    /// <param name="fullName">Tên người nhận</param>
    /// <param name="bookingId">Booking ID (không hiển thị trong email)</param>
    /// <param name="branchName">Tên chi nhánh</param>
    /// <param name="branchAddress">Địa chỉ chi nhánh</param>
    /// <param name="branchPhone">Số điện thoại chi nhánh (optional)</param>
    /// <param name="refundAmount">Số tiền hoàn</param>
    public async Task SendRefundConfirmedAsync(
        string toEmail, string fullName, Guid bookingId, 
        string branchName, string branchAddress, string? branchPhone, 
        decimal refundAmount)
    {
        var subject = "💰 Xác nhận hoàn tiền - SmashCourt";
        var amountStr = refundAmount.ToString("N0") + " VNĐ";

        var body = $"""
    <!DOCTYPE html>
    <html lang="vi">
    <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1.0"></head>
    <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f4f7f6; margin: 0; padding: 0;">
        <table width="100%" cellpadding="0" cellspacing="0" style="background-color: #f4f7f6; padding: 40px 0;">
            <tr><td align="center">
                <table width="100%" cellpadding="0" cellspacing="0" style="max-width: 600px; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 10px 25px rgba(0,0,0,0.05); border: 1px solid #e2e8f0; margin: 0 auto;">
                    <tr><td style="background-color: #1e3a8a; padding: 35px 30px; text-align: center;">
                        <h1 style="color: #ffffff; margin: 0; font-size: 32px; font-weight: 800; letter-spacing: 2px;">SMASHCOURT</h1>
                        <p style="color: #bfdbfe; margin: 8px 0 0 0; font-size: 15px;">Nền Tảng Đặt Sân Thể Thao Hàng Đầu</p>
                    </td></tr>
                    <tr><td style="padding: 45px 35px;">
                        <div style="text-align: center; margin-bottom: 25px; font-size: 64px;">💰</div>
                        <h2 style="color: #16a34a; margin: 0 0 20px 0; font-size: 24px; font-weight: 700; text-align: center;">Hoàn tiền thành công!</h2>
                        <p style="color: #475569; font-size: 16px; line-height: 1.8; margin: 0 0 30px 0;">
                            Xin chào <strong style="color: #0f172a;">{fullName}</strong>, yêu cầu hoàn tiền cho đơn đặt sân của bạn đã được xử lý thành công.
                        </p>
                        <table width="100%" cellpadding="0" cellspacing="0" style="background-color: #f0fdf4; border-radius: 10px; margin-bottom: 25px; border: 2px solid #16a34a; box-shadow: 0 2px 8px rgba(22, 101, 52, 0.1);">
                            <tr><td style="padding: 25px; text-align: center;">
                                <p style="margin: 0 0 8px 0; color: #15803d; font-size: 15px; text-transform: uppercase; font-weight: 700; letter-spacing: 1px;">💵 Số tiền hoàn</p>
                                <p style="margin: 0; color: #16a34a; font-size: 32px; font-weight: 800;">{amountStr}</p>
                            </td></tr>
                        </table>
                        <table width="100%" cellpadding="0" cellspacing="0" style="background: linear-gradient(135deg, #dbeafe 0%, #bfdbfe 100%); border-radius: 12px; margin-bottom: 25px; border: 2px solid #3b82f6; box-shadow: 0 2px 8px rgba(59, 130, 246, 0.1);">
                            <tr><td style="padding: 25px;">
                                <p style="margin: 0 0 15px 0; color: #1e40af; font-size: 16px; font-weight: 700;">
                                    📍 Cách nhận tiền hoàn
                                </p>
                                <table width="100%" cellpadding="0" cellspacing="0" style="margin-bottom: 15px;">
                                    <tr>
                                        <td style="padding: 8px 0; vertical-align: top; width: 30px;">
                                            <span style="font-size: 18px;">🏢</span>
                                        </td>
                                        <td style="padding: 8px 0; vertical-align: top;">
                                            <p style="margin: 0; color: #1e40af; font-size: 14px; line-height: 1.6;">
                                                <strong style="display: block; margin-bottom: 4px; color: #1e3a8a;">Chi nhánh:</strong>
                                                {branchName}
                                            </p>
                                        </td>
                                    </tr>
                                    <tr>
                                        <td style="padding: 8px 0; vertical-align: top;">
                                            <span style="font-size: 18px;">📍</span>
                                        </td>
                                        <td style="padding: 8px 0; vertical-align: top;">
                                            <p style="margin: 0; color: #1e40af; font-size: 14px; line-height: 1.6;">
                                                <strong style="display: block; margin-bottom: 4px; color: #1e3a8a;">Địa chỉ:</strong>
                                                {branchAddress}
                                            </p>
                                        </td>
                                    </tr>
                                    {(!string.IsNullOrEmpty(branchPhone) ? $"""
                                    <tr>
                                        <td style="padding: 8px 0; vertical-align: top;">
                                            <span style="font-size: 18px;">📞</span>
                                        </td>
                                        <td style="padding: 8px 0; vertical-align: top;">
                                            <p style="margin: 0; color: #1e40af; font-size: 14px; line-height: 1.6;">
                                                <strong style="display: block; margin-bottom: 4px; color: #1e3a8a;">Điện thoại:</strong>
                                                {branchPhone}
                                            </p>
                                        </td>
                                    </tr>
                                    """ : "")}
                                </table>
                                <div style="background-color: #ffffff; border-radius: 8px; padding: 15px; border-left: 4px solid #3b82f6;">
                                    <p style="margin: 0; color: #1e40af; font-size: 14px; line-height: 1.6;">
                                        <strong>💡 Lưu ý quan trọng:</strong><br/>
                                        • Mang theo CMND/CCCD để xác nhận danh tính<br/>
                                        • Vui lòng đến chi nhánh trong giờ làm việc<br/>
                                        • Liên hệ trước nếu cần hỗ trợ
                                    </p>
                                </div>
                            </td></tr>
                        </table>
                        <table width="100%" cellpadding="0" cellspacing="0" style="background-color: #fef3c7; border-radius: 8px; margin-bottom: 25px; border-left: 4px solid #f59e0b;">
                            <tr><td style="padding: 20px;">
                                <p style="margin: 0; color: #92400e; font-size: 14px; line-height: 1.6;">
                                    ⚠️ <strong>Lưu ý:</strong> Tiền hoàn sẽ được trả bằng tiền mặt tại chi nhánh. Không chuyển khoản qua ngân hàng.
                                </p>
                            </td></tr>
                        </table>
                    </td></tr>
                    <tr><td style="background-color: #f8fafc; padding: 25px 30px; text-align: center; border-top: 1px solid #e2e8f0;">
                        <p style="margin: 0 0 5px 0; color: #64748b; font-size: 13px;">Trân trọng,</p>
                        <p style="margin: 0; color: #0f172a; font-size: 15px; font-weight: 600;">Đội ngũ phát triển SmashCourt</p>
                        <p style="margin: 12px 0 0 0; color: #94a3b8; font-size: 12px;">&copy; {DateTime.UtcNow.Year} SmashCourt. Tất cả các quyền được bảo lưu.</p>
                    </td></tr>
                </table>
            </td></tr>
        </table>
    </body></html>
    """;

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
