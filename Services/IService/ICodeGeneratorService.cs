namespace SmashCourt_BE.Services.IService
{
    public interface ICodeGeneratorService
    {
        Task<string> GenerateBookingCodeAsync();
        Task<string> GenerateInvoiceCodeAsync();
    }
}