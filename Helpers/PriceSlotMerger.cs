using SmashCourt_BE.DTOs.PriceConfig;

namespace SmashCourt_BE.Helpers
{
    /// <summary>
    /// Helper class để merge các timeslot liên tiếp có cùng giá
    /// </summary>
    public static class PriceSlotMerger
    {
        /// <summary>
        /// Merge các CurrentPriceDto liên tiếp có cùng giá
        /// Xử lý riêng weekday và weekend price
        /// </summary>
        public static List<CurrentPriceDto> MergeConsecutivePriceSlots(List<CurrentPriceDto> slots)
        {
            if (slots == null || slots.Count == 0)
                return new List<CurrentPriceDto>();

            if (slots.Count == 1)
                return slots;

            var result = new List<CurrentPriceDto>();

            // Group theo (CourtTypeId, EffectiveFrom) để xử lý từng nhóm độc lập
            var groups = slots
                .GroupBy(s => new { s.CourtTypeId, s.EffectiveFrom })
                .OrderBy(g => g.Key.CourtTypeId)
                .ThenBy(g => g.Key.EffectiveFrom);

            foreach (var group in groups)
            {
                var groupSlots = group.OrderBy(s => s.StartTime).ToList();

                // Merge weekday prices và weekend prices độc lập
                var mergedWeekday = MergeByWeekdayPrice(groupSlots);
                var mergedWeekend = MergeByWeekendPrice(groupSlots);

                // Combine: tạo slot mới với cả weekday và weekend đã merge
                var combined = CombineWeekdayWeekendMerges(mergedWeekday, mergedWeekend, groupSlots.First());

                result.AddRange(combined);
            }

            return result
                .OrderBy(s => s.CourtTypeName)
                .ThenBy(s => s.EffectiveFrom)
                .ThenBy(s => s.StartTime)
                .ToList();
        }

        /// <summary>
        /// Merge các EffectivePriceDto liên tiếp có cùng giá và cùng source
        /// </summary>
        public static List<EffectivePriceDto> MergeConsecutiveEffectivePriceSlots(List<EffectivePriceDto> slots)
        {
            if (slots == null || slots.Count == 0)
                return new List<EffectivePriceDto>();

            if (slots.Count == 1)
                return slots;

            var result = new List<EffectivePriceDto>();

            // Group theo (CourtTypeId, EffectiveFrom, PriceSource) để xử lý từng nhóm độc lập
            var groups = slots
                .GroupBy(s => new { s.CourtTypeId, s.EffectiveFrom, s.PriceSource })
                .OrderBy(g => g.Key.CourtTypeId)
                .ThenBy(g => g.Key.EffectiveFrom);

            foreach (var group in groups)
            {
                var groupSlots = group.OrderBy(s => s.StartTime).ToList();

                // Merge weekday prices và weekend prices độc lập
                var mergedWeekday = MergeByWeekdayPriceEffective(groupSlots);
                var mergedWeekend = MergeByWeekendPriceEffective(groupSlots);

                // Combine: tạo slot mới với cả weekday và weekend đã merge
                var combined = CombineWeekdayWeekendMergesEffective(mergedWeekday, mergedWeekend, groupSlots.First());

                result.AddRange(combined);
            }

            return result
                .OrderBy(s => s.CourtTypeName)
                .ThenBy(s => s.EffectiveFrom)
                .ThenBy(s => s.StartTime)
                .ToList();
        }

        #region Private Helper Methods for CurrentPriceDto

        private static List<(TimeSpan Start, TimeSpan End, decimal Price)> MergeByWeekdayPrice(List<CurrentPriceDto> slots)
        {
            var result = new List<(TimeSpan Start, TimeSpan End, decimal Price)>();
            if (!slots.Any()) return result;

            var current = (Start: slots[0].StartTime, End: slots[0].EndTime, Price: slots[0].WeekdayPrice);

            for (int i = 1; i < slots.Count; i++)
            {
                var next = slots[i];

                // Kiểm tra có thể merge: liên tiếp và cùng giá
                if (current.End == next.StartTime && current.Price == next.WeekdayPrice)
                {
                    // Merge: mở rộng EndTime
                    current.End = next.EndTime;
                }
                else
                {
                    // Không merge được: lưu current, bắt đầu mới
                    result.Add(current);
                    current = (next.StartTime, next.EndTime, next.WeekdayPrice);
                }
            }

            // Add slot cuối cùng
            result.Add(current);
            return result;
        }

