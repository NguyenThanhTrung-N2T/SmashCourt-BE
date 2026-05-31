using Hangfire;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using System.Diagnostics;

namespace SmashCourt_BE.Jobs
{
    /// <summary>
    /// All scheduled booking maintenance jobs.
    ///
    /// Idempotency contract:
    ///   Every job filters strictly on status + time predicates, so re-running
    ///   an already-processed batch is a no-op (rows no longer match the WHERE).
    ///
    /// Concurrency contract:
    ///   Each job method carries [DisableConcurrentExecution] so Hangfire's
    ///   distributed lock prevents two app instances running the same job
    ///   simultaneously. The timeout (10 s) is intentionally short — if a lock
    ///   cannot be acquired the attempt is dropped; the next scheduled tick wins.
    ///
    /// Court-release contract:
    ///   A court is set AVAILABLE only when no other active booking still holds it.
    ///   "Active" = CONFIRMED | PAID_ONLINE | IN_PROGRESS.
    ///   The booking being processed is excluded from the busy-court check so it
    ///   does not block its own release.
    /// </summary>
    public class BookingJob : IBookingJob
    {
        // ── Dependencies ────────────────────────────────────────────────────
        private readonly SmashCourtContext _db;
        private readonly ISlotInterestRepository _slotInterestRepo;
        private readonly ILogger<BookingJob> _logger;

        // ── Constants ───────────────────────────────────────────────────────

        /// <summary>Statuses that keep a court occupied.</summary>
        private static readonly BookingStatus[] ActiveStatuses =
        [
            BookingStatus.CONFIRMED,
            BookingStatus.PAID_ONLINE,
            BookingStatus.IN_PROGRESS,
        ];

        /// <summary>
        /// Grace period before a confirmed-but-unchecked-in booking is
        /// declared NO_SHOW.
        /// </summary>
        private const int NoShowGraceMinutes = 15;

        // ── Constructor ─────────────────────────────────────────────────────
        public BookingJob(
            SmashCourtContext db,
            ISlotInterestRepository slotInterestRepo,
            ILogger<BookingJob> logger)
        {
            _db = db;
            _slotInterestRepo = slotInterestRepo;
            _logger = logger;
        }

        // ════════════════════════════════════════════════════════════════════
        // Job-01 · Cancel expired PENDING bookings  (every 1 min)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Cancels PENDING bookings whose hold window has expired.
        ///
        /// Covered cases:
        ///   • PENDING + ExpiresAt elapsed  → CANCELLED, courts released
        ///   • PENDING + ExpiresAt not yet  → skipped (not in query)
        ///   • Court held by another booking → court left as-is (busy guard)
        ///
        /// Idempotent: cancelled bookings no longer match Status == PENDING.
        /// </summary>
        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        public async Task CancelExpiredPendingBookingsAsync()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInformation("[Job-01] Starting");

