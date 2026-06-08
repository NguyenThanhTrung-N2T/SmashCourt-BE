using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Services.Helpers;

namespace SmashCourt_BE.Services
{
    public class SystemPriceService : ISystemPriceService
    {
        private readonly ISystemPriceRepository _repo;
        private readonly ITimeSlotRepository _timeSlotRepo;
        private readonly ICourtTypeRepository _courtTypeRepo;

        public SystemPriceService(
            ISystemPriceRepository repo,
            ITimeSlotRepository timeSlotRepo,
            ICourtTypeRepository courtTypeRepo)
        {
            _repo = repo;
            _timeSlotRepo = timeSlotRepo;
            _courtTypeRepo = courtTypeRepo;
        }

        // ─── Public Methods ──────────────────────────────────────────────────────────

        // GET /api/system-prices/versions
        // Returns all version dates for a court type with their statuses.
        // Each version is tagged as ACTIVE, SCHEDULED, or EXPIRED.
        public async Task<SystemPriceVersionsResponse> GetVersionsAsync(Guid courtTypeId)
        {
            // Validate court type exists
            var courtType = await _courtTypeRepo.GetByIdAsync(courtTypeId);
            if (courtType == null)
                throw new AppException(404, "Không tìm thấy loại sân", ErrorCodes.NotFound);

            // Distinct effective_from dates across all slots for this court type, sorted DESC
            var effectiveDates = await _repo.GetVersionsAsync(courtTypeId);

            var today = DateTimeHelper.GetTodayInVietnam();

            // Active = latest effective_from that is on or before today
            // Default(DateOnly) means no version has taken effect yet — all are SCHEDULED
            var activeVersion = effectiveDates
                .Where(d => d <= today)
                .OrderByDescending(d => d)
                .FirstOrDefault();

            var versions = effectiveDates.Select(d => new VersionSummary
            {
                EffectiveFrom = d.ToString("yyyy-MM-dd"),
                Status = ResolveVersionStatus(d, activeVersion, today)
            }).ToList();

            return new SystemPriceVersionsResponse
            {
                CourtTypeId = courtTypeId,
                Versions = versions
            };
        }

        // GET /api/system-prices/versions/{effectiveFrom}
        // Returns the exact set of slots configured on a specific version date.
        // Uses exact match (effective_from = date), NOT a resolved snapshot.
        // "What did the admin set on this date?" — not "What price applies on this date?"
        public async Task<SystemPriceVersionDetailDto> GetVersionDetailAsync(
            Guid courtTypeId,
            DateOnly effectiveFrom)
        {
            // Validate court type exists
            var courtType = await _courtTypeRepo.GetByIdAsync(courtTypeId);
            if (courtType == null)
                throw new AppException(404, "Không tìm thấy loại sân", ErrorCodes.NotFound);

            return await BuildVersionDetailAsync(courtTypeId, effectiveFrom);
        }

