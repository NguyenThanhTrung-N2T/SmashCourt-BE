#!/usr/bin/env bash
# CD: triển khai đúng một commit đã qua CI, kiểm tra, hỏng thì tự quay về bản đang chạy trước đó (Epic 6C, gói 6C-2b, task 1.37–1.39).
# Job `deploy` cuối ci.yml của BE, TADSS, AI gọi script này trên runner của máy deploy. Chạy tay cũng được:
#   bash deploy/cd.sh <be|tadss|ai> <sha đầy đủ 40 ký tự của commit>
# Thoát: 0 = bản mới chạy và kiểm đạt
#        1 = bản mới hỏng, đã quay về bản trước và bản trước kiểm đạt
#        2 = UNSAFE: bản mới hỏng và quay về cũng hỏng (hoặc không có bản trước) — cần người xử lý
#        3 = từ chối, chưa đụng gì: sha sai dạng hoặc image sha-<7> không có trên GHCR
# Kiểm tra = container chạy đúng commit (nhãn org.opencontainers.image.revision) + health + smoke chỉ đọc (không tạo dữ liệu).
set -uo pipefail

service="${1:-}"
sha="${2:-}"
case "$service" in
  be)    container=smashcourt-be;  image=ghcr.io/nguyenthanhtrung-n2t/smashcourt-be ;;
  tadss) container=tadss-runtime;  image=ghcr.io/nguyenthanhtrung-n2t/tadss-runtime ;;
  ai)    container=smashcourt-ai;  image=ghcr.io/nguyenthanhtrung-n2t/smashcourt-ai ;;
  *) echo "cd.sh: service phải là be, tadss hoặc ai" >&2; exit 3 ;;
esac
[[ "$sha" =~ ^[0-9a-f]{40}$ ]] || { echo "cd.sh: sha phải là 40 ký tự hex (nhận: '${sha}')" >&2; exit 3; }
tag="sha-${sha:0:7}"
cd "$(dirname "$0")" || exit 3

docker manifest inspect "${image}:${tag}" >/dev/null 2>&1 || { echo "cd.sh: không có image ${image}:${tag} trên GHCR, không triển khai" >&2; exit 3; }

revision_of() { docker inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$container" 2>/dev/null; }

# Smoke chỉ đọc theo từng service, gọi từ BÊN TRONG container (docker exec): đúng cả khi cd.sh chạy trên máy lẫn trong container runner
http_get() { docker exec "$container" wget -qO- -T 10 "http://127.0.0.1:$1"; }
smoke() {
  case "$service" in
    be)    [ "$(http_get 8080/health/ready)" = "Healthy" ] && http_get 8080/api/cancel-policies | grep -q '"success":true' ;;
    tadss) [ "$(http_get 8080/health/ready)" = "Healthy" ] ;;
    ai)    docker exec "$container" python -c "import urllib.request; print(urllib.request.urlopen('http://127.0.0.1:8000/health', timeout=10).read().decode())" \
             | grep -q '"status":"ok"' ;;
  esac
}

# $1 = tag cần chạy, $2 = sha đầy đủ phải thấy trong nhãn của container
deploy_and_check() {
  bash deploy.sh "$service" "$1" || { echo "   deploy.sh lỗi (pull hoặc health check của Docker không đạt)"; return 1; }
  [ "$(revision_of)" = "$2" ] || { echo "   container không chạy đúng commit ${2:0:7}"; return 1; }
  # Thử lại vài lần: request đầu sau khi PostgreSQL vừa khởi động lại có thể dính kết nối cũ đã bị cắt (57P01)
  local attempt
  for attempt in 1 2 3; do
    smoke && return 0
    echo "   smoke test lần ${attempt} chưa đạt"
    [ "$attempt" -lt 3 ] && sleep 5
  done
  echo "   smoke test không đạt"; return 1
}

previous="$(revision_of || true)"
echo "== Triển khai ${service} ${tag} (đang chạy: ${previous:0:7})"
if deploy_and_check "$tag" "$sha"; then
  # Dọn image cũ của service này: chỉ giữ bản đang chạy và bản ngay trước (để còn quay về tay)
  docker images --format '{{.Repository}}:{{.Tag}}' "$image" \
    | grep -vE ":(${tag}|sha-${previous:0:7})$" | xargs -r docker rmi >/dev/null 2>&1 || true
  echo "cd.sh: OK — ${service} chạy ${tag}"
  exit 0
fi

echo "== Bản mới hỏng, quay về bản trước"
if [[ ! "$previous" =~ ^[0-9a-f]{40}$ ]] || [ "$previous" = "$sha" ]; then
  echo "cd.sh: UNSAFE — không có bản trước để quay về" >&2
  exit 2
fi
if deploy_and_check "sha-${previous:0:7}" "$previous"; then
  echo "cd.sh: ĐÃ QUAY VỀ — ${service} chạy lại sha-${previous:0:7}; bản ${tag} bị loại" >&2
  exit 1
fi
echo "cd.sh: UNSAFE — quay về sha-${previous:0:7} cũng không đạt" >&2
exit 2
