#!/usr/bin/env bash
# Điểm vào của container runner (Epic 6C, gói 6C-2b).
#   register: đăng ký runner MỘT lần bằng mã đăng ký ngắn hạn (RUNNER_TOKEN), lưu thông tin đăng ký vào volume /runner-state.
#   (mặc định): chép thông tin đăng ký từ volume rồi chạy runner. Không giữ token nào trong container.
set -euo pipefail
cd /home/runner
state=/runner-state
files=(.runner .credentials .credentials_rsaparams)

if [ "${1:-}" = "register" ]; then
  : "${RUNNER_URL:?}" "${RUNNER_NAME:?}" "${RUNNER_TOKEN:?Thiếu RUNNER_TOKEN (mã đăng ký của repo)}"
  ./config.sh --unattended --url "$RUNNER_URL" --token "$RUNNER_TOKEN" --name "$RUNNER_NAME" \
    --labels smashcourt-deploy --work _work --replace --disableupdate
  cp "${files[@]}" "$state/"
  echo "entrypoint: đã đăng ký ${RUNNER_NAME}"
  exit 0
fi

for f in "${files[@]}"; do
  [ -f "$state/$f" ] || { echo "entrypoint: runner chưa đăng ký (thiếu $f): chạy lệnh register một lần" >&2; exit 1; }
  cp "$state/$f" .
done
exec ./run.sh
