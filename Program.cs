using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Data;
using SmashCourt_BE.DTOs.Auth;
using SmashCourt_BE.Extensions;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Middlewares;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;
using SmashCourt_BE.Repositories.Interfaces;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.Interfaces;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Utils;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Text;


var builder = WebApplication.CreateBuilder(args);

// Database 
var dataSourceBuilder = new NpgsqlDataSourceBuilder(
    builder.Configuration.GetConnectionString("DefaultConnection"));

// Sử dụng UpperCaseEnumTranslator để giữ nguyên tên enum khi map sang PostgreSQL
var translator = new UpperCaseEnumTranslator();

// Tự động map enum C# sang enum type của PostgreSQL
dataSourceBuilder.MapEnum<UserRole>("user_role", translator);
dataSourceBuilder.MapEnum<UserStatus>("user_status", translator);
dataSourceBuilder.MapEnum<OtpType>("otp_type", translator);
dataSourceBuilder.MapEnum<BranchStatus>("branch_status", translator);
dataSourceBuilder.MapEnum<CourtTypeStatus>("court_type_status", translator);
dataSourceBuilder.MapEnum<CourtStatus>("court_status", translator);
dataSourceBuilder.MapEnum<UserBranchRole>("user_branch_role", translator);
dataSourceBuilder.MapEnum<DayType>("day_type", translator);
dataSourceBuilder.MapEnum<ServiceStatus>("service_status", translator);
dataSourceBuilder.MapEnum<BranchServiceStatus>("branch_service_status", translator);
dataSourceBuilder.MapEnum<LoyaltyTransactionType>("loyalty_transaction_type", translator);
dataSourceBuilder.MapEnum<PromotionStatus>("promotion_status", translator);
dataSourceBuilder.MapEnum<BookingStatus>("booking_status", translator);
dataSourceBuilder.MapEnum<BookingSource>("booking_source", translator);
dataSourceBuilder.MapEnum<CancelSourceEnum>("cancel_source_enum", translator);
dataSourceBuilder.MapEnum<InvoicePaymentStatus>("invoice_payment_status", translator);
dataSourceBuilder.MapEnum<PaymentTxStatus>("payment_tx_status", translator);
dataSourceBuilder.MapEnum<PaymentTxMethod>("payment_tx_method", translator);
dataSourceBuilder.MapEnum<RefundStatus>("refund_status", translator);
dataSourceBuilder.MapEnum<IpnProvider>("ipn_provider", translator);

var dataSource = dataSourceBuilder.Build();

// Đăng ký DbContext
builder.Services.AddDbContext<SmashCourtContext>(options =>
    options.UseNpgsql(dataSource));
// Đăng ký Hangfire
builder.Services.AddHangfireServices(builder.Configuration);

// Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });

// Option dùng cho các middleware trả về WriteAsJsonAsync
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

// Đăng ký DI cho Repositories và Services
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IOtpRepository, OtpRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<ILoyaltyTierService, LoyaltyTierService>();
builder.Services.AddScoped<ILoyaltyTierRepository, LoyaltyTierRepository>();
builder.Services.AddScoped<IGoogleAuthService, GoogleAuthService>();
builder.Services.AddScoped<IOAuthAccountRepository, OAuthAccountRepository>();
builder.Services.AddScoped<ILoyaltyService, LoyaltyService>();
builder.Services.AddScoped<ICustomerLoyaltyRepository, CustomerLoyaltyRepository>();
builder.Services.AddScoped<ILoyaltyTransactionRepository, LoyaltyTransactionRepository>();
builder.Services.AddScoped<ICancelPolicyRepository, CancelPolicyRepository>();
builder.Services.AddScoped<ICancelPolicyService, CancelPolicyService>();
builder.Services.AddScoped<ICourtTypeService, CourtTypeService>();
builder.Services.AddScoped<ICourtTypeRepository, CourtTypeRepository>();

builder.Services.AddScoped<EmailService>();


builder.Services.AddScoped<OtpService>();
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<CookieSettings>(builder.Configuration.GetSection("Cookie"));
builder.Services.AddScoped<CookieHelper>();
builder.Services.Configure<GoogleSettings>(
    builder.Configuration.GetSection("Google"));

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();



// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Rate limiting - 100 requests/minute
builder.Services.AddRateLimiter(options =>
{
    // Login - rất chặt
    options.AddFixedWindowLimiter("login", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    // Booking / Payment
    options.AddFixedWindowLimiter("booking", opt =>
    {
        opt.PermitLimit = 20;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    // Upload / Email
    options.AddFixedWindowLimiter("sensitive", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    options.RejectionStatusCode = 429;

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";

        var response = System.Text.Json.JsonSerializer.Serialize(new
        {
            message = "Too many requests",
            detail = "Bạn gửi quá nhiều request, vui lòng thử lại sau"
        }, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        await context.HttpContext.Response.WriteAsync(response, token);
    };
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Bearer {token}",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

// JWT
var jwtSettings = builder.Configuration.GetSection("Jwt");
var secretKey = jwtSettings["Key"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,

        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],

        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(secretKey!)
        ),

        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            context.NoResult();

            if (context.Response.HasStarted)
            {
                return Task.CompletedTask;
            }

            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";

            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                message = "Invalid token",
                detail = context.Exception?.Message ?? "Token không hợp lệ"
            }, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

            return context.Response.WriteAsync(result);
        },

        OnChallenge = context =>
        {
            context.HandleResponse();

            if (context.Response.HasStarted)
            {
                return Task.CompletedTask;
            }

            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";

            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                message = "Unauthorized",
                detail = "Bạn chưa xác thực hoặc thiếu token !"
            }, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

            return context.Response.WriteAsync(result);
        },

        OnForbidden = context =>
        {
            if (context.Response.HasStarted)
            {
                return Task.CompletedTask;
            }

            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";

            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                message = "Forbidden",
                detail = "Bạn không có quyền truy cập vào tài nguyên này !"
            }, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

            return context.Response.WriteAsync(result);
        }
    };

});

// Authorization
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Sử dụng CORS policy đã định nghĩa
app.UseCors("AllowFrontend");
// Thêm middleware rate limiting toàn cục — có thể override bằng attribute ở controller/action
app.UseRateLimiter();
// Middleware xử lý lỗi toàn cục — trả về JSON chuẩn
app.UseMiddleware<ExceptionMiddleware>();
// Clear default claim type mapping để giữ nguyên tên claim trong token
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
// Xác thực và phân quyền
app.UseAuthentication();
app.UseAuthorization();
// Hangfire dashboard — chỉ cho phép admin xem
app.UseHangfireServices(app.Configuration);

app.MapControllers();

// Kiểm tra kết nối database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SmashCourtContext>();
    try
    {
        await db.Database.CanConnectAsync();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✓ Database connected successfully");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"✗ Database connection failed: {ex.Message}");
        Console.ResetColor();
    }
}

// Thông báo URL của Hangfire dashboard
Console.WriteLine("http://localhost:5179/hangfire - Dashboard Hangfire (admin only)");

app.Run();