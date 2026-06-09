using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Services.AccessControl;

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
        // Lấy thông tin giá áp dụng thực tế của chi nhánh tại một ngày cụ thể.
        // Với mỗi khung giờ: Giá chi nhánh (override) ưu tiên trước, nếu không có sẽ dùng giá hệ thống làm dự phòng.
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

            // Lấy thông tin giá từ cả 2 nguồn (chi nhánh và hệ thống) cho ngày mục tiêu
            var branchPrices = await _repo.GetCurrentForDateAsync(branchId, date, courtTypeId);
            var systemPrices = await _systemPriceRepo.GetCurrentForDateAsync(date, courtTypeId);

            // Đánh chỉ mục giá override của chi nhánh theo (CourtTypeId, StartTime, EndTime) để tìm kiếm O(1) khi gộp
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

            // Gộp: Giá hệ thống định nghĩa các khung giờ có sẵn; giá chi nhánh ghi đè lên nếu tồn tại
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

            // Gộp các khung giờ liên tiếp có giá ngày thường và cuối tuần giống nhau (tối ưu hiển thị)
            var merged = PriceSlotMerger.MergeConsecutiveEffectivePriceSlots(effectivePrices);

            // Nhóm theo loại sân
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
        // Lấy danh sách các ngày có phiên bản giá override của chi nhánh và loại sân.
        // Mỗi phiên bản được đánh trạng thái ACTIVE (Đang áp dụng), SCHEDULED (Lên lịch), hoặc EXPIRED (Hết hạn).
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

            // Lấy danh sách ngày hiệu lực phân biệt của chi nhánh và loại sân, sắp xếp giảm dần
            var effectiveDates = await _repo.GetVersionsAsync(branchId, courtTypeId);

            var today = DateTimeHelper.GetTodayInVietnam();

            // Phiên bản hoạt động = ngày hiệu lực mới nhất nhỏ hơn hoặc bằng hôm nay
            // Nếu mặc định (DateOnly) nghĩa là chưa có phiên bản nào có hiệu lực -> toàn bộ là SCHEDULED
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
        // Lấy thông tin cấu hình chính xác của phiên bản giá vào một ngày hiệu lực cụ thể.
        // So khớp chính xác ngày hiệu lực (effective_from = date), không phải dạng lấy snapshot đang áp dụng.
        // Trả lời câu hỏi: "Quản lý đã thiết lập giá gì cho ngày này?" - không phải "Giá nào đang áp dụng cho ngày này?"
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
        // Tạo mới hoặc cập nhật một phần phiên bản giá override của chi nhánh.
        //
        // Cơ chế PATCH: chỉ những khung giờ được gửi lên mới bị tác động - các khung giờ khác trong phiên bản giữ nguyên.
        // Điều này cho phép quản lý chỉ sửa một khoảng thời gian mà không cần gửi lại toàn bộ cấu hình ngày đó.
        //
        // Hỗ trợ khoảng thời gian lớn: Một khoảng thời gian lớn (ví dụ: 06:00 - 12:00) sẽ tự động
        // được phân tách thành các khung giờ nhỏ hơn cấu hình trong DB.
        public async Task<(PriceOverrideVersionDetailDto Response, bool IsCreated)> UpsertPriceOverrideVersionAsync(
            Guid? requestedBranchId,
            Guid courtTypeId,
            DateOnly effectiveFrom,
            UpsertPriceRequest request,
            Guid currentUserId,
            string currentUserRole)
        {
            // 1. Kiểm tra Role và phân giải Chi nhánh
            if (!Enum.TryParse<UserRole>(currentUserRole, true, out var roleEnum))
                throw new AppException(403, "Role không hợp lệ", ErrorCodes.Forbidden);

            var branchId = await _branchScopeResolver.ResolveRequiredBranchIdAsync(
                requestedBranchId, currentUserId, roleEnum);

            // 2. Xác thực ngày hiệu lực
            ValidateEffectiveDate(effectiveFrom);

            // 3. Xác thực loại sân phải được kích hoạt tại chi nhánh này
            var isCourtTypeEnabled = await _branchRepo.IsCourtTypeEnabledAsync(branchId, courtTypeId);
            if (!isCourtTypeEnabled)
                throw new AppException(400,
                    "Loại sân không hợp lệ hoặc không thuộc chi nhánh này",
                    ErrorCodes.BadRequest);

            // 4. Tải tất cả khung giờ của hệ thống
            var allTimeSlots = await _timeSlotRepo.GetAllAsync();

            // 5. Mở rộng các slot nhập vào và kiểm tra khoảng cách/trùng lấp
            var expandedSlots = ExpandAndValidateInputSlots(request.Slots, allTimeSlots);

            // 6. Kiểm tra cấu hình giá override hiện có của ngày này
            var existingPrices = await _repo.GetExactDatePricesAsync(branchId, courtTypeId, effectiveFrom);
            var isCreated = !existingPrices.Any();

            // 7. Lập danh sách các bản ghi cần thêm mới và cập nhật
            var (inserts, updates) = BuildPriceOverrides(branchId, courtTypeId, effectiveFrom, expandedSlots, existingPrices);

            // 8. Lưu các thay đổi vào cơ sở dữ liệu
            await _repo.UpsertBatchAsync(inserts, updates);

            // 9. Xây dựng và trả về thông tin chi tiết của phiên bản giá
            var response = await BuildVersionDetailAsync(branchId, courtTypeId, effectiveFrom);
            return (response, isCreated);
        }

        private static void ValidateEffectiveDate(DateOnly effectiveFrom)
        {
            var today = DateTimeHelper.GetTodayInVietnam();
            if (effectiveFrom < today)
                throw new AppException(400,
                    "Không thể tạo hoặc cập nhật phiên bản giá trong quá khứ",
                    ErrorCodes.BadRequest);
        }

        private static List<(PriceSlotInput Input, List<TimeSlot> Slots)> ExpandAndValidateInputSlots(
            List<PriceSlotInput> inputs,
            List<TimeSlot> allTimeSlots)
        {
            var allMatchedSlotIds = new HashSet<Guid>();
            var expandedSlots = new List<(PriceSlotInput Input, List<TimeSlot> Slots)>();

            foreach (var slotInput in inputs)
            {
                if (!DateTimeHelper.TryParseTimeOnly(slotInput.StartTime, out var startTime))
                    throw new AppException(400,
                        $"Định dạng giờ bắt đầu không hợp lệ: {slotInput.StartTime}. Sử dụng HH:mm hoặc HH:mm:ss",
                        ErrorCodes.BadRequest);

                if (!DateTimeHelper.TryParseTimeOnly(slotInput.EndTime, out var endTime))
                    throw new AppException(400,
                        $"Định dạng giờ kết thúc không hợp lệ: {slotInput.EndTime}. Sử dụng HH:mm hoặc HH:mm:ss",
                        ErrorCodes.BadRequest);

                if (startTime >= endTime)
                    throw new AppException(400,
                        $"Giờ bắt đầu phải nhỏ hơn giờ kết thúc: {slotInput.StartTime} - {slotInput.EndTime}",
                        ErrorCodes.BadRequest);

                if (slotInput.WeekdayPrice < 0 || slotInput.WeekendPrice < 0)
                    throw new AppException(400,
                        $"Giá không được âm tại khung giờ {slotInput.StartTime} - {slotInput.EndTime}",
                        ErrorCodes.BadRequest);

                var matched = allTimeSlots
                    .Where(ts => ts.StartTime >= startTime && ts.EndTime <= endTime)
                    .ToList();

                if (!matched.Any())
                    throw new AppException(400,
                        $"Không tìm thấy khung giờ nào trong khoảng {startTime:HH\\:mm} - {endTime:HH\\:mm}",
                        ErrorCodes.BadRequest);

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

                foreach (var ts in matched)
                {
                    if (!allMatchedSlotIds.Add(ts.Id))
                        throw new AppException(400,
                            "Các khoảng thời gian trong yêu cầu bị chồng lấp nhau",
                            ErrorCodes.BadRequest);
                }

                expandedSlots.Add((slotInput, matched));
            }

            return expandedSlots;
        }

        private static (List<BranchPriceOverride> Inserts, List<BranchPriceOverride> Updates) BuildPriceOverrides(
            Guid branchId,
            Guid courtTypeId,
            DateOnly effectiveFrom,
            List<(PriceSlotInput Input, List<TimeSlot> Slots)> expandedSlots,
            List<BranchPriceOverride> existingPrices)
        {
            var inserts = new List<BranchPriceOverride>();
            var updates = new List<BranchPriceOverride>();

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
                        updates.Add(existing);
                    }
                    else
                    {
                        inserts.Add(new BranchPriceOverride
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

            return (inserts, updates);
        }

        // DELETE /api/prices/overrides/{effectiveFrom}
        // Xóa toàn bộ phiên bản giá override của chi nhánh - toàn bộ các dòng của (branchId, courtTypeId, effectiveFrom).
        // Chỉ các phiên bản ở trạng thái SCHEDULED (tương lai) mới được phép xóa.
        // Các phiên bản ACTIVE và EXPIRED sẽ bị khóa vì chúng là hồ sơ lịch sử áp dụng giá.
        //
        // LƯU Ý: yêu cầu IBranchPriceRepository.DeleteVersionAsync(branchId, courtTypeId, effectiveFrom)
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
        // Tính toán phí thuê cho một sân cụ thể, ngày đặt sân và khoảng thời gian đặt.
        // Lấy giá tại ngày đặt sân (không phải hôm nay) để các lượt đặt trước trong tương lai
        // sử dụng chính xác phiên bản giá sẽ có hiệu lực vào ngày hôm đó.
        public async Task<CalculatePriceResultDto> CalculateAsync(Guid? branchId, CalculatePriceDto dto)
        {
            // Chuyển đổi kiểu dữ liệu
            var startTime = TimeOnly.FromTimeSpan(dto.StartTime);
            var endTime = TimeOnly.FromTimeSpan(dto.EndTime);
            var bookingDate = DateOnly.FromDateTime(dto.BookingDate);

            // Xác thực dữ liệu
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

            // Sử dụng chi nhánh của sân nếu không cung cấp trong query
            var resolvedBranchId = branchId ?? court.BranchId;

            // Xác định loại ngày: ngày thường (WEEKDAY) hay cuối tuần (WEEKEND)
            var dayType = (dto.BookingDate.DayOfWeek == DayOfWeek.Saturday ||
                           dto.BookingDate.DayOfWeek == DayOfWeek.Sunday)
                ? DayType.WEEKEND
                : DayType.WEEKDAY;

            var relevantSlots = await _timeSlotRepo.GetByDayTypeAsync(dayType);
            if (!relevantSlots.Any())
                throw new AppException(400,
                    "Chưa cấu hình khung giờ cho hệ thống",
                    ErrorCodes.BadRequest);

            // Lấy giá tại ngày đặt sân - không phải hôm nay
            // Điều này đảm bảo việc đặt lịch hôm nay cho tháng sau sẽ dùng đúng cấu hình giá tương lai của tháng sau
            var branchPrices = await _repo.GetCurrentForDateAsync(resolvedBranchId, bookingDate, court.CourtTypeId);
            var systemPrices = await _systemPriceRepo.GetCurrentForDateAsync(bookingDate, court.CourtTypeId);

            var breakdown = new List<PriceBreakdownDto>();
            decimal courtFee = 0;

            foreach (var slot in relevantSlots)
            {
                // Tính toán khoảng thời gian chồng lấp (overlap) giữa khung giờ DB và khoảng đặt sân yêu cầu
                var overlapStart = slot.StartTime > startTime ? slot.StartTime : startTime;
                var overlapEnd = slot.EndTime < endTime ? slot.EndTime : endTime;

                if (overlapStart >= overlapEnd) continue;

                var hours = (decimal)(overlapEnd - overlapStart).TotalHours;

                // Giá override của chi nhánh được ưu tiên cao hơn giá hệ thống
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

                // Tính giá theo tỷ lệ (pro-rate) nếu lượt đặt chỉ chiếm một phần khung giờ
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

        // Hàm dùng chung để xây dựng thông tin chi tiết của phiên bản giá.
        // Được sử dụng bởi GetPriceOverrideVersionDetailAsync và UpsertPriceOverrideVersionAsync
        // để tránh việc phân giải chi nhánh hai lần và tránh các truy vấn DB trùng lặp.
        private async Task<PriceOverrideVersionDetailDto> BuildVersionDetailAsync(
            Guid branchId,
            Guid courtTypeId,
            DateOnly effectiveFrom)
        {
            // Khớp chính xác ngày - chỉ lấy những dòng được tạo với ngày effective_from này.
            // Điều này hiển thị "phiên bản này đã cấu hình những gì", KHÔNG phải bức tranh giá thực tế đang áp dụng.
            var prices = await _repo.GetExactDatePricesAsync(branchId, courtTypeId, effectiveFrom);

            if (!prices.Any())
                throw new AppException(404,
                    "Không tìm thấy phiên bản giá override cho ngày hiệu lực này",
                    ErrorCodes.NotFound);

            // Trạng thái: SCHEDULED là cố định - không cần gọi DB cho các ngày trong tương lai
            var today = DateTimeHelper.GetTodayInVietnam();
            string status;

            if (effectiveFrom > today)
            {
                status = "SCHEDULED";
            }
            else
            {
                // Với các ngày trong quá khứ hoặc hôm nay, cần phiên bản active để phân biệt giữa ACTIVE và EXPIRED
                var allVersions = await _repo.GetVersionsAsync(branchId, courtTypeId);
                var activeVersion = allVersions
                    .Where(d => d <= today)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();

                status = ResolveVersionStatus(effectiveFrom, activeVersion, today);
            }

            // Gộp các hàng WEEKDAY + WEEKEND thành một đối tượng slot duy nhất
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

        // Phân giải trạng thái phiên bản dựa trên ngày hiệu lực, phiên bản active hiện tại và hôm nay.
        //
        // Quy tắc trạng thái:
        //   SCHEDULED  → ngày hiệu lực ở tương lai (chưa áp dụng)
        //   ACTIVE     → phiên bản mới nhất có ngày hiệu lực <= hôm nay (đang áp dụng hiện tại)
        //   EXPIRED    → ngày hiệu lực <= hôm nay nhưng đã bị thay thế bởi phiên bản mới hơn
        //
        // Khi activeVersion là giá trị mặc định (chưa có phiên bản nào có hiệu lực),
        // tất cả các ngày đều là tương lai (SCHEDULED) hoặc không hợp lý để chạy vào luồng này.
        private static string ResolveVersionStatus(DateOnly date, DateOnly activeVersion, DateOnly today)
        {
            if (date > today) return "SCHEDULED";
            if (activeVersion == default) return "SCHEDULED"; // no version is active yet (safety guard)
            if (date == activeVersion) return "ACTIVE";
            return "EXPIRED";
        }
    }
}