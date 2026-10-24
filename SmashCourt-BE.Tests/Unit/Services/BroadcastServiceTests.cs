using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Common.Constants;
using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Hubs;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Services;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services;

public class BroadcastServiceTests
{
    /// <summary>Hub giả: ghi lại (nhóm, sự kiện, payload) của mỗi lần gửi; <see cref="Fail"/> = true thì mọi lần gửi ném lỗi.</summary>
    private sealed class RecordingHub
    {
        public List<(string Group, string Event, object? Payload)> Sent { get; } = [];
        public bool Fail { get; set; }
        public Mock<IHubContext<NotificationHub>> Context { get; } = new();

        public RecordingHub()
        {
            var clients = new Mock<IHubClients>();
            clients.Setup(x => x.Group(It.IsAny<string>())).Returns((string group) =>
            {
                var proxy = new Mock<IClientProxy>();
                proxy.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                    .Returns((string method, object?[] args, CancellationToken _) =>
                    {
                        if (Fail)
                            return Task.FromException(new InvalidOperationException("SignalR lỗi"));
                        lock (Sent)
                            Sent.Add((group, method, args.FirstOrDefault()));
                        return Task.CompletedTask;
                    });
                return proxy.Object;
            });
            Context.Setup(x => x.Clients).Returns(clients.Object);
        }

        public IEnumerable<string> Groups => Sent.Select(s => s.Group).Order();
    }

    private static Booking BookingWithCourt(out Court court)
    {
        court = TestDataFactory.CreateCourt();
        var booking = TestDataFactory.CreateBooking(branchId: court.BranchId);
        booking.BookingCourts = [new BookingCourt { CourtId = court.Id, Court = court, Date = new DateOnly(2026, 11, 20) }];
        return booking;
    }

    [Fact]
    public async Task BroadcastBookingEventAsync_SignalRFailureIsSwallowed()
    {
        var service = new BroadcastService(Mock.Of<IHubContext<NotificationHub>>(), Mock.Of<ILogger<BroadcastService>>());

        await service.BroadcastBookingEventAsync("booking.updated", new BookingNotificationDto(), null!);
    }

    [Fact]
    public async Task BroadcastBookingEventAsync_IncludesTheCustomerGroup()
    {
        // Đối chứng cho hàm "chỉ nhân viên" bên dưới
        var hub = new RecordingHub();
        var booking = BookingWithCourt(out _);

        await new BroadcastService(hub.Context.Object, Mock.Of<ILogger<BroadcastService>>())
            .BroadcastBookingEventAsync(SignalREvents.BookingCreated, new BookingNotificationDto(), booking);

        Assert.Contains($"user_{booking.CustomerId}", hub.Groups);
    }

