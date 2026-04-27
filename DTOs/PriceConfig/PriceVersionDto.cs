using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class PriceVersionListDto
    {
        public string EffectiveFrom { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
    }

    public class PriceVersionDetailDto
    {
        public Guid CourtTypeId { get; set; }
        public string EffectiveFrom { get; set; } = string.Empty;
        public List<PriceVersionRowDto> Rows { get; set; } = new();
    }

    public class BranchPriceVersionDetailDto : PriceVersionDetailDto
    {
        public Guid BranchId { get; set; }
    }

    public class PriceVersionRowDto
    {
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public decimal WeekdayPrice { get; set; }
        public decimal WeekendPrice { get; set; }
    }
}
