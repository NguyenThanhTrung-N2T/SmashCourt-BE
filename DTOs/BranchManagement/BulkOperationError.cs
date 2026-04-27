namespace SmashCourt_BE.DTOs.BranchManagement
{
    public class BulkOperationError
    {
        public Guid UserId { get; set; }
        public string UserName { get; set; } = null!;
        public string ErrorMessage { get; set; } = null!;
    }
}