    [Fact]
    public async Task BroadcastBookingEventToStaffAsync_SkipsTheCustomerGroup_AndReachesStaffOwnerAndTimeGrid()
    {
        // C2: đơn bị từ chối, khách đã nhận 400 nên không đẩy BookingCancelled vào user_{id}
        var hub = new RecordingHub();
        var booking = BookingWithCourt(out var court);
        var notification = new BookingNotificationDto { BookingId = booking.Id };

        await new BroadcastService(hub.Context.Object, Mock.Of<ILogger<BroadcastService>>())
            .BroadcastBookingEventToStaffAsync(SignalREvents.BookingCancelled, notification, booking, includeTimeGrid: true);

        Assert.Equal(
            new[] { $"branch_{booking.BranchId}", "role_OWNER", $"timegrid_{booking.BranchId}_{court.CourtTypeId}_2026-11-20" }.Order(),
            hub.Groups);
        Assert.All(hub.Sent, s =>
        {
            Assert.Equal(SignalREvents.BookingCancelled, s.Event);
            Assert.Same(notification, s.Payload);
        });
        Assert.DoesNotContain(hub.Sent, s => s.Group.StartsWith("user_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BroadcastBookingEventToStaffAsync_WithoutTimeGrid_OnlyStaffAndOwner()
    {
        var hub = new RecordingHub();
        var booking = BookingWithCourt(out _);

        await new BroadcastService(hub.Context.Object, Mock.Of<ILogger<BroadcastService>>())
            .BroadcastBookingEventToStaffAsync(SignalREvents.BookingCancelled, new BookingNotificationDto(), booking);

        Assert.Equal(new[] { $"branch_{booking.BranchId}", "role_OWNER" }.Order(), hub.Groups);
    }

    [Fact]
    public async Task BroadcastBookingPendingApprovalAsync_ReachesBranchAndOwnerOnly()
    {
        var hub = new RecordingHub();
        var notification = new BookingPendingApprovalNotificationDto { BookingId = Guid.NewGuid(), BranchId = Guid.NewGuid() };

        await new BroadcastService(hub.Context.Object, Mock.Of<ILogger<BroadcastService>>())
            .BroadcastBookingPendingApprovalAsync(notification);

        Assert.Equal(new[] { $"branch_{notification.BranchId}", "role_OWNER" }.Order(), hub.Groups);
        Assert.All(hub.Sent, s =>
        {
            Assert.Equal("BookingPendingApproval", s.Event);
            Assert.Same(notification, s.Payload);
        });
    }

    [Fact]
    public async Task BroadcastPendingApprovalQueueChangedAsync_ReachesBranchAndOwnerOnly()
    {
        var hub = new RecordingHub();
        var notification = new PendingApprovalQueueChangedNotificationDto { BookingId = Guid.NewGuid(), BranchId = Guid.NewGuid() };

        await new BroadcastService(hub.Context.Object, Mock.Of<ILogger<BroadcastService>>())
            .BroadcastPendingApprovalQueueChangedAsync(notification);

        Assert.Equal(new[] { $"branch_{notification.BranchId}", "role_OWNER" }.Order(), hub.Groups);
        Assert.All(hub.Sent, s =>
        {
            Assert.Equal("PendingApprovalQueueChanged", s.Event);
            Assert.Same(notification, s.Payload);
        });
    }

    [Fact]
    [Trait("Catalog", "BE-IT-18")]
    public async Task BroadcastBookingApprovalResolvedAsync_ReachesCustomerBranchAndOwner()
    {
        var hub = new RecordingHub();
        var customerId = Guid.NewGuid();
        var notification = new BookingApprovalResolvedNotificationDto { BookingId = Guid.NewGuid(), BranchId = Guid.NewGuid() };

        await new BroadcastService(hub.Context.Object, Mock.Of<ILogger<BroadcastService>>())
            .BroadcastBookingApprovalResolvedAsync(notification, customerId);

        Assert.Equal(new[] { $"user_{customerId}", $"branch_{notification.BranchId}", "role_OWNER" }.Order(), hub.Groups);
        Assert.All(hub.Sent, s =>
        {
            Assert.Equal("BookingApprovalResolved", s.Event);
            Assert.Same(notification, s.Payload);
        });
    }

    [Fact]
    [Trait("Catalog", "BE-IT-18")]
    public async Task BroadcastBookingApprovalResolvedAsync_WithoutCustomerAccount_OnlyStaffAndOwner()
    {
        var hub = new RecordingHub();
        var notification = new BookingApprovalResolvedNotificationDto { BookingId = Guid.NewGuid(), BranchId = Guid.NewGuid() };

        await new BroadcastService(hub.Context.Object, Mock.Of<ILogger<BroadcastService>>())
            .BroadcastBookingApprovalResolvedAsync(notification, null);

        Assert.Equal(new[] { $"branch_{notification.BranchId}", "role_OWNER" }.Order(), hub.Groups);
    }

    [Fact]
    public async Task NewBroadcasts_SwallowSignalRFailures_AndLogThem()
    {
        var hub = new RecordingHub { Fail = true };
        var logger = new ListLogger<BroadcastService>();
        var service = new BroadcastService(hub.Context.Object, logger);
        var booking = BookingWithCourt(out _);

        await service.BroadcastBookingEventToStaffAsync(SignalREvents.BookingCancelled, new BookingNotificationDto(), booking, true);
        await service.BroadcastBookingPendingApprovalAsync(new BookingPendingApprovalNotificationDto());
        await service.BroadcastPendingApprovalQueueChangedAsync(new PendingApprovalQueueChangedNotificationDto());
        await service.BroadcastBookingApprovalResolvedAsync(new BookingApprovalResolvedNotificationDto(), Guid.NewGuid());

        Assert.Equal(4, logger.Entries.Count(e => e.Level == LogLevel.Error && e.Exception != null));
    }

    [Theory]
    [InlineData(nameof(SignalREvents.BookingPendingApproval), "BookingPendingApproval")]
    [InlineData(nameof(SignalREvents.BookingApprovalResolved), "BookingApprovalResolved")]
    [InlineData(nameof(SignalREvents.PendingApprovalQueueChanged), "PendingApprovalQueueChanged")]
    public void NewEventNames_MatchTheApiDoc(string constant, string value)
    {
        // Giá trị là hợp đồng với FE (smashcourt-fe/src/lib/signalr-events.ts)
        Assert.Equal(value, typeof(SignalREvents).GetField(constant)?.GetValue(null));
    }

    [Theory]
    [InlineData(nameof(SignalREvents.QueueChangeAdded), "ADDED")]
    [InlineData(nameof(SignalREvents.QueueChangeRemoved), "REMOVED")]
    [InlineData(nameof(SignalREvents.QueueReasonNew), "NEW")]
    [InlineData(nameof(SignalREvents.QueueReasonApproved), "APPROVED")]
    [InlineData(nameof(SignalREvents.QueueReasonRejected), "REJECTED")]
    public void QueueChangeValues_MatchTheApiDoc(string constant, string value)
    {
        Assert.Equal(value, typeof(SignalREvents).GetField(constant)?.GetValue(null));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-18")]
    public void ApprovalResolvedPayload_HasExactlyTheApiDocFields()
    {
        // DEPOSIT_APPROVAL_API 6: không số tiền, SĐT, email, lý do, decisionId, customerId (khách nhận qua nhóm user_)
        var names = typeof(BookingApprovalResolvedNotificationDto).GetProperties().Select(p => p.Name).Order();

        Assert.Equal(
            new[] { "ApprovalResult", "BookingCode", "BookingId", "BranchId", "Message", "Status", "Timestamp" },
            names);
    }

    [Theory]
    [InlineData(typeof(BookingNotificationDto))]
    [InlineData(typeof(BookingPendingApprovalNotificationDto))]
    [InlineData(typeof(PendingApprovalQueueChangedNotificationDto))]
    [InlineData(typeof(BookingApprovalResolvedNotificationDto))]
    public void BookingPayloads_CarryNoMoneyApprovalReasonsOrTadssFields(Type payload)
    {
        string[] forbidden =
        [
            "Amount", "Deposit", "Remaining", "FinalTotal", "ApprovalReason", "Decision", "Strategy", "Fallback", "Execution",
            "Phone", "Email", "Note", "PaymentUrl", "Resolution"
        ];

        var leaks = payload.GetProperties()
            .Where(p => forbidden.Any(f => p.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name);

        Assert.Empty(leaks);
    }
}
