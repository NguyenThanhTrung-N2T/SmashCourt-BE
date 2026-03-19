using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
namespace SmashCourt_BE.Data
{
    public class SmashCourtContext : DbContext
    {
        public SmashCourtContext(DbContextOptions<SmashCourtContext> option) : base(option) {}

        // ── Module 1 ──────────────────────────────
        public DbSet<User> Users => Set<User>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<OAuthAccount> OAuthAccounts => Set<OAuthAccount>();
        public DbSet<OtpCode> OtpCodes => Set<OtpCode>();

        // ── Module 2 ──────────────────────────────
        public DbSet<Branch> Branches => Set<Branch>();
        public DbSet<UserBranch> UserBranches => Set<UserBranch>();
        public DbSet<CourtType> CourtTypes => Set<CourtType>();
        public DbSet<BranchCourtType> BranchCourtTypes => Set<BranchCourtType>();
        public DbSet<Court> Courts => Set<Court>();

        // ── Module 3 ──────────────────────────────
        public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();
        public DbSet<SystemPrice> SystemPrices => Set<SystemPrice>();
        public DbSet<BranchPriceOverride> BranchPriceOverrides => Set<BranchPriceOverride>();
        public DbSet<CancelPolicy> CancelPolicies => Set<CancelPolicy>();

        // ── Module 4 ──────────────────────────────
        public DbSet<Service> Services => Set<Service>();
        public DbSet<BranchService> BranchServices => Set<BranchService>();

        // ── Module 5 ──────────────────────────────
        public DbSet<LoyaltyTier> LoyaltyTiers => Set<LoyaltyTier>();
        public DbSet<CustomerLoyalty> CustomerLoyalties => Set<CustomerLoyalty>();
        public DbSet<LoyaltyTransaction> LoyaltyTransactions => Set<LoyaltyTransaction>();

        // ── Module 6 ──────────────────────────────
        public DbSet<Promotion> Promotions => Set<Promotion>();

        // ── Module 7 ──────────────────────────────
        public DbSet<Booking> Bookings => Set<Booking>();
        public DbSet<BookingCourt> BookingCourts => Set<BookingCourt>();
        public DbSet<BookingPriceItem> BookingPriceItems => Set<BookingPriceItem>();
        public DbSet<BookingService> BookingServices => Set<BookingService>();
        public DbSet<SlotLock> SlotLocks => Set<SlotLock>();
        public DbSet<BookingPromotion> BookingPromotions => Set<BookingPromotion>();

        // ── Module 8 ──────────────────────────────
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<PaymentIpnLog> PaymentIpnLogs => Set<PaymentIpnLog>();
        public DbSet<Refund> Refunds => Set<Refund>();


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Map tat ca enum sang PortgreSQL enum type
            modelBuilder.HasPostgresEnum<UserRole>("user_role");
            modelBuilder.HasPostgresEnum<UserStatus>("user_status");
            modelBuilder.HasPostgresEnum<OtpType>("otp_type");
            modelBuilder.HasPostgresEnum<BranchStatus>("branch_status");
            modelBuilder.HasPostgresEnum<CourtTypeStatus>("court_type_status");
            modelBuilder.HasPostgresEnum<CourtStatus>("court_status");
            modelBuilder.HasPostgresEnum<UserBranchRole>("user_branch_role");
            modelBuilder.HasPostgresEnum<DayType>("day_type");
            modelBuilder.HasPostgresEnum<ServiceStatus>("service_status");
            modelBuilder.HasPostgresEnum<BranchServiceStatus>("branch_service_status");
            modelBuilder.HasPostgresEnum<LoyaltyTransactionType>("loyalty_transaction_type");
            modelBuilder.HasPostgresEnum<PromotionStatus>("promotion_status");
            modelBuilder.HasPostgresEnum<BookingStatus>("booking_status");
            modelBuilder.HasPostgresEnum<BookingSource>("booking_source");
            modelBuilder.HasPostgresEnum<CancelSourceEnum>("cancel_source_enum");
            modelBuilder.HasPostgresEnum<InvoicePaymentStatus>("invoice_payment_status");
            modelBuilder.HasPostgresEnum<PaymentTxStatus>("payment_tx_status");
            modelBuilder.HasPostgresEnum<PaymentTxMethod>("payment_tx_method");
            modelBuilder.HasPostgresEnum<RefundStatus>("refund_status");
            modelBuilder.HasPostgresEnum<IpnProvider>("ipn_provider");

