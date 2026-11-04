-- =====================================================================================================
-- Phase 6 — Epic 6C, P6-53 (05/11/2026). Đặc tả: TADSS-Runtime/spec/PHASE6_PLAN_6C_DEVOPS_OBSERVABILITY_PERF.md (Mục 1, giai đoạn 2C).
-- Lỗi có sẵn (schema Đồ án 1): bookings có hai index cùng khóa (status, booking_date):
--   idx_booking_status_date  (status, booking_date) INCLUDE (id, branch_id, created_at)   ⇐ GIỮ (phủ cả bản kia, quét chỉ index được)
--   idx_bookings_status_date (status, booking_date)                                         ⇐ BỎ
-- Bản trùng không giúp truy vấn nào nhưng mọi lần ghi bookings phải cập nhật nó, và mọi câu lập kế hoạch trên bookings phải khóa nó
-- (đo hiệu năng 04/11: câu đọc chi tiết booking giữ ~66 khóa, PostgreSQL khóa mọi index của bảng khi lập kế hoạch).
--
-- Áp lên Neon: branch `tadss-dev` (spec gọi là `staging`) trước; branch `main` (production) sau Gate M6 cùng các script Phase 6.
-- PostgreSQL cục bộ của bản deploy (SmashCourt-BE/deploy, nạp từ tadss-dev bằng seed-db.sh) áp cùng script này.
-- Cách chạy:  psql "<chuỗi kết nối>" -v ON_ERROR_STOP=1 -f phase6_drop_duplicate_booking_index.sql   (hoặc dán vào SQL Editor)
-- Chạy lại được: DROP INDEX IF EXISTS. Khối kiểm dừng script (không bỏ gì) nếu index giữ lại không còn hoặc khác cột.
-- Kiểm sau khi áp (chỉ đọc): python TADSS-Runtime/tools/database/schema_snapshot.py --target tadss-dev ⇒ "đã áp đủ"
-- Sau khi áp: dump lại TADSS-Runtime/docs/database/schema_smashcourt.sql (bản trong repo đã cập nhật sẵn đúng nội dung này).
-- Rollback: CREATE INDEX idx_bookings_status_date ON public.bookings USING btree (status, booking_date);
-- =====================================================================================================

BEGIN;

DO $$
DECLARE
    kept text := (SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'idx_booking_status_date');
BEGIN
    IF kept IS NULL OR kept NOT LIKE '%(status, booking_date) INCLUDE (id, branch_id, created_at)' THEN
        RAISE EXCEPTION 'Dừng: idx_booking_status_date không có hoặc khác cột (%), không bỏ idx_bookings_status_date', kept;
    END IF;
END $$;

DROP INDEX IF EXISTS public.idx_bookings_status_date;

COMMIT;
