#!/usr/bin/env bash
# Kiểm một image Docker trước khi đẩy lên registry (Epic 6C, gói 6C-1, K7; mã test OPS-01, OPS-02).
# Dùng chung cho SmashCourt-BE, TADSS-Runtime, SmashCourt-AI (bản chép giống hệt ở tools/ci/ của mỗi repo).
#   1. Thư mục ứng dụng /app không chứa file bí mật: appsettings.Development.json, appsettings.*.local.json, .env, .env.* (trừ .env.example), *.pfx, *.pem, *.key
#   2. Container không chạy bằng root (Config.User khác rỗng, root, 0)
#   3. Có nhãn org.opencontainers.image.revision (sha của commit, CD dùng để kiểm đúng phiên bản)
# Cách dùng: bash tools/ci/check-image.sh <image>      Thoát 0 khi cả ba đạt; thoát 1 và in từng lỗi (không in nội dung file).
set -euo pipefail

image="${1:?Cách dùng: check-image.sh <image>}"
errors=0

cid="$(docker create "$image")"
trap 'docker rm -f "$cid" >/dev/null 2>&1 || true' EXIT

# 1. File bí mật trong /app (chỉ xét /app: image nền có chứng chỉ CA dạng .pem hợp lệ ở /etc/ssl)
forbidden="$(docker export "$cid" | tar -t | grep -E '^app/' | grep -E '(^|/)(appsettings\.Development\.json|appsettings\.[^/]*\.local\.json|\.env(\.[^/]*)?|[^/]*\.(pfx|pem|key))$' | grep -vE '(^|/)\.env\.example$' || true)"
if [ -n "$forbidden" ]; then
  echo "FAIL  OPS-01 image chứa file bí mật trong /app:"
  printf '        %s\n' $forbidden
  errors=$((errors + 1))
else
  echo "PASS  OPS-01 /app không chứa file bí mật"
fi

# 2. User chạy container
user="$(docker image inspect --format '{{.Config.User}}' "$image")"
case "$user" in
  ""|root|0|0:*|root:*)
    echo "FAIL  OPS-02 container chạy bằng root (Config.User='${user}')"
    errors=$((errors + 1)) ;;
  *)
    echo "PASS  OPS-02 container chạy bằng user '${user}'" ;;
esac

# 3. Nhãn revision
revision="$(docker image inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$image" 2>/dev/null || true)"
if [ -z "$revision" ] || [ "$revision" = "<no value>" ]; then
  echo "FAIL  nhãn org.opencontainers.image.revision chưa có (build phải truyền --build-arg GIT_SHA)"
  errors=$((errors + 1))
else
  echo "PASS  nhãn revision = ${revision}"
fi

if [ "$errors" -gt 0 ]; then
  echo "check-image: ${errors} mục không đạt"
  exit 1
fi
echo "check-image: đạt"
