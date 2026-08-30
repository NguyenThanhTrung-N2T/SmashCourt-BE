using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Hubs;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Services;

public class BroadcastServiceTests
{
    [Fact]
    public async Task BroadcastBookingEventAsync_SignalRFailureIsSwallowed()
    {
        var service = new BroadcastService(Mock.Of<IHubContext<NotificationHub>>(), Mock.Of<ILogger<BroadcastService>>());

        await service.BroadcastBookingEventAsync("booking.updated", new BookingNotificationDto(), null!);
    }
}
