using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Ngày giờ đi một vòng FE ⇒ BE ⇒ PostgreSQL ⇒ BE ⇒ FE (P6-22, T-7, T-8, R-6). Request dựng đúng như FE gửi:
/// đặt online <c>bookingDate = new Date("yyyy-MM-dd").toISOString()</c> (nửa đêm UTC, có <c>Z</c>), đơn tại quầy <c>"yyyy-MM-dd"</c>,
/// giờ <c>"HH:mm:ss"</c>. Giá trị đúng của mốc thời gian đọc thẳng từ PostgreSQL bằng <c>AT TIME ZONE 'Asia/Ho_Chi_Minh'</c>,
/// không qua cách EF đổi <c>DateTime</c>, nên không lặp lại logic của <c>VietnamDateTimeConverter</c> đang bị kiểm.
/// Phía FE đọc lại các chuỗi này bằng <c>parseBackendDate</c> / <c>calculateHoursUntil</c>: unit test của FE dùng đúng dạng chuỗi ở đây.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Booking")]
public sealed class BookingDateTimeRoundTripTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : ApprovalTestBase(database, host)
{
    private const string BackendDateTime = "dd/MM/yyyy HH:mm:ss";

    // Ngày dễ lệch khi đổi múi giờ hoặc cộng ngày: mai, cuối tháng, sang năm mới; giờ đầu ngày, cuối ngày, nửa giờ
    public static TheoryData<string, string, string> OnlineCases => new()
    {
        { "tomorrow", "06:00:00", "07:00:00" },
        { "month-end", "21:00:00", "22:00:00" },
        { "next-month-first", "10:30:00", "12:00:00" },
        { "dec-31", "21:30:00", "22:00:00" },
        { "jan-1", "06:00:00", "06:30:00" },
    };

    [Theory]
    [MemberData(nameof(OnlineCases))]
    public async Task OnlineBooking_SentLikeTheFrontend_IsStoredOnTheRightDayAndReadBackUnchanged(string day, string start, string end)
    {
        var court = await SeedCourtAsync(200_000m);
        var customer = await SeedUserAsync();
        var date = DateFor(day);

        var call = await PostOnlineAsync(await CustomerClientAsync(customer), new
        {
            courts = new[] { new { courtId = court.CourtId, startTime = start, endTime = end } },
            bookingDate = FrontendIsoDate(date),
            guestName = "",
            guestPhone = "",
            guestEmail = ""
        });
        await AssertStatusAsync(HttpStatusCode.Created, call);

        // DB: đúng ngày và giờ chơi đã chọn, không lùi / tiến một ngày
        var stored = await RequireBookingAsync(call.BookingId);
        Assert.Equal(date, stored.BookingDate);
        var storedCourt = Assert.Single(stored.BookingCourts);
        Assert.Equal(date, storedCourt.Date);
        Assert.Equal(TimeOnly.Parse(start), storedCourt.StartTime);
        Assert.Equal(TimeOnly.Parse(end), storedCourt.EndTime);

        // Response tạo đơn: hạn thanh toán là giờ VN của invoices.expires_at
        var truth = await VietnamTimesAsync(call.BookingId);
        Assert.Equal(truth.InvoiceExpiresAt, call.DataString("expiresAt"));

        // Đọc lại qua mọi màn khách và nhân viên dùng
        var manager = await CreateClientForAsync(await SeedManagerAsync(court.BranchId));
        foreach (var view in await ViewsAsync(await CustomerClientAsync(customer), manager, call.BookingId))
            AssertReadBack(view, date, start, end, truth);
    }

    [Fact]
    public async Task WalkInBooking_SentLikeTheFrontend_IsStoredOnTheRightDayAndReadBackUnchanged()
    {
        var court = await SeedCourtAsync(200_000m);
        var staff = await SeedStaffAsync(court.BranchId);
        var date = DateFor("month-end");

        var created = await SendAsync((await CreateClientForAsync(staff)).PostAsJsonAsync("/api/bookings/walk-in", new
        {
            bookingDate = date.ToString("yyyy-MM-dd"),
            courts = new[] { new { courtId = court.CourtId, startTime = "21:30:00", endTime = "22:00:00" } },
            customerId = (Guid?)null,
            guestName = "Khách tại quầy",
            guestPhone = "0900000000",
            guestEmail = (string?)null,
            promotionId = (Guid?)null,
            note = (string?)null,
            payNow = false
        }));
        await AssertApiStatusAsync(HttpStatusCode.Created, created);
        var bookingId = created.Data.GetProperty("id").GetGuid();

        var stored = await RequireBookingAsync(bookingId);
        Assert.Equal(date, stored.BookingDate);
        var storedCourt = Assert.Single(stored.BookingCourts);
        Assert.Equal(date, storedCourt.Date);
        Assert.Equal(new TimeOnly(21, 30), storedCourt.StartTime);
        Assert.Equal(new TimeOnly(22, 0), storedCourt.EndTime);

        var truth = await VietnamTimesAsync(bookingId);
        AssertReadBack(created.Data, date, "21:30:00", "22:00:00", truth);
    }

    [Fact]
    public async Task TimeGrid_QueriedWithTheFrontendDateKey_ShowsTheBookingOnTheSameDayAndHours()
    {
        var court = await SeedCourtAsync(200_000m);
        var customer = await SeedUserAsync();
        var date = DateFor("next-month-first");
        var call = await PostOnlineAsync(await CustomerClientAsync(customer), new
        {
            courts = new[] { new { courtId = court.CourtId, startTime = "10:00:00", endTime = "11:00:00" } },
            bookingDate = FrontendIsoDate(date)
        });
        await AssertStatusAsync(HttpStatusCode.Created, call);

        // FE gọi lưới giờ bằng khóa ngày "yyyy-MM-dd" (fetchTimeGrid); ngày trước đó không được thấy đơn này
        var grid = await TimeGridAsync(customer, court, date);
        var held = grid.Where(s => s.Status != "AVAILABLE").ToList();
        Assert.NotEmpty(held);
        Assert.All(held, s =>
        {
            Assert.Equal("LOCKED", s.Status); // đơn PENDING giữ sân
            Assert.True(string.CompareOrdinal(s.StartTime, "10:00:00") >= 0 && string.CompareOrdinal(s.EndTime, "11:00:00") <= 0,
                $"Ô bận {s.StartTime}–{s.EndTime} nằm ngoài giờ đã đặt");
        });
        Assert.Contains(held, s => s.StartTime == "10:00:00");
        Assert.Contains(held, s => s.EndTime == "11:00:00");
        Assert.DoesNotContain(await TimeGridAsync(customer, court, date.AddDays(-1)), s => s.Status != "AVAILABLE");
    }

    // ───────────────────────── Dữ liệu ─────────────────────────

    /// <summary><c>new Date("yyyy-MM-dd").toISOString()</c> của JS: chuỗi ngày không giờ được hiểu là nửa đêm UTC.</summary>
    private static string FrontendIsoDate(DateOnly date) => $"{date:yyyy-MM-dd}T00:00:00.000Z";

    private static DateOnly DateFor(string day)
    {
        var today = DateTimeHelper.GetTodayInVietnam();
        var tomorrow = today.AddDays(1);
        var monthEnd = new DateOnly(tomorrow.Year, tomorrow.Month, DateTime.DaysInMonth(tomorrow.Year, tomorrow.Month));
        return day switch
        {
            "tomorrow" => tomorrow,
            "month-end" => monthEnd,
            "next-month-first" => monthEnd.AddDays(1),
            // Sau ngày mai nên luôn là tương lai; 31/12 là hôm nay hoặc mai thì lấy năm sau
            "dec-31" => NextDecember31(tomorrow),
            "jan-1" => new DateOnly(tomorrow.Year + 1, 1, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(day), day, null)
        };
    }

    private static DateOnly NextDecember31(DateOnly after)
    {
        var thisYear = new DateOnly(after.Year, 12, 31);
        return thisYear > after ? thisYear : thisYear.AddYears(1);
    }

    private sealed record VietnamTimes(string CreatedAt, string? InvoiceExpiresAt);