        private static List<(TimeSpan Start, TimeSpan End, decimal Price)> MergeByWeekendPrice(List<CurrentPriceDto> slots)
        {
            var result = new List<(TimeSpan Start, TimeSpan End, decimal Price)>();
            if (!slots.Any()) return result;

            var current = (Start: slots[0].StartTime, End: slots[0].EndTime, Price: slots[0].WeekendPrice);

            for (int i = 1; i < slots.Count; i++)
            {
                var next = slots[i];

                // Kiểm tra có thể merge: liên tiếp và cùng giá
                if (current.End == next.StartTime && current.Price == next.WeekendPrice)
                {
                    // Merge: mở rộng EndTime
                    current.End = next.EndTime;
                }
                else
                {
                    // Không merge được: lưu current, bắt đầu mới
                    result.Add(current);
                    current = (next.StartTime, next.EndTime, next.WeekendPrice);
                }
            }

            // Add slot cuối cùng
            result.Add(current);
            return result;
        }

        private static List<CurrentPriceDto> CombineWeekdayWeekendMerges(
            List<(TimeSpan Start, TimeSpan End, decimal Price)> weekdayMerged,
            List<(TimeSpan Start, TimeSpan End, decimal Price)> weekendMerged,
            CurrentPriceDto template)
        {
            var result = new List<CurrentPriceDto>();

            // Tạo tập hợp tất cả các time boundaries
            var allTimePoints = new SortedSet<TimeSpan>();
            foreach (var slot in weekdayMerged)
            {
                allTimePoints.Add(slot.Start);
                allTimePoints.Add(slot.End);
            }
            foreach (var slot in weekendMerged)
            {
                allTimePoints.Add(slot.Start);
                allTimePoints.Add(slot.End);
            }

            var timeRanges = new List<(TimeSpan Start, TimeSpan End)>();
            var timeList = allTimePoints.ToList();
            for (int i = 0; i < timeList.Count - 1; i++)
            {
                timeRanges.Add((timeList[i], timeList[i + 1]));
            }

            // Với mỗi time range, tìm giá tương ứng
            foreach (var range in timeRanges)
            {
                var weekdayPrice = FindPriceForRange(range.Start, range.End, weekdayMerged);
                var weekendPrice = FindPriceForRange(range.Start, range.End, weekendMerged);

                // Chỉ tạo slot nếu có ít nhất 1 giá hợp lệ
                if (weekdayPrice >= 0 || weekendPrice >= 0)
                {
                    result.Add(new CurrentPriceDto
                    {
                        CourtTypeId = template.CourtTypeId,
                        CourtTypeName = template.CourtTypeName,
                        StartTime = range.Start,
                        EndTime = range.End,
                        WeekdayPrice = weekdayPrice,
                        WeekendPrice = weekendPrice,
                        EffectiveFrom = template.EffectiveFrom
                    });
                }
            }

            // Merge lại các slot liên tiếp có cùng cả weekday và weekend price
            return FinalMergeCurrentPriceDto(result);
        }

        private static decimal FindPriceForRange(TimeSpan start, TimeSpan end, List<(TimeSpan Start, TimeSpan End, decimal Price)> mergedSlots)
        {
            foreach (var slot in mergedSlots)
            {
                // Range [start, end) nằm hoàn toàn trong [slot.Start, slot.End)
                if (start >= slot.Start && end <= slot.End)
                {
                    return slot.Price;
                }
            }
            return 0; // Không tìm thấy
        }

        private static List<CurrentPriceDto> FinalMergeCurrentPriceDto(List<CurrentPriceDto> slots)
        {
            if (slots.Count <= 1) return slots;

            var result = new List<CurrentPriceDto>();
            var current = slots[0];

            for (int i = 1; i < slots.Count; i++)
            {
                var next = slots[i];

                // Merge nếu: liên tiếp và cùng CẢ weekday và weekend price
                if (current.EndTime == next.StartTime &&
                    current.WeekdayPrice == next.WeekdayPrice &&
                    current.WeekendPrice == next.WeekendPrice)
                {
                    current.EndTime = next.EndTime;
                }
                else
                {
                    result.Add(current);
                    current = next;
                }
            }

            result.Add(current);
            return result;
        }

