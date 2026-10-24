using System.Transactions;

namespace SmashCourt_BE.Helpers
{
    /// <summary>
    /// TransactionScope mức Read Committed cho luồng ghi bằng câu cập nhật có điều kiện trạng thái.
    /// Mặc định của TransactionScope là Serializable: hai đơn ghi cùng lúc dễ lỗi 40001 khi commit.
    /// Không dùng cho luồng còn đọc rồi ghi không điều kiện, vì ở đó hạ mức cô lập sẽ gây mất cập nhật.
    /// </summary>
    public static class TransactionScopeHelper
    {
        public static TransactionScope ReadCommitted() => new(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted, Timeout = TransactionManager.DefaultTimeout },
            TransactionScopeAsyncFlowOption.Enabled);
    }
}
