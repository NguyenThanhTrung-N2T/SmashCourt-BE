using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Nền cho BE-IT của đặt sân online (gói 6A-2c Task 4): seed một sân đặt được thật (bộ tính giá chạy thật nên cần khung giờ, giá hệ thống),
/// gọi <c>POST /api/bookings/online</c> bằng JWT thật, đọc lại DB và bật/tắt cờ TADSS trong phạm vi một test.
/// <para>
/// Dữ liệu commit thật: mỗi test một chi nhánh, một loại sân (giá riêng nên biết trước <c>finalTotal</c>) và một sân mới; ngày đặt luôn ở
/// tương lai vì A9 chặn giờ đã bắt đầu theo đồng hồ thật của host.
/// </para>
/// </summary>
public abstract class OnlineBookingTestBase(PostgreSqlIntegrationFixture database, HttpTestHostFixture host) : HttpTestBase(database, host)
{
    protected const string OnlinePath = "/api/bookings/online";
    protected const string EnableKey = "TadssConfig:EnableTadssDecision";
    protected const string TimeoutKey = "TadssConfig:TimeoutMs";

    /// <summary>Sân đặt được: chi nhánh, loại sân và giá theo giờ riêng của test.</summary>
    protected sealed record BookableCourt(Guid BranchId, Guid CourtTypeId, Guid CourtId, string CourtName, decimal PricePerHour);

    /// <summary>Kết quả gọi đặt sân: mã HTTP, envelope và (nếu có) booking id lấy từ <c>data.bookingId</c>.</summary>
    protected sealed record BookingCall(HttpStatusCode Status, JsonElement Envelope)
    {
        public string? Code => Envelope.GetProperty("code").GetString();
        public string? Message => Envelope.GetProperty("message").GetString();
        public JsonElement Data => Envelope.GetProperty("data");
        public Guid BookingId => Data.GetProperty("bookingId").GetGuid();
        public string? DataString(string name) => Data.GetProperty(name).GetString();
        public decimal? DataDecimal(string name) =>
            Data.GetProperty(name).ValueKind == JsonValueKind.Null ? null : Data.GetProperty(name).GetDecimal();
    }

    // ───────────────────────── Cấu hình trong phạm vi test ─────────────────────────

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    /// <summary>
    /// TimeoutMs cho ca TADSS trả lời thành công: đủ rộng để lần gọi đầu của tiến trình (JIT, kết nối nguội) không bị cắt oan thành nhánh d.
    /// Ca kiểm timeout/lỗi dùng <see cref="UseTadssWithShortTimeoutAsync"/> (400 ms của host).
    /// </summary>
    protected const int AnsweringTadssTimeoutMs = 2_000;

    /// <summary>Bật cờ TADSS (và đổi TimeoutMs) trong phạm vi một test, rồi trả về mặc định của host.</summary>
    protected async Task<IDisposable> UseTadssAsync(int? timeoutMs = AnsweringTadssTimeoutMs)
    {
        var factory = await GetFactoryAsync();
        factory.SetSetting(EnableKey, "true");
        if (timeoutMs != null)
            factory.SetSetting(TimeoutKey, timeoutMs.Value.ToString());
        return new Restore(() =>
        {
            factory.SetSetting(EnableKey, null);
            factory.SetSetting(TimeoutKey, null);
        });
    }

    /// <summary>Bật cờ TADSS, giữ TimeoutMs ngắn của host: cho ca timeout, lỗi mạng, lỗi HTTP.</summary>
    protected Task<IDisposable> UseTadssWithShortTimeoutAsync() => UseTadssAsync(SmashCourtWebApplicationFactory.TadssTimeoutMs);

    // ───────────────────────── Seed ─────────────────────────

    private static readonly SemaphoreSlim TimeSlotGate = new(1, 1);

