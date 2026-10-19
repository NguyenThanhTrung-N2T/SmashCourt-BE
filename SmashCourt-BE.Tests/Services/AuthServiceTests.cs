using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Auth;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Services;

[Trait("Category", TestCategories.Auth)]
[Trait("Category", TestCategories.Security)]
public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IOtpRepository> _otps = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<ICustomerLoyaltyRepository> _loyalty = new();
    private readonly Mock<ILoyaltyTierRepository> _tiers = new();

    [Fact]
    public async Task LoginAsync_WhenEmailDoesNotExist_ThrowsUnauthorized()
    {
        _users.Setup(x => x.GetUserByEmailAsync("missing@example.com"))
            .ReturnsAsync((User?)null);

        var exception = await Assert.ThrowsAsync<AppException>(() => CreateService().LoginAsync(
            new LoginDto { Email = " Missing@Example.com ", Password = TestConstants.WrongPassword }));

        exception.ShouldBeAppException(401, ErrorCodes.Unauthorized, "Email hoặc mật khẩu không đúng");
    }

    [Fact]
    public async Task LoginAsync_WhenEmailHasWhitespaceAndDifferentCase_NormalizesAndFindsUser()
    {
        var user = TestDataFactory.CreateUser(
            email: TestConstants.TestEmail,
            passwordHash: BCrypt.Net.BCrypt.HashPassword(TestConstants.CorrectPassword));
        
        _users.Setup(x => x.GetUserByEmailAsync(TestConstants.TestEmail))
            .ReturnsAsync(user);
        _tokens.Setup(x => x.GenerateAccessToken(user)).Returns("access-token");
        _tokens.Setup(x => x.GenerateRefreshToken()).Returns("refresh-token");

        var result = await CreateService().LoginAsync(
            new LoginDto { Email = TestConstants.TestEmailWithSpaces, Password = TestConstants.CorrectPassword });

        Assert.Equal("Success", result.Status);
        _users.Verify(x => x.GetUserByEmailAsync(TestConstants.TestEmail), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WhenOAuthAccount_ThrowsBadRequest()
    {
        var user = TestUserFactory.CreateOAuthUser();
        _users.Setup(x => x.GetUserByEmailAsync(user.Email)).ReturnsAsync(user);

        var exception = await Assert.ThrowsAsync<AppException>(() => CreateService().LoginAsync(
            new LoginDto { Email = user.Email, Password = TestConstants.CorrectPassword }));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest);
        Assert.Contains("Google", exception.Message);
    }

    [Fact]
    public async Task LoginAsync_WhenEmailNotVerified_ThrowsForbidden()
    {
        var user = TestUserFactory.CreateUnverifiedUser();
        _users.Setup(x => x.GetUserByEmailAsync(user.Email)).ReturnsAsync(user);

        var exception = await Assert.ThrowsAsync<AppException>(() => CreateService().LoginAsync(
            new LoginDto { Email = user.Email, Password = TestConstants.WrongPassword }));

        exception.ShouldBeAppException(403, ErrorCodes.EmailNotVerified, "xác thực email");
        _users.Verify(x => x.UpdateUserAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenAccountLockedTemporarily_ThrowsAccountLocked()
    {
        var user = TestUserFactory.CreateLockedUser(lockDurationMinutes: 10);
        _users.Setup(x => x.GetUserByEmailAsync(user.Email)).ReturnsAsync(user);

        var exception = await Assert.ThrowsAsync<AppException>(() => CreateService().LoginAsync(
            new LoginDto { Email = user.Email, Password = TestConstants.CorrectPassword }));

        exception.ShouldBeAppException(403, ErrorCodes.AccountLocked);
        Assert.Contains("tạm khóa", exception.Message);
    }

    [Fact]
    public async Task LoginAsync_WhenAccountLockedAndTimePassed_AllowsLogin()
    {
        var user = TestDataFactory.CreateUser(
            passwordHash: BCrypt.Net.BCrypt.HashPassword(TestConstants.CorrectPassword),
            lockedUntil: DateTime.UtcNow.AddMinutes(-1)); // Lock expired 1 minute ago
        
        _users.Setup(x => x.GetUserByEmailAsync(user.Email)).ReturnsAsync(user);
        _tokens.Setup(x => x.GenerateAccessToken(user)).Returns("access-token");
        _tokens.Setup(x => x.GenerateRefreshToken()).Returns("refresh-token");

        var result = await CreateService().LoginAsync(
            new LoginDto { Email = user.Email, Password = TestConstants.CorrectPassword });

        Assert.Equal("Success", result.Status);
    }

    [Fact]
    public async Task LoginAsync_WhenWrongPasswordFirstTime_IncrementsFailureCount()
    {
        var user = TestDataFactory.CreateUser(
            passwordHash: BCrypt.Net.BCrypt.HashPassword(TestConstants.CorrectPassword),
            failedLoginCount: 0);
        
        _users.Setup(x => x.GetUserByEmailAsync(user.Email)).ReturnsAsync(user);

        var exception = await Assert.ThrowsAsync<AppException>(() => CreateService().LoginAsync(
            new LoginDto { Email = user.Email, Password = TestConstants.WrongPassword }));

        exception.ShouldBeAppException(401, ErrorCodes.Unauthorized);
        Assert.Equal(1, user.FailedLoginCount);
        _users.Verify(x => x.UpdateUserAsync(It.Is<User>(u => u.FailedLoginCount == 1)), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WhenWrongPasswordFifthTime_LocksAccount()
    {
        var user = TestDataFactory.CreateUser(
            passwordHash: BCrypt.Net.BCrypt.HashPassword(TestConstants.CorrectPassword),
            failedLoginCount: 4);
        
        _users.Setup(x => x.GetUserByEmailAsync(user.Email)).ReturnsAsync(user);

        var exception = await Assert.ThrowsAsync<AppException>(() => CreateService().LoginAsync(
            new LoginDto { Email = user.Email, Password = TestConstants.WrongPassword }));

        exception.ShouldBeAppException(403, ErrorCodes.AccountLocked);
        Assert.NotNull(user.LockedUntil);
        Assert.True(user.LockedUntil > DateTime.UtcNow.AddMinutes(TestConstants.AccountLockDurationMinutes - 1));
        Assert.Equal(0, user.FailedLoginCount);
        _users.Verify(x => x.UpdateUserAsync(user), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WhenCorrectPasswordAfterFailures_ResetsFailureCount()
    {
        var user = TestDataFactory.CreateUser(
            passwordHash: BCrypt.Net.BCrypt.HashPassword(TestConstants.CorrectPassword),
            failedLoginCount: 3);
        
        _users.Setup(x => x.GetUserByEmailAsync(user.Email)).ReturnsAsync(user);
        _tokens.Setup(x => x.GenerateRefreshToken()).Returns("refresh-token");
        _tokens.Setup(x => x.GenerateAccessToken(user)).Returns("access-token");

        var result = await CreateService().LoginAsync(
            new LoginDto { Email = user.Email, Password = TestConstants.CorrectPassword });

        Assert.Equal("Success", result.Status);
        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("refresh-token", result.RefreshToken);
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockedUntil);
        _users.Verify(x => x.UpdateUserAsync(It.Is<User>(u => 
            u.FailedLoginCount == 0 && 
            u.LockedUntil == null)), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WhenSuccessful_RevokesOldRefreshTokens()
    {
        var user = TestDataFactory.CreateUser(
            passwordHash: BCrypt.Net.BCrypt.HashPassword(TestConstants.CorrectPassword));
        
        _users.Setup(x => x.GetUserByEmailAsync(user.Email)).ReturnsAsync(user);
        _tokens.Setup(x => x.GenerateRefreshToken()).Returns("refresh-token");
        _tokens.Setup(x => x.GenerateAccessToken(user)).Returns("access-token");

        await CreateService().LoginAsync(
            new LoginDto { Email = user.Email, Password = TestConstants.CorrectPassword });

        _refreshTokens.Verify(x => x.RevokeAllByUserIdAsync(user.Id), Times.Once);
        _refreshTokens.Verify(x => x.CreateAsync(It.Is<RefreshToken>(t => 
            t.UserId == user.Id && 
            t.TokenHash != null)), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WhenMustChangePassword_ReturnsTempTokenWithoutStarting2FA()
    {
        var user = TestDataFactory.CreateUser(
            passwordHash: BCrypt.Net.BCrypt.HashPassword(TestConstants.CorrectPassword),
            mustChangePassword: true,
            is2FAEnabled: true);
        _users.Setup(x => x.GetUserByEmailAsync(user.Email)).ReturnsAsync(user);
        _tokens.Setup(x => x.GenerateTempToken(user.Id, "change_password_temp"))
            .Returns("change-password-token");

        var result = await CreateService().LoginAsync(new LoginDto
        {
            Email = user.Email,
            Password = TestConstants.CorrectPassword
        });

        Assert.Equal("must_change_password", result.Status);
        Assert.Equal("change-password-token", result.TempToken);
        _otps.Verify(x => x.InvalidateAllOtpAsync(It.IsAny<Guid>(), OtpType.TWO_FA), Times.Never);
        _refreshTokens.Verify(x => x.CreateAsync(It.IsAny<RefreshToken>()), Times.Never);
    }

    [Fact]
    public async Task Login2FAAsync_WhenTempTokenIsInvalid_ThrowsTokenInvalidWithoutLoadingUser()
    {
        _tokens.Setup(x => x.ValidateTempToken("expired-temp", "2fa_temp"))
            .Returns((Guid?)null);

        var exception = await Assert.ThrowsAsync<AppException>(() => CreateService().Login2FAAsync(
            new Login2FADto { TempToken = "expired-temp", OtpCode = TestConstants.ValidOtpCode }));

        exception.ShouldBeAppException(401, ErrorCodes.TokenInvalid);
        _users.Verify(x => x.GetUserByIdAsync(It.IsAny<Guid>()), Times.Never);
        _otps.Verify(x => x.GetLatestActiveOtpAsync(It.IsAny<Guid>(), OtpType.TWO_FA), Times.Never);
    }

    [Fact]
    public async Task Login2FAAsync_WhenOtpAttemptsAreExhausted_InvalidatesOtpAndThrowsLimitExceeded()
    {
        var user = TestDataFactory.CreateUser();
        var otp = new OtpCode { UserId = user.Id, Type = OtpType.TWO_FA, AttemptCount = 3 };
        _tokens.Setup(x => x.ValidateTempToken("temp", "2fa_temp")).Returns(user.Id);
        _users.Setup(x => x.GetUserByIdAsync(user.Id)).ReturnsAsync(user);
        _otps.Setup(x => x.GetLatestActiveOtpAsync(user.Id, OtpType.TWO_FA)).ReturnsAsync(otp);

        var exception = await Assert.ThrowsAsync<AppException>(() => CreateService().Login2FAAsync(
            new Login2FADto { TempToken = "temp", OtpCode = TestConstants.ValidOtpCode }));

        exception.ShouldBeAppException(401, ErrorCodes.OtpLimitExceeded);
        _otps.Verify(x => x.InvalidateAllOtpAsync(user.Id, OtpType.TWO_FA), Times.Once);
        _otps.Verify(x => x.UpdateOtpAsync(It.IsAny<OtpCode>()), Times.Never);
        _refreshTokens.Verify(x => x.CreateAsync(It.IsAny<RefreshToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenTokenDoesNotExist_ThrowsTokenInvalidWithoutLoadingUser()
    {
        _refreshTokens.Setup(x => x.GetActiveByTokenHashAsync(It.IsAny<string>()))
            .ReturnsAsync((RefreshToken?)null);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            CreateService().RefreshTokenAsync("unknown-refresh-token"));

        exception.ShouldBeAppException(401, ErrorCodes.TokenInvalid);
        _users.Verify(x => x.GetUserByIdAsync(It.IsAny<Guid>()), Times.Never);
        _refreshTokens.Verify(x => x.RotateRefreshTokenAsync(
            It.IsAny<Guid>(), It.IsAny<RefreshToken>()), Times.Never);
    }

    private AuthService CreateService()
    {
        var configuration = TestConfigurationFactory.Create();
        var httpContext = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        return new AuthService(
            _users.Object,
            _otps.Object,
            new OtpService(configuration),
            new EmailService(configuration),
            Mock.Of<ILogger<AuthService>>(),
            _tokens.Object,
            _refreshTokens.Object,
            Options.Create(new JwtSettings
            {
                Key = TestConstants.JwtKey,
                Issuer = TestConstants.JwtIssuer,
                Audience = TestConstants.JwtAudience,
                AccessTokenExpirationMinutes = TestConstants.JwtAccessTokenExpirationMinutes
            }),
            _loyalty.Object,
            _tiers.Object,
            httpContext);
    }
}
