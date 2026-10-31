#!/usr/bin/env bash
# Triển khai (hoặc cập nhật) một service của bản deploy bằng image GHCR (Epic 6C, gói 6C-2b).
# Cách dùng: bash deploy/deploy.sh <be|tadss|ai|all> [tag]
#   tag mặc định: be, tadss = develop; ai = master. Chạy bản master: deploy.sh be master. Quay về bản cũ: deploy.sh be sha-xxxxxxx
#   Bí mật: SMASHCOURT_ENV_FILE (mặc định ~/.smashcourt-deploy/.env), mẫu deploy/.env.example.
# Chỉ khởi động lại đúng service đó (be kèm ngrok); PostgreSQL, Redis tự bật nếu chưa chạy.
set -euo pipefail

service="${1:?Cách dùng: deploy.sh <be|tadss|ai|all> [tag]}"
tag="${2:-}"
case "$service" in
  be)    targets=(be ngrok); [ -n "$tag" ] && export BE_TAG="$tag" ;;
  tadss) targets=(tadss);    [ -n "$tag" ] && export TADSS_TAG="$tag" ;;
  ai)    targets=(ai);       [ -n "$tag" ] && export AI_TAG="$tag" ;;
  all)   targets=(be ngrok tadss ai); [ -z "$tag" ] || { echo "deploy.sh: 'all' không nhận tag" >&2; exit 1; } ;;
  *) echo "deploy.sh: service phải là be, tadss, ai hoặc all (nhận: ${service})" >&2; exit 1 ;;
esac

env_file="${SMASHCOURT_ENV_FILE:-$HOME/.smashcourt-deploy/.env}"
[ -f "$env_file" ] || { echo "deploy.sh: thiếu $env_file (mẫu: deploy/.env.example)" >&2; exit 1; }
command -v cygpath >/dev/null 2>&1 && env_file="$(cygpath -m "$env_file")"

cd "$(dirname "$0")"
compose() { docker compose --env-file "$env_file" "$@"; }
compose up -d --wait postgres redis
compose pull --quiet "${targets[@]}"
compose up -d --no-deps --wait "${targets[@]}"

declare -A containers=([be]=smashcourt-be [tadss]=tadss-runtime [ai]=smashcourt-ai)
for s in "${targets[@]}"; do
  c="${containers[$s]:-}"
  [ -n "$c" ] || continue   # ngrok không có nhãn phiên bản
  image="$(docker inspect --format '{{.Config.Image}}' "$c")"
  revision="$(docker inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$c")"
  echo "deploy.sh: ${c} chạy ${image##*:} (commit ${revision:0:7})"
done
