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
    public class BranchPriceService : IBranchPriceService
    {
        private readonly IBranchPriceRepository _repo;
        private readonly ISystemPriceRepository _systemPriceRepo;
        private readonly ITimeSlotRepository _timeSlotRepo;
        private readonly IBranchRepository _branchRepo;
        private readonly ICourtRepository _courtRepo;
        private readonly IBranchScopeResolver _branchScopeResolver;

        public BranchPriceService(
            IBranchPriceRepository repo,
            ISystemPriceRepository systemPriceRepo,
            ITimeSlotRepository timeSlotRepo,
            IBranchRepository branchRepo,
            ICourtRepository courtRepo,
            IBranchScopeResolver branchScopeResolver)
        {
            _repo = repo;
            _systemPriceRepo = systemPriceRepo;
            _timeSlotRepo = timeSlotRepo;
            _branchRepo = branchRepo;
            _courtRepo = courtRepo;
            _branchScopeResolver = branchScopeResolver;
        }

        // ─── Public Methods ──────────────────────────────────────────────────────────

        // GET /api/prices
        // Returns effective pricing snapshot for a branch on a specific date.
        // For each time slot: branch override wins if it exists, otherwise falls back to system price.
        public async Task<EffectivePricesResponse> GetEffectivePricesAsync(
            Guid? requestedBranchId,
            DateOnly date,
            Guid? courtTypeId,
            Guid currentUserId,
            string currentUserRole)
        {
            if (!Enum.TryParse<UserRole>(currentUserRole, true, out var roleEnum))
                throw new AppException(403, "Role không hợp lệ", ErrorCodes.Forbidden);

            var branchId = await _branchScopeResolver.ResolveRequiredBranchIdAsync(
                requestedBranchId, currentUserId, roleEnum);

            // Fetch both price sources for the target date
            var branchPrices = await _repo.GetCurrentForDateAsync(branchId, date, courtTypeId);
            var systemPrices = await _systemPriceRepo.GetCurrentForDateAsync(date, courtTypeId);

            // Index branch overrides by (CourtTypeId, StartTime, EndTime) for O(1) merge lookup
            var branchOverrideDict = branchPrices
                .GroupBy(bp => new { bp.CourtTypeId, bp.TimeSlot.StartTime, bp.TimeSlot.EndTime })
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        WeekdayPrice = g.FirstOrDefault(x => x.TimeSlot.DayType == DayType.WEEKDAY)?.Price ?? 0,
                        WeekendPrice = g.FirstOrDefault(x => x.TimeSlot.DayType == DayType.WEEKEND)?.Price ?? 0,
                        EffectiveFrom = g.First().EffectiveFrom
                    });

            // Merge: system prices define available slots; branch override replaces price where it exists
            var effectivePrices = systemPrices
                .GroupBy(sp => new { sp.CourtTypeId, sp.TimeSlot.StartTime, sp.TimeSlot.EndTime })
                .Select(g =>
                {
                    var key = g.Key;
                    var hasBranch = branchOverrideDict.TryGetValue(key, out var branch);

                    return new EffectivePriceDto
                    {
                        CourtTypeId = key.CourtTypeId,
                        CourtTypeName = g.First().CourtType?.Name ?? "N/A",
                        StartTime = key.StartTime.ToTimeSpan(),
                        EndTime = key.EndTime.ToTimeSpan(),
                        WeekdayPrice = hasBranch
                            ? branch!.WeekdayPrice
                            : (g.FirstOrDefault(x => x.TimeSlot.DayType == DayType.WEEKDAY)?.Price ?? 0),
                        WeekendPrice = hasBranch
                            ? branch!.WeekendPrice
                            : (g.FirstOrDefault(x => x.TimeSlot.DayType == DayType.WEEKEND)?.Price ?? 0),
                        EffectiveFrom = (hasBranch ? branch!.EffectiveFrom : g.First().EffectiveFrom)
                            .ToString("yyyy-MM-dd"),
                        PriceSource = hasBranch ? "BRANCH" : "SYSTEM"
                    };
                })
                .OrderBy(p => p.CourtTypeName)
                .ThenBy(p => p.StartTime)
                .ToList();

            // Merge consecutive slots with identical weekday + weekend prices (display optimization)
            var merged = PriceSlotMerger.MergeConsecutiveEffectivePriceSlots(effectivePrices);

            // Group by court type
            var courtTypeGroups = merged
                .GroupBy(p => new { p.CourtTypeId, p.CourtTypeName })
                .Select(g => new CourtTypeEffectivePrices
                {
                    CourtTypeId = g.Key.CourtTypeId,
                    CourtTypeName = g.Key.CourtTypeName,
                    Slots = g.Select(s => new EffectiveSlot
                    {
                        StartTime = s.StartTime,
                        EndTime = s.EndTime,
                        WeekdayPrice = s.WeekdayPrice,
                        WeekendPrice = s.WeekendPrice,
                        EffectiveFrom = s.EffectiveFrom,
                        PriceSource = s.PriceSource
                    })
                    .OrderBy(s => s.StartTime)
                    .ToList()
                })
                .OrderBy(ct => ct.CourtTypeName)
                .ToList();

            return new EffectivePricesResponse
            {
                BranchId = branchId,
                Date = date.ToString("yyyy-MM-dd"),
                CourtTypes = courtTypeGroups
            };
        }

        // GET /api/prices/overrides
        // Returns all override version dates for a branch + court type.
        // Each version is tagged as ACTIVE, SCHEDULED, or EXPIRED.
        public async Task<PriceOverrideVersionsResponse> GetPriceOverrideVersionsAsync(
            Guid? requestedBranchId,
            Guid courtTypeId,
            Guid currentUserId,
            string currentUserRole)
        {
            if (!Enum.TryParse<UserRole>(currentUserRole, true, out var roleEnum))
                throw new AppException(403, "Role không hợp lệ", ErrorCodes.Forbidden);

            var branchId = await _branchScopeResolver.ResolveRequiredBranchIdAsync(
                requestedBranchId, currentUserId, roleEnum);

            // Distinct effective_from dates across all slots for this branch + court type, sorted DESC
            var effectiveDates = await _repo.GetVersionsAsync(branchId, courtTypeId);

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

            return new PriceOverrideVersionsResponse
            {
                BranchId = branchId,
                CourtTypeId = courtTypeId,
                Versions = versions
            };
        }

        // GET /api/prices/overrides/{effectiveFrom}
        // Returns the exact set of slots configured on a specific version date.
        // Uses exact match (effective_from = date), NOT a resolved snapshot.
        // "What did the manager set on this date?" — not "What price applies on this date?"
        public async Task<PriceOverrideVersionDetailDto> GetPriceOverrideVersionDetailAsync(
            Guid? requestedBranchId,
            Guid courtTypeId,
            DateOnly effectiveFrom,
            Guid currentUserId,
            string currentUserRole)
        {
            if (!Enum.TryParse<UserRole>(currentUserRole, true, out var roleEnum))
                throw new AppException(403, "Role không hợp lệ", ErrorCodes.Forbidden);

            var branchId = await _branchScopeResolver.ResolveRequiredBranchIdAsync(
                requestedBranchId, currentUserId, roleEnum);

            return await BuildVersionDetailAsync(branchId, courtTypeId, effectiveFrom);
        }

        // PATCH /api/prices/overrides/{effectiveFrom}
        // Creates or partially updates a price override version.
        //
        // PATCH semantics: only submitted slots are touched — other slots in the version
        // are left unchanged. This allows a manager to change just one time range
        // without having to re-submit the entire version.
        //
        // Supports large time spans: a single slot input (e.g. 06:00–12:00) is
        // automatically expanded into all constituent DB time slots.
        public async Task<(PriceOverrideVersionDetailDto Response, bool IsCreated)> UpsertPriceOverrideVersionAsync(
            Guid? requestedBranchId,
            Guid courtTypeId,
            DateOnly effectiveFrom,
            UpsertPriceRequest request,
            Guid currentUserId,
            string currentUserRole)
        {
            // 1. Role + branch
            if (!Enum.TryParse<UserRole>(currentUserRole, true, out var roleEnum))
                throw new AppException(403, "Role không hợp lệ", ErrorCodes.Forbidden);

            var branchId = await _branchScopeResolver.ResolveRequiredBranchIdAsync(
                requestedBranchId, currentUserId, roleEnum);

            // 2. Cannot modify past versions
            var today = DateTimeHelper.GetTodayInVietnam();
            if (effectiveFrom < today)
                throw new AppException(400,
                    "Không thể tạo hoặc cập nhật phiên bản giá trong quá khứ",
                    ErrorCodes.BadRequest);

            // 3. Court type must be enabled for this branch
            var isCourtTypeEnabled = await _branchRepo.IsCourtTypeEnabledAsync(branchId, courtTypeId);
            if (!isCourtTypeEnabled)
                throw new AppException(400,
                    "Loại sân không hợp lệ hoặc không thuộc chi nhánh này",
                    ErrorCodes.BadRequest);

            // 4. Load all time slots once — used for range expansion in the loop below
            var allTimeSlots = await _timeSlotRepo.GetAllAsync();

            // 5. First pass: validate + expand all input ranges before any DB writes
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

            // 6. Determine whether this is a create or update (for response status code)
            var existingPrices = await _repo.GetExactDatePricesAsync(branchId, courtTypeId, effectiveFrom);
            var isCreated = !existingPrices.Any();

            // 7. Second pass: build insert / update lists
            //    PATCH semantics — only rows in the request are touched;
            //    existing rows for other time slots in this version are left unchanged.
            var insertPrices = new List<BranchPriceOverride>();
            var updatePrices = new List<BranchPriceOverride>();

            foreach (var (slotInput, matchedSlots) in expandedSlots)
            {
                foreach (var ts in matchedSlots)
                {
                    var priceToApply = ts.DayType == DayType.WEEKDAY
                        ? slotInput.WeekdayPrice
                        : slotInput.WeekendPrice;

                    var existing = existingPrices.FirstOrDefault(bp => bp.TimeSlotId == ts.Id);
                    if (existing != null)
                    {
                        existing.Price = priceToApply;
                        updatePrices.Add(existing);
                    }
                    else
                    {
                        insertPrices.Add(new BranchPriceOverride
                        {
                            BranchId = branchId,
                            CourtTypeId = courtTypeId,
                            TimeSlotId = ts.Id,
                            Price = priceToApply,
                            EffectiveFrom = effectiveFrom,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
            }

            // 8. Persist
            await _repo.UpsertBatchAsync(insertPrices, updatePrices);

            // 9. Return the full version detail (branchId already resolved — no double lookup)
            var response = await BuildVersionDetailAsync(branchId, courtTypeId, effectiveFrom);
            return (response, isCreated);
        }

        // DELETE /api/prices/overrides/{effectiveFrom}
        // Deletes an entire override version — all rows for (branchId, courtTypeId, effectiveFrom).
        // Only SCHEDULED (future) versions can be deleted.
        // Active and expired versions are locked — they are historical records.
        //
        // NOTE: requires IBranchPriceRepository.DeleteVersionAsync(branchId, courtTypeId, effectiveFrom)
        // SQL: DELETE FROM branch_price_overrides
        //      WHERE branch_id = @branchId AND court_type_id = @courtTypeId AND effective_from = @effectiveFrom
        public async Task DeleteVersionAsync(
            Guid? requestedBranchId,
            Guid courtTypeId,
            DateOnly effectiveFrom,
            Guid currentUserId,
            string currentUserRole)
        {
            if (!Enum.TryParse<UserRole>(currentUserRole, true, out var roleEnum))
                throw new AppException(403, "Role không hợp lệ", ErrorCodes.Forbidden);

            var branchId = await _branchScopeResolver.ResolveRequiredBranchIdAsync(
                requestedBranchId, currentUserId, roleEnum);

            var today = DateTimeHelper.GetTodayInVietnam();
            if (effectiveFrom <= today)
                throw new AppException(400,
                    "Không thể xóa phiên bản giá đã hoặc đang có hiệu lực",
                    ErrorCodes.BadRequest);

            var deleted = await _repo.DeleteVersionAsync(branchId, courtTypeId, effectiveFrom);

            if (deleted == 0)
                throw new AppException(404,
                    "Không tìm thấy phiên bản giá để xóa",
                    ErrorCodes.NotFound);
        }

        // POST /api/prices/calculate
        // Calculates rental fee for a specific court, booking date, and time range.
        // Fetches prices as of the booking date (not today) so future bookings
        // correctly use whatever price version will be active on that date.
        public async Task<CalculatePriceResultDto> CalculateAsync(Guid? branchId, CalculatePriceDto dto)
        {
            // Convert types
            var startTime = TimeOnly.FromTimeSpan(dto.StartTime);
            var endTime = TimeOnly.FromTimeSpan(dto.EndTime);
            var bookingDate = DateOnly.FromDateTime(dto.BookingDate);

            // Validate
            if (startTime >= endTime)
                throw new AppException(400,
                    "Giờ bắt đầu phải nhỏ hơn giờ kết thúc",
                    ErrorCodes.BadRequest);

            var today = DateTimeHelper.GetTodayInVietnam();
            if (bookingDate < today)
                throw new AppException(400,
                    "Không thể tính giá cho ngày trong quá khứ",
                    ErrorCodes.BadRequest);

            var court = await _courtRepo.GetByIdAsync(dto.CourtId, branchId);
            if (court == null)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);
            if (court.Status == CourtStatus.SUSPENDED || court.Status == CourtStatus.LOCKED)
                throw new AppException(400, "Sân hiện đang bị khóa hoặc bảo trì", ErrorCodes.BadRequest);

            // Use court's branch if not provided in query
            var resolvedBranchId = branchId ?? court.BranchId;

            // Determine weekday / weekend
            var dayType = (dto.BookingDate.DayOfWeek == DayOfWeek.Saturday ||
                           dto.BookingDate.DayOfWeek == DayOfWeek.Sunday)
                ? DayType.WEEKEND
                : DayType.WEEKDAY;

            var relevantSlots = await _timeSlotRepo.GetByDayTypeAsync(dayType);
            if (!relevantSlots.Any())
                throw new AppException(400,
                    "Chưa cấu hình khung giờ cho hệ thống",
                    ErrorCodes.BadRequest);

            // Fetch prices as of bookingDate — not today
            // This ensures a booking made today for next month uses next month's scheduled prices
            var branchPrices = await _repo.GetCurrentForDateAsync(resolvedBranchId, bookingDate, court.CourtTypeId);
            var systemPrices = await _systemPriceRepo.GetCurrentForDateAsync(bookingDate, court.CourtTypeId);

            var breakdown = new List<PriceBreakdownDto>();
            decimal courtFee = 0;

            foreach (var slot in relevantSlots)
            {
                // Compute overlap between this DB slot and the requested booking range
                var overlapStart = slot.StartTime > startTime ? slot.StartTime : startTime;
                var overlapEnd = slot.EndTime < endTime ? slot.EndTime : endTime;

                if (overlapStart >= overlapEnd) continue;

                var hours = (decimal)(overlapEnd - overlapStart).TotalHours;

                // Branch override takes priority over system price
                var branchPrice = branchPrices.FirstOrDefault(p =>
                    p.TimeSlot.StartTime == slot.StartTime &&
                    p.TimeSlot.EndTime == slot.EndTime);

                var systemPrice = systemPrices.FirstOrDefault(p =>
                    p.TimeSlot.StartTime == slot.StartTime &&
                    p.TimeSlot.EndTime == slot.EndTime);

                decimal unitPrice = branchPrice?.Price > 0
                    ? branchPrice.Price
                    : systemPrice?.Price ?? 0;

                if (unitPrice == 0)
                    throw new AppException(400,
                        $"Chưa cấu hình giá cho khung giờ {slot.StartTime:HH\\:mm} - {slot.EndTime:HH\\:mm}",
                        ErrorCodes.BadRequest);

                // Pro-rate: charge proportionally if the booking partially covers the slot
                var slotHours = (decimal)(slot.EndTime - slot.StartTime).TotalHours;
                var subTotal = unitPrice * (hours / slotHours);
                courtFee += subTotal;

                breakdown.Add(new PriceBreakdownDto
                {
                    StartTime = overlapStart.ToTimeSpan(),
                    EndTime = overlapEnd.ToTimeSpan(),
                    UnitPrice = unitPrice,
                    Hours = hours,
                    SubTotal = subTotal,
                    PriceSource = branchPrice?.Price > 0 ? "BRANCH" : "SYSTEM"
                });
            }

            if (!breakdown.Any())
                throw new AppException(400,
                    "Thời gian đặt nằm ngoài giờ hoạt động của sân",
                    ErrorCodes.BadRequest);

            return new CalculatePriceResultDto
            {
                CourtFee = courtFee,
                Breakdown = breakdown
            };
        }

        // ─── Private Helpers ─────────────────────────────────────────────────────────

        // Shared implementation for building version detail.
        // Used by GetPriceOverrideVersionDetailAsync and UpsertPriceOverrideVersionAsync
        // to avoid double branch resolution and redundant DB calls.
        private async Task<PriceOverrideVersionDetailDto> BuildVersionDetailAsync(
            Guid branchId,
            Guid courtTypeId,
            DateOnly effectiveFrom)
        {
            // Exact match — only rows physically created with this effective_from date.
            // This shows "what was configured in this version", NOT the resolved price picture.
            var prices = await _repo.GetExactDatePricesAsync(branchId, courtTypeId, effectiveFrom);

            if (!prices.Any())
                throw new AppException(404,
                    "Không tìm thấy phiên bản giá override cho ngày hiệu lực này",
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
                var allVersions = await _repo.GetVersionsAsync(branchId, courtTypeId);
                var activeVersion = allVersions
                    .Where(d => d <= today)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();

                status = ResolveVersionStatus(effectiveFrom, activeVersion, today);
            }

            // Group WEEKDAY + WEEKEND rows into single slot records
            var slots = prices
                .GroupBy(bp => new { bp.TimeSlot.StartTime, bp.TimeSlot.EndTime })
                .Select(g => new PriceSlotDetail
                {
                    StartTime = g.Key.StartTime.ToString("HH:mm:ss"),
                    EndTime = g.Key.EndTime.ToString("HH:mm:ss"),
                    WeekdayPrice = g.FirstOrDefault(bp => bp.TimeSlot.DayType == DayType.WEEKDAY)?.Price ?? 0,
                    WeekendPrice = g.FirstOrDefault(bp => bp.TimeSlot.DayType == DayType.WEEKEND)?.Price ?? 0
                })
                .OrderBy(s => s.StartTime)
                .ToList();

            return new PriceOverrideVersionDetailDto
            {
                BranchId = branchId,
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
    }
}