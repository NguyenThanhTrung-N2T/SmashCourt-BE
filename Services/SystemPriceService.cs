using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

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
        // Lấy toàn bộ danh sách các ngày có phiên bản cấu hình giá hệ thống cho một loại sân kèm theo trạng thái.
        // Mỗi phiên bản được đánh trạng thái ACTIVE (Đang áp dụng), SCHEDULED (Lên lịch), hoặc EXPIRED (Hết hạn).
        public async Task<SystemPriceVersionsResponse> GetVersionsAsync(Guid courtTypeId)
        {
            // Kiểm tra loại sân có tồn tại không
            var courtType = await _courtTypeRepo.GetByIdAsync(courtTypeId);
            if (courtType == null)
                throw new AppException(404, "Không tìm thấy loại sân", ErrorCodes.NotFound);

            // Lấy danh sách ngày hiệu lực phân biệt của loại sân này, sắp xếp giảm dần
            var effectiveDates = await _repo.GetVersionsAsync(courtTypeId);

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

            return new SystemPriceVersionsResponse
            {
                CourtTypeId = courtTypeId,
                Versions = versions
            };
        }

        // GET /api/system-prices/versions/{effectiveFrom}
        // Lấy thông tin cấu hình chi tiết của một phiên bản giá hệ thống vào một ngày hiệu lực chính xác.
        // So khớp chính xác ngày hiệu lực (effective_from = date), không phải dạng lấy snapshot đang áp dụng.
        // Trả lời câu hỏi: "Admin đã cấu hình giá hệ thống gì cho ngày này?" - không phải "Giá nào đang áp dụng cho ngày này?"
        public async Task<SystemPriceVersionDetailDto> GetVersionDetailAsync(
            Guid courtTypeId,
            DateOnly effectiveFrom)
        {
            // Kiểm tra loại sân có tồn tại không
            var courtType = await _courtTypeRepo.GetByIdAsync(courtTypeId);
            if (courtType == null)
                throw new AppException(404, "Không tìm thấy loại sân", ErrorCodes.NotFound);

            return await BuildVersionDetailAsync(courtTypeId, effectiveFrom);
        }

        // PATCH /api/system-prices/versions/{effectiveFrom}
        // Tạo mới hoặc cập nhật một phần phiên bản giá hệ thống.
        //
        // Cơ chế PATCH: chỉ những khung giờ được gửi lên mới bị tác động - các khung giờ khác trong phiên bản giữ nguyên.
        // Điều này cho phép admin chỉ sửa một khoảng thời gian mà không cần gửi lại toàn bộ cấu hình ngày đó.
        //
        // Hỗ trợ khoảng thời gian lớn: Một khoảng thời gian lớn (ví dụ: 06:00 - 12:00) sẽ tự động
        // được phân tách thành các khung giờ nhỏ hơn cấu hình trong DB.
        public async Task<(SystemPriceVersionDetailDto Response, bool IsCreated)> UpsertVersionAsync(
            Guid courtTypeId,
            DateOnly effectiveFrom,
            UpsertPriceRequest request)
        {
            // 1. Kiểm tra loại sân có tồn tại không
            var courtType = await _courtTypeRepo.GetByIdAsync(courtTypeId);
            if (courtType == null)
                throw new AppException(404, "Không tìm thấy loại sân", ErrorCodes.NotFound);

            // 2. Xác thực ngày hiệu lực
            ValidateEffectiveDate(effectiveFrom);

            // 3. Tải tất cả khung giờ của hệ thống
            var allTimeSlots = await _timeSlotRepo.GetAllAsync();

            // 4. Mở rộng các slot nhập vào và kiểm tra khoảng cách/trùng lấp
            var expandedSlots = ExpandAndValidateInputSlots(request.Slots, allTimeSlots);

            // 5. Kiểm tra cấu hình giá hệ thống hiện có của ngày này
            var existingPrices = await _repo.GetExactDatePricesAsync(courtTypeId, effectiveFrom);
            var isCreated = !existingPrices.Any();

            // 6. Lập danh sách các bản ghi cần thêm mới và cập nhật
            var (inserts, updates) = BuildSystemPrices(courtTypeId, effectiveFrom, expandedSlots, existingPrices);

            // 7. Lưu các thay đổi vào cơ sở dữ liệu
            await _repo.UpsertBatchAsync(inserts, updates);

            // 8. Xây dựng và trả về thông tin chi tiết của phiên bản giá
            var response = await BuildVersionDetailAsync(courtTypeId, effectiveFrom);
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

        private static (List<SystemPrice> Inserts, List<SystemPrice> Updates) BuildSystemPrices(
            Guid courtTypeId,
            DateOnly effectiveFrom,
            List<(PriceSlotInput Input, List<TimeSlot> Slots)> expandedSlots,
            List<SystemPrice> existingPrices)
        {
            var inserts = new List<SystemPrice>();
            var updates = new List<SystemPrice>();

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
                        updates.Add(existing);
                    }
                    else
                    {
                        inserts.Add(new SystemPrice
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

            return (inserts, updates);
        }

        // DELETE /api/system-prices/versions/{effectiveFrom}
        // Xóa toàn bộ một phiên bản giá hệ thống - toàn bộ các dòng của (courtTypeId, effectiveFrom).
        // Chỉ các phiên bản ở trạng thái SCHEDULED (tương lai) mới được phép xóa.
        // Các phiên bản ACTIVE và EXPIRED sẽ bị khóa vì chúng là hồ sơ lịch sử áp dụng giá.
        //
        // LƯU Ý: yêu cầu ISystemPriceRepository.DeleteVersionAsync(courtTypeId, effectiveFrom)
        // SQL: DELETE FROM system_prices
        //      WHERE court_type_id = @courtTypeId AND effective_from = @effectiveFrom
        public async Task DeleteVersionAsync(Guid courtTypeId, DateOnly effectiveFrom)
        {
            // Kiểm tra loại sân có tồn tại không
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

        // Phương thức cũ phục vụ tính toán giá nội bộ
        // Lấy giá chung resolved cho 1 ngày cụ thể
        public async Task<List<CurrentPriceDto>> GetEffectivePricesAsync(DateOnly date, Guid? courtTypeId = null)
        {
            var prices = await _repo.GetCurrentForDateAsync(date, courtTypeId);
            var grouped = GroupPrices(prices);
            return PriceSlotMerger.MergeConsecutivePriceSlots(grouped);
        }

        // ─── Private Helpers ─────────────────────────────────────────────────────────

        // Hàm dùng chung để xây dựng thông tin chi tiết của phiên bản giá.
        // Được sử dụng bởi GetVersionDetailAsync và UpsertVersionAsync
        // để tránh các truy vấn DB trùng lặp.
        private async Task<SystemPriceVersionDetailDto> BuildVersionDetailAsync(
            Guid courtTypeId,
            DateOnly effectiveFrom)
        {
            // Khớp chính xác ngày - chỉ lấy những dòng được tạo với ngày effective_from này.
            // Điều này hiển thị "phiên bản này đã cấu hình những gì", KHÔNG phải bức tranh giá thực tế đang áp dụng.
            var prices = await _repo.GetExactDatePricesAsync(courtTypeId, effectiveFrom);

            if (!prices.Any())
                throw new AppException(404,
                    "Không tìm thấy phiên bản giá cho ngày hiệu lực này",
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
                var allVersions = await _repo.GetVersionsAsync(courtTypeId);
                var activeVersion = allVersions
                    .Where(d => d <= today)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();

                status = ResolveVersionStatus(effectiveFrom, activeVersion, today);
            }

            // Gộp các hàng WEEKDAY + WEEKEND thành một đối tượng slot duy nhất
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

        // Gộp các hàng WEEKDAY + WEEKEND thành bản ghi duy nhất (hàm helper cũ cho GetEffectivePricesAsync)
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
