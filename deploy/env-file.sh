#!/usr/bin/env bash
# In đường dẫn file bí mật DUY NHẤT của bản deploy (dùng chung cho deploy.sh, seed-db.sh). Thứ tự:
#   1. SMASHCOURT_ENV_FILE nếu có đặt.
#   2. smashcourt-deploy.env ở thư mục gốc workspace (cha của SmashCourt-BE; không phải git repo nên không bao giờ bị commit).
#   3. ~/.smashcourt-deploy/.env: chỗ compose của runner gắn file ở (2) vào container (chỉ đọc), dùng khi CD chạy trong runner.
set -euo pipefail

if [ -n "${SMASHCOURT_ENV_FILE:-}" ]; then
  echo "$SMASHCOURT_ENV_FILE"
  exit 0
fi
root_file="$(cd "$(dirname "$0")/../.." && pwd)/smashcourt-deploy.env"
if [ -f "$root_file" ]; then
  echo "$root_file"
else
  echo "$HOME/.smashcourt-deploy/.env"
fi
