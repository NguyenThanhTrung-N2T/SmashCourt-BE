namespace SmashCourt_BE.DTOs.BranchManagement
{
    public class BulkStaffOperationResultDto
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<BulkOperationError> Errors { get; set; } = [];
    }
}
