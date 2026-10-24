using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Deposit;

/// <summary>
/// Bốn trường cọc trả cho FE.
/// </summary>
/// <param name="RemainingAmount">Phần thu tại quầy: tổng hiện tại trừ tiền cọc, không âm</param>
/// <param name="Source">Ai quyết định chính sách cọc</param>
public readonly record struct DepositFields(decimal Rate, decimal Amount, decimal RemainingAmount, DepositSource Source);