            try
            {
                var now = DateTimeHelper.GetUtcNow();
                // 1. Query invoices that have expired and are still unpaid
                var expiredInvoices = await _db.Invoices
                    .Include(i => i.Booking)
                        .ThenInclude(b => b.BookingCourts)
                            .ThenInclude(bc => bc.Court)
                    .Where(i => i.PaymentStatus == InvoicePaymentStatus.UNPAID 
                             && i.ExpiresAt < now
                             && i.Booking.Status == BookingStatus.PENDING)
                    .ToListAsync();

                if (expiredInvoices.Count == 0)
                {
                    _logger.LogInformation("[Job-01] No expired PENDING bookings. Elapsed: {Ms}ms", sw.ElapsedMilliseconds);
                    return;
                }
                // Pull the booking entities out for slot processing
                var expiredBookings = expiredInvoices.Select(i => i.Booking).ToList();

                var cancelledCourtIds = expiredBookings
                    .SelectMany(b => b.BookingCourts)
                    .Select(bc => bc.CourtId)
                    .Distinct()
                    .ToHashSet();

                var cancelledBookingIds = expiredBookings
                    .Select(b => b.Id)
                    .ToHashSet();

                var busyCourtIds = await GetBusyCourtIdsAsync(cancelledCourtIds, cancelledBookingIds);

                // 2. Loop through the invoices to update both the invoice and related booking
                foreach (var invoice in expiredInvoices)
                {
                    // Update Invoice Status
                    invoice.PaymentStatus = InvoicePaymentStatus.EXPIRED;
                    invoice.UpdatedAt = now;

                    // Update Booking Status
                    var booking = invoice.Booking;
                    if (booking != null)
                    {
                        booking.Status = BookingStatus.CANCELLED;
                        booking.CancelledAt = now;
                        booking.CancelSource = CancelSourceEnum.SYSTEM;
                        booking.UpdatedAt = now;

                        ReleaseCourtSlots(booking.BookingCourts, busyCourtIds, now, deactivate: true);
                    }
                }

                // Purge any stale slot locks in the same transaction
                await _db.SlotLocks
                    .Where(sl => sl.ExpiresAt <= now)
                    .ExecuteDeleteAsync();

                await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "[Job-01] Cancelled {Count} expired PENDING bookings. Elapsed: {Ms}ms",
                    expiredBookings.Count, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job-01] Unhandled error. Elapsed: {Ms}ms", sw.ElapsedMilliseconds);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Job-02 · Finalize expired active bookings  (every 1 min)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Finalizes bookings whose court time has elapsed.
        ///
        /// Covered cases:
        /// ┌────────────────┬──────────────────────────┬────────────────────────────────┐
        /// │ Status         │ Condition                │ Outcome                        │
        /// ├────────────────┼──────────────────────────┼────────────────────────────────┤
        /// │ IN_PROGRESS    │ invoice == PAID          │ → COMPLETED                    │
        /// │ IN_PROGRESS    │ invoice != PAID          │ → PENDING_PAYMENT              │
        /// │ PAID_ONLINE    │ CheckedInAt == null      │ skipped → Job-04 (NO_SHOW)     │
        /// │ PAID_ONLINE    │ checked-in + PAID        │ → COMPLETED                    │
        /// │ PAID_ONLINE    │ checked-in + PARTIALLY   │ → PENDING_PAYMENT (intentional;|
        /// │                │                          │   service fee still due)       │
        /// │ PAID_ONLINE    │ checked-in + null invoice│ → PENDING_PAYMENT + WARNING    │
        /// │ CONFIRMED      │ CheckedInAt == null      │ skipped → Job-04 (NO_SHOW)     │
        /// │ CONFIRMED      │ checked-in + PAID        │ → COMPLETED                    │
        /// │ CONFIRMED      │ checked-in + != PAID     │ → PENDING_PAYMENT              │
        /// └────────────────┴──────────────────────────┴────────────────────────────────┘
        ///
        /// Court release:
        ///   COMPLETED     → IsActive = false; Court = AVAILABLE (if not busy)
        ///   PENDING_PAYMENT → same; staff will re-confirm after checkout
        ///
        /// Idempotent: processed bookings leave the query's status filter.
        /// </summary>
        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        public async Task ProcessExpiredActiveBookingsAsync()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInformation("[Job-02] Starting");

            try
            {
                var now = DateTimeHelper.GetUtcNow();

                var activeBookings = await _db.Bookings
                    .Include(b => b.BookingCourts)
                        .ThenInclude(bc => bc.Court)
                    .Include(b => b.Invoice)
                    .Where(b =>
                        b.Status == BookingStatus.IN_PROGRESS ||
                        b.Status == BookingStatus.PAID_ONLINE ||
                        b.Status == BookingStatus.CONFIRMED)
                    .ToListAsync();

                if (activeBookings.Count == 0)
                {
                    _logger.LogInformation("[Job-02] No active bookings to process. Elapsed: {Ms}ms", sw.ElapsedMilliseconds);
                    return;
                }

                // Filter to only time-elapsed bookings in memory —
                // avoids a DB computed-column or complex EF expression.
                var expiredBookings = activeBookings
                    .Where(b => b.BookingCourts.Any() &&
                                ToUtcFromVietnam(
                                    b.BookingCourts.OrderByDescending(bc => bc.Date)
                                                   .ThenByDescending(bc => bc.EndTime)
                                                   .First().Date,
                                    b.BookingCourts.OrderByDescending(bc => bc.Date)
                                                   .ThenByDescending(bc => bc.EndTime)
                                                   .First().EndTime) <= now)
                    .ToList();

                if (expiredBookings.Count == 0)
                {
                    _logger.LogInformation("[Job-02] No time-elapsed bookings. Elapsed: {Ms}ms", sw.ElapsedMilliseconds);
                    return;
                }

                var affectedCourtIds = expiredBookings
                    .SelectMany(b => b.BookingCourts)
                    .Select(bc => bc.CourtId)
                    .Distinct()
                    .ToHashSet();

                var processingIds = expiredBookings
                    .Select(b => b.Id)
                    .ToHashSet();

                var busyCourtIds = await GetBusyCourtIdsAsync(affectedCourtIds, processingIds);

                int completed = 0;
                int pendingPayment = 0;
                int skipped = 0;
                int errors = 0;

                foreach (var booking in expiredBookings)
                {
                    try
                    {
                        var result = FinalizeExpiredBooking(booking, busyCourtIds, now);

                        switch (result)
                        {
                            case FinalizeResult.Completed: completed++; break;
                            case FinalizeResult.PendingPayment: pendingPayment++; break;
                            case FinalizeResult.Skipped: skipped++; break;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(ex,
                            "[Job-02] Failed to finalize booking {BookingId} (Status={Status}). Skipping.",
                            booking.Id, booking.Status);
                        // Roll back in-memory changes on this booking to avoid
                        // persisting a partial state.
                        _db.Entry(booking).State = EntityState.Unchanged;
                        foreach (var bc in booking.BookingCourts)
                            _db.Entry(bc).State = EntityState.Unchanged;
                        if (booking.Invoice != null)
                            _db.Entry(booking.Invoice).State = EntityState.Unchanged;
                    }
                }

                await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "[Job-02] Done. Completed={Completed} PendingPayment={PendingPayment} " +
                    "Skipped={Skipped} Errors={Errors} Elapsed={Ms}ms",
                    completed, pendingPayment, skipped, errors, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job-02] Unhandled error. Elapsed: {Ms}ms", sw.ElapsedMilliseconds);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Job-03 · Purge expired slot locks  (every 30 s)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Deletes SlotLock rows whose expiry has passed.
        ///
        /// Court status is NOT updated here — Job-01 handles the linked
        /// PENDING booking and its courts at the same ExpiresAt boundary.
        ///
        /// Idempotent: already-deleted rows are gone.
        /// </summary>
        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        public async Task CleanupExpiredSlotLocksAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var now = DateTimeHelper.GetUtcNow();
                var deleted = await _db.SlotLocks
                    .Where(sl => sl.ExpiresAt <= now)
                    .ExecuteDeleteAsync();

                if (deleted > 0)
                    _logger.LogInformation(
                        "[Job-03] Deleted {Count} expired slot locks. Elapsed: {Ms}ms",
                        deleted, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job-03] Error. Elapsed: {Ms}ms", sw.ElapsedMilliseconds);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Job-04 · Detect NO_SHOW bookings  (every 5 min)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Marks CONFIRMED / PAID_ONLINE bookings as NO_SHOW when the
        /// customer did not check in within the grace period.
        ///
        /// Covered cases:
        ///   • CONFIRMED  + no check-in + StartTime+15min elapsed → NO_SHOW
        ///   • PAID_ONLINE + no check-in + StartTime+15min elapsed → NO_SHOW
        ///   • Either status + check-in present → status already IN_PROGRESS; not in query
        ///   • Within grace window → skipped
        ///   • Court SUSPENDED / INACTIVE → court status left unchanged
        ///
        /// Note: PAID_ONLINE bookings past EndTime with no check-in are also
        /// caught here (Job-02 defers them). The NO_SHOW outcome is correct
        /// even when EndTime has passed — the customer simply never showed.
        ///
        /// Idempotent: NO_SHOW bookings leave the status filter.
        /// </summary>
        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        public async Task DetectNoShowBookingsAsync()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInformation("[Job-04] Starting NO_SHOW detection");

            try
            {
                var now = DateTimeHelper.GetUtcNow();
                var eligibleStatuses = BookingStatusTransition.GetNoShowEligibleStatuses();

                var candidates = await _db.Bookings
                    .Include(b => b.BookingCourts)
                        .ThenInclude(bc => bc.Court)
                    .Include(b => b.Invoice)
                    .Where(b => eligibleStatuses.Contains(b.Status) && b.CheckedInAt == null)
                    .ToListAsync();

                if (candidates.Count == 0)
                {
                    _logger.LogInformation("[Job-04] No NO_SHOW candidates. Elapsed: {Ms}ms", sw.ElapsedMilliseconds);
                    return;
                }

                var noShowBookings = candidates
                    .Where(b =>
                    {
                        if (!b.BookingCourts.Any()) return false;
                        var first = b.BookingCourts
                            .OrderBy(bc => bc.Date)
                            .ThenBy(bc => bc.StartTime)
                            .First();
                        return ToUtcFromVietnam(first.Date, first.StartTime)
                                   .AddMinutes(NoShowGraceMinutes) < now;
                    })
                    .ToList();

                if (noShowBookings.Count == 0)
                {
                    _logger.LogInformation("[Job-04] No bookings past grace window. Elapsed: {Ms}ms", sw.ElapsedMilliseconds);
                    return;
                }

                var affectedCourtIds = noShowBookings
                    .SelectMany(b => b.BookingCourts)
                    .Select(bc => bc.CourtId)
                    .Distinct()
                    .ToHashSet();

                var processingIds = noShowBookings
                    .Select(b => b.Id)
                    .ToHashSet();

                var busyCourtIds = await GetBusyCourtIdsAsync(affectedCourtIds, processingIds);

                int marked = 0;
                int errors = 0;

                foreach (var booking in noShowBookings)
                {
                    try
                    {
                        booking.Status = BookingStatus.NO_SHOW;
                        booking.UpdatedAt = now;

                        foreach (var bc in booking.BookingCourts)
                        {
                            bc.IsActive = false;

                            if (bc.Court != null
                                && !busyCourtIds.Contains(bc.CourtId)
                                && bc.Court.Status != CourtStatus.SUSPENDED
                                && bc.Court.Status != CourtStatus.INACTIVE)
                            {
                                bc.Court.Status = CourtStatus.AVAILABLE;
                                bc.Court.UpdatedAt = now;
                            }
                        }

                        marked++;

                        var first = booking.BookingCourts.OrderBy(bc => bc.Date).ThenBy(bc => bc.StartTime).First();
                        var courtIdList = string.Join(", ", booking.BookingCourts.Select(bc => bc.CourtId));

                        _logger.LogWarning(
                            "[Job-04] NO_SHOW — BookingId={BookingId} CustomerId={CustomerId} " +
                            "Courts=[{CourtIds}] Date={Date} StartTime={Start} " +
                            "PaymentTiming={Timing} Amount={Amount}",
                            booking.Id,
                            booking.CustomerId ?? Guid.Empty,
                            courtIdList,
                            first.Date,
                            first.StartTime,
                            booking.Invoice?.PaymentTiming.ToString() ?? "N/A",
                            booking.Invoice?.FinalTotal ?? 0);
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(ex,
                            "[Job-04] Failed to mark booking {BookingId} as NO_SHOW. Skipping.",
                            booking.Id);
                        _db.Entry(booking).State = EntityState.Unchanged;
                        foreach (var bc in booking.BookingCourts)
                            _db.Entry(bc).State = EntityState.Unchanged;
                    }
                }

                await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "[Job-04] Marked={Marked} Errors={Errors} Elapsed={Ms}ms",
                    marked, errors, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job-04] Unhandled error. Elapsed: {Ms}ms", sw.ElapsedMilliseconds);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Job-05 · Purge expired slot interests  (every 1 h)
        // ════════════════════════════════════════════════════════════════════

        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        public async Task CleanupExpiredSlotInterestsAsync()
        {
            try
            {
                var deleted = await _slotInterestRepo.DeleteExpiredAsync();
                if (deleted > 0)
                    _logger.LogInformation("[Job-05] Deleted {Count} expired slot interests", deleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job-05] Error in slot interest cleanup");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Private helpers
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns court IDs that are currently held by another active booking
        /// (i.e. not the batch being processed).
        /// Single shared query used by Jobs 01, 02, and 04.
        /// </summary>
        private async Task<HashSet<Guid>> GetBusyCourtIdsAsync(
            HashSet<Guid> candidateCourtIds,
            HashSet<Guid> excludeBookingIds)
        {
            if (candidateCourtIds.Count == 0) return [];

            var ids = await _db.BookingCourts
                .Where(other =>
                    candidateCourtIds.Contains(other.CourtId) &&
                    other.IsActive &&
                    !excludeBookingIds.Contains(other.BookingId) &&
                    ActiveStatuses.Contains(other.Booking.Status))
                .Select(other => other.CourtId)
                .Distinct()
                .ToListAsync();

            return ids.ToHashSet();
        }

        /// <summary>
        /// Deactivates BookingCourt slots and, where safe, sets the court AVAILABLE.
        /// Used by Job-01 (full release on cancellation).
        /// </summary>
        private static void ReleaseCourtSlots(
            IEnumerable<BookingCourt> slots,
            HashSet<Guid> busyCourtIds,
            DateTime now,
            bool deactivate)
        {
            foreach (var bc in slots)
            {
                if (deactivate) bc.IsActive = false;

                if (bc.Court != null
                    && !busyCourtIds.Contains(bc.CourtId)
                    && bc.Court.Status != CourtStatus.SUSPENDED
                    && bc.Court.Status != CourtStatus.INACTIVE)
                {
                    bc.Court.Status = CourtStatus.AVAILABLE;
                    bc.Court.UpdatedAt = now;
                }
            }
        }

        /// <summary>
        /// Core finalization logic for Job-02.
        /// Each booking is processed independently; exceptions bubble to the
        /// caller which rolls back in-memory state for that booking only.
        ///
        /// Returns the outcome so the caller can aggregate counters.
        /// </summary>
        private FinalizeResult FinalizeExpiredBooking(
            Booking booking,
            HashSet<Guid> busyCourtIds,
            DateTime now)
        {
            // Bookings with no courts are data anomalies — skip and warn.
            if (!booking.BookingCourts.Any())
            {
                _logger.LogWarning(
                    "[Job-02] Booking {BookingId} has no BookingCourts. Skipping.",
                    booking.Id);
                return FinalizeResult.Skipped;
            }

            var invoice = booking.Invoice;

            switch (booking.Status)
            {
                // ── PAID_ONLINE / CONFIRMED ─────────────────────────────
                // Both follow the same rule: if the customer never checked in,
                // Job-04 owns the NO_SHOW transition; we skip here.
                case BookingStatus.PAID_ONLINE:
                case BookingStatus.CONFIRMED:
                    {
                        if (booking.CheckedInAt == null)
                            return FinalizeResult.Skipped;

                        return CompleteOrPendPayment(booking, invoice, busyCourtIds, now);
                    }

                // ── IN_PROGRESS ─────────────────────────────────────────
                // Staff already checked the customer in; always finalize.
                case BookingStatus.IN_PROGRESS:
                    {
                        return CompleteOrPendPayment(booking, invoice, busyCourtIds, now);
                    }

                default:
                    // Should never reach here given the query filter,
                    // but guard defensively.
                    _logger.LogWarning(
                        "[Job-02] Unexpected status {Status} on booking {BookingId}.",
                        booking.Status, booking.Id);
                    return FinalizeResult.Skipped;
            }
        }

        /// <summary>
        /// Decides COMPLETED vs PENDING_PAYMENT based on invoice state,
        /// then releases courts consistently for both outcomes.
        ///
        /// PARTIALLY_PAID intentionally → PENDING_PAYMENT:
        ///   The court fee was pre-paid online (VNPay IPN sets PARTIALLY_PAID),
        ///   but a service fee added at check-in may still be outstanding.
        ///   Staff completes the checkout to settle the remainder.
        /// </summary>
        private FinalizeResult CompleteOrPendPayment(
            Booking booking,
            Invoice? invoice,
            HashSet<Guid> busyCourtIds,
            DateTime now)
        {
            if (invoice == null)
            {
                _logger.LogWarning(
                    "[Job-02] Booking {BookingId} (Status={Status}) has no invoice. " +
                    "Defaulting to PENDING_PAYMENT.",
                    booking.Id, booking.Status);
            }

            bool fullyPaid = invoice?.PaymentStatus == InvoicePaymentStatus.PAID;

            booking.Status = fullyPaid ? BookingStatus.COMPLETED : BookingStatus.PENDING_PAYMENT;
            booking.UpdatedAt = now;

            if (invoice != null) invoice.UpdatedAt = now;

            // Release courts for both outcomes:
            //   COMPLETED        — booking is truly done
            //   PENDING_PAYMENT  — slot is freed; staff re-books if needed after checkout
            foreach (var bc in booking.BookingCourts)
            {
                bc.IsActive = false;

                if (bc.Court != null
                    && !busyCourtIds.Contains(bc.CourtId)
                    && bc.Court.Status != CourtStatus.SUSPENDED
                    && bc.Court.Status != CourtStatus.INACTIVE)
                {
                    bc.Court.Status = CourtStatus.AVAILABLE;
                    bc.Court.UpdatedAt = now;
                }
            }

            return fullyPaid ? FinalizeResult.Completed : FinalizeResult.PendingPayment;
        }

        /// <summary>
        /// Converts a Vietnam-local date+time pair to UTC.
        ///
        /// BookingCourt.Date and StartTime/EndTime are stored in VN local time.
        /// Using SpecifyKind(Utc) would misrepresent them as already-UTC,
        /// causing a 7-hour offset against DateTime.UtcNow.
        /// ConvertTimeToUtc performs the correct zone conversion.
        /// </summary>
        private static DateTime ToUtcFromVietnam(DateOnly date, TimeOnly time)
        {
            var local = date.ToDateTime(time);
            return TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
                DateTimeHelper.VNTimezone);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Supporting enum (internal to this file; move to Enums/ if reused)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Result returned by FinalizeExpiredBooking for counter aggregation.</summary>
    internal enum FinalizeResult
    {
        Completed,
        PendingPayment,
        Skipped,
    }
}