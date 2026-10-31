#!/usr/bin/env bash
# Nạp (hoặc nạp lại) database `smashcourt` của bản deploy từ Neon DB TEST tadss-dev (Epic 6C, gói 6C-2b, task 1.28).
# Cách dùng: bash deploy/seed-db.sh        (bí mật: SMASHCOURT_ENV_FILE, mặc định ~/.smashcourt-deploy/.env)
#
# Chép schema `public` (cấu trúc + dữ liệu). KHÔNG chép `hangfire` (BE tự tạo khi khởi động; chép job cũ có thể làm job chạy lại).
# Neon chỉ bị ĐỌC (phiên chỉ đọc). Nạp lại = XÓA SẠCH database smashcourt trên máy rồi chép mới.
# Chặn chép nhầm DB, sai một bước là dừng, chưa ghi gì:
#   1. SEED_SOURCE_URL phải trỏ đúng endpoint SEED_SOURCE_ENDPOINT (mã endpoint Neon của tadss-dev).
#   2. DB nguồn phải đã áp Phase 6 (có cột bookings.tadss_decision_id): DB gốc chưa áp nên bị từ chối.
#   3. Người chạy gõ "smashcourt" để xác nhận.
# Sau khi nạp: đếm chính xác từng bảng ở nguồn và đích, lệch thì báo lỗi. Không in chuỗi kết nối hay mật khẩu.
set -euo pipefail

env_file="${SMASHCOURT_ENV_FILE:-$HOME/.smashcourt-deploy/.env}"
[ -f "$env_file" ] || { echo "seed-db: thiếu $env_file" >&2; exit 1; }
# Đọc đúng các khóa cần dùng như văn bản (không `source`: giá trị khác như SMTP_FROM="Tên <email>" không phải cú pháp bash)
env_get() { sed -n "s/^$1=//p" "$env_file" | tail -n 1; }
POSTGRES_USER="$(env_get POSTGRES_USER)"
SEED_SOURCE_URL="$(env_get SEED_SOURCE_URL)"
SEED_SOURCE_ENDPOINT="$(env_get SEED_SOURCE_ENDPOINT)"
export SEED_SOURCE_URL
: "${POSTGRES_USER:?Thiếu POSTGRES_USER}" "${SEED_SOURCE_URL:?Thiếu SEED_SOURCE_URL}" "${SEED_SOURCE_ENDPOINT:?Thiếu SEED_SOURCE_ENDPOINT}"
db=smashcourt

# psql/pg_dump chạy bằng image postgres (máy không cần cài PostgreSQL). Nguồn: Neon qua Internet. Đích: vào thẳng container postgres.
# Chỉ lệnh nạp (đích có -i) đọc stdin; lệnh khác lấy stdin từ /dev/null để không nuốt câu trả lời xác nhận.
src_sql() { MSYS_NO_PATHCONV=1 docker run --rm -e SEED_SOURCE_URL postgres:17.6-alpine \
  sh -c 'psql "$SEED_SOURCE_URL" -XqtA -v ON_ERROR_STOP=1 -c "SET default_transaction_read_only = on" -c "$1"' _ "$1" </dev/null; }
dst_sql() { docker exec smashcourt-postgres psql -U "$POSTGRES_USER" -XqtA -v ON_ERROR_STOP=1 "$@" </dev/null; }

echo "== 1. Kiểm DB nguồn"
case "$SEED_SOURCE_URL" in
  *"@${SEED_SOURCE_ENDPOINT}."*|*"@${SEED_SOURCE_ENDPOINT}-pooler."*) echo "   endpoint ${SEED_SOURCE_ENDPOINT} ✓" ;;
  *) echo "seed-db: SEED_SOURCE_URL không trỏ tới endpoint ${SEED_SOURCE_ENDPOINT}, dừng" >&2; exit 1 ;;
esac
phase6="$(src_sql "SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='bookings' AND column_name='tadss_decision_id')")"
[ "$phase6" = "t" ] || { echo "seed-db: DB nguồn chưa áp Phase 6 (có thể là DB gốc), dừng" >&2; exit 1; }
echo "   đã áp Phase 6 ✓"
src_sql "SELECT '   nguồn: ' || (SELECT count(*) FROM bookings) || ' booking, ' || (SELECT count(*) FROM users) || ' user'"

echo
echo "Sẽ XÓA SẠCH database ${db} của bản deploy rồi chép lại từ tadss-dev."
read -r -p "Gõ \"${db}\" để xác nhận: " answer
[ "$answer" = "$db" ] || { echo "seed-db: không xác nhận, không làm gì"; exit 1; }

echo "== 2. Tạo lại database ${db}"
be_running="$(docker inspect --format '{{.State.Running}}' smashcourt-be 2>/dev/null || echo false)"
if [ "$be_running" = "true" ]; then docker stop smashcourt-be >/dev/null; echo "   tạm dừng smashcourt-be"; fi
dst_sql -d postgres -c "DROP DATABASE IF EXISTS ${db} WITH (FORCE)" -c "CREATE DATABASE ${db}"
# pg_dump một schema không kèm CREATE EXTENSION: tạo trước các extension nguồn đang dùng (btree_gist, pgcrypto…)
for ext in $(src_sql "SELECT extname FROM pg_extension WHERE extname <> 'plpgsql'"); do
  dst_sql -d "$db" -c "CREATE EXTENSION IF NOT EXISTS \"${ext}\""
done

echo "== 3. Chép schema public"
MSYS_NO_PATHCONV=1 docker run --rm -e SEED_SOURCE_URL postgres:17.6-alpine \
    sh -c 'pg_dump "$SEED_SOURCE_URL" --schema=public --no-owner --no-privileges --no-comments' </dev/null \
  | grep -v -E '^(CREATE SCHEMA public;|ALTER SCHEMA public OWNER)' \
  | docker exec -i smashcourt-postgres psql -U "$POSTGRES_USER" -d "$db" -Xq -v ON_ERROR_STOP=1 --single-transaction >/dev/null

echo "== 4. Đối chiếu số dòng từng bảng"
tables="$(dst_sql -d "$db" -c "SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY 1")"
count_sql="$(for t in $tables; do printf "SELECT '%s', count(*) FROM public.\"%s\" UNION ALL " "$t" "$t"; done | sed 's/ UNION ALL $//')"
src_counts="$(src_sql "$count_sql")"
dst_counts="$(dst_sql -d "$db" -c "$count_sql")"
if [ "$src_counts" != "$dst_counts" ]; then
  echo "seed-db: số dòng LỆCH giữa nguồn và ${db} (nếu có người vừa ghi vào tadss-dev thì chạy lại):" >&2
  diff <(echo "$src_counts") <(echo "$dst_counts") >&2 || true
  exit 1
fi
echo "   $(echo "$tables" | wc -w) bảng, $(echo "$dst_counts" | awk -F'|' '{s+=$2} END {print s}') dòng, khớp nguồn ✓"

if [ "$be_running" = "true" ]; then docker start smashcourt-be >/dev/null; echo "   bật lại smashcourt-be"; fi
echo "seed-db: xong"
