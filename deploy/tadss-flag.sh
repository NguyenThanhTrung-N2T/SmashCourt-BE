#!/usr/bin/env bash
# Bật / tắt TADSS trên bản deploy khi đang chạy, không khởi động lại BE (cờ TadssConfig:EnableTadssDecision, Phase 6).
# Cách dùng: bash deploy/tadss-flag.sh <on|off|status>
#   on     đơn online tạo SAU lúc bật hỏi TADSS mức cọc (APPROVED / NEED_APPROVAL / REJECTED)
#   off    đơn online tạo SAU lúc tắt cọc 100 %, không gọi TADSS (van an toàn khi TADSS có sự cố)
#   status in giá trị trong file cờ và các dòng log gần nhất của BE về cờ
# Đơn đang xử lý dở, đơn chờ duyệt và thao tác duyệt của Manager không bị gián đoạn. BE nhận giá trị mới trong khoảng 1 giây (IOptionsMonitor);
# script đợi tới khi BE ghi log xác nhận (tối đa 10 giây). File cờ nằm trong volume smashcourt-deploy-be-runtime-config nên giữ nguyên qua
# các lần CD tạo lại container. Chi tiết: docs/guides/RUNBOOK.md của TADSS-Runtime, mục 2.4.
set -euo pipefail
# Git Bash trên Windows đổi tham số dạng /app/... thành đường dẫn Windows trước khi gửi cho docker exec: tắt việc đổi này (như seed-db.sh)
export MSYS_NO_PATHCONV=1

action="${1:?Cách dùng: tadss-flag.sh <on|off|status>}"
container="${SMASHCOURT_BE_CONTAINER:-smashcourt-be}"   # đổi tên chỉ khi thử trên container khác
file=/app/runtime-config/feature-flags.json

docker inspect --format '{{.State.Running}}' "$container" 2>/dev/null | grep -q true \
  || { echo "tadss-flag.sh: container ${container} không chạy (bật bản deploy: bash deploy/deploy.sh be)" >&2; exit 1; }
docker exec "$container" test -d /app/runtime-config \
  || { echo "tadss-flag.sh: image BE đang chạy chưa hỗ trợ cờ đổi lúc chạy (cần image có Extensions/RuntimeConfigExtensions)" >&2; exit 1; }

flag_log() { docker logs --since "$1" "$container" 2>&1 | grep "Cờ EnableTadssDecision" || true; }

case "$action" in
  on|off)
    value=false; [ "$action" = on ] && value=true
    current="$(docker exec "$container" sh -c "cat ${file} 2>/dev/null" | grep -oE 'true|false' || true)"
    if [ "$current" = "$value" ]; then echo "tadss-flag.sh: cờ đã = ${value} từ trước, không đổi gì"; exit 0; fi
    since="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    # Ghi file tạm rồi mv (thay nguyên file một lần): BE không bao giờ đọc phải file ghi dở
    docker exec -u 0 "$container" sh -c "printf '{ \"TadssConfig\": { \"EnableTadssDecision\": %s } }\n' $value > ${file}.tmp && chmod 644 ${file}.tmp && mv ${file}.tmp ${file}"
    # Chưa có file và tắt: mặc định của image (appsettings.json) vốn đã tắt nên BE không ghi dòng "đổi"; file giờ ghi rõ false
    if [ -z "$current" ] && [ "$value" = false ]; then echo "tadss-flag.sh: cờ = false (vốn đã tắt theo mặc định, nay ghi rõ vào file)"; exit 0; fi
    for _ in $(seq 1 20); do
      if flag_log "$since" | grep -q "⇒"; then flag_log "$since" | tail -n 1; echo "tadss-flag.sh: cờ = ${value}"; exit 0; fi
      sleep 0.5
    done
    echo "tadss-flag.sh: đã ghi file cờ = ${value} nhưng BE chưa xác nhận trong 10 giây. Kiểm: bash deploy/tadss-flag.sh status, docker logs smashcourt-be" >&2
    exit 1
    ;;
  status)
    echo "File cờ ${file}:"
    docker exec "$container" sh -c "cat ${file} 2>/dev/null || echo '  (chưa có: dùng giá trị trong appsettings.json, mặc định TẮT)'"
    echo "Log của BE về cờ (24 giờ gần nhất):"
    flag_log 24h | tail -n 5
    ;;
  *) echo "tadss-flag.sh: action phải là on, off hoặc status (nhận: ${action})" >&2; exit 1 ;;
esac