    /// <summary>
    /// Khung giờ 1 tiếng phủ cả ngày cho ngày thường và cuối tuần (bảng dùng chung toàn hệ thống, unique theo giờ + loại ngày);
    /// chỉ thêm khung còn thiếu nên gọi nhiều lần không sao.
    /// </summary>
    protected async Task EnsureTimeSlotsAsync()
    {
        await TimeSlotGate.WaitAsync();
        try
        {
            await WithDbAsync(async db =>
            {
                var existing = await db.TimeSlots.Select(t => new { t.StartTime, t.EndTime, t.DayType }).ToListAsync();
                foreach (var dayType in new[] { DayType.WEEKDAY, DayType.WEEKEND })
                {
                    for (var hour = 0; hour < 24; hour++)
                    {
                        var start = new TimeOnly(hour, 0);
                        var end = hour == 23 ? new TimeOnly(23, 59, 59) : new TimeOnly(hour + 1, 0);
                        if (existing.Any(t => t.StartTime == start && t.EndTime == end && t.DayType == dayType))
                            continue;
                        db.TimeSlots.Add(new TimeSlot
                        {
                            Id = Guid.NewGuid(), StartTime = start, EndTime = end, DayType = dayType, CreatedAt = DateTime.UtcNow
                        });
                    }
                }
                return await db.SaveChangesAsync();
            });
        }
        finally
        {
            TimeSlotGate.Release();
        }
    }

    /// <summary>Chi nhánh, loại sân có giá hệ thống <paramref name="pricePerHour"/> cho mọi khung giờ, và một sân AVAILABLE.</summary>
    protected async Task<BookableCourt> SeedCourtAsync(decimal pricePerHour = 200_000m)
    {
        await EnsureTimeSlotsAsync();
        return await WithDbAsync(async db =>
        {
            var now = DateTime.UtcNow;
            var suffix = Guid.NewGuid().ToString("N")[..10];
            var branch = new Branch
            {
                Id = Guid.NewGuid(), Name = $"CN test {suffix}", Address = "1 Đường Test", Phone = "0280000000",
                OpenTime = new TimeOnly(0, 0), CloseTime = new TimeOnly(23, 59, 59), Status = BranchStatus.ACTIVE,
                CreatedAt = now, UpdatedAt = now
            };
            var courtType = new CourtType
            {
                Id = Guid.NewGuid(), Name = $"Loại sân {suffix}", Status = CourtTypeStatus.ACTIVE, CreatedAt = now, UpdatedAt = now
            };
            var court = new Court
            {
                Id = Guid.NewGuid(), BranchId = branch.Id, CourtTypeId = courtType.Id, Name = $"Sân {suffix}",
                Status = CourtStatus.AVAILABLE, CreatedAt = now, UpdatedAt = now
            };
            db.Branches.Add(branch);
            db.CourtTypes.Add(courtType);
            db.Courts.Add(court);
            foreach (var slot in await db.TimeSlots.ToListAsync())
            {
                db.SystemPrices.Add(new SystemPrice
                {
                    Id = Guid.NewGuid(), CourtTypeId = courtType.Id, TimeSlotId = slot.Id, Price = pricePerHour,
                    EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now
                });
            }
            await db.SaveChangesAsync();
            return new BookableCourt(branch.Id, courtType.Id, court.Id, court.Name, pricePerHour);
        });
    }

    /// <summary>Thêm một sân nữa cùng chi nhánh và loại sân (đơn nhiều sân).</summary>
    protected Task<Guid> SeedSecondCourtAsync(BookableCourt court) =>
        WithDbAsync(async db =>
        {
            var now = DateTime.UtcNow;
            var second = new Court
            {
                Id = Guid.NewGuid(), BranchId = court.BranchId, CourtTypeId = court.CourtTypeId, Name = $"{court.CourtName} B",
                Status = CourtStatus.AVAILABLE, CreatedAt = now, UpdatedAt = now
            };
            db.Courts.Add(second);
            await db.SaveChangesAsync();
            return second.Id;
        });