            // Apply configurations từng entity
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(SmashCourtContext).Assembly);


            // ── MODULE 1 ──────────────────────────
            modelBuilder.Entity<User>(e =>
            {
                e.ToTable("users");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
                e.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(255);
                e.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(255).IsRequired();
                e.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(20);
                e.Property(x => x.AvatarUrl).HasColumnName("avatar_url");
                e.Property(x => x.Role).HasColumnName("role");
                e.Property(x => x.Status).HasColumnName("status");
                e.Property(x => x.LockReason).HasColumnName("lock_reason");
                e.Property(x => x.IsEmailVerified).HasColumnName("is_email_verified").HasDefaultValue(false);
                e.Property(x => x.Is2faEnabled).HasColumnName("is_2fa_enabled").HasDefaultValue(false);
                e.Property(x => x.MustChangePassword).HasColumnName("must_change_password").HasDefaultValue(false);
                e.Property(x => x.FailedLoginCount).HasColumnName("failed_login_count").HasDefaultValue(0);
                e.Property(x => x.LockedUntil).HasColumnName("locked_until");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

                // Relationships
                e.HasMany(x => x.RefreshTokens).WithOne(x => x.User).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.OAuthAccounts).WithOne(x => x.User).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.OtpCodes).WithOne(x => x.User).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.UserBranches).WithOne(x => x.User).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
                e.HasMany(x => x.Bookings).WithOne(x => x.Customer).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                e.HasMany(x => x.CreatedBookings).WithOne(x => x.CreatedByUser).HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
                e.HasMany(x => x.CancelledBookings).WithOne(x => x.CancelledByUser).HasForeignKey(x => x.CancelledBy).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.CustomerLoyalty).WithOne(x => x.User).HasForeignKey<CustomerLoyalty>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
                e.HasMany(x => x.LoyaltyTransactions).WithOne(x => x.User).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
                e.HasMany(x => x.ProcessedRefunds).WithOne(x => x.ProcessedByUser).HasForeignKey(x => x.ProcessedBy).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<RefreshToken>(e =>
            {
                e.ToTable("refresh_tokens");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(255).IsRequired();
                e.Property(x => x.RotatedFromId).HasColumnName("rotated_from_id");
                e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
                e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

                e.HasOne(x => x.RotatedFrom).WithMany().HasForeignKey(x => x.RotatedFromId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<OAuthAccount>(e =>
            {
                e.ToTable("oauth_accounts");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(20).IsRequired();
                e.Property(x => x.ProviderUserId).HasColumnName("provider_user_id").HasMaxLength(255).IsRequired();
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

                e.HasIndex(x => new { x.Provider, x.ProviderUserId }).IsUnique();
            });

            modelBuilder.Entity<OtpCode>(e =>
            {
                e.ToTable("otp_codes");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.Type).HasColumnName("type");
                e.Property(x => x.CodeHash).HasColumnName("code_hash").HasMaxLength(255).IsRequired();
                e.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
                e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
                e.Property(x => x.UsedAt).HasColumnName("used_at");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            });

            // ── MODULE 2 ──────────────────────────
            modelBuilder.Entity<Branch>(e =>
            {
                e.ToTable("branches");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
                e.Property(x => x.Address).HasColumnName("address").IsRequired();
                e.Property(x => x.Latitude).HasColumnName("latitude").HasPrecision(10, 7);
                e.Property(x => x.Longitude).HasColumnName("longitude").HasPrecision(10, 7);
                e.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(20);
                e.Property(x => x.AvatarUrl).HasColumnName("avatar_url");
                e.Property(x => x.OpenTime).HasColumnName("open_time");
                e.Property(x => x.CloseTime).HasColumnName("close_time");
                e.Property(x => x.Status).HasColumnName("status");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            });

            modelBuilder.Entity<UserBranch>(e =>
            {
                e.ToTable("user_branches");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.BranchId).HasColumnName("branch_id");
                e.Property(x => x.Role).HasColumnName("role");
                e.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
                e.Property(x => x.AssignedAt).HasColumnName("assigned_at").HasDefaultValueSql("now()");
                e.Property(x => x.EndedAt).HasColumnName("ended_at");

                e.HasOne(x => x.User).WithMany(x => x.UserBranches).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Branch).WithMany(x => x.UserBranches).HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CourtType>(e =>
            {
                e.ToTable("court_types");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
                e.Property(x => x.Description).HasColumnName("description");
                e.Property(x => x.Status).HasColumnName("status");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            });

            modelBuilder.Entity<BranchCourtType>(e =>
            {
                e.ToTable("branch_court_types");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.BranchId).HasColumnName("branch_id");
                e.Property(x => x.CourtTypeId).HasColumnName("court_type_id");
                e.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

                e.HasIndex(x => new { x.BranchId, x.CourtTypeId }).IsUnique();
                e.HasOne(x => x.Branch).WithMany(x => x.BranchCourtTypes).HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.CourtType).WithMany(x => x.BranchCourtTypes).HasForeignKey(x => x.CourtTypeId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Court>(e =>
            {
                e.ToTable("courts");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.BranchId).HasColumnName("branch_id");
                e.Property(x => x.CourtTypeId).HasColumnName("court_type_id");
                e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
                e.Property(x => x.Description).HasColumnName("description");
                e.Property(x => x.AvatarUrl).HasColumnName("avatar_url");
                e.Property(x => x.Status).HasColumnName("status");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

                e.HasIndex(x => new { x.BranchId, x.Name }).IsUnique();
                e.HasOne(x => x.Branch).WithMany(x => x.Courts).HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.CourtType).WithMany(x => x.Courts).HasForeignKey(x => x.CourtTypeId).OnDelete(DeleteBehavior.Restrict);
            });

            // ── MODULE 3 ──────────────────────────
            modelBuilder.Entity<TimeSlot>(e =>
            {
                e.ToTable("time_slots");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.StartTime).HasColumnName("start_time");
                e.Property(x => x.EndTime).HasColumnName("end_time");
                e.Property(x => x.DayType).HasColumnName("day_type");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

                e.HasIndex(x => new { x.StartTime, x.EndTime, x.DayType }).IsUnique();
            });

            modelBuilder.Entity<SystemPrice>(e =>
            {
                e.ToTable("system_prices");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.CourtTypeId).HasColumnName("court_type_id");
                e.Property(x => x.TimeSlotId).HasColumnName("time_slot_id");
                e.Property(x => x.Price).HasColumnName("price").HasPrecision(12, 2);
                e.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

                e.HasIndex(x => new { x.CourtTypeId, x.TimeSlotId, x.EffectiveFrom }).IsUnique();
                e.HasOne(x => x.CourtType).WithMany(x => x.SystemPrices).HasForeignKey(x => x.CourtTypeId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.TimeSlot).WithMany(x => x.SystemPrices).HasForeignKey(x => x.TimeSlotId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<BranchPriceOverride>(e =>
            {
                e.ToTable("branch_price_overrides");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.BranchId).HasColumnName("branch_id");
                e.Property(x => x.CourtTypeId).HasColumnName("court_type_id");
                e.Property(x => x.TimeSlotId).HasColumnName("time_slot_id");
                e.Property(x => x.Price).HasColumnName("price").HasPrecision(12, 2);
                e.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

                e.HasIndex(x => new { x.BranchId, x.CourtTypeId, x.TimeSlotId, x.EffectiveFrom }).IsUnique();
                e.HasOne(x => x.Branch).WithMany(x => x.BranchPriceOverrides).HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.CourtType).WithMany(x => x.BranchPriceOverrides).HasForeignKey(x => x.CourtTypeId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.TimeSlot).WithMany(x => x.BranchPriceOverrides).HasForeignKey(x => x.TimeSlotId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CancelPolicy>(e =>
            {
                e.ToTable("cancel_policies");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.HoursBefore).HasColumnName("hours_before");
                e.Property(x => x.RefundPercent).HasColumnName("refund_percent").HasPrecision(5, 2);
                e.Property(x => x.Description).HasColumnName("description");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

                e.HasIndex(x => x.HoursBefore).IsUnique();
            });

            // ── MODULE 4 ──────────────────────────
            modelBuilder.Entity<Service>(e =>
            {
                e.ToTable("services");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
                e.Property(x => x.Description).HasColumnName("description");
                e.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(50).IsRequired();
                e.Property(x => x.DefaultPrice).HasColumnName("default_price").HasPrecision(12, 2);
                e.Property(x => x.Status).HasColumnName("status");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            });

            modelBuilder.Entity<BranchService>(e =>
            {
                e.ToTable("branch_services");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.BranchId).HasColumnName("branch_id");
                e.Property(x => x.ServiceId).HasColumnName("service_id");
                e.Property(x => x.Price).HasColumnName("price").HasPrecision(12, 2);
                e.Property(x => x.Status).HasColumnName("status");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

                e.HasIndex(x => new { x.BranchId, x.ServiceId }).IsUnique();
                e.HasOne(x => x.Branch).WithMany(x => x.BranchServices).HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Service).WithMany(x => x.BranchServices).HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            });

            // ── MODULE 5 ──────────────────────────
            modelBuilder.Entity<LoyaltyTier>(e =>
            {
                e.ToTable("loyalty_tiers");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
                e.Property(x => x.MinPoints).HasColumnName("min_points");
                e.Property(x => x.DiscountRate).HasColumnName("discount_rate").HasPrecision(5, 2).HasDefaultValue(0m);
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

                e.HasIndex(x => x.Name).IsUnique();
                e.HasIndex(x => x.MinPoints).IsUnique();
            });

            modelBuilder.Entity<CustomerLoyalty>(e =>
            {
                e.ToTable("customer_loyalty");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.TierId).HasColumnName("tier_id");
                e.Property(x => x.TotalPoints).HasColumnName("total_points").HasDefaultValue(0);
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

                e.HasIndex(x => x.UserId).IsUnique();
                e.HasOne(x => x.Tier).WithMany(x => x.CustomerLoyalties).HasForeignKey(x => x.TierId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<LoyaltyTransaction>(e =>
            {
                e.ToTable("loyalty_transactions");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.BookingId).HasColumnName("booking_id");
                e.Property(x => x.Points).HasColumnName("points");
                e.Property(x => x.TotalPointsAfter).HasColumnName("total_points_after");
                e.Property(x => x.Type).HasColumnName("type");
                e.Property(x => x.Note).HasColumnName("note");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

                e.HasOne(x => x.Booking).WithMany(x => x.LoyaltyTransactions).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            });

            // ── MODULE 6 ──────────────────────────
            modelBuilder.Entity<Promotion>(e =>
            {
                e.ToTable("promotions");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
                e.Property(x => x.DiscountRate).HasColumnName("discount_rate").HasPrecision(5, 2);
                e.Property(x => x.StartDate).HasColumnName("start_date");
                e.Property(x => x.EndDate).HasColumnName("end_date");
                e.Property(x => x.Status).HasColumnName("status");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            });

            // ── MODULE 7 ──────────────────────────
            modelBuilder.Entity<Booking>(e =>
            {
                e.ToTable("bookings");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.BranchId).HasColumnName("branch_id");
                e.Property(x => x.CustomerId).HasColumnName("customer_id");
                e.Property(x => x.GuestName).HasColumnName("guest_name").HasMaxLength(255);
                e.Property(x => x.GuestPhone).HasColumnName("guest_phone").HasMaxLength(20);
                e.Property(x => x.GuestEmail).HasColumnName("guest_email").HasMaxLength(255);
                e.Property(x => x.BookingDate).HasColumnName("booking_date");
                e.Property(x => x.Status).HasColumnName("status");
                e.Property(x => x.Source).HasColumnName("source");
                e.Property(x => x.Note).HasColumnName("note");
                e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
                e.Property(x => x.CreatedBy).HasColumnName("created_by");
                e.Property(x => x.CancelledBy).HasColumnName("cancelled_by");
                e.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
                e.Property(x => x.CancelSource).HasColumnName("cancel_source");
                e.Property(x => x.CancelTokenHash).HasColumnName("cancel_token_hash").HasMaxLength(255);
                e.Property(x => x.CancelTokenExpiresAt).HasColumnName("cancel_token_expires_at");
                e.Property(x => x.CancelTokenUsedAt).HasColumnName("cancel_token_used_at");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

                e.HasOne(x => x.Branch).WithMany(x => x.Bookings).HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Invoice).WithOne(x => x.Booking).HasForeignKey<Invoice>(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.BookingPromotion).WithOne(x => x.Booking).HasForeignKey<BookingPromotion>(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.SlotLock).WithOne(x => x.Booking).HasForeignKey<SlotLock>(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<BookingCourt>(e =>
            {
                e.ToTable("booking_courts");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.BookingId).HasColumnName("booking_id");
                e.Property(x => x.CourtId).HasColumnName("court_id");
                e.Property(x => x.Date).HasColumnName("date");
                e.Property(x => x.StartTime).HasColumnName("start_time");
                e.Property(x => x.EndTime).HasColumnName("end_time");
                e.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

                e.HasOne(x => x.Booking).WithMany(x => x.BookingCourts).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Court).WithMany(x => x.BookingCourts).HasForeignKey(x => x.CourtId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<BookingPriceItem>(e =>
            {
                e.ToTable("booking_price_items");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.BookingCourtId).HasColumnName("booking_court_id");
                e.Property(x => x.TimeSlotId).HasColumnName("time_slot_id");
                e.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(12, 2);
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

                e.HasOne(x => x.BookingCourt).WithMany(x => x.BookingPriceItems).HasForeignKey(x => x.BookingCourtId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.TimeSlot).WithMany(x => x.BookingPriceItems).HasForeignKey(x => x.TimeSlotId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<BookingService>(e =>
            {
                e.ToTable("booking_services");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.BookingId).HasColumnName("booking_id");
                e.Property(x => x.ServiceId).HasColumnName("service_id");
                e.Property(x => x.ServiceName).HasColumnName("service_name").HasMaxLength(255).IsRequired();
                e.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(50).IsRequired();
                e.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(12, 2);
                e.Property(x => x.Quantity).HasColumnName("quantity").HasDefaultValue(1);
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

                e.HasOne(x => x.Booking).WithMany(x => x.BookingServices).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Service).WithMany(x => x.BookingServices).HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<SlotLock>(e =>
            {
                e.ToTable("slot_locks");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.CourtId).HasColumnName("court_id");
                e.Property(x => x.BookingId).HasColumnName("booking_id");
                e.Property(x => x.SessionId).HasColumnName("session_id").HasMaxLength(255);
                e.Property(x => x.Date).HasColumnName("date");
                e.Property(x => x.StartTime).HasColumnName("start_time");
                e.Property(x => x.EndTime).HasColumnName("end_time");
                e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

                e.HasOne(x => x.Court).WithMany(x => x.SlotLocks).HasForeignKey(x => x.CourtId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<BookingPromotion>(e =>
            {
                e.ToTable("booking_promotions");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.BookingId).HasColumnName("booking_id");
                e.Property(x => x.PromotionId).HasColumnName("promotion_id");
                e.Property(x => x.PromotionNameSnapshot).HasColumnName("promotion_name_snapshot").HasMaxLength(255).IsRequired();
                e.Property(x => x.DiscountRateSnapshot).HasColumnName("discount_rate_snapshot").HasPrecision(5, 2);
                e.Property(x => x.DiscountAmount).HasColumnName("discount_amount").HasPrecision(12, 2);
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

                e.HasOne(x => x.Promotion).WithMany(x => x.BookingPromotions).HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
            });

            // ── MODULE 8 ──────────────────────────
            modelBuilder.Entity<Invoice>(e =>
            {
                e.ToTable("invoices");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.BookingId).HasColumnName("booking_id");
                e.Property(x => x.CourtFee).HasColumnName("court_fee").HasPrecision(12, 2);
                e.Property(x => x.ServiceFee).HasColumnName("service_fee").HasPrecision(12, 2).HasDefaultValue(0m);
                e.Property(x => x.LoyaltyDiscountAmount).HasColumnName("loyalty_discount_amount").HasPrecision(12, 2).HasDefaultValue(0m);
                e.Property(x => x.PromotionDiscountAmount).HasColumnName("promotion_discount_amount").HasPrecision(12, 2).HasDefaultValue(0m);
                e.Property(x => x.FinalTotal).HasColumnName("final_total").HasPrecision(12, 2);
                e.Property(x => x.PaymentStatus).HasColumnName("payment_status");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            });

            modelBuilder.Entity<Payment>(e =>
            {
                e.ToTable("payments");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.InvoiceId).HasColumnName("invoice_id");
                e.Property(x => x.Method).HasColumnName("method");
                e.Property(x => x.Amount).HasColumnName("amount").HasPrecision(12, 2);
                e.Property(x => x.RefundedAmount).HasColumnName("refunded_amount").HasPrecision(12, 2).HasDefaultValue(0m);
                e.Property(x => x.Status).HasColumnName("status");
                e.Property(x => x.TransactionRef).HasColumnName("transaction_ref").HasMaxLength(255);
                e.Property(x => x.PaidAt).HasColumnName("paid_at");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

                e.HasOne(x => x.Invoice).WithMany(x => x.Payments).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PaymentIpnLog>(e =>
            {
                e.ToTable("payment_ipn_logs");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.PaymentId).HasColumnName("payment_id");
                e.Property(x => x.Provider).HasColumnName("provider");
                e.Property(x => x.ProviderTransactionId).HasColumnName("provider_transaction_id").HasMaxLength(255);
                e.Property(x => x.RawPayload).HasColumnName("raw_payload").IsRequired();
                e.Property(x => x.IsValid).HasColumnName("is_valid");
                e.Property(x => x.ProcessedAt).HasColumnName("processed_at").HasDefaultValueSql("now()");

                e.HasOne(x => x.Payment).WithMany(x => x.PaymentIpnLogs).HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Refund>(e =>
            {
                e.ToTable("refunds");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                e.Property(x => x.PaymentId).HasColumnName("payment_id");
                e.Property(x => x.Amount).HasColumnName("amount").HasPrecision(12, 2);
                e.Property(x => x.RefundPercent).HasColumnName("refund_percent").HasPrecision(5, 2);
                e.Property(x => x.Status).HasColumnName("status");
                e.Property(x => x.ProcessedBy).HasColumnName("processed_by");
                e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
                e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

                e.HasOne(x => x.Payment).WithMany(x => x.Refunds).HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