    /// <summary>Mốc thời gian theo giờ VN đọc bằng chính PostgreSQL (không qua quy đổi DateTime của EF / Npgsql).</summary>
    private Task<VietnamTimes> VietnamTimesAsync(Guid bookingId) =>
        WithDbAsync(async db =>
        {
            var createdAt = await db.Database.SqlQuery<string>($"""
                SELECT to_char(created_at AT TIME ZONE 'Asia/Ho_Chi_Minh', 'DD/MM/YYYY HH24:MI:SS') AS "Value"
                FROM bookings WHERE id = {bookingId}
                """).SingleAsync();
            var expiresAt = await db.Database.SqlQuery<string?>($"""
                SELECT to_char(expires_at AT TIME ZONE 'Asia/Ho_Chi_Minh', 'DD/MM/YYYY HH24:MI:SS') AS "Value"
                FROM invoices WHERE booking_id = {bookingId}
                """).SingleAsync();
            return new VietnamTimes(createdAt, expiresAt);
        });

    // ───────────────────────── Đọc lại ─────────────────────────

    /// <summary>Chi tiết + lịch sử của khách; chi tiết theo id, chi tiết đầy đủ, dòng danh sách của nhân viên.</summary>
    private static async Task<List<JsonElement>> ViewsAsync(HttpClient customer, HttpClient staff, Guid bookingId)
    {
        var views = new List<JsonElement>();
        foreach (var (client, path, inList) in new[]
                 {
                     (customer, $"/api/me/bookings/{bookingId}", false),
                     (customer, "/api/me/bookings?pageSize=50", true),
                     (staff, $"/api/bookings/{bookingId}", false),
                     (staff, $"/api/bookings/details/{bookingId}", false),
                 })
        {
            var call = await SendAsync(client.GetAsync(path));
            Assert.True(call.Status == HttpStatusCode.OK, $"{path}: {call.Status} {call.Raw}");
            views.Add(inList
                ? call.Data.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == bookingId)
                : call.Data);
        }
        return views;
    }

    private static void AssertReadBack(JsonElement booking, DateOnly date, string start, string end, VietnamTimes truth)
    {
        // bookingDate ra "dd/MM/yyyy 07:00:00": phần giờ là sản phẩm phụ của converter, FE chỉ lấy phần ngày (bẫy 17)
        var bookingDate = booking.GetProperty("bookingDate").GetString()!;
        Assert.Equal(date.ToString("dd/MM/yyyy"), bookingDate.Split(' ')[0]);
        var court = Assert.Single(booking.GetProperty("courts").EnumerateArray());
        Assert.Equal(start, court.GetProperty("startTime").GetString());
        Assert.Equal(end, court.GetProperty("endTime").GetString());
        // Mốc thời gian: "dd/MM/yyyy HH:mm:ss" giờ VN, bằng đúng giá trị trong DB
        Assert.Equal(truth.CreatedAt, booking.GetProperty("createdAt").GetString());
        var expiresAt = booking.GetProperty("expiresAt");
        Assert.Equal(truth.InvoiceExpiresAt, expiresAt.ValueKind == JsonValueKind.Null ? null : expiresAt.GetString());
        Assert.True(DateTime.TryParseExact(truth.CreatedAt, BackendDateTime, null, System.Globalization.DateTimeStyles.None, out _));
    }

    private sealed record GridSlot(string StartTime, string EndTime, string Status);

    private async Task<List<GridSlot>> TimeGridAsync(User customer, BookableCourt court, DateOnly date)
    {
        var call = await SendAsync((await CustomerClientAsync(customer))
            .GetAsync($"/api/branches/{court.BranchId}/courts/{court.CourtId}/time-grid?date={date:yyyy-MM-dd}"));
        Assert.True(call.Status == HttpStatusCode.OK, call.Raw);
        return call.Data.EnumerateArray()
            .Select(s => new GridSlot(s.GetProperty("startTime").GetString()!, s.GetProperty("endTime").GetString()!,
                s.GetProperty("status").GetString()!))
            .ToList();
    }
}