    /// <summary>Khuyến mãi không điều kiện, đang hiệu lực quanh ngày đặt của test.</summary>
    protected Task<Promotion> SeedPromotionAsync(
        DiscountTypeEnum type, decimal value, int? usageLimit = null, int usedCount = 0, int? usagePerUserLimit = null) =>
        WithDbAsync(async db =>
        {
            var now = DateTime.UtcNow;
            var today = DateTimeHelper.GetTodayInVietnam();
            var promotion = new Promotion
            {
                Id = Guid.NewGuid(), Name = $"KM test {Guid.NewGuid():N}"[..20], Code = $"T{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                DiscountType = type, DiscountValue = value, UsageLimit = usageLimit, UsedCount = usedCount,
                UsagePerUserLimit = usagePerUserLimit, StartDate = today.AddDays(-1), EndDate = today.AddDays(60),
                Status = PromotionStatus.ACTIVE, CreatedAt = now, UpdatedAt = now
            };
            db.Promotions.Add(promotion);
            await db.SaveChangesAsync();
            return promotion;
        });

    /// <summary>Nhân viên có quyền ở chi nhánh của sân (<c>user_branches</c> đang hiệu lực).</summary>
    protected async Task<User> SeedStaffAsync(Guid branchId)
    {
        var staff = await SeedUserAsync(UserRole.STAFF);
        await WithDbAsync(async db =>
        {
            db.UserBranches.Add(new UserBranch
            {
                Id = Guid.NewGuid(), UserId = staff.Id, BranchId = branchId, Role = UserBranchRole.STAFF, IsActive = true,
                AssignedAt = DateTime.UtcNow
            });
            return await db.SaveChangesAsync();
        });
        return staff;
    }

    // ───────────────────────── Gọi API ─────────────────────────

    /// <summary>Ngày đặt luôn ở tương lai theo giờ VN (A9).</summary>
    protected static DateOnly FutureDate(int days = 3) => DateTimeHelper.GetTodayInVietnam().AddDays(days);

    protected static object OnlineRequest(
        IEnumerable<Guid> courtIds, DateOnly date, int hour = 10, Guid? promotionId = null, int hours = 1) => new
    {
        bookingDate = date.ToString("yyyy-MM-dd"),
        courts = courtIds.Select(id => new
        {
            courtId = id,
            startTime = new TimeSpan(hour, 0, 0).ToString(@"hh\:mm\:ss"),
            endTime = new TimeSpan(hour + hours, 0, 0).ToString(@"hh\:mm\:ss")
        }),
        promotionId,
        // P6-01: các trường guest* vẫn được nhận nhưng BE bỏ qua
        guestName = "Không dùng",
        guestEmail = "guest-ignored@example.invalid"
    };

