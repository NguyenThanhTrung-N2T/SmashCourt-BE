namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>Trạng thái mạch ngắt của lệnh đánh giá cọc (P6-57). Giá trị số là giá trị của gauge <c>smashcourt.tadss.circuit.state</c>.</summary>
public enum TadssCircuitState
{
    /// <summary>Bình thường: mọi đơn online gọi TADSS (timeout + một lần thử lại như trước P6-57).</summary>
    Closed = 0,

    /// <summary>TADSS được xác nhận không trả lời: đơn online cọc 100 % ngay, không gọi TADSS.</summary>
    Open = 1,

    /// <summary>Cho đúng một đơn đi thử (một lần gọi, không thử lại); các đơn khác vẫn cọc 100 % ngay.</summary>
    HalfOpen = 2
}
