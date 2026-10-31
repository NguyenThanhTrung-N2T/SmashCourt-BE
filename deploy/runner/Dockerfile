# Runner GitHub Actions chạy trong Docker cho CD (Epic 6C, gói 6C-2b): image chính thức của GitHub + Docker Compose (image gốc không có).
# Đổi phiên bản runner: sửa thẻ FROM rồi build lại (docker compose -f deploy/runner/docker-compose.yml build).
FROM ghcr.io/actions/actions-runner:2.338.2

ARG COMPOSE_VERSION=v5.6.1
ARG COMPOSE_SHA256=47737f4236e6d3ccea7291995203f8f018058783b4c3a9469119811a0b5db8c0

USER root
RUN mkdir -p /usr/local/lib/docker/cli-plugins \
 && curl -fsSL -o /usr/local/lib/docker/cli-plugins/docker-compose \
      "https://github.com/docker/compose/releases/download/${COMPOSE_VERSION}/docker-compose-linux-x86_64" \
 && echo "${COMPOSE_SHA256}  /usr/local/lib/docker/cli-plugins/docker-compose" | sha256sum -c - \
 && chmod 755 /usr/local/lib/docker/cli-plugins/docker-compose \
 && mkdir -p /runner-state && chown runner:runner /runner-state
COPY --chmod=755 entrypoint.sh /entrypoint.sh

USER runner
ENTRYPOINT ["/entrypoint.sh"]
