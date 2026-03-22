using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = null!;
        public string? PasswordHash { get; set; }
        public string FullName { get; set; } = null!;
        public string? Phone { get; set; }
        public string? AvatarUrl { get; set; }
        public UserRole Role { get; set; } = UserRole.CUSTOMER;
        public UserStatus Status { get; set; } = UserStatus.ACTIVE;
        public string? LockReason { get; set; }
        public bool IsEmailVerified { get; set; } = false;
        public bool Is2faEnabled { get; set; } = false;
        public bool MustChangePassword { get; set; } = false;
        public int FailedLoginCount { get; set; } = 0;
        public DateTime? LockedUntil { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }


        // Navigation
        public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
        public ICollection<OAuthAccount> OAuthAccounts { get; set; } = [];
        public ICollection<OtpCode> OtpCodes { get; set; } = [];
        public ICollection<UserBranch> UserBranches { get; set; } = [];
        public ICollection<Booking> Bookings { get; set; } = [];
        public ICollection<Booking> CreatedBookings { get; set; } = [];
        public ICollection<Booking> CancelledBookings { get; set; } = [];
        public CustomerLoyalty? CustomerLoyalty { get; set; }
        public ICollection<LoyaltyTransaction> LoyaltyTransactions { get; set; } = [];
        public ICollection<Refund> ProcessedRefunds { get; set; } = [];
    }
}
