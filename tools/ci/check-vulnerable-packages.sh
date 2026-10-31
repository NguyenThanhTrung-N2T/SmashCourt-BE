#!/usr/bin/env bash
# SEC-BE-7 (Epic 6C, task 1.16; chép từ SEC-7 của TADSS-Runtime): quét package có lỗ hổng (kể cả phụ thuộc bắc cầu) cho toàn bộ solution.
# Exit 1 khi có dòng mức High hoặc Critical; exit khác 0 khi chính lệnh dotnet lỗi (ví dụ mất mạng); exit 0 khi sạch.
set -euo pipefail

cd "$(dirname "$0")/../.."

output_file="$(mktemp)"
trap 'rm -f "$output_file"' EXIT

# Không dùng "|| true": lỗi của dotnet phải làm script đỏ. Ghi ra file để vừa in nguyên output vừa quét được.
dotnet_exit=0
dotnet list SmashCourt-BE.slnx package --vulnerable --include-transitive >"$output_file" 2>&1 || dotnet_exit=$?

cat "$output_file"

if [ "$dotnet_exit" -ne 0 ]; then
  echo "LỖI: 'dotnet list package --vulnerable' thoát với mã $dotnet_exit (có thể do mất mạng hoặc nguồn NuGet không truy cập được)." >&2
  exit "$dotnet_exit"
fi

# Dòng kết quả có dạng: "> <package>  <resolved>  <severity>  <advisory url>"; chỉ nhận mức High/Critical làm cột riêng.
# Nguồn không tải được (warning NU1900 hoặc "no data") nghĩa là không quét được, cũng coi là lỗi.
if grep -E -i 'NU1900|error:|Unable to load the service index' "$output_file" >/dev/null; then
  echo "LỖI: không quét được lỗ hổng (nguồn NuGet không truy cập được)." >&2
  exit 2
fi

if grep -E '^[[:space:]]*>.*[[:space:]](High|Critical)[[:space:]]' "$output_file" >/dev/null; then
  echo "LỖI: phát hiện package có lỗ hổng mức High hoặc Critical." >&2
  exit 1
fi

echo "OK: không có package lỗ hổng mức High/Critical."