        // PATCH /api/system-prices/versions/{effectiveFrom}
        // Creates or partially updates a system price version.
        //
        // PATCH semantics: only submitted slots are touched — other slots in the version
        // are left unchanged. This allows an admin to change just one time range
        // without having to re-submit the entire version.
        //
        // Supports large time spans: a single slot input (e.g. 06:00–12:00) is
        // automatically expanded into all constituent DB time slots.
        public async Task<(SystemPriceVersionDetailDto Response, bool IsCreated)> UpsertVersionAsync(
            Guid courtTypeId,
            DateOnly effectiveFrom,
            UpsertPriceRequest request)
        {
            // 1. Validate court type exists
            var courtType = await _courtTypeRepo.GetByIdAsync(courtTypeId);
            if (courtType == null)
                throw new AppException(404, "Không tìm thấy loại sân", ErrorCodes.NotFound);

            // 2. Cannot modify past versions
            var today = DateTimeHelper.GetTodayInVietnam();
            if (effectiveFrom < today)
                throw new AppException(400,
                    "Không thể tạo hoặc cập nhật phiên bản giá trong quá khứ",
                    ErrorCodes.BadRequest);

            // 3. Load all time slots once — used for range expansion in the loop below
            var allTimeSlots = await _timeSlotRepo.GetAllAsync();

            // 4. First pass: validate + expand all input ranges before any DB writes
            //
            //    allMatchedSlotIds accumulates every DB time_slot_id covered by the request.
            //    If the same slot_id appears in two input ranges, those ranges overlap — fail fast.
            var allMatchedSlotIds = new HashSet<Guid>();
            var expandedSlots = new List<(PriceSlotInput Input, List<TimeSlot> Slots)>();

            foreach (var slotInput in request.Slots)
            {
                // Parse time strings
                if (!DateTimeHelper.TryParseTimeOnly(slotInput.StartTime, out var startTime))
                    throw new AppException(400,
                        $"Định dạng giờ bắt đầu không hợp lệ: {slotInput.StartTime}. Sử dụng HH:mm hoặc HH:mm:ss",
                        ErrorCodes.BadRequest);

                if (!DateTimeHelper.TryParseTimeOnly(slotInput.EndTime, out var endTime))
                    throw new AppException(400,
                        $"Định dạng giờ kết thúc không hợp lệ: {slotInput.EndTime}. Sử dụng HH:mm hoặc HH:mm:ss",
                        ErrorCodes.BadRequest);

                // Start must be strictly before end
                if (startTime >= endTime)
                    throw new AppException(400,
                        $"Giờ bắt đầu phải nhỏ hơn giờ kết thúc: {slotInput.StartTime} - {slotInput.EndTime}",
                        ErrorCodes.BadRequest);

                // Prices must be non-negative
                if (slotInput.WeekdayPrice < 0 || slotInput.WeekendPrice < 0)
                    throw new AppException(400,
                        $"Giá không được âm tại khung giờ {slotInput.StartTime} - {slotInput.EndTime}",
                        ErrorCodes.BadRequest);

                // Expand: find all DB time slots fully contained within the submitted range
                var matched = allTimeSlots
                    .Where(ts => ts.StartTime >= startTime && ts.EndTime <= endTime)
                    .ToList();

                if (!matched.Any())
                    throw new AppException(400,
                        $"Không tìm thấy khung giờ nào trong khoảng {startTime:HH\\:mm} - {endTime:HH\\:mm}",
                        ErrorCodes.BadRequest);

                // Validate the expanded slots form an unbroken chain that exactly covers the input range
                var uniqueRanges = matched
                    .GroupBy(ts => new { ts.StartTime, ts.EndTime })
                    .OrderBy(g => g.Key.StartTime)
                    .ToList();

                if (uniqueRanges.First().Key.StartTime != startTime ||
                    uniqueRanges.Last().Key.EndTime != endTime)
                    throw new AppException(400,
                        $"Khoảng {startTime:HH\\:mm} - {endTime:HH\\:mm} không khớp với cấu hình khung giờ hệ thống",
                        ErrorCodes.BadRequest);

                for (int i = 0; i < uniqueRanges.Count - 1; i++)
                {
                    if (uniqueRanges[i].Key.EndTime != uniqueRanges[i + 1].Key.StartTime)
                        throw new AppException(400,
                            $"Khoảng {startTime:HH\\:mm} - {endTime:HH\\:mm} bị đứt quãng trong cấu hình hệ thống",
                            ErrorCodes.BadRequest);
                }

                // Overlap detection: a slot ID that already appears in another input range is an overlap
                foreach (var ts in matched)
                {
                    if (!allMatchedSlotIds.Add(ts.Id))
                        throw new AppException(400,
                            "Các khoảng thời gian trong yêu cầu bị chồng lấp nhau",
                            ErrorCodes.BadRequest);
                }

                expandedSlots.Add((slotInput, matched));
            }

            // 5. Determine whether this is a create or update (for response status code)
            var existingPrices = await _repo.GetExactDatePricesAsync(courtTypeId, effectiveFrom);
            var isCreated = !existingPrices.Any();

            // 6. Second pass: build insert / update lists
            //    PATCH semantics — only rows in the request are touched;
            //    existing rows for other time slots in this version are left unchanged.
            var insertPrices = new List<SystemPrice>();
            var updatePrices = new List<SystemPrice>();

            foreach (var (slotInput, matchedSlots) in expandedSlots)
            {
                foreach (var ts in matchedSlots)
                {
                    var priceToApply = ts.DayType == DayType.WEEKDAY
                        ? slotInput.WeekdayPrice
                        : slotInput.WeekendPrice;

                    var existing = existingPrices.FirstOrDefault(sp => sp.TimeSlotId == ts.Id);
                    if (existing != null)
                    {
                        existing.Price = priceToApply;
                        updatePrices.Add(existing);
                    }
                    else
                    {
                        insertPrices.Add(new SystemPrice
                        {
                            CourtTypeId = courtTypeId,
                            TimeSlotId = ts.Id,
                            Price = priceToApply,
                            EffectiveFrom = effectiveFrom,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
            }

            // 7. Persist
            await _repo.UpsertBatchAsync(insertPrices, updatePrices);

            // 8. Return the full version detail
            var response = await BuildVersionDetailAsync(courtTypeId, effectiveFrom);
            return (response, isCreated);
        }

        // DELETE /api/system-prices/versions/{effectiveFrom}
        // Deletes an entire system price version — all rows for (courtTypeId, effectiveFrom).
        // Only SCHEDULED (future) versions can be deleted.
        // Active and expired versions are locked — they are historical records.
        //
        // NOTE: requires ISystemPriceRepository.DeleteVersionAsync(courtTypeId, effectiveFrom)
        // SQL: DELETE FROM system_prices
        //      WHERE court_type_id = @courtTypeId AND effective_from = @effectiveFrom
        public async Task DeleteVersionAsync(Guid courtTypeId, DateOnly effectiveFrom)
        {
            // Validate court type exists
            var courtType = await _courtTypeRepo.GetByIdAsync(courtTypeId);
            if (courtType == null)
                throw new AppException(404, "Không tìm thấy loại sân", ErrorCodes.NotFound);

            var today = DateTimeHelper.GetTodayInVietnam();
            if (effectiveFrom <= today)
                throw new AppException(400,
                    "Không thể xóa phiên bản giá đã hoặc đang có hiệu lực",
                    ErrorCodes.BadRequest);

            var deleted = await _repo.DeleteVersionAsync(courtTypeId, effectiveFrom);

            if (deleted == 0)
                throw new AppException(404,
                    "Không tìm thấy phiên bản giá để xóa",
                    ErrorCodes.NotFound);
        }

        // Legacy method for internal price calculations
        // Lấy giá chung resolved cho 1 ngày cụ thể
        public async Task<List<CurrentPriceDto>> GetEffectivePricesAsync(DateOnly date, Guid? courtTypeId = null)
        {
            var prices = await _repo.GetCurrentForDateAsync(date, courtTypeId);
            var grouped = GroupPrices(prices);
            return PriceSlotMerger.MergeConsecutivePriceSlots(grouped);
        }

        // ─── Private Helpers ─────────────────────────────────────────────────────────

        // Shared implementation for building version detail.
        // Used by GetVersionDetailAsync and UpsertVersionAsync
        // to avoid redundant DB calls.
        private async Task<SystemPriceVersionDetailDto> BuildVersionDetailAsync(
            Guid courtTypeId,
            DateOnly effectiveFrom)
        {
            // Exact match — only rows physically created with this effective_from date.
            // This shows "what was configured in this version", NOT the resolved price picture.
            var prices = await _repo.GetExactDatePricesAsync(courtTypeId, effectiveFrom);

            if (!prices.Any())
                throw new AppException(404,
                    "Không tìm thấy phiên bản giá cho ngày hiệu lực này",
                    ErrorCodes.NotFound);

            // Status: SCHEDULED is deterministic — no DB call needed for future dates
            var today = DateTimeHelper.GetTodayInVietnam();
            string status;

            if (effectiveFrom > today)
            {
                status = "SCHEDULED";
            }
            else
            {
                // For past/today dates we need the active version to distinguish ACTIVE from EXPIRED
                var allVersions = await _repo.GetVersionsAsync(courtTypeId);
                var activeVersion = allVersions
                    .Where(d => d <= today)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();

                status = ResolveVersionStatus(effectiveFrom, activeVersion, today);
            }

            // Group WEEKDAY + WEEKEND rows into single slot records
            var slots = prices
                .GroupBy(sp => new { sp.TimeSlot.StartTime, sp.TimeSlot.EndTime })
                .Select(g => new PriceSlotDetail
                {
                    StartTime = g.Key.StartTime.ToString("HH:mm:ss"),
                    EndTime = g.Key.EndTime.ToString("HH:mm:ss"),
                    WeekdayPrice = g.FirstOrDefault(sp => sp.TimeSlot.DayType == DayType.WEEKDAY)?.Price ?? 0,
                    WeekendPrice = g.FirstOrDefault(sp => sp.TimeSlot.DayType == DayType.WEEKEND)?.Price ?? 0
                })
                .OrderBy(s => s.StartTime)
                .ToList();

            return new SystemPriceVersionDetailDto
            {
                CourtTypeId = courtTypeId,
                CourtTypeName = prices.First().CourtType?.Name ?? "N/A",
                EffectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
                Status = status,
                Slots = slots
            };
        }

        // Resolves version status from a date, the known active version, and today.
        //
        // Status rules:
        //   SCHEDULED  → effective_from is in the future (not yet in effect)
        //   ACTIVE     → latest version whose effective_from <= today (currently applying)
        //   EXPIRED    → effective_from <= today but superseded by a newer version
        //
        // When activeVersion is default (no version has taken effect yet),
        // all dates are either future (SCHEDULED) or should not logically reach this path.
        private static string ResolveVersionStatus(DateOnly date, DateOnly activeVersion, DateOnly today)
        {
            if (date > today) return "SCHEDULED";
            if (activeVersion == default) return "SCHEDULED"; // no version is active yet (safety guard)
            if (date == activeVersion) return "ACTIVE";
            return "EXPIRED";
        }

        // Group WEEKDAY + WEEKEND rows into single records (legacy helper for GetEffectivePricesAsync)
        private static List<CurrentPriceDto> GroupPrices(List<SystemPrice> prices)
        {
            return prices
                .GroupBy(sp => new
                {
                    sp.CourtTypeId,
                    sp.TimeSlot.StartTime,
                    sp.TimeSlot.EndTime,
                    sp.EffectiveFrom
                })
                .Select(g => new CurrentPriceDto
                {
                    CourtTypeId = g.Key.CourtTypeId,
                    CourtTypeName = g.First().CourtType?.Name ?? "N/A",
                    StartTime = g.Key.StartTime.ToTimeSpan(),
                    EndTime = g.Key.EndTime.ToTimeSpan(),
                    WeekdayPrice = g.FirstOrDefault(sp =>
                        sp.TimeSlot.DayType == DayType.WEEKDAY)?.Price ?? 0,
                    WeekendPrice = g.FirstOrDefault(sp =>
                        sp.TimeSlot.DayType == DayType.WEEKEND)?.Price ?? 0,
                    EffectiveFrom = g.Key.EffectiveFrom.ToString("yyyy-MM-dd")
                })
                .OrderBy(p => p.CourtTypeName)
                .ThenBy(p => p.StartTime)
                .ToList();
        }
    }
}
