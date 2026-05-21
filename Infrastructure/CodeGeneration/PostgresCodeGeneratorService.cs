using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Services.IService;
using System.Data;

namespace SmashCourt_BE.Infrastructure.CodeGeneration
{
    /// <summary>
    /// Service sinh mã nghiệp vụ dựa trên PostgreSQL sequence.
    /// Sequence là global, không reset theo ngày.
    /// </summary>
    public class PostgresCodeGeneratorService : ICodeGeneratorService
    {
        /// <summary>
        /// Tên PostgreSQL sequence dùng để sinh mã booking.
        /// </summary>
        private const string BookingCodeSequence = "booking_code_seq";

        /// <summary>
        /// Tên PostgreSQL sequence dùng để sinh mã hóa đơn.
        /// </summary>
        private const string InvoiceCodeSequence = "invoice_code_seq";

        /// <summary>
        /// Danh sách sequence được phép gọi, tránh truyền tên sequence tùy ý vào SQL.
        /// </summary>
        private static readonly HashSet<string> AllowedSequences = new(StringComparer.Ordinal)
        {
            BookingCodeSequence,
            InvoiceCodeSequence
        };

        private readonly SmashCourtContext _context;

        /// <summary>
        /// Khởi tạo service sinh mã bằng DbContext hiện tại.
        /// </summary>
        public PostgresCodeGeneratorService(SmashCourtContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Sinh mã booking theo format BK-yyyyMMdd-######.
        /// Phần ngày dùng giờ Việt Nam, phần số lấy từ sequence global trong PostgreSQL.
        /// </summary>
        public async Task<string> GenerateBookingCodeAsync()
        {
            var sequenceValue = await GetNextSequenceValueAsync(BookingCodeSequence);
            var today = DateTimeHelper.ToVietnamTime(DateTime.UtcNow);

            return $"BK-{today:yyyyMMdd}-{sequenceValue:D6}";
        }

        /// <summary>
        /// Sinh mã hóa đơn theo format INV-yyyyMMdd-######.
        /// Phần ngày dùng giờ Việt Nam, phần số lấy từ sequence global trong PostgreSQL.
        /// </summary>
        public async Task<string> GenerateInvoiceCodeAsync()
        {
            var sequenceValue = await GetNextSequenceValueAsync(InvoiceCodeSequence);
            var today = DateTimeHelper.ToVietnamTime(DateTime.UtcNow);

            return $"INV-{today:yyyyMMdd}-{sequenceValue:D6}";
        }

        /// <summary>
        /// Lấy giá trị tiếp theo từ PostgreSQL sequence đã được whitelist.
        /// </summary>
        /// <param name="sequenceName">Tên sequence cần lấy next value.</param>
        /// <returns>Giá trị sequence tiếp theo.</returns>
        private async Task<long> GetNextSequenceValueAsync(string sequenceName)
        {
            if (!AllowedSequences.Contains(sequenceName))
            {
                throw new ArgumentException("Tên sequence không hợp lệ", nameof(sequenceName));
            }

            var connection = _context.Database.GetDbConnection();

            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT nextval('{sequenceName}')";

            var result = await command.ExecuteScalarAsync();

            return Convert.ToInt64(result);
        }
    }
}
