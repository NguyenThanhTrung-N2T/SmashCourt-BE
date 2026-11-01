#!/usr/bin/env bash
# Triển khai (hoặc cập nhật) một service của bản deploy bằng image GHCR (Epic 6C, gói 6C-2b).
# Cách dùng: bash deploy/deploy.sh <be|tadss|ai|all|obs> [tag]
#   tag mặc định: be, tadss = develop; ai = master. Chạy bản master: deploy.sh be master. Quay về bản cũ: deploy.sh be sha-xxxxxxx
#   obs: bật/cập nhật alloy (gửi số liệu lên Grafana Cloud, gói 6C-3a); cần 3 khóa GRAFANA_CLOUD_* trong file bí mật.
#   Bí mật: smashcourt-deploy.env ở thư mục gốc workspace (ngoài mọi repo), mẫu deploy/.env.example; đổi chỗ bằng SMASHCOURT_ENV_FILE.
# Chỉ khởi động lại đúng service đó (be kèm ngrok); PostgreSQL, Redis tự bật nếu chưa chạy.
set -euo pipefail

service="${1:?Cách dùng: deploy.sh <be|tadss|ai|all|obs> [tag]}"
tag="${2:-}"
recreate=()
case "$service" in
  be)    targets=(be ngrok); [ -n "$tag" ] && export BE_TAG="$tag" ;;
  tadss) targets=(tadss);    [ -n "$tag" ] && export TADSS_TAG="$tag" ;;
  ai)    targets=(ai);       [ -n "$tag" ] && export AI_TAG="$tag" ;;
  all)   targets=(be ngrok tadss ai); [ -z "$tag" ] || { echo "deploy.sh: 'all' không nhận tag" >&2; exit 1; } ;;
  # Cấu hình Alloy nằm trong configs.content mà compose không so khi quyết định tạo lại container: luôn tạo lại
  obs)   targets=(alloy); recreate=(--force-recreate); [ -z "$tag" ] || { echo "deploy.sh: 'obs' không nhận tag" >&2; exit 1; } ;;
  *) echo "deploy.sh: service phải là be, tadss, ai, all hoặc obs (nhận: ${service})" >&2; exit 1 ;;
esac

env_file="$(bash "$(dirname "$0")/env-file.sh")"
[ -f "$env_file" ] || { echo "deploy.sh: thiếu $env_file (mẫu: deploy/.env.example)" >&2; exit 1; }
command -v cygpath >/dev/null 2>&1 && env_file="$(cygpath -m "$env_file")"

if [ "$service" = obs ]; then
  # Chỉ kiểm có giá trị, không in giá trị
  for key in GRAFANA_CLOUD_OTLP_ENDPOINT GRAFANA_CLOUD_INSTANCE_ID GRAFANA_CLOUD_API_TOKEN; do
    [ -n "$(sed -n "s/^${key}=//p" "$env_file" | tail -n 1)" ] \
      || { echo "deploy.sh: thiếu ${key} trong ${env_file} (xem docs/guides/DOCKER_COMMANDS.md của TADSS-Runtime, mục Grafana Cloud)" >&2; exit 1; }
  done
fi

cd "$(dirname "$0")"
compose() { docker compose --env-file "$env_file" "$@"; }
compose up -d --wait postgres redis
compose pull --quiet "${targets[@]}"
compose up -d --no-deps --wait "${recreate[@]}" "${targets[@]}"

declare -A containers=([be]=smashcourt-be [tadss]=tadss-runtime [ai]=smashcourt-ai)
for s in "${targets[@]}"; do
  c="${containers[$s]:-}"
  [ -n "$c" ] || continue   # ngrok không có nhãn phiên bản
  image="$(docker inspect --format '{{.Config.Image}}' "$c")"
  revision="$(docker inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$c")"
  echo "deploy.sh: ${c} chạy ${image##*:} (commit ${revision:0:7})"
done
