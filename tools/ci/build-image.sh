#!/usr/bin/env bash
# Build image một lần, kiểm bằng check-image.sh, rồi đẩy đúng image đó lên GHCR (Epic 6C, gói 6C-2a; task 1.17, 1.18, 1.19, 1.49).
# Dùng chung cho SmashCourt-BE, TADSS-Runtime, SmashCourt-AI (bản chép giống hệt ở tools/ci/ của mỗi repo).
#   Tag: sha-<7> (bất biến, CD triển khai theo tag này) và <nhánh> (bản mới nhất của nhánh, dùng làm cache build).
#   Nhãn org.opencontainers.image.revision phải bằng sha đầy đủ của commit.
#   sha-<7> đã có trên registry (chạy lại CI cùng commit) ⇒ không ghi đè, không đẩy gì thêm.
# Cách dùng: bash tools/ci/build-image.sh <image> [--push]
#   <image>  tên không kèm tag, chữ thường, ví dụ ghcr.io/nguyenthanhtrung-n2t/smashcourt-be
#   --push   đẩy lên registry (phải docker login trước); chỉ cho nhánh master, develop. Không có ⇒ chỉ build + kiểm (pull request, máy dev).
#   Biến môi trường: GIT_SHA (mặc định git rev-parse HEAD), BRANCH (mặc định nhánh hiện tại).
set -euo pipefail

image="${1:?Cách dùng: build-image.sh <image> [--push]}"
push=false
[ "${2:-}" = "--push" ] && push=true

cd "$(dirname "$0")/../.."

sha="${GIT_SHA:-$(git rev-parse HEAD)}"
branch="${BRANCH:-$(git rev-parse --abbrev-ref HEAD)}"
short="${sha:0:7}"
sha_tag="${image}:sha-${short}"
branch_tag="${image}:$(printf '%s' "$branch" | tr '/' '-')"

if $push; then
  case "$branch" in
    master|develop) ;;
    *) echo "build-image: chỉ đẩy image của nhánh master hoặc develop (nhánh hiện tại: ${branch})" >&2; exit 1 ;;
  esac
  if docker manifest inspect "$sha_tag" >/dev/null 2>&1; then
    echo "build-image: ${sha_tag} đã có trên registry, giữ nguyên (tag sha bất biến)"
    exit 0
  fi
fi

# Cache inline: lấy lớp từ bản mới nhất của nhánh nếu đã có trên registry
cache_args=()
if $push && docker pull --quiet "$branch_tag" >/dev/null 2>&1; then
  cache_args=(--cache-from "$branch_tag")
fi

DOCKER_BUILDKIT=1 docker build \
  --build-arg GIT_SHA="$sha" \
  --build-arg BUILD_VERSION="${branch}-${short}" \
  --build-arg BUILDKIT_INLINE_CACHE=1 \
  "${cache_args[@]}" \
  --tag "$sha_tag" \
  --tag "$branch_tag" \
  .

# Kiểm đúng image sẽ đẩy: không file bí mật, non-root, có nhãn revision (OPS-01, OPS-02)
bash tools/ci/check-image.sh "$sha_tag"

revision="$(docker image inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$sha_tag")"
if [ "$revision" != "$sha" ]; then
  echo "build-image: nhãn revision '${revision}' khác sha của commit '${sha}'" >&2
  exit 1
fi

if ! $push; then
  echo "build-image: đã build và kiểm ${sha_tag} (không đẩy)"
  exit 0
fi

docker push --quiet "$sha_tag"
docker push --quiet "$branch_tag"
digest="$(docker image inspect --format '{{index .RepoDigests 0}}' "$sha_tag")"
echo "build-image: đã đẩy ${sha_tag} và ${branch_tag} (${digest})"

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "### Image"
    echo "| Tag | Digest | Revision |"
    echo "| --- | --- | --- |"
    echo "| \`${sha_tag}\`, \`${branch_tag}\` | \`${digest#*@}\` | \`${sha}\` |"
  } >>"$GITHUB_STEP_SUMMARY"
fi
