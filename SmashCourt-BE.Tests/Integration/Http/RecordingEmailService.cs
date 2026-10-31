using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Thay <see cref="EmailService"/> trong host test: ghi lại thư thay vì gửi qua SMTP (SmtpClient của BE bật SSL nên không giả được
/// bằng server SMTP đơn giản). Mọi loại email đều đi qua <c>SendAsync</c> nên test kiểm được người nhận, tiêu đề, nội dung và link hủy.
/// </summary>
public sealed class RecordingEmailService(IConfiguration configuration) : EmailService(configuration)
{
    public sealed record SentEmail(string To, string Subject, string Body);

    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public IReadOnlyCollection<SentEmail> Sent => _sent.ToArray();

    /// <summary>Đặt true để mô phỏng SMTP hỏng: BE phải bắt lỗi và không làm hỏng đơn.</summary>
    public bool FailOnSend { get; set; }

    /// <summary>Chỉ hỏng khi gửi tới các địa chỉ này (kiểm lỗi một người nhận không chặn người khác).</summary>
    public HashSet<string> FailFor { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<SentEmail> To(string address) =>
        _sent.Where(e => string.Equals(e.To, address, StringComparison.OrdinalIgnoreCase)).ToList();

    public void Clear() => _sent.Clear();

    protected override Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        if (FailOnSend || FailFor.Contains(toEmail))
            return Task.FromException(new InvalidOperationException("Mô phỏng SMTP hỏng"));

        _sent.Enqueue(new SentEmail(toEmail, subject, htmlBody));
        return Task.CompletedTask;
    }
}