    /// <summary>Kiểm mã HTTP; sai thì in nguyên envelope để biết lý do (500 không có stack trace trong response).</summary>
    protected async Task AssertStatusAsync(HttpStatusCode expected, BookingCall call)
    {
        if (call.Status == expected)
            return;
        var factory = await GetFactoryAsync();
        var errors = factory.Logs.Entries.Where(e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error).TakeLast(3);
        Assert.Fail($"Mong {expected}, nhận {call.Status}: {call.Envelope}{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
    }

    protected static async Task<BookingCall> PostOnlineAsync(HttpClient client, object request)
    {
        using var response = await client.PostAsJsonAsync(OnlinePath, request);
        var text = await response.Content.ReadAsStringAsync();
        var envelope = string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        return new BookingCall(response.StatusCode, envelope);
    }

    /// <summary>
    /// Client của người dùng kèm IP máy khách: thư viện VNPay lấy IP từ request để dựng URL, mà TestServer không có IP thật
    /// (host tin <c>X-Forwarded-For</c>, giới hạn E1). Ngoài đời request luôn có IP.
    /// </summary>
    protected Task<HttpClient> CustomerClientAsync(User user) => CreateClientForAsync(user, "203.0.113.10");

    /// <summary>Khách mới đặt một sân, mặc định 10:00–11:00 ba ngày sau.</summary>
    protected async Task<(BookingCall Call, User Customer)> BookAsync(
        BookableCourt court, Guid? promotionId = null, User? customer = null, DateOnly? date = null, int hour = 10)
    {
        customer ??= await SeedUserAsync();
        var client = await CustomerClientAsync(customer);
        var call = await PostOnlineAsync(client, OnlineRequest([court.CourtId], date ?? FutureDate(), hour, promotionId));
        return (call, customer);
    }

    // ───────────────────────── Đọc DB ─────────────────────────

    protected Task<Booking?> ReadBookingAsync(Guid bookingId) =>
        WithDbAsync(db => db.Bookings.AsNoTracking()
            .Include(b => b.Invoice).ThenInclude(i => i!.Payments)
            .Include(b => b.BookingCourts)
            .Include(b => b.BookingPromotion)
            .FirstOrDefaultAsync(b => b.Id == bookingId));

    protected async Task<Booking> RequireBookingAsync(Guid bookingId) =>
        await ReadBookingAsync(bookingId) ?? throw new InvalidOperationException($"Không thấy booking {bookingId} trong DB.");

    /// <summary>Booking duy nhất đặt trên sân này (dùng khi response không về được, VD khách ngắt kết nối).</summary>
    protected Task<Booking?> FindBookingOnCourtAsync(Guid courtId) =>
        WithDbAsync(db => db.Bookings.AsNoTracking()
            .Include(b => b.Invoice).ThenInclude(i => i!.Payments)
            .Include(b => b.BookingCourts)
            .Where(b => b.BookingCourts.Any(bc => bc.CourtId == courtId))
            .OrderByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync());

    protected Task<List<SlotLock>> ReadLocksAsync(Guid bookingId) =>
        WithDbAsync(db => db.SlotLocks.AsNoTracking().Where(l => l.BookingId == bookingId).ToListAsync());

    protected Task<int> ReadUsedCountAsync(Guid promotionId) =>
        WithDbAsync(db => db.Promotions.AsNoTracking().Where(p => p.Id == promotionId).Select(p => p.UsedCount).SingleAsync());

    protected Task<bool> HasSlotInterestAsync(Guid courtId) =>
        WithDbAsync(db => db.SlotInterests.AsNoTracking().AnyAsync(i => i.CourtId == courtId));

    /// <summary>Đọc lại tới khi <paramref name="done"/> đúng hoặc hết <paramref name="timeout"/> (không sleep cố định).</summary>
    protected static async Task<T> WaitUntilAsync<T>(Func<Task<T>> read, Func<T, bool> done, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var value = await read();
            if (done(value) || watch.Elapsed > timeout)
                return value;
            await Task.Delay(50);
        }
    }

    // ───────────────────────── VNPay ─────────────────────────

    /// <summary><c>vnp_Amount</c> trong URL thanh toán đã chia 100 (đơn vị đồng).</summary>
    protected static decimal VnPayAmount(string paymentUrl)
    {
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(paymentUrl).Query);
        return decimal.Parse(query["vnp_Amount"] ?? throw new InvalidOperationException("URL không có vnp_Amount")) / 100m;
    }

    /// <summary>
    /// IPN giả có chữ ký đúng: ký HMAC-SHA512 bằng <c>VnPay:HashSecret</c> của host theo đúng cách <c>VnPayService.VerifyIpn</c> kiểm
    /// (khóa vnp_* sắp xếp ordinal, giá trị URL-encode).
    /// </summary>
    protected static string SignedIpnQuery(string transactionRef, decimal amount, bool success = true)
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Amount"] = ((long)(amount * 100)).ToString(),
            ["vnp_ResponseCode"] = success ? "00" : "24",
            ["vnp_TransactionStatus"] = success ? "00" : "02",
            ["vnp_TxnRef"] = transactionRef,
            ["vnp_TmnCode"] = SmashCourtWebApplicationFactory.VnPayTmnCode,
            ["vnp_TransactionNo"] = "14000001"
        };
        var data = string.Join("&", fields.Select(f => $"{f.Key}={WebUtility.UrlEncode(f.Value)}"));
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(SmashCourtWebApplicationFactory.VnPayHashSecret));
        var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
        return $"{data}&vnp_SecureHash={hash}";
    }
}
