"""Dashboard + cảnh báo "Vận hành" trên Grafana Cloud (Epic 6C, gói 6C-3a). Chỉ dùng thư viện chuẩn Python.

    python deploy/grafana/grafana_cloud.py check      kiểm mọi panel đủ mô tả 6 ý, đúng metric (OB-BE-3); CI chạy lệnh này
    python deploy/grafana/grafana_cloud.py push       đẩy 3 dashboard, điểm nhận email và 5 luật cảnh báo (ghi đè bản cũ cùng uid)

push đọc GRAFANA_URL, GRAFANA_SA_TOKEN, ALERT_EMAIL_TO từ file bí mật (SMASHCOURT_ENV_FILE, mặc định ~/.smashcourt-deploy/.env); không in giá trị.
Thử với Grafana local không cần token: GRAFANA_URL=http://127.0.0.1:3000 python deploy/grafana/grafana_cloud.py push --no-auth
"""
import json
import os
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from dashboards import DASHBOARDS, FOLDER_TITLE, KNOWN_METRICS, TAG, Dashboard, Panel  # noqa: E402

FOLDER_UID = "smashcourt-van-hanh"
CONTACT_POINT = "smashcourt-email"
RULE_GROUP = "smashcourt-van-hanh"
METRIC_PATTERN = re.compile(r"\b([a-z][a-z0-9_]*?)(?:_bucket|_count|_sum)?\s*[{\[(]")
PROMQL_WORDS = {"sum", "max", "min", "rate", "increase", "histogram_quantile", "by", "vector", "or", "and", "on", "count"}


# ── Dựng JSON ────────────────────────────────────────────────────────────────

def _target(expr: str, legend: str, ref: str, logs: bool) -> dict:
    datasource = {"type": "loki", "uid": "${logs}"} if logs else {"type": "prometheus", "uid": "${datasource}"}
    return {"refId": ref, "datasource": datasource, "expr": expr, "legendFormat": legend}


def _panel_json(panel: Panel, panel_id: int, x: int, y: int) -> dict:
    logs = panel.kind == "logs"
    steps = [{"value": None if i == 0 else value, "color": color} for i, (value, color) in enumerate(panel.thresholds)]
    body = {
        "id": panel_id,
        "type": panel.kind,
        "title": panel.title,
        "description": panel.description.render(),
        "gridPos": {"x": x, "y": y, "w": panel.width, "h": panel.height},
        "datasource": {"type": "loki", "uid": "${logs}"} if logs else {"type": "prometheus", "uid": "${datasource}"},
        "targets": [_target(expr, legend, chr(ord("A") + i), logs) for i, (expr, legend) in enumerate(panel.targets)],
        "fieldConfig": {
            "defaults": {
                "unit": panel.unit,
                "mappings": panel.mappings,
                "thresholds": {"mode": "absolute", "steps": steps or [{"value": None, "color": "green"}]},
            },
            "overrides": [],
        },
    }
    if panel.kind == "stat":
        body["options"] = {
            "textMode": panel.text_mode,
            "colorMode": "background" if panel.thresholds else "none",
            "reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False},
        }
    elif logs:
        body["options"] = {"showTime": True, "wrapLogMessage": True, "sortOrder": "Descending", "enableLogDetails": True}
    return body


def build(dashboard: Dashboard) -> dict:
    panels = [{
        "id": 1,
        "type": "text",
        "title": "Cách đọc tab này",
        "description": "Hướng dẫn đọc dashboard; không có số liệu.",
        "gridPos": {"x": 0, "y": 0, "w": 24, "h": 5},
        "options": {"mode": "markdown", "content": dashboard.guide},
    }]
    x, y, row_height = 0, 5, 0
    for index, panel in enumerate(dashboard.panels, start=2):
        if x + panel.width > 24:
            x, y, row_height = 0, y + row_height, 0
        panels.append(_panel_json(panel, index, x, y))
        x += panel.width
        row_height = max(row_height, panel.height)

    return {
        "uid": dashboard.uid,
        "title": dashboard.title,
        "tags": [TAG],
        "timezone": "browser",
        "refresh": "1m",
        "time": {"from": "now-6h", "to": "now"},
        "schemaVersion": 39,
        # 3 dashboard cùng tag hiện thành hàng nút ở đầu trang, dùng như 3 tab BE / TADSS / AI
        "links": [{"type": "dashboards", "tags": [TAG], "asDropdown": False, "includeVars": True, "keepTime": True, "title": "Vận hành"}],
        "templating": {"list": [
            {"name": "datasource", "label": "Prometheus", "type": "datasource", "query": "prometheus", "current": {}, "hide": 0},
            {"name": "logs", "label": "Loki", "type": "datasource", "query": "loki", "current": {}, "hide": 0},
        ]},
        "panels": panels,
    }


# ── Kiểm (CI) ────────────────────────────────────────────────────────────────

def metrics_in(expr: str) -> set[str]:
    return {name for name in METRIC_PATTERN.findall(expr) if name not in PROMQL_WORDS}


