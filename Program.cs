using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;
using System.Reflection;
using System.Text;


var builder = WebApplication.CreateBuilder(args);

// Database 
var dataSourceBuilder = new NpgsqlDataSourceBuilder(
    builder.Configuration.GetConnectionString("DefaultConnection"));

// Tự động map enum C# sang enum type của PostgreSQL
dataSourceBuilder.MapEnum<UserRole>("user_role");
dataSourceBuilder.MapEnum<UserStatus>("user_status");
dataSourceBuilder.MapEnum<OtpType>("otp_type");
dataSourceBuilder.MapEnum<BranchStatus>("branch_status");
dataSourceBuilder.MapEnum<CourtTypeStatus>("court_type_status");
dataSourceBuilder.MapEnum<CourtStatus>("court_status");
dataSourceBuilder.MapEnum<UserBranchRole>("user_branch_role");
dataSourceBuilder.MapEnum<DayType>("day_type");
dataSourceBuilder.MapEnum<ServiceStatus>("service_status");
dataSourceBuilder.MapEnum<BranchServiceStatus>("branch_service_status");
dataSourceBuilder.MapEnum<LoyaltyTransactionType>("loyalty_transaction_type");
dataSourceBuilder.MapEnum<PromotionStatus>("promotion_status");
dataSourceBuilder.MapEnum<BookingStatus>("booking_status");
dataSourceBuilder.MapEnum<BookingSource>("booking_source");
dataSourceBuilder.MapEnum<CancelSourceEnum>("cancel_source_enum");
dataSourceBuilder.MapEnum<InvoicePaymentStatus>("invoice_payment_status");
dataSourceBuilder.MapEnum<PaymentTxStatus>("payment_tx_status");
dataSourceBuilder.MapEnum<PaymentTxMethod>("payment_tx_method");
dataSourceBuilder.MapEnum<RefundStatus>("refund_status");
dataSourceBuilder.MapEnum<IpnProvider>("ipn_provider");

var dataSource = dataSourceBuilder.Build();

// Đăng ký DbContext
builder.Services.AddDbContext<SmashCourtContext>(options =>
    options.UseNpgsql(dataSource));


// Controllers
builder.Services.AddControllers();

// Đăng ký DI cho Repositories và Services
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IOtpRepository, OtpRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<OtpService>();
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<CookieSettings>(builder.Configuration.GetSection("Cookie"));
builder.Services.AddScoped<CookieHelper>();

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
        });

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
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";

            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                message = "Invalid token",
                detail = context.Exception.Message
            });

            return context.Response.WriteAsync(result);
        },

        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";

            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                message = "Unauthorized",
                detail = "Bạn chưa xác thực hoặc thiếu token !"
            });

            return context.Response.WriteAsync(result);
        },

        OnForbidden = context =>
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";

            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                message = "Forbidden",
                detail = "Bạn không có quyền truy cập vào tài nguyên này !"
            });

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

app.UseCors("AllowFrontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

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

app.Run();