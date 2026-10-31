using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// BE-IT-26 (gói 6A-5 Task 12, B2, B6, B7 — P6-25): job đổi trạng thái booking đua với thao tác của khách / nhân viên, có điểm chặn.
/// Bên A mở transaction bằng tay và giữ khóa dòng booking (chưa commit); bên B (job hoặc dịch vụ của job, scope DI riêng) phải đứng chờ;
/// A commit thì B thua và không ghi gì. Hai request bắn song song không ép được thứ tự xen kẽ này nên kết quả ổn định.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Booking")]
[Trait("Catalog", "BE-IT-26")]
public sealed class ExpiryConcurrencyTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : PaymentCancellationTestBase(database, host)
{
    private static readonly TimeSpan BlockedFor = TimeSpan.FromMilliseconds(500);

    /// <summary>Transaction của bên A trên kết nối riêng; dispose mà chưa commit thì rollback (test hỏng không treo bên B).</summary>
    private sealed class HeldTransaction(IServiceScope scope, SmashCourtContext db, IDbContextTransaction transaction) : IAsyncDisposable
    {
        public SmashCourtContext Db => db;
        public BookingRepository Bookings { get; } = new(db);
        public InvoiceRepository Invoices { get; } = new(db);
        public PaymentRepository Payments { get; } = new(db);
        public CourtRepository Courts { get; } = new(db);

        public Task CommitAsync() => transaction.CommitAsync();

        // Phần còn lại của lõi hủy như BookingCancellationService (sau câu điều kiện): nhả sân và slot lock (SC6-3)
        public async Task ReleaseCourtsLikeTheCancellationServiceAsync(Guid bookingId)
        {
            await Bookings.UpdateCourtActiveStatusAsync(bookingId, false);
            await new SlotLockRepository(db).DeleteByBookingIdAsync(bookingId);
        }

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            scope.Dispose();
        }
    }

    private async Task<HeldTransaction> BeginAsync()
    {
        var factory = await GetFactoryAsync();
        var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmashCourtContext>();
        return new HeldTransaction(scope, db, await db.Database.BeginTransactionAsync());
    }

    /// <summary>Chạy bên B trong scope DI riêng (kết nối riêng), không chờ.</summary>
    private async Task<Task<T>> StartAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        var factory = await GetFactoryAsync();
        return Task.Run(async () =>
        {
            using var scope = factory.Services.CreateScope();
            return await work(scope.ServiceProvider);
        });
    }

    private static async Task AssertBlockedAsync(Task task)
    {
        var finished = await Task.WhenAny(task, Task.Delay(BlockedFor));
        Assert.NotSame(task, finished);
    }

    private static async Task<T> FinishAsync<T>(Task<T> task) => await task.WaitAsync(TimeSpan.FromSeconds(15));

    private Task ExpirePaymentWindowAsync(Guid bookingId) =>
        WithDbAsync(async db =>
        {
            var expired = DateTime.UtcNow.AddSeconds(-1);
            await db.SlotLocks.Where(l => l.BookingId == bookingId).ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAt, expired));
            return await db.Invoices.Where(i => i.BookingId == bookingId).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.CreatedAt, DateTime.UtcNow.AddMinutes(-60))
                .SetProperty(i => i.HoldDeadline, (DateTime?)DateTime.UtcNow.AddMinutes(30))
                .SetProperty(i => i.ExpiresAt, (DateTime?)expired));
        });

    private Task MovePlayToYesterdayAsync(Guid bookingId)
    {
        var yesterday = DateTimeHelper.GetTodayInVietnam().AddDays(-1);
        return WithDbAsync(async db =>
        {
            await db.Bookings.Where(b => b.Id == bookingId).ExecuteUpdateAsync(s => s.SetProperty(b => b.BookingDate, yesterday));
            return await db.BookingCourts.Where(bc => bc.BookingId == bookingId).ExecuteUpdateAsync(s => s
                .SetProperty(bc => bc.Date, yesterday)
                .SetProperty(bc => bc.StartTime, new TimeOnly(10, 0))
                .SetProperty(bc => bc.EndTime, new TimeOnly(11, 0)));
        });
    }

    private Task<Court> ReadCourtAsync(Guid courtId) => WithDbAsync(db => db.Courts.AsNoTracking().SingleAsync(c => c.Id == courtId));

    // ───────────────────────── Job-01 (B2) ─────────────────────────

    [Fact]
    public async Task Job01_LosesToARetryThatExtendedTheWindow()
    {
        var order = await BookPendingAsync();
        await ExpirePaymentWindowAsync(order.BookingId);
        var newExpiry = DateTime.UtcNow.AddMinutes(10);
        var factory = await GetFactoryAsync();
        factory.Emails.Clear();

        await using (var a = await BeginAsync())
        {
            Assert.True(await a.Bookings.ExtendPaymentWindowAsync(order.BookingId, newExpiry, DateTime.UtcNow));
            var b = await StartAsync(sp => sp.GetRequiredService<IBookingCancellationService>().ExpireUnpaidAsync(order.BookingId));
            await AssertBlockedAsync(b);

            await a.CommitAsync();
            Assert.False(await FinishAsync(b));
        }

        var booking = await ReadApprovalBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.PENDING, booking.Status);
        Assert.Null(booking.CancelReason);
        Assert.Null(booking.CancelledAt);
        Assert.Equal(InvoicePaymentStatus.UNPAID, booking.Invoice!.PaymentStatus);
        Assert.Equal(newExpiry, Assert.NotNull(booking.Invoice.ExpiresAt), TimeSpan.FromMilliseconds(1));
        Assert.Equal(PaymentTxStatus.PENDING, Assert.Single(booking.Invoice.Payments).Status);
        Assert.All(booking.SlotLocks, l => Assert.Equal(newExpiry, l.ExpiresAt, TimeSpan.FromMilliseconds(1)));
        Assert.All(booking.BookingCourts, c => Assert.True(c.IsActive));
        Assert.Empty(factory.Emails.Sent);

        // Khách tải lại trang thấy hạn mới
        var detail = await SendAsync((await CustomerClientAsync(order.Customer)).GetAsync($"/api/me/bookings/{order.BookingId}"));
        await AssertApiStatusAsync(HttpStatusCode.OK, detail);
        // API trả giờ VN dạng dd/MM/yyyy HH:mm:ss (VietnamDateTimeConverter)
        Assert.Equal(DateTimeHelper.ToVietnamTime(newExpiry).ToString("dd/MM/yyyy HH:mm:ss"), detail.DataString("expiresAt"));
    }

    [Fact]
    public async Task Job01_WinsFirst_RetryIsRefusedAndCreatesNoPayment()
    {
        var order = await BookPendingAsync();
        await ExpirePaymentWindowAsync(order.BookingId);

        await RunPaymentExpiryJobAsync();
        var retry = await RetryAsync(order.Customer, order.BookingId);

        // Đơn đã hủy: dịch vụ thanh toán từ chối ở bước kiểm trạng thái (400); 409 chỉ khi hủy chen giữa lúc đọc và lúc gia hạn
        Assert.Equal(HttpStatusCode.BadRequest, retry.Status);
        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal(PaymentTxStatus.FAILED, Assert.Single(booking.Invoice!.Payments).Status);
    }

    [Fact]
    public async Task Job01_LosesToASuccessfulPayment()
    {
        var order = await BookPendingAsync();
        await ExpirePaymentWindowAsync(order.BookingId);
        var payment = Assert.Single((await RequireBookingAsync(order.BookingId)).Invoice!.Payments);

        await using (var a = await BeginAsync())
        {
            // Như IPN thành công: khóa booking trước, rồi PENDING ⇒ PAID_ONLINE, Payment ⇒ SUCCESS
            await a.Bookings.LockAsync(order.BookingId);
            Assert.Equal(1, await a.Bookings.UpdateWithStatusCheckAsync(order.BookingId, BookingStatus.PAID_ONLINE, BookingStatus.PENDING));
            Assert.True(await a.Payments.TryMarkSuccessAsync(payment.Id, DateTime.UtcNow));
            var b = await StartAsync(sp => sp.GetRequiredService<IBookingCancellationService>().ExpireUnpaidAsync(order.BookingId));
            await AssertBlockedAsync(b);

            await a.CommitAsync();
            Assert.False(await FinishAsync(b)); // không deadlock 40P01: cả hai khóa booking trước
        }

        var booking = await ReadApprovalBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.PAID_ONLINE, booking.Status);
        Assert.Equal(PaymentTxStatus.SUCCESS, Assert.Single(booking.Invoice!.Payments).Status);
        Assert.Null(booking.CancelReason);
        Assert.All(booking.BookingCourts, c => Assert.True(c.IsActive));
    }

    // ───────────────────────── Job hết hạn duyệt và Quản lý duyệt ở biên hạn ─────────────────────────

    [Fact]
    public async Task ApprovalExpiry_RacingTheManagerAtTheDeadline_ExactlyOneSideWinsEveryTime()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var factory = await GetFactoryAsync();
        for (var round = 0; round < 5; round++)
        {
            var pending = await CreatePendingApprovalAsync(tadss);
            var manager = await SeedManagerAsync(pending.Court.BranchId);
            tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m);
            var client = await CreateClientForAsync(manager);
            var deadline = DateTime.UtcNow.AddSeconds(1);
            await MoveApprovalExpiryAsync(pending.BookingId, deadline);

            // Quản lý bấm sát hạn, job chạy đúng lúc hết hạn
            await Task.Delay(Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalMilliseconds - 30 - round * 10));
            var approve = ApproveAsync(client, pending.BookingId, 0.3m);
            var job = Task.Run(async () =>
            {
                await Task.Delay(Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalMilliseconds));
                using var scope = factory.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IBookingApprovalJob>().ExpireApprovalsAsync();
            });
            var call = await approve;
            await job;
            // Lần chạy job sau hạn chắc chắn (nếu Quản lý thắng thì job vẫn không được hủy)
            using (var scope = factory.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<IBookingApprovalJob>().ExpireApprovalsAsync();

            var booking = await RequireBookingAsync(pending.BookingId);
            Assert.NotEqual(HttpStatusCode.InternalServerError, call.Status);
            if (call.Status == HttpStatusCode.OK)
            {
                Assert.Equal(BookingStatus.PENDING, booking.Status);
                Assert.Equal(ApprovalResult.APPROVED, booking.ApprovalResult);
                Assert.Null(booking.CancelReason);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, call.Status);
                Assert.Equal("APPROVAL_EXPIRED", call.Code);
                Assert.Equal(BookingStatus.CANCELLED, booking.Status);
                Assert.Equal(BookingCancelReason.APPROVAL_EXPIRED, booking.CancelReason);
                Assert.Null(booking.ApprovalResult);
            }
        }
    }

    // ───────────────────────── Job-04 (B6) ─────────────────────────

    [Fact]
    public async Task Job04_LosesToACheckInAtTheLastSecond()
    {
        var court = await SeedCourtAsync();
        var bookingId = await CreateWalkInAsync(await SeedStaffAsync(court.BranchId), court, payNow: false);
        await MovePlayToYesterdayAsync(bookingId);
        var checkedInAt = DateTime.UtcNow;

        await using (var a = await BeginAsync())
        {
            // Như check-in của nhân viên: câu cập nhật có điều kiện trên booking, rồi sân ⇒ IN_USE
            Assert.True(await a.Bookings.CheckInAsync(bookingId, BookingStatus.CONFIRMED, checkedInAt));
            await a.Courts.BatchUpdateStatusAsync([court.CourtId], CourtStatus.IN_USE, checkedInAt);
            var b = await StartAsync(async sp =>
            {
                await sp.GetRequiredService<IBookingJob>().DetectNoShowBookingsAsync();
                return true;
            });
            await AssertBlockedAsync(b);

            await a.CommitAsync();
            await FinishAsync(b);
        }

        var booking = await RequireBookingAsync(bookingId);
        Assert.Equal(BookingStatus.IN_PROGRESS, booking.Status);
        Assert.Equal(checkedInAt, Assert.NotNull(booking.CheckedInAt), TimeSpan.FromMilliseconds(1));
        Assert.All(booking.BookingCourts, c => Assert.True(c.IsActive));
        Assert.Equal(CourtStatus.IN_USE, (await ReadCourtAsync(court.CourtId)).Status);
        // Job không ghi gì: updated_at vẫn là mốc của lần check-in (MarkNoShow thắng thì ghi mốc của job)
        Assert.Equal(checkedInAt, booking.UpdatedAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Job04_LosesToAStaffCancellationWithRefund()
    {
        await EnsureCancelPoliciesAsync();
        var order = await BookPaidAsync();
        await MovePlayToYesterdayAsync(order.BookingId);
        var staff = await SeedStaffAsync(order.Court.BranchId);
        var payment = Assert.Single((await RequireBookingAsync(order.BookingId)).Invoice!.Payments);

        await using (var a = await BeginAsync())
        {
            var now = DateTime.UtcNow;
            Assert.Equal(1, await a.Bookings.CancelWithStatusCheckAsync(order.BookingId, [BookingStatus.PAID_ONLINE],
                BookingStatus.CANCELLED_PENDING_REFUND, CancelSourceEnum.STAFF, BookingCancelReason.STAFF_CANCELLED, staff.Id, now));
            await a.ReleaseCourtsLikeTheCancellationServiceAsync(order.BookingId);
            a.Db.Refunds.Add(new Refund
            {
                Id = Guid.NewGuid(), PaymentId = payment.Id, Amount = payment.Amount, RefundPercent = 100m, Status = RefundStatus.PENDING,
                CreatedAt = now
            });
            await a.Db.SaveChangesAsync();
            var b = await StartAsync(async sp =>
            {
                await sp.GetRequiredService<IBookingJob>().DetectNoShowBookingsAsync();
                return true;
            });
            await AssertBlockedAsync(b);

            await a.CommitAsync();
            await FinishAsync(b);
        }

        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED_PENDING_REFUND, booking.Status);
        Assert.Equal(BookingCancelReason.STAFF_CANCELLED, booking.CancelReason);
        Assert.Single(await ReadRefundsAsync(order.BookingId));
        // Không bị đè thành NO_SHOW nên nhân viên xác nhận hoàn tiền được
        await AssertApiStatusAsync(HttpStatusCode.OK, await ConfirmRefundAsync(staff, order.BookingId));
        Assert.Equal(BookingStatus.CANCELLED_REFUNDED, (await RequireBookingAsync(order.BookingId)).Status);
    }

    [Fact]
    public async Task Job04_WinsFirst_ACheckInLoadedEarlierLoses()
    {
        var court = await SeedCourtAsync();
        var bookingId = await CreateWalkInAsync(await SeedStaffAsync(court.BranchId), court, payNow: false);
        await MovePlayToYesterdayAsync(bookingId);
        var factory = await GetFactoryAsync();
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IBookingJob>().DetectNoShowBookingsAsync();
        Assert.Equal(BookingStatus.NO_SHOW, (await RequireBookingAsync(bookingId)).Status);

        // Nhân viên đã nạp đơn (CONFIRMED) trước lúc job commit: câu check-in có điều kiện thua ⇒ dịch vụ trả 409
        await using var a = await BeginAsync();
        Assert.False(await a.Bookings.CheckInAsync(bookingId, BookingStatus.CONFIRMED, DateTime.UtcNow));
    }

    // ───────────────────────── Job-02 (B7) ─────────────────────────

    [Fact]
    public async Task Job02_LosesToACheckoutThatJustCommitted()
    {
        var order = await BookPaidAsync();
        await StartPlayingAsync(order.BookingId);
        await MovePlayToYesterdayAsync(order.BookingId);
        var checkoutAt = DateTime.UtcNow.AddSeconds(-30);

        await using (var a = await BeginAsync())
        {
            // Như check-out: câu có điều kiện trên booking chạy đầu tiên, rồi thu tiền mặt phần còn lại, hóa đơn PAID
            var now = checkoutAt;
            Assert.Equal(1, await a.Bookings.UpdateWithStatusCheckAsync(order.BookingId, BookingStatus.COMPLETED, BookingStatus.IN_PROGRESS));
            var invoice = await a.Db.Invoices.AsNoTracking().SingleAsync(i => i.BookingId == order.BookingId);
            await a.Invoices.SetPaymentStatusAsync(invoice.Id, InvoicePaymentStatus.PAID, now);
            await a.Bookings.UpdateCourtActiveStatusAsync(order.BookingId, false);
            var b = await StartAsync(async sp =>
            {
                await sp.GetRequiredService<IBookingJob>().ProcessExpiredActiveBookingsAsync();
                return true;
            });
            await AssertBlockedAsync(b);

            await a.CommitAsync();
            await FinishAsync(b);
        }

        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.COMPLETED, booking.Status); // không bị đè thành PENDING_PAYMENT
        Assert.Equal(InvoicePaymentStatus.PAID, booking.Invoice!.PaymentStatus);
        Assert.Single(booking.Invoice.Payments);
        // Job không ghi gì: updated_at của hóa đơn vẫn là mốc check-out (EndPlay thắng thì ghi mốc của job)
        Assert.Equal(checkoutAt, booking.Invoice.UpdatedAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Job02_ChoosesTheTargetFromTheInvoiceReadUnderTheLock()
    {
        // Ứng viên được đọc khi hóa đơn còn PARTIALLY_PAID; trong lúc job chờ khóa, hóa đơn được ghi PAID (đơn vẫn IN_PROGRESS)
        var order = await BookPaidAsync();
        await StartPlayingAsync(order.BookingId);
        await WithDbAsync(db => db.Invoices.Where(i => i.BookingId == order.BookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.PaymentStatus, InvoicePaymentStatus.PARTIALLY_PAID)));
        await MovePlayToYesterdayAsync(order.BookingId);

        await using (var a = await BeginAsync())
        {
            await a.Bookings.LockAsync(order.BookingId);
            var invoice = await a.Db.Invoices.AsNoTracking().SingleAsync(i => i.BookingId == order.BookingId);
            await a.Invoices.SetPaymentStatusAsync(invoice.Id, InvoicePaymentStatus.PAID, DateTime.UtcNow);
            var b = await StartAsync(async sp =>
            {
                await sp.GetRequiredService<IBookingJob>().ProcessExpiredActiveBookingsAsync();
                return true;
            });
            await AssertBlockedAsync(b);

            await a.CommitAsync();
            await FinishAsync(b);
        }

        Assert.Equal(BookingStatus.COMPLETED, (await RequireBookingAsync(order.BookingId)).Status);
    }

    [Fact]
    public async Task Job02_WinsFirst_CheckoutThenRunsFromPendingPayment()
    {
        var order = await BookPaidAsync();
        await StartPlayingAsync(order.BookingId);
        await WithDbAsync(db => db.Invoices.Where(i => i.BookingId == order.BookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.PaymentStatus, InvoicePaymentStatus.PARTIALLY_PAID)));
        await MovePlayToYesterdayAsync(order.BookingId);
        var factory = await GetFactoryAsync();
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IBookingJob>().ProcessExpiredActiveBookingsAsync();
        Assert.Equal(BookingStatus.PENDING_PAYMENT, (await RequireBookingAsync(order.BookingId)).Status);

        await AssertApiStatusAsync(HttpStatusCode.OK, await CheckoutAsync(await SeedStaffAsync(order.Court.BranchId), order.BookingId));

        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.COMPLETED, booking.Status);
        Assert.DoesNotContain(booking.Invoice!.Payments, p => p.Method == PaymentTxMethod.CASH); // đã trả đủ online, không thu thêm
        Assert.DoesNotContain(factory.Logs.Entries, e => e.Level >= LogLevel.Error && e.Message.Contains(order.BookingId.ToString()));
    }

    // ───────────────────────── Job hết hạn duyệt và lệnh hủy của nhân viên ─────────────────────────

    [Fact]
    public async Task ApprovalExpiry_LosesToAStaffCancellationThatJustCommitted()
    {
        var tadss = await GetTadssAsync();
        PendingApproval pending;
        using (await UseTadssAsync())
            pending = await CreatePendingApprovalAsync(tadss);
        await MoveApprovalExpiryAsync(pending.BookingId, DateTime.UtcNow.AddSeconds(-1));
        var staff = await SeedStaffAsync(pending.Court.BranchId);
        var cancelledAt = DateTime.UtcNow.AddSeconds(-5);
        var factory = await GetFactoryAsync();
        factory.Emails.Clear();

        await using (var a = await BeginAsync())
        {
            // Như lõi hủy của nhân viên: câu cập nhật có điều kiện trên booking chạy đầu tiên
            Assert.Equal(1, await a.Bookings.CancelWithStatusCheckAsync(pending.BookingId, [BookingStatus.PENDING_APPROVAL],
                BookingStatus.CANCELLED, CancelSourceEnum.STAFF, BookingCancelReason.STAFF_CANCELLED, staff.Id, cancelledAt));
            await a.ReleaseCourtsLikeTheCancellationServiceAsync(pending.BookingId);
            var b = await StartAsync(sp => sp.GetRequiredService<IBookingCancellationService>().ExpireApprovalAsync(pending.BookingId));
            await AssertBlockedAsync(b);

            await a.CommitAsync();
            Assert.False(await FinishAsync(b));
        }

        var booking = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Equal(CancelSourceEnum.STAFF, booking.CancelSource);
        Assert.Equal(BookingCancelReason.STAFF_CANCELLED, booking.CancelReason);
        Assert.Equal(staff.Id, booking.CancelledBy);
        Assert.Equal(cancelledAt, Assert.NotNull(booking.CancelledAt), TimeSpan.FromMilliseconds(1));
        Assert.Empty(factory.Emails.To(pending.Customer.Email)); // không có email "hết hạn duyệt" của job
    }
}