def check() -> list[str]:
    errors: list[str] = []
    uids: set[str] = set()
    for dashboard in DASHBOARDS:
        if dashboard.uid in uids:
            errors.append(f"{dashboard.uid}: trùng uid")
        uids.add(dashboard.uid)
        titles: set[str] = set()
        for panel in dashboard.panels:
            where = f"{dashboard.title} / {panel.title}"
            if panel.title in titles:
                errors.append(f"{where}: trùng tên panel trong cùng dashboard")
            titles.add(panel.title)
            for part, value in vars(panel.description).items():
                if not value.strip():
                    errors.append(f"{where}: mô tả thiếu ý '{part}'")
            if not re.search(r"[A-Z]+-[A-Z0-9-]+|SC\d", panel.description.tests):
                errors.append(f"{where}: mô tả thiếu mã test")
            if panel.kind != "logs" and not panel.unit:
                errors.append(f"{where}: thiếu đơn vị")
            if panel.kind not in {"stat", "timeseries", "logs"}:
                errors.append(f"{where}: loại panel '{panel.kind}' không hỗ trợ")
            if panel.width < 1 or panel.width > 24:
                errors.append(f"{where}: độ rộng phải trong 1..24")
            if panel.kind == "logs":
                continue
            for expr, _ in panel.targets:
                for name in metrics_in(expr) - KNOWN_METRICS.keys():
                    errors.append(f"{where}: metric '{name}' không có trong KNOWN_METRICS (dashboards.py)")
        build(dashboard)  # dựng được JSON
    for rule in alert_rules("prom-uid"):
        for name in metrics_in(rule["data"][0]["model"]["expr"]) - KNOWN_METRICS.keys():
            errors.append(f"cảnh báo {rule['uid']}: metric '{name}' không có trong KNOWN_METRICS")
    return errors


# ── Cảnh báo ─────────────────────────────────────────────────────────────────

def _rule(uid: str, title: str, expr: str, op: str, threshold: float, duration: str, no_data: str, summary: str) -> dict:
    return {
        "uid": uid,
        "title": title,
        "ruleGroup": RULE_GROUP,
        "folderUID": FOLDER_UID,
        "condition": "C",
        "for": duration,
        "noDataState": no_data,
        "execErrState": "Error",
        "labels": {"he_thong": "smashcourt-deploy"},
        "annotations": {"summary": summary},
        "notification_settings": {"receiver": CONTACT_POINT},
        "data": [
            {
                "refId": "A",
                "relativeTimeRange": {"from": 900, "to": 0},
                "datasourceUid": None,  # điền lúc push
                "model": {"refId": "A", "expr": expr, "instant": True},
            },
            {
                "refId": "C",
                "datasourceUid": "__expr__",
                "model": {
                    "refId": "C",
                    "type": "threshold",
                    "expression": "A",
                    "conditions": [{"evaluator": {"type": op, "params": [threshold]}}],
                },
            },
        ],
    }


def alert_rules(prometheus_uid: str) -> list[dict]:
    def http_errors(job: str) -> str:
        return (
            f'(sum(rate(http_server_request_duration_seconds_count{{job="{job}",http_response_status_code=~"5.."}}[5m])) or vector(0))'
            f' / sum(rate(http_server_request_duration_seconds_count{{job="{job}"}}[5m]))'
        )

    rules = [
        # Không có số liệu = máy deploy tắt, mất mạng hoặc Alloy dừng: cũng báo
        _rule("smashcourt-down", "Service không sẵn sàng", "min by (job) (probe_success)", "lt", 1, "2m", "Alerting",
              "{{ $labels.job }} không trả health 2xx trong 2 phút (hoặc máy deploy / Alloy mất số liệu)."),
        _rule("smashcourt-be-5xx", "BE lỗi 5xx > 5 %", http_errors("smashcourt-be"), "gt", 0.05, "5m", "OK",
              "Hơn 5 % request vào BE trả 5xx trong 5 phút."),
        _rule("smashcourt-ai-5xx", "AI lỗi 5xx > 5 %", http_errors("smashcourt-ai"), "gt", 0.05, "5m", "OK",
              "Hơn 5 % request vào AI trả 5xx trong 5 phút."),
        _rule("smashcourt-tadss-fallback", "TADSS đường lùi > 20 %",
              '(sum(rate(tadss_fallbacks_total{job="tadss-runtime"}[10m])) or vector(0)) / sum(rate(tadss_decisions_total{job="tadss-runtime"}[10m]))',
              "gt", 0.2, "10m", "OK", "Hơn 20 % quyết định TADSS dùng đường lùi (cọc 100 %) trong 10 phút."),
        _rule("smashcourt-gemini-quota", "Gemini bị giới hạn (429) liên tục",
              'sum(rate(gemini_request_duration_seconds_count{job="smashcourt-ai",outcome="quota"}[5m]))',
              "gt", 0, "10m", "OK", "Gemini trả 429 liên tục 10 phút: hết giới hạn gói miễn phí."),
    ]
    for rule in rules:
        rule["data"][0]["datasourceUid"] = prometheus_uid
    return rules


