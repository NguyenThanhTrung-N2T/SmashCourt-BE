using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Court;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Services.Helpers;
namespace SmashCourt_BE.Services
{
    public class CourtService : ICourtService
    {
        private readonly ICourtRepository _repo;
        private readonly IBranchRepository _branchRepo;
        private readonly IUserBranchRepository _userBranchRepo;
        private readonly ICourtTypeRepository _courtTypeRepo;
        private readonly IBranchPriceRepository _branchPriceRepo;
        private readonly ISystemPriceRepository _systemPriceRepo;
        private readonly ITimeSlotRepository _timeSlotRepo;
        private readonly IBranchScopeResolver _branchScopeResolver;

        public CourtService(
            ICourtRepository repo,
            IBranchRepository branchRepo,
            IUserBranchRepository userBranchRepo,
            ICourtTypeRepository courtTypeRepo,
            IBranchPriceRepository branchPriceRepo,
            ISystemPriceRepository systemPriceRepo,
            ITimeSlotRepository timeSlotRepo,
            IBranchScopeResolver branchScopeResolver)
        {
            _repo = repo;
            _branchRepo = branchRepo;
            _userBranchRepo = userBranchRepo;
            _courtTypeRepo = courtTypeRepo;
            _branchPriceRepo = branchPriceRepo;
            _systemPriceRepo = systemPriceRepo;
            _timeSlotRepo = timeSlotRepo;
            _branchScopeResolver = branchScopeResolver;
        }

        public async Task<List<CourtDto>> GetAllAsync(
            Guid? requestedBranchId, Guid? typeId,
            Guid? currentUserId, string? currentUserRole)
        {
            // Determine if requester is staff or above
            var isStaffOrAbove = currentUserRole != null && (
                currentUserRole == UserRole.OWNER.ToString() ||
                currentUserRole == UserRole.BRANCH_MANAGER.ToString() ||
                currentUserRole == UserRole.STAFF.ToString()
            );

            // Resolve branch logic for non-public users
            Guid branchId;
            if (isStaffOrAbove)
            {
                branchId = await _branchScopeResolver.ResolveBranchIdAsync(requestedBranchId, currentUserId!.Value, currentUserRole!);
            }
            else
            {
                // Public/Customer must provide branchId
                if (!requestedBranchId.HasValue)
                    throw new AppException(400, "Vui lòng chọn chi nhánh", ErrorCodes.BadRequest);
                branchId = requestedBranchId.Value;
            }

            var branch = await _branchRepo.GetByIdAsync(branchId);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            // Check branch status for customers
            if (!isStaffOrAbove && branch.Status != BranchStatus.ACTIVE)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            var courts = await _repo.GetAllByBranchAsync(branchId, isStaffOrAbove, typeId);
            return courts.Select(MapToDto).ToList();
        }

        public async Task<CourtDto> GetByIdAsync(Guid id, Guid? requestedBranchId, Guid? currentUserId, string? currentUserRole)
        {
            var isStaffOrAbove = currentUserRole != null && (
                currentUserRole == UserRole.OWNER.ToString() ||
                currentUserRole == UserRole.BRANCH_MANAGER.ToString() ||
                currentUserRole == UserRole.STAFF.ToString()
            );

            // Scope resolution if branchId is provided or if user is internal
            Guid? branchId = null;
            if (isStaffOrAbove || requestedBranchId.HasValue)
            {
                branchId = await _branchScopeResolver.ResolveBranchIdAsync(requestedBranchId,
                   currentUserId ?? Guid.Empty,
                   currentUserRole ?? "");
            }

            var court = await _repo.GetByIdAsync(id, branchId);
            if (court == null)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            var branch = await _branchRepo.GetByIdAsync(court.BranchId);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            if (!isStaffOrAbove && branch.Status != BranchStatus.ACTIVE)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            if (!isStaffOrAbove && court.Status == CourtStatus.SUSPENDED)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            return MapToDto(court);
        }