        #endregion

        #region Private Helper Methods for EffectivePriceDto

        private static List<(TimeSpan Start, TimeSpan End, decimal Price)> MergeByWeekdayPriceEffective(List<EffectivePriceDto> slots)
        {
            var result = new List<(TimeSpan Start, TimeSpan End, decimal Price)>();
            if (!slots.Any()) return result;

            var current = (Start: slots[0].StartTime, End: slots[0].EndTime, Price: slots[0].WeekdayPrice);

            for (int i = 1; i < slots.Count; i++)
            {
                var next = slots[i];

                if (current.End == next.StartTime && current.Price == next.WeekdayPrice)
                {
                    current.End = next.EndTime;
                }
                else
                {
                    result.Add(current);
                    current = (next.StartTime, next.EndTime, next.WeekdayPrice);
                }
            }

            result.Add(current);
            return result;
        }

        private static List<(TimeSpan Start, TimeSpan End, decimal Price)> MergeByWeekendPriceEffective(List<EffectivePriceDto> slots)
        {
            var result = new List<(TimeSpan Start, TimeSpan End, decimal Price)>();
            if (!slots.Any()) return result;

            var current = (Start: slots[0].StartTime, End: slots[0].EndTime, Price: slots[0].WeekendPrice);

            for (int i = 1; i < slots.Count; i++)
            {
                var next = slots[i];

                if (current.End == next.StartTime && current.Price == next.WeekendPrice)
                {
                    current.End = next.EndTime;
                }
                else
                {
                    result.Add(current);
                    current = (next.StartTime, next.EndTime, next.WeekendPrice);
                }
            }

            result.Add(current);
            return result;
        }

        private static List<EffectivePriceDto> CombineWeekdayWeekendMergesEffective(
            List<(TimeSpan Start, TimeSpan End, decimal Price)> weekdayMerged,
            List<(TimeSpan Start, TimeSpan End, decimal Price)> weekendMerged,
            EffectivePriceDto template)
        {
            var result = new List<EffectivePriceDto>();

            var allTimePoints = new SortedSet<TimeSpan>();
            foreach (var slot in weekdayMerged)
            {
                allTimePoints.Add(slot.Start);
                allTimePoints.Add(slot.End);
            }
            foreach (var slot in weekendMerged)
            {
                allTimePoints.Add(slot.Start);
                allTimePoints.Add(slot.End);
            }

            var timeRanges = new List<(TimeSpan Start, TimeSpan End)>();
            var timeList = allTimePoints.ToList();
            for (int i = 0; i < timeList.Count - 1; i++)
            {
                timeRanges.Add((timeList[i], timeList[i + 1]));
            }

            foreach (var range in timeRanges)
            {
                var weekdayPrice = FindPriceForRange(range.Start, range.End, weekdayMerged);
                var weekendPrice = FindPriceForRange(range.Start, range.End, weekendMerged);

                if (weekdayPrice >= 0 || weekendPrice >= 0)
                {
                    result.Add(new EffectivePriceDto
                    {
                        CourtTypeId = template.CourtTypeId,
                        CourtTypeName = template.CourtTypeName,
                        StartTime = range.Start,
                        EndTime = range.End,
                        WeekdayPrice = weekdayPrice,
                        WeekendPrice = weekendPrice,
                        EffectiveFrom = template.EffectiveFrom,
                        PriceSource = template.PriceSource
                    });
                }
            }

            return FinalMergeEffectivePriceDto(result);
        }

        private static List<EffectivePriceDto> FinalMergeEffectivePriceDto(List<EffectivePriceDto> slots)
        {
            if (slots.Count <= 1) return slots;

            var result = new List<EffectivePriceDto>();
            var current = slots[0];

            for (int i = 1; i < slots.Count; i++)
            {
                var next = slots[i];

                if (current.EndTime == next.StartTime &&
                    current.WeekdayPrice == next.WeekdayPrice &&
                    current.WeekendPrice == next.WeekendPrice)
                {
                    current.EndTime = next.EndTime;
                }
                else
                {
                    result.Add(current);
                    current = next;
                }
            }

            result.Add(current);
            return result;
        }

        #endregion
    }
}