# ── Đẩy lên Grafana ──────────────────────────────────────────────────────────

def read_env() -> dict[str, str]:
    path = Path(os.environ.get("SMASHCOURT_ENV_FILE", Path.home() / ".smashcourt-deploy" / ".env"))
    values: dict[str, str] = {}
    if path.is_file():
        for line in path.read_text(encoding="utf-8").splitlines():
            if "=" in line and not line.lstrip().startswith("#"):
                key, value = line.split("=", 1)
                values[key.strip()] = value.strip()
    for key in ("GRAFANA_URL", "GRAFANA_SA_TOKEN", "ALERT_EMAIL_TO"):
        if os.environ.get(key):
            values[key] = os.environ[key]
    return values


class Grafana:
    def __init__(self, url: str, token: str | None) -> None:
        self.url = url.rstrip("/")
        self.headers = {"Content-Type": "application/json", "X-Disable-Provenance": "true"}  # vẫn sửa được trên giao diện
        if token:
            self.headers["Authorization"] = f"Bearer {token}"

    def call(self, method: str, path: str, body: object | None = None, ok_missing: bool = False) -> object | None:
        data = json.dumps(body).encode() if body is not None else None
        request = urllib.request.Request(self.url + path, data=data, method=method, headers=self.headers)
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                raw = response.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            if ok_missing and error.code == 404:
                return None
            detail = error.read().decode(errors="replace")[:300]
            raise SystemExit(f"grafana_cloud.py: {method} {path} ⇒ HTTP {error.code}: {detail}") from None


def push(no_auth: bool) -> None:
    env = read_env()
    url = env.get("GRAFANA_URL", "")
    token = None if no_auth else env.get("GRAFANA_SA_TOKEN", "")
    email = env.get("ALERT_EMAIL_TO", "")
    missing = [k for k, v in (("GRAFANA_URL", url), ("GRAFANA_SA_TOKEN", token if not no_auth else "x"), ("ALERT_EMAIL_TO", email)) if not v]
    if missing:
        raise SystemExit(f"grafana_cloud.py: thiếu {', '.join(missing)} trong file bí mật (xem DOCKER_COMMANDS.md, mục Grafana Cloud)")

    grafana = Grafana(url, token)
    datasources = grafana.call("GET", "/api/datasources") or []
    prometheus = [d for d in datasources if d.get("type") == "prometheus"]
    if not prometheus:
        raise SystemExit("grafana_cloud.py: Grafana không có datasource Prometheus")
    # Grafana Cloud có sẵn grafanacloud-<stack>-prom; ưu tiên nó nếu có nhiều datasource
    prometheus_uid = next((d["uid"] for d in prometheus if d.get("name", "").startswith("grafanacloud-")), prometheus[0]["uid"])

    if grafana.call("GET", f"/api/folders/{FOLDER_UID}", ok_missing=True) is None:
        grafana.call("POST", "/api/folders", {"uid": FOLDER_UID, "title": FOLDER_TITLE})
    for dashboard in DASHBOARDS:
        grafana.call("POST", "/api/dashboards/db", {"dashboard": build(dashboard), "folderUid": FOLDER_UID, "overwrite": True})
        print(f"dashboard: {dashboard.title}")

    contact = {"uid": CONTACT_POINT, "name": CONTACT_POINT, "type": "email", "settings": {"addresses": email, "singleEmail": True}}
    existing = [c for c in grafana.call("GET", f"/api/v1/provisioning/contact-points?name={CONTACT_POINT}") or [] if c.get("uid") == CONTACT_POINT]
    if existing:
        grafana.call("PUT", f"/api/v1/provisioning/contact-points/{CONTACT_POINT}", contact)
    else:
        grafana.call("POST", "/api/v1/provisioning/contact-points", contact)
    print(f"điểm nhận cảnh báo: {CONTACT_POINT} (email)")

    # Một lệnh PUT cả nhóm: tạo/cập nhật mọi luật theo uid, chu kỳ đánh giá 60 giây (khớp chu kỳ gửi số liệu)
    rules = alert_rules(prometheus_uid)
    grafana.call("PUT", f"/api/v1/provisioning/folder/{FOLDER_UID}/rule-groups/{RULE_GROUP}",
                 {"title": RULE_GROUP, "folderUid": FOLDER_UID, "interval": 60, "rules": rules})
    for rule in rules:
        print(f"cảnh báo: {rule['title']}")
    print(f"xong: {url}/dashboards/f/{FOLDER_UID}")


def main(argv: list[str]) -> int:
    command = argv[1] if len(argv) > 1 else ""
    if command == "check":
        errors = check()
        for error in errors:
            print(f"LỖI: {error}")
        if errors:
            return 1
        panels = sum(len(d.panels) for d in DASHBOARDS)
        print(f"OK: {len(DASHBOARDS)} dashboard, {panels} panel, mô tả đủ 6 ý, metric đều có trong KNOWN_METRICS")
        return 0
    if command == "push":
        push(no_auth="--no-auth" in argv)
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(main(sys.argv))