        public async Task<CourtDto> CreateAsync(Guid? requestedBranchId, CreateCourtDto dto, Guid currentUserId, string currentUserRole)
        {
            var branchId = await _branchScopeResolver.ResolveBranchIdAsync(requestedBranchId, currentUserId, currentUserRole);
            var branch = await _branchRepo.GetByIdAsync(branchId);
            if (branch == null || branch.Status != BranchStatus.ACTIVE)
                throw new AppException(400, "Chi nhánh không khả dụng", ErrorCodes.BadRequest);

            var exists = await _repo.ExistsByNameAsync(dto.Name, branchId);
            if (exists)
                throw new AppException(409, "Tên sân đã tồn tại", ErrorCodes.Conflict);

            var courtType = await _courtTypeRepo.GetByIdAsync(dto.CourtTypeId);
            if (courtType == null || courtType.Status != CourtTypeStatus.ACTIVE)
                throw new AppException(404, "Loại sân không khả dụng", ErrorCodes.NotFound);

            var isEnabled = await _branchRepo.IsCourtTypeEnabledAsync(branchId, dto.CourtTypeId);
            if (!isEnabled)
                throw new AppException(400, "Loại sân chưa được bật tại chi nhánh", ErrorCodes.BadRequest);

            var court = new Court
            {
                BranchId = branchId,
                CourtTypeId = dto.CourtTypeId,
                Name = dto.Name.Trim(),
                Description = dto.Description?.Trim(),
                AvatarUrl = dto.AvatarUrl?.Trim(),
                Status = CourtStatus.AVAILABLE,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _repo.CreateAsync(court);
            court.CourtType = courtType;
            return MapToDto(court);
        }

        public async Task<CourtDto> UpdateAsync(Guid id, Guid? requestedBranchId, UpdateCourtDto dto, Guid currentUserId, string currentUserRole)
        {
            var branchId = await _branchScopeResolver.ResolveBranchIdAsync(requestedBranchId, currentUserId, currentUserRole);
            var court = await _repo.GetByIdAsync(id, branchId);
            if (court == null)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            var exists = await _repo.ExistsByNameAsync(dto.Name, branchId, id);
            if (exists)
                throw new AppException(409, "Tên sân đã tồn tại", ErrorCodes.Conflict);

            var courtType = await _courtTypeRepo.GetByIdAsync(dto.CourtTypeId);
            if (courtType == null || courtType.Status != CourtTypeStatus.ACTIVE)
                throw new AppException(404, "Loại sân không khả dụng", ErrorCodes.NotFound);

            var isEnabled = await _branchRepo.IsCourtTypeEnabledAsync(branchId, dto.CourtTypeId);
            if (!isEnabled)
                throw new AppException(400, "Loại sân chưa được bật tại chi nhánh", ErrorCodes.BadRequest);

            court.Name = dto.Name.Trim();
            court.Description = dto.Description?.Trim();
            court.AvatarUrl = dto.AvatarUrl?.Trim();
            court.CourtTypeId = dto.CourtTypeId;
            court.UpdatedAt = DateTime.UtcNow;

            await _repo.UpdateAsync(court);
            court.CourtType = courtType;
            return MapToDto(court);
        }

        public async Task SuspendAsync(Guid id, Guid? requestedBranchId, Guid currentUserId, string currentUserRole)
        {
            var branchId = await _branchScopeResolver.ResolveBranchIdAsync(requestedBranchId, currentUserId, currentUserRole);
            var court = await _repo.GetByIdAsync(id, branchId);
            if (court == null) throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            if (court.Status == CourtStatus.SUSPENDED)
                throw new AppException(400, "Sân đã bị tạm ngưng", ErrorCodes.BadRequest);

            if (court.Status == CourtStatus.IN_USE)
                throw new AppException(400, "Sân đang có khách chơi", ErrorCodes.BadRequest);

            if (await _repo.HasActiveBookingsAsync(id))
                throw new AppException(400, "Sân có đơn đặt chưa hoàn thành", ErrorCodes.ResourceInUse);

            court.Status = CourtStatus.SUSPENDED;
            court.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(court);
        }

        public async Task ActivateAsync(Guid id, Guid? requestedBranchId, Guid currentUserId, string currentUserRole)
        {
            var branchId = await _branchScopeResolver.ResolveBranchIdAsync(requestedBranchId, currentUserId, currentUserRole);
            var court = await _repo.GetByIdAsync(id, branchId);
            if (court == null) throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            if (court.Status != CourtStatus.SUSPENDED)
                throw new AppException(400, "Sân không ở trạng thái tạm ngưng", ErrorCodes.BadRequest);

            court.Status = CourtStatus.AVAILABLE;
            court.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(court);
        }

        public async Task DeleteAsync(Guid id, Guid? requestedBranchId, Guid currentUserId, string currentUserRole)
        {
            var branchId = await _branchScopeResolver.ResolveBranchIdAsync(requestedBranchId, currentUserId, currentUserRole);
            var court = await _repo.GetByIdAsync(id, branchId);
            if (court == null) throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            if (court.Status == CourtStatus.IN_USE)
                throw new AppException(400, "Sân đang có khách chơi", ErrorCodes.BadRequest);

            if (await _repo.HasActiveBookingsAsync(id))
                throw new AppException(400, "Sân có đơn đặt chưa hoàn thành", ErrorCodes.ResourceInUse);

            court.Status = CourtStatus.INACTIVE;
            court.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(court);
        }

        public async Task<CourtManagementStatsDto> GetManagementStatsAsync(
            Guid? requestedBranchId, DateOnly? date,
            Guid currentUserId, string currentUserRole)
        {
            var branchId = await _branchScopeResolver.ResolveBranchIdAsync(requestedBranchId, currentUserId, currentUserRole);
            var targetDate = date ?? DateTimeHelper.GetTodayInVietnam();
            var data = await _repo.GetManagementDashboardDataAsync(branchId, targetDate, null, null);

            var bookingCourts = data.TodayBookingCourts;
            var now = TimeOnly.FromDateTime(DateTimeHelper.GetVietnamNow());

            var bcByCourt = bookingCourts
                .GroupBy(bc => bc.CourtId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var statuses = data.Courts.Select(court =>
                DeriveOperationalStatus(court, bcByCourt.GetValueOrDefault(court.Id, []), now)).ToList();

            return new CourtManagementStatsDto
            {
                Date = targetDate,
                Playing = statuses.Count(s => s == CourtOperationalStatus.PLAYING),
                Booked = statuses.Count(s => s == CourtOperationalStatus.BOOKED),
                Suspended = statuses.Count(s => s == CourtOperationalStatus.SUSPENDED),
                Ready = statuses.Count(s => s == CourtOperationalStatus.READY),
                Total = statuses.Count
            };
        }

        public async Task<Common.PagedResult<CourtManagementCardDto>> GetManagementCourtsAsync(
            Guid? requestedBranchId, DateOnly? date, string? search, Guid? typeId,
            int page, int pageSize,
            Guid currentUserId, string currentUserRole)
        {
            var branchId = await _branchScopeResolver.ResolveBranchIdAsync(requestedBranchId, currentUserId, currentUserRole);
            var targetDate = date ?? DateTimeHelper.GetTodayInVietnam();
            var data = await _repo.GetManagementDashboardDataAsync(branchId, targetDate, search, typeId);

            var branch = data.Branch;
            var courts = data.Courts;
            var bookingCourts = data.TodayBookingCourts;
            var slots = await GetTimelineSlotsForDateAsync(branch.OpenTime, branch.CloseTime, targetDate);
            var priceSummaries = await GetCurrentPriceSummariesAsync(
                branchId,
                courts.Select(c => c.CourtTypeId).Distinct());

            var bcByCourt = bookingCourts
                .GroupBy(bc => bc.CourtId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var now = TimeOnly.FromDateTime(DateTimeHelper.GetVietnamNow());

            var cards = courts.Select(court =>
            {
                var courtBcs = bcByCourt.GetValueOrDefault(court.Id, []);
                var opStatus = DeriveOperationalStatus(court, courtBcs, now);
                var timeline = BuildTimeline(courtBcs, slots);
                priceSummaries.TryGetValue(court.CourtTypeId, out var priceSummary);

                return new CourtManagementCardDto
                {
                    Id = court.Id,
                    Name = court.Name,
                    TypeName = court.CourtType?.Name ?? "N/A",
                    OperationalStatus = opStatus,
                    BookingsCount = courtBcs.Select(bc => bc.BookingId).Distinct().Count(),
                    BasePrice = priceSummary?.NormalPrice,
                    ScheduleTimeline = timeline
                };
            }).ToList();

            // Pagination (in-memory since all courts are already fetched for stats)
            var totalItems = cards.Count;
            var pageSize_ = Math.Max(1, pageSize);
            var page_ = Math.Max(1, page);
            var pagedItems = cards.Skip((page_ - 1) * pageSize_).Take(pageSize_).ToList();

            return new Common.PagedResult<CourtManagementCardDto>
            {
                Items = pagedItems,
                Page = page_,
                PageSize = pageSize_,
                TotalItems = totalItems
            };
        }

        public async Task<CourtManagementTimelineDto> GetManagementTimelineAsync(
            Guid? requestedBranchId, DateOnly date, Guid? typeId,
            Guid currentUserId, string currentUserRole)
        {
            var branchId = await _branchScopeResolver.ResolveBranchIdAsync(requestedBranchId, currentUserId, currentUserRole);
            var data = await _repo.GetManagementTimelineDataAsync(branchId, date, typeId);

            var branch = data.Branch;
            var courts = data.Courts;
            var bookingCourts = data.TodayBookingCourts;
            var slots = await GetTimelineSlotsForDateAsync(branch.OpenTime, branch.CloseTime, date);

            var bcByCourt = bookingCourts
                .GroupBy(bc => bc.CourtId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var now = TimeOnly.FromDateTime(DateTimeHelper.GetVietnamNow());

            var courtRows = courts.Select(court =>
            {
                var courtBcs = bcByCourt.GetValueOrDefault(court.Id, []);
                var opStatus = DeriveOperationalStatus(court, courtBcs, now);
                var detailSlots = BuildDetailTimeline(courtBcs, slots);

                return new CourtTimelineRowDto
                {
                    Id = court.Id,
                    Name = court.Name,
                    TypeName = court.CourtType?.Name ?? "N/A",
                    OperationalStatus = opStatus,
                    Slots = detailSlots
                };
            }).ToList();

            return new CourtManagementTimelineDto
            {
                Date = date,
                OperatingHours = new OperatingHoursDto
                {
                    Open = branch.OpenTime.ToString("HH:mm"),
                    Close = branch.CloseTime.ToString("HH:mm")
                },
                Courts = courtRows
            };
        }

        public async Task<CourtManagementDetailDto> GetManagementDetailAsync(
            Guid id, DateOnly? date, Guid currentUserId, string currentUserRole)
        {
            var court = await _repo.GetByIdAsync(id);
            if (court == null) throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            // Scope resolution for the court's branch
            await _branchScopeResolver.ResolveBranchIdAsync(court.BranchId, currentUserId, currentUserRole);

            var branch = await _branchRepo.GetByIdAsync(court.BranchId)
                ?? throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            var targetDate = date ?? DateTimeHelper.GetTodayInVietnam();
            var isToday = targetDate == DateTimeHelper.GetTodayInVietnam();
            var now = TimeOnly.FromDateTime(DateTimeHelper.GetVietnamNow());

            var data = await _repo.GetManagementDashboardDataAsync(court.BranchId, targetDate, null, null);
            var courtBcs = data.TodayBookingCourts.Where(bc => bc.CourtId == id).OrderBy(bc => bc.StartTime).ToList();
            var priceSummaries = await GetCurrentPriceSummariesAsync(court.BranchId, [court.CourtTypeId]);
            priceSummaries.TryGetValue(court.CourtTypeId, out var priceSummary);

            // Current player: only meaningful when viewing today's date
            var currentBc = isToday
                ? courtBcs.FirstOrDefault(bc => bc.StartTime <= now && bc.EndTime > now && bc.Booking.Status == BookingStatus.IN_PROGRESS)
                : null;

            // Upcoming bookings: all future slots today; all bookings when browsing another date
            var upcomingBcs = isToday
                ? courtBcs.Where(bc => bc.StartTime > now).ToList()
                : courtBcs.ToList();

            return new CourtManagementDetailDto
            {
                Id = court.Id,
                Name = court.Name,
                BranchName = branch.Name,
                OperationalStatus = DeriveOperationalStatus(court, courtBcs, now),
                TypeName = court.CourtType?.Name ?? "N/A",
                Prices = new CourtPriceConfigDto
                {
                    NormalPrice = priceSummary?.NormalPrice,
                    PeakPrice = priceSummary?.PeakPrice
                },
                CurrentPlayer = currentBc == null ? null : new CurrentPlayerDto
                {
                    Name = currentBc.Booking.Customer?.FullName ?? currentBc.Booking.GuestName ?? "Khách vãng lai",
                    StartTime = currentBc.StartTime.ToString("HH:mm"),
                    EndTime = currentBc.EndTime.ToString("HH:mm")
                },
                BookingsCount = courtBcs.Select(bc => bc.BookingId).Distinct().Count(),
                UpcomingBookings = upcomingBcs.Select(bc => new UpcomingBookingDto
                {
                    BookingId = bc.BookingId,
                    TimeRange = $"{bc.StartTime:HH:mm} - {bc.EndTime:HH:mm}",
                    PlayerName = bc.Booking.Customer?.FullName ?? bc.Booking.GuestName ?? "Khách vãng lai",
                    Status = bc.Booking.Status.ToString(),
                    StatusShort = MapStatusShort(bc.Booking.Status)
                }).ToList()
            };
        }

        private static CourtOperationalStatus DeriveOperationalStatus(Court court, List<Models.Entities.BookingCourt> courtBcs, TimeOnly now)
        {
            if (court.Status == CourtStatus.SUSPENDED) return CourtOperationalStatus.SUSPENDED;

            // Current overlapping bookings
            var overlappingNowStatuses = courtBcs
                .Where(bc => bc.StartTime <= now && bc.EndTime > now)
                .Select(bc => bc.Booking.Status)
                .ToList();

            if (overlappingNowStatuses.Any())
            {
                if (overlappingNowStatuses.Contains(BookingStatus.IN_PROGRESS))
                    return CourtOperationalStatus.PLAYING;

                if (overlappingNowStatuses.Any(s => IsBookingConsideredActive(s)))
                    return CourtOperationalStatus.BOOKED;
            }

            // Upcoming bookings (future)
            var hasUpcoming = courtBcs.Any(bc => bc.StartTime > now && IsBookingConsideredActive(bc.Booking.Status));
            if (hasUpcoming) return CourtOperationalStatus.BOOKED;

            return CourtOperationalStatus.READY;
        }

        private static List<CourtTimelineSlotDetailDto> BuildDetailTimeline(
            List<Models.Entities.BookingCourt> courtBcs, List<TimeSlot> slots)
        {
            // Compute raw per-slot status + first overlapping booking
            var perSlot = slots.Select(slot =>
            {
                var overlapping = courtBcs
                    .Where(bc => bc.StartTime < slot.EndTime && bc.EndTime > slot.StartTime)
                    .ToList();

                var status = CourtTimelineSlotStatus.AVAILABLE;
                Models.Entities.BookingCourt? representativeBc = null;
                if (overlapping.Count > 0)
                {
                    var inProgress = overlapping.FirstOrDefault(bc => bc.Booking.Status == BookingStatus.IN_PROGRESS);
                    var active = overlapping.FirstOrDefault(bc => IsBookingConsideredActive(bc.Booking.Status));
                    representativeBc = inProgress ?? active;
                    if (inProgress != null) status = CourtTimelineSlotStatus.PLAYING;
                    else if (active != null) status = CourtTimelineSlotStatus.BOOKED;
                }

                return new { Start = slot.StartTime, End = slot.EndTime, Status = status, Bc = representativeBc };
            }).ToList();

            var result = new List<CourtTimelineSlotDetailDto>();
            if (!perSlot.Any()) return result;

            // Merge contiguous slots with identical status AND same booking
            var curStart = perSlot[0].Start;
            var curEnd = perSlot[0].End;
            var curStatus = perSlot[0].Status;
            var curBc = perSlot[0].Bc;

            for (int i = 1; i < perSlot.Count; i++)
            {
                var s = perSlot[i];
                var sameSegment = s.Status == curStatus && s.Start == curEnd && s.Bc?.BookingId == curBc?.BookingId;
                if (sameSegment)
                {
                    curEnd = s.End;
                }
                else
                {
                    result.Add(MakeDetailSlot(curStart, curEnd, curStatus, curBc));
                    curStart = s.Start; curEnd = s.End; curStatus = s.Status; curBc = s.Bc;
                }
            }
            result.Add(MakeDetailSlot(curStart, curEnd, curStatus, curBc));
            return result;
        }

        private static CourtTimelineSlotDetailDto MakeDetailSlot(
            TimeOnly start, TimeOnly end, CourtTimelineSlotStatus status, Models.Entities.BookingCourt? bc) =>
            new()
            {
                StartTime = start.ToString("HH:mm"),
                EndTime = end.ToString("HH:mm"),
                Status = status,
                BookingId = bc?.BookingId,
                PlayerName = bc == null ? null : (bc.Booking.Customer?.FullName ?? bc.Booking.GuestName ?? "Khách vãng lai"),
                BookingStatus = bc?.Booking.Status.ToString()
            };

        private static List<CourtTimelineSlotDto> BuildTimeline(List<Models.Entities.BookingCourt> courtBcs, List<TimeSlot> slots)
        {
            // Compute raw status per slot
            var perSlot = slots.Select(slot =>
            {
                var overlappingStatuses = courtBcs
                    .Where(bc => bc.StartTime < slot.EndTime && bc.EndTime > slot.StartTime)
                    .Select(bc => bc.Booking.Status)
                    .ToList();

                var status = CourtTimelineSlotStatus.AVAILABLE;
                if (overlappingStatuses.Count > 0)
                {
                    if (overlappingStatuses.Contains(BookingStatus.IN_PROGRESS)) status = CourtTimelineSlotStatus.PLAYING;
                    else if (overlappingStatuses.Any(s => IsBookingConsideredActive(s))) status = CourtTimelineSlotStatus.BOOKED;
                }

                return new { Start = slot.StartTime, End = slot.EndTime, Status = status };
            }).ToList();

            var result = new List<CourtTimelineSlotDto>();
            if (!perSlot.Any()) return result;

            // Merge contiguous slots with identical status
            var curStart = perSlot[0].Start;
            var curEnd = perSlot[0].End;
            var curStatus = perSlot[0].Status;

            for (int i = 1; i < perSlot.Count; i++)
            {
                var s = perSlot[i];
                if (s.Status == curStatus && s.Start == curEnd)
                {
                    // extend current merged segment
                    curEnd = s.End;
                }
                else
                {
                    result.Add(new CourtTimelineSlotDto
                    {
                        StartTime = curStart.ToString("HH:mm"),
                        EndTime = curEnd.ToString("HH:mm"),
                        Status = curStatus
                    });

                    curStart = s.Start;
                    curEnd = s.End;
                    curStatus = s.Status;
                }
            }

            // push final segment
            result.Add(new CourtTimelineSlotDto
            {
                StartTime = curStart.ToString("HH:mm"),
                EndTime = curEnd.ToString("HH:mm"),
                Status = curStatus
            });

            return result;
        }

        private static bool IsBookingConsideredActive(BookingStatus status) =>
            status == BookingStatus.CONFIRMED ||
            status == BookingStatus.PAID_ONLINE ||
            status == BookingStatus.PENDING_PAYMENT ||
            status == BookingStatus.PENDING;

        private async Task<List<TimeSlot>> GetTimelineSlotsForDateAsync(TimeOnly openTime, TimeOnly closeTime, DateOnly date)
        {
            var slots = await _timeSlotRepo.GetByDayTypeAsync(GetDayType(date));

            return slots
                .Where(slot => slot.StartTime >= openTime && slot.EndTime <= closeTime)
                .OrderBy(slot => slot.StartTime)
                .ToList();
        }

        private async Task<Dictionary<Guid, PriceSummary>> GetCurrentPriceSummariesAsync(
            Guid branchId,
            IEnumerable<Guid> courtTypeIds)
        {
            var typeIds = courtTypeIds.Distinct().ToHashSet();
            if (typeIds.Count == 0)
                return [];

            var today = DateTimeHelper.GetTodayInVietnam();
            var dayType = GetDayType(today);
            var branchPrices = await _branchPriceRepo.GetCurrentForDateAsync(branchId, today);
            var systemPrices = await _systemPriceRepo.GetCurrentForDateAsync(today);

            var branchPriceBySlot = branchPrices
                .Where(p => typeIds.Contains(p.CourtTypeId) && p.TimeSlot.DayType == dayType)
                .GroupBy(p => new PriceSlotKey(p.CourtTypeId, p.TimeSlot.StartTime, p.TimeSlot.EndTime))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.EffectiveFrom).First());

            var pricesByCourtType = new Dictionary<Guid, List<decimal>>();

            foreach (var systemPrice in systemPrices.Where(p => typeIds.Contains(p.CourtTypeId) && p.TimeSlot.DayType == dayType))
            {
                var key = new PriceSlotKey(systemPrice.CourtTypeId, systemPrice.TimeSlot.StartTime, systemPrice.TimeSlot.EndTime);
                var price = branchPriceBySlot.TryGetValue(key, out var branchPrice) && branchPrice.Price > 0
                    ? branchPrice.Price
                    : systemPrice.Price;

                AddPositivePrice(pricesByCourtType, systemPrice.CourtTypeId, price);
            }

            foreach (var branchPrice in branchPriceBySlot.Values)
            {
                var systemHasSlot = systemPrices.Any(sp =>
                    sp.CourtTypeId == branchPrice.CourtTypeId &&
                    sp.TimeSlot.DayType == dayType &&
                    sp.TimeSlot.StartTime == branchPrice.TimeSlot.StartTime &&
                    sp.TimeSlot.EndTime == branchPrice.TimeSlot.EndTime);

                if (!systemHasSlot)
                    AddPositivePrice(pricesByCourtType, branchPrice.CourtTypeId, branchPrice.Price);
            }

            return pricesByCourtType.ToDictionary(
                pair => pair.Key,
                pair => new PriceSummary(pair.Value.Min(), pair.Value.Max()));
        }

        private static void AddPositivePrice(Dictionary<Guid, List<decimal>> pricesByCourtType, Guid courtTypeId, decimal price)
        {
            if (price <= 0) return;

            if (!pricesByCourtType.TryGetValue(courtTypeId, out var prices))
            {
                prices = [];
                pricesByCourtType[courtTypeId] = prices;
            }

            prices.Add(price);
        }

        private static DayType GetDayType(DateOnly date) =>
            date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday
                ? DayType.WEEKEND
                : DayType.WEEKDAY;

        private static string MapStatusShort(BookingStatus status) => status switch
        {
            BookingStatus.CONFIRMED => "Đã XN",
            BookingStatus.PAID_ONLINE => "Đã TT",
            BookingStatus.PENDING_PAYMENT => "Chờ TT",
            BookingStatus.IN_PROGRESS => "Đang chơi",
            BookingStatus.PENDING => "Chờ XN",
            BookingStatus.COMPLETED => "Hoàn thành",
            BookingStatus.CANCELLED => "Đã hủy",
            BookingStatus.CANCELLED_PENDING_REFUND => "Chờ hoàn tiền",
            BookingStatus.CANCELLED_REFUNDED => "Đã hoàn tiền",
            BookingStatus.NO_SHOW => "Không đến",
            _ => status.ToString()
        };

        private static CourtDto MapToDto(Court c) => new()
        {
            Id = c.Id,
            BranchId = c.BranchId,
            CourtTypeId = c.CourtTypeId,
            CourtTypeName = c.CourtType?.Name ?? "N/A",
            Name = c.Name,
            Description = c.Description,
            AvatarUrl = c.AvatarUrl,
            Status = c.Status,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        };

        private sealed record PriceSummary(decimal NormalPrice, decimal PeakPrice);

        private sealed record PriceSlotKey(Guid CourtTypeId, TimeOnly StartTime, TimeOnly EndTime);
    }
}
