-- =====================================================================================================
-- Phase 6 — Cọc TADSS (T6.1). Đặc tả: TADSS-Runtime/spec/PHASE6_DESIGN_SPEC.md Mục 6.
-- Áp lên Neon: branch `tadss-dev` trước (spec gọi là `staging`), branch `main` (production) chỉ sau Gate M6.
--
-- Cách chạy:
--   * psql:        psql "<chuỗi kết nối>" -v ON_ERROR_STOP=1 -f phase6_deposit.sql   (chạy cả file một lần)
--   * SQL Editor:  chạy PHẦN 1 riêng, xong mới chạy PHẦN 2 (giá trị enum mới phải được commit trước khi dùng)
-- Kiểm sau khi áp (chỉ đọc):
--   python TADSS-Runtime/tools/database/schema_snapshot.py --target tadss-dev   ⇒ Trạng thái "đã áp đủ"
-- Sau khi áp: dump lại TADSS-Runtime/docs/database/schema_smashcourt.sql và khởi động lại BE.
--
-- Không chạy lại lần hai: PHẦN 2 dừng ở CREATE TYPE (transaction tự rollback, DB không đổi).
-- Không rollback được giá trị enum PENDING_APPROVAL; rollback mã = tắt cờ, cột mới để nguyên (đều cho phép NULL).
-- =====================================================================================================

-- PHẦN 1: giá trị enum mới (tự commit, ngoài transaction)
ALTER TYPE public.booking_status ADD VALUE IF NOT EXISTS 'PENDING_APPROVAL';

-- PHẦN 2
BEGIN;

CREATE TYPE public.deposit_source AS ENUM ('TADSS', 'MANAGER', 'FULL_PAYMENT_FALLBACK');
CREATE TYPE public.approval_result AS ENUM ('APPROVED', 'REJECTED');
CREATE TYPE public.booking_cancel_reason AS ENUM (
    'PAYMENT_EXPIRED', 'APPROVAL_EXPIRED', 'APPROVAL_REJECTED', 'POLICY_REJECTED',
    'CUSTOMER_CANCELLED', 'STAFF_CANCELLED', 'SYSTEM_CLEANUP');

ALTER TABLE public.bookings
    ADD COLUMN tadss_decision_id uuid,
    ADD COLUMN approval_expires_at timestamp with time zone,
    ADD COLUMN approval_reason_code character varying(64),
    ADD COLUMN approval_reasons text[],
    ADD COLUMN approval_result public.approval_result,
    ADD COLUMN approval_resolved_by uuid,
    ADD COLUMN approval_resolved_at timestamp with time zone,
    ADD COLUMN approval_resolution_note character varying(500),
    ADD COLUMN cancel_reason public.booking_cancel_reason,
    ADD COLUMN tadss_outcome_reported_at timestamp with time zone,
    ADD CONSTRAINT fk_bookings_approval_resolved_by FOREIGN KEY (approval_resolved_by) REFERENCES public.users(id) ON DELETE RESTRICT,
    ADD CONSTRAINT chk_bookings_pending_approval CHECK (
        status <> 'PENDING_APPROVAL'::public.booking_status
        OR (tadss_decision_id IS NOT NULL AND approval_expires_at IS NOT NULL)),
    ADD CONSTRAINT chk_bookings_approval_resolution CHECK ((approval_result IS NULL) = (approval_resolved_at IS NULL)),
    ADD CONSTRAINT chk_bookings_cancel_reason CHECK (
        cancel_reason IS NULL
        OR status = ANY (ARRAY['CANCELLED'::public.booking_status, 'CANCELLED_PENDING_REFUND'::public.booking_status,
                               'CANCELLED_REFUNDED'::public.booking_status]));

ALTER TABLE public.invoices
    ADD COLUMN deposit_rate numeric(5,4),
    ADD COLUMN deposit_amount numeric(12,2),
    ADD COLUMN deposit_source public.deposit_source,
    ADD COLUMN hold_deadline timestamp with time zone,
    ADD CONSTRAINT chk_invoices_deposit_rate_range CHECK (deposit_rate IS NULL OR (deposit_rate >= 0 AND deposit_rate <= 1)),
    ADD CONSTRAINT chk_invoices_deposit_amount_positive CHECK (deposit_amount IS NULL OR deposit_amount >= 0),
    ADD CONSTRAINT chk_invoices_deposit_fields CHECK (
        (deposit_source IS NULL) = (deposit_amount IS NULL) AND (deposit_rate IS NULL) = (deposit_amount IS NULL));

CREATE UNIQUE INDEX uq_bookings_tadss_decision ON public.bookings (tadss_decision_id) WHERE tadss_decision_id IS NOT NULL;
CREATE INDEX idx_bookings_pending_approval ON public.bookings (approval_expires_at)
    WHERE status = 'PENDING_APPROVAL'::public.booking_status;
CREATE INDEX idx_bookings_outcome_pending ON public.bookings (updated_at)
    WHERE tadss_decision_id IS NOT NULL AND tadss_outcome_reported_at IS NULL;

-- P6-13: một slot lock cho mỗi sân của đơn (excl_slot_locks_no_overlap vẫn chặn chồng giờ)
ALTER TABLE public.slot_locks DROP CONSTRAINT IF EXISTS uq_slot_locks_booking;

-- P6-04: backfill lý do hủy cho dữ liệu cũ, đúng quy tắc phân loại của Phase 5 (export_smashcourt_bookings.sql)
UPDATE public.bookings
SET cancel_reason = CASE cancel_source
        WHEN 'SYSTEM' THEN 'PAYMENT_EXPIRED'
        WHEN 'STAFF' THEN 'STAFF_CANCELLED'
        WHEN 'CUSTOMER' THEN 'CUSTOMER_CANCELLED'
        WHEN 'LINK' THEN 'CUSTOMER_CANCELLED'
    END::public.booking_cancel_reason
WHERE status = ANY (ARRAY['CANCELLED'::public.booking_status, 'CANCELLED_PENDING_REFUND'::public.booking_status,
                          'CANCELLED_REFUNDED'::public.booking_status])
  AND cancel_source IS NOT NULL AND cancel_reason IS NULL;

COMMIT;
