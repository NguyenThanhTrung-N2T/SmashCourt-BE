-- =====================================================================================================
-- Phase 6 — bổ sung N-28 (23/10/2026). Đặc tả: TADSS-Runtime/spec/PHASE6_DESIGN_SPEC.md Mục 3 (N-28) và 6.
-- Lỗi có sẵn: booking_promotions.discount_value_snapshot là numeric(5,2) kèm CHECK 0 < giá trị ≤ 100 (chỉ hợp khuyến mãi phần trăm),
-- trong khi promotions.discount_value của loại FIXED tới 99.999.999,99 ⇒ mọi đơn (online, tại quầy) dùng mã FIXED > 100 đ lỗi 500.
-- Sửa: cột numeric(12,2) (khớp EF HasPrecision(12, 2)), CHECK theo discount_type_snapshot, cùng giới hạn với chk_promotions_discount_range.
--
-- Áp lên Neon: branch `tadss-dev` (spec gọi là `staging`) sau phase6_deposit.sql; branch `main` (production) cùng đợt với phase6_deposit.sql.
-- Cách chạy:  psql "<chuỗi kết nối>" -v ON_ERROR_STOP=1 -f phase6_fix_promotion_snapshot.sql   (hoặc dán vào SQL Editor)
-- Chạy lại được: bỏ ràng buộc cũ/mới nếu có rồi tạo lại. Dữ liệu cũ luôn hợp lệ với ràng buộc mới (mọi dòng hiện có đều ≤ 100).
-- Kiểm sau khi áp (chỉ đọc): python TADSS-Runtime/tools/database/schema_snapshot.py --target tadss-dev ⇒ "đã áp đủ"
-- Sau khi áp: dump lại TADSS-Runtime/docs/database/schema_smashcourt.sql (bản trong repo đã cập nhật sẵn đúng nội dung này).
-- Rollback: không cần (chỉ nới kiểu cột và điều kiện); muốn trả về thì ALTER ngược khi chưa có dòng FIXED > 999,99.
-- =====================================================================================================

BEGIN;

ALTER TABLE public.booking_promotions DROP CONSTRAINT IF EXISTS chk_booking_promotions_rate;
ALTER TABLE public.booking_promotions DROP CONSTRAINT IF EXISTS chk_booking_promotions_discount_value;

ALTER TABLE public.booking_promotions ALTER COLUMN discount_value_snapshot TYPE numeric(12,2);

ALTER TABLE public.booking_promotions ADD CONSTRAINT chk_booking_promotions_discount_value CHECK (
    (discount_type_snapshot = 'PERCENT' AND discount_value_snapshot > 0 AND discount_value_snapshot <= 100)
    OR (discount_type_snapshot = 'FIXED' AND discount_value_snapshot > 0 AND discount_value_snapshot <= 99999999.99));

COMMIT;
