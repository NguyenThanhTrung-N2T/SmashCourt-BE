using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.TestData;

internal static class TestUserFactory
{
    public static User CreateActiveUser(
        UserRole role = UserRole.CUSTOMER,
        string? email = null,
        string? password = null)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email ?? TestConstants.TestEmail,
            FullName = TestConstants.TestFullName,
            Role = role,
            Status = UserStatus.ACTIVE,
            IsEmailVerified = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password ?? TestConstants.CorrectPassword),
            CreatedAt = DateTime.UtcNow
        };
    }

    public static User CreateUnverifiedUser(
        string? email = null,
        string? password = null)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email ?? TestConstants.TestEmail,
            FullName = TestConstants.TestFullName,
            Role = UserRole.CUSTOMER,
            Status = UserStatus.ACTIVE,
            IsEmailVerified = false,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password ?? TestConstants.CorrectPassword),
            CreatedAt = DateTime.UtcNow
        };
    }

    public static User CreateLockedUser(
        int lockDurationMinutes = TestConstants.AccountLockDurationMinutes,
        string? email = null)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email ?? TestConstants.TestEmail,
            FullName = TestConstants.TestFullName,
            Role = UserRole.CUSTOMER,
            Status = UserStatus.ACTIVE,
            IsEmailVerified = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(TestConstants.CorrectPassword),
            FailedLoginCount = 0,
            LockedUntil = DateTime.UtcNow.AddMinutes(lockDurationMinutes),
            CreatedAt = DateTime.UtcNow
        };
    }

    public static User CreateOAuthUser(
        string? email = null,
        UserRole role = UserRole.CUSTOMER)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email ?? TestConstants.TestEmail,
            FullName = TestConstants.TestFullName,
            Role = role,
            Status = UserStatus.ACTIVE,
            IsEmailVerified = true,
            PasswordHash = null, // OAuth users don't have password
            CreatedAt = DateTime.UtcNow
        };
    }

    public static User CreateUserWithMustChangePassword(
        string? email = null)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email ?? TestConstants.TestEmail,
            FullName = TestConstants.TestFullName,
            Role = UserRole.CUSTOMER,
            Status = UserStatus.ACTIVE,
            IsEmailVerified = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(TestConstants.CorrectPassword),
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static User CreateUserWith2FA(
        string? email = null)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email ?? TestConstants.TestEmail,
            FullName = TestConstants.TestFullName,
            Role = UserRole.CUSTOMER,
            Status = UserStatus.ACTIVE,
            IsEmailVerified = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(TestConstants.CorrectPassword),
            Is2faEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
    }
}
