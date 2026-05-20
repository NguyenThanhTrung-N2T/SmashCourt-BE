using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Data;
using SmashCourt_BE.Services.IService;
using System.Data;
namespace SmashCourt_BE.Services
{
    public class CodeGeneratorService : ICodeGeneratorService
    {
        private readonly SmashCourtContext _context;

        public CodeGeneratorService(SmashCourtContext context)
        {
            _context = context;
        }

        public async Task<string> GenerateBookingCodeAsync()
        {
            var sequenceValue = await GetNextSequenceValueAsync("booking_code_seq");

            return $"BK-{DateTime.UtcNow:yyyyMMdd}-{sequenceValue:D6}";
        }

        public async Task<string> GenerateInvoiceCodeAsync()
        {
            var sequenceValue = await GetNextSequenceValueAsync("invoice_code_seq");

            return $"INV-{DateTime.UtcNow:yyyyMMdd}-{sequenceValue:D6}";
        }

        private async Task<long> GetNextSequenceValueAsync(string sequenceName)
        {
            var allowedSequences = new[]
            {
                "booking_code_seq",
                "invoice_code_seq"
            };

            if (!allowedSequences.Contains(sequenceName))
            {
                throw new ArgumentException("Invalid sequence name");
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