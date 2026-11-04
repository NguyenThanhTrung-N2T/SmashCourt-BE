using System.Data.Common;
using Npgsql;

namespace SmashCourt_BE.Extensions;

public static class NpgsqlAutoPrepareExtensions
{
    // Số câu SQL tối đa Npgsql tự prepare trên mỗi kết nối (P6-54, plan 6C giai đoạn 2C).
    // Câu đã prepare dùng kế hoạch chung nên PostgreSQL không lập kế hoạch lại và không khóa mọi index mỗi lần chạy (đo 04/11: CPU PostgreSQL −44 %).
    // Mặc định 0 = tắt. Người dùng chốt 05/11: chỉ môi trường kiểm thử và đánh giá hiệu năng (perf) bật, bản deploy KHÔNG bật.
    // Qua PgBouncer chế độ transaction (Neon "-pooler") phải kiểm riêng trước khi bật.
    // Đổi kiểu một cột đang được đọc khi BE đang chạy ⇒ câu đã prepare lỗi "cached plan must not change result type": chạy script đổi schema xong phải khởi động lại BE.
    public const string ConfigKey = "Database:MaxAutoPrepare";
    public const int MaxAllowed = 256;

    private static readonly string[] ConnectionStringKeys = ["Max Auto Prepare", "MaxAutoPrepare"];

    // Dùng chung cho app (Program.cs) và integration test để test chạy đúng cấu hình kết nối của app
    public static NpgsqlDataSourceBuilder UseSmashCourtAutoPrepare(this NpgsqlDataSourceBuilder builder, IConfiguration configuration)
    {
        var configured = ReadConfigured(configuration);

        // Chuỗi kết nối ghi rõ thì chuỗi thắng (môi trường perf đổi bằng chuỗi kết nối, thí nghiệm E4)
        var raw = new DbConnectionStringBuilder { ConnectionString = builder.ConnectionString };
        if (ConnectionStringKeys.Any(raw.ContainsKey))
            return builder;

        builder.ConnectionStringBuilder.MaxAutoPrepare = configured;
        return builder;
    }

    private static int ReadConfigured(IConfiguration configuration)
    {
        var text = configuration[ConfigKey];
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        if (!int.TryParse(text, out var value) || value < 0 || value > MaxAllowed)
            throw new InvalidOperationException($"Cấu hình {ConfigKey} phải là số nguyên từ 0 đến {MaxAllowed} (0 = tắt).");

        return value;
    }
}
