"""Dashboard + cảnh báo "Vận hành" trên Grafana Cloud (Epic 6C, gói 6C-3a). Chỉ dùng thư viện chuẩn Python.

    python deploy/grafana/grafana_cloud.py check      kiểm mọi panel đủ mô tả 6 ý, đúng metric (OB-BE-3); CI chạy lệnh này
    python deploy/grafana/grafana_cloud.py push       đẩy 3 dashboard, mẫu email tiếng Việt, điểm nhận email và 5 luật cảnh báo (ghi đè bản cũ cùng uid)

push đọc GRAFANA_URL, GRAFANA_SA_TOKEN, ALERT_EMAIL_TO từ file bí mật smashcourt-deploy.env ở gốc workspace (hoặc SMASHCOURT_ENV_FILE); không in giá trị.
Thử với Grafana local không cần token: GRAFANA_URL=http://127.0.0.1:3000 python deploy/grafana/grafana_cloud.py push --no-auth
"""
import json
import os
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path

sys.dont_write_bytecode = True  # không sinh __pycache__ trong repo khi import dashboards.py
sys.path.insert(0, str(Path(__file__).resolve().parent))
from dashboards import DASHBOARDS, FOLDER_TITLE, KNOWN_METRICS, TAG, Dashboard, Panel  # noqa: E402

FOLDER_UID = "smashcourt-van-hanh"
CONTACT_POINT = "smashcourt-email"
RULE_GROUP = "smashcourt-van-hanh"
# Grafana Cloud có thêm datasource phụ cùng loại (grafanacloud-usage, alert-state-history): ẩn để biến tự chọn đúng kho chính
DATASOURCE_REGEX = "/^(?!.*(usage|alert-state)).*$/"
METRIC_PATTERN = re.compile(r"\b([a-z][a-z0-9_]*?)(?:_bucket|_count|_sum)?\s*[{\[(]")
PROMQL_WORDS = {"sum", "max", "min", "rate", "increase", "histogram_quantile", "by", "vector", "or", "and", "on", "count", "label_replace", "clamp_min"}


# ── Dựng JSON ────────────────────────────────────────────────────────────────

def _target(expr: str, legend: str, ref: str, logs: bool) -> dict:
    datasource = {"type": "loki", "uid": "${logs}"} if logs else {"type": "prometheus", "uid": "${datasource}"}
    return {"refId": ref, "datasource": datasource, "expr": expr, "legendFormat": legend}


_TIMESERIES_STYLE = {"lineWidth": 2, "fillOpacity": 0, "showPoints": "never", "spanNulls": True, "axisSoftMin": 0}


def _panel_json(panel: Panel, panel_id: int, x: int, y: int) -> dict:
    logs = panel.kind == "logs"
    # Bậc gốc trong suốt: ô chưa có số liệu không bị tô màu "tốt" (xanh); màu chỉ bắt đầu từ ngưỡng đầu tiên trở lên
    steps = [{"value": None, "color": "transparent"}] + [{"value": value, "color": color} for value, color in panel.thresholds]
    mappings = list(panel.mappings)
    if panel.kind == "stat" and panel.no_value:
        # p95 khi không có request là NaN (không phải "không có chuỗi"): hiện cùng chữ với noValue
        mappings.append({"type": "special", "options": {"match": "null+nan", "result": {"text": panel.no_value}}})
    defaults: dict = {
        "unit": panel.unit,
        "mappings": mappings,
        "thresholds": {"mode": "absolute", "steps": steps if panel.thresholds else [{"value": None, "color": "green"}]},
    }
    if panel.no_value:
        defaults["noValue"] = panel.no_value
    if panel.kind == "timeseries":
        defaults["custom"] = dict(_TIMESERIES_STYLE)
        defaults["color"] = {"mode": "palette-classic"}
    if panel.soft_max is not None:
        defaults["min"] = 0
        defaults.setdefault("custom", {})["axisSoftMax"] = panel.soft_max

    body = {
        "id": panel_id,
        "type": panel.kind,
        "title": panel.title,
        "description": panel.description.render(),
        "gridPos": {"x": x, "y": y, "w": panel.width, "h": panel.height},
        "datasource": {"type": "loki", "uid": "${logs}"} if logs else {"type": "prometheus", "uid": "${datasource}"},
        "targets": [_target(expr, legend, chr(ord("A") + i), logs) for i, (expr, legend) in enumerate(panel.targets)],
        "fieldConfig": {"defaults": defaults, "overrides": []},
    }
    if panel.kind == "stat":
        body["options"] = {
            "textMode": panel.text_mode,
            "colorMode": "background" if panel.thresholds else "none",
            "graphMode": "area" if panel.sparkline else "none",
            "justifyMode": "center",
            # Cỡ chữ cố định để các ô KPI đều nhau (tự co giãn làm tag image to gấp đôi ô bên cạnh)
            "text": {"titleSize": 13, "valueSize": 26},
            "reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False},
        }
    elif panel.kind == "timeseries":
        body["options"] = {
            "legend": {"displayMode": "list", "placement": "bottom", "showLegend": True},
            "tooltip": {"mode": "multi", "sort": "desc"},
        }
    elif panel.kind == "state-timeline":
        body["options"] = {"showValue": "always", "mergeValues": True, "rowHeight": 0.7, "alignValue": "center", "legend": {"showLegend": False}}
    elif logs:
        body["options"] = {"showTime": True, "wrapLogMessage": True, "sortOrder": "Descending", "enableLogDetails": True}
    return body


def _row(row_id: int, title: str, y: int, collapsed: bool, children: list[dict]) -> dict:
    return {
        "id": row_id,
        "type": "row",
        "title": title,
        "collapsed": collapsed,
        "gridPos": {"x": 0, "y": y, "w": 24, "h": 1},
        # Grafana: nhóm đang đóng giữ panel con bên trong; nhóm mở thì panel con nằm ngay sau nó ở mảng ngoài
        "panels": children if collapsed else [],
    }


def _layout(panels: list[Panel], start_id: int, y: int) -> tuple[list[dict], int]:
    """xếp panel trái ⇒ phải, hết 24 cột thì xuống hàng; trả về JSON và y kế tiếp"""
    result: list[dict] = []
    x, row_height = 0, 0
    for offset, panel in enumerate(panels):
        if x + panel.width > 24:
            x, y, row_height = 0, y + row_height, 0
        result.append(_panel_json(panel, start_id + offset, x, y))
        x += panel.width
        row_height = max(row_height, panel.height)
    return result, y + row_height


def build(dashboard: Dashboard) -> dict:
    # Grafana coi mọi panel đứng SAU một row là thuộc row đó: hàng KPI (không row) phải đứng đầu, nhóm thu gọn để cuối
    panels: list[dict] = []
    y, next_id = 0, 10
    for section in dashboard.sections:
        if section.title is None:
            children, y = _layout(section.panels, next_id, y)
            panels.extend(children)
        else:
            row_id, row_y = next_id, y
            children, end_y = _layout(section.panels, next_id + 1, y + 1)
            panels.append(_row(row_id, section.title, row_y, section.collapsed, children))
            if section.collapsed:
                y = row_y + 1  # nhóm đóng chỉ chiếm một dòng
            else:
                panels.extend(children)
                y = end_y
        next_id += len(section.panels) + 1
    guide = {
        "id": 2,
        "type": "text",
        "title": "Cách đọc tab này",
        "description": "Hướng dẫn đọc dashboard; không có số liệu.",
        "gridPos": {"x": 0, "y": y + 1, "w": 24, "h": 6},
        "options": {"mode": "markdown", "content": dashboard.guide},
    }
    panels.append(_row(1, "ℹ Cách đọc tab này", y, True, [guide]))

    return {
        "uid": dashboard.uid,
        "title": dashboard.title,
        "tags": [TAG],
        "timezone": "browser",
        "refresh": "1m",
        "time": {"from": "now-6h", "to": "now"},
        "schemaVersion": 39,
        "graphTooltip": 1,  # rê chuột trên một biểu đồ thì mọi biểu đồ cùng hiện mốc thời gian đó
        # 3 dashboard cùng tag hiện thành hàng nút ở đầu trang, dùng như 3 tab BE / TADSS / AI
        "links": [{"type": "dashboards", "tags": [TAG], "asDropdown": False, "includeVars": True, "keepTime": True, "title": "Vận hành"}],
        "templating": {"list": [
            {"name": "datasource", "label": "Prometheus", "type": "datasource", "query": "prometheus", "regex": DATASOURCE_REGEX, "current": {}, "hide": 2},
            {"name": "logs", "label": "Loki", "type": "datasource", "query": "loki", "regex": DATASOURCE_REGEX, "current": {}, "hide": 2},
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
            if panel.kind not in {"stat", "timeseries", "state-timeline", "logs"}:
                errors.append(f"{where}: loại panel '{panel.kind}' không hỗ trợ")
            if panel.width < 1 or panel.width > 24:
                errors.append(f"{where}: độ rộng phải trong 1..24")
        for section in dashboard.sections:
            widths = sum(panel.width for panel in section.panels)
            if widths % 24:
                errors.append(f"{dashboard.title} / nhóm '{section.title or 'KPI'}': tổng độ rộng {widths} không chia hết cho 24 (hàng bị hở)")
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

# Email tiếng Việt thay khối mặc định tiếng Anh của Grafana (Gmail tự dịch ra chữ khó hiểu). Mỗi dòng của message là một dòng email.
# Ca "mất số liệu" (máy deploy/Docker tắt, Alloy dừng) tách khỏi ca service lỗi thật; giờ hiện theo giờ Việt Nam.
EMAIL_TEMPLATE_NAME = "smashcourt-email"
EMAIL_TEMPLATE = """\
{{ define "smashcourt.time" }}{{ . | tz "Asia/Ho_Chi_Minh" | date "15:04 02/01/2006" }}{{ end }}

{{ define "smashcourt.service" }}{{ with .dich_vu }} ({{ . }}){{ else }}{{ with .job }} ({{ reReplaceAll "^integrations/blackbox/" "" . | toUpper }}){{ end }}{{ end }}{{ end }}

{{ define "smashcourt.subject" -}}
[SmashCourt] {{ if not .Alerts.Firing }}ĐÃ ỔN LẠI: {{ else if eq (index .Alerts.Firing 0).Annotations.grafana_state_reason "NoData" }}MẤT SỐ LIỆU: {{ else }}SỰ CỐ: {{ end -}}
{{ .CommonLabels.alertname }}{{ template "smashcourt.service" .CommonLabels }}
{{- end }}

{{ define "smashcourt.alert" -}}
▶ {{ .Labels.alertname }}{{ template "smashcourt.service" .Labels }}
{{ if eq .Status "resolved" -}}
• Bắt đầu {{ template "smashcourt.time" .StartsAt }}, hết lúc {{ template "smashcourt.time" .EndsAt }}.
{{- else if eq .Annotations.grafana_state_reason "NoData" -}}
• Tình trạng: Grafana Cloud KHÔNG NHẬN ĐƯỢC SỐ LIỆU từ máy deploy (chưa chắc service lỗi).
• Thường do: Docker Desktop hoặc máy deploy đang tắt, mất mạng, hoặc container smashcourt-alloy dừng.
• Cách xử lý: bật Docker Desktop (hệ thống tự chạy lại); vẫn báo thì chạy "bash deploy/deploy.sh obs" trong SmashCourt-BE.
• Bắt đầu: {{ template "smashcourt.time" .StartsAt }}
{{- else if eq .Annotations.grafana_state_reason "Error" -}}
• Tình trạng: Grafana Cloud không chạy được truy vấn của luật cảnh báo (lỗi phía Grafana, không phải service).
• Bắt đầu: {{ template "smashcourt.time" .StartsAt }}
{{- else -}}
• Tình trạng: {{ .Annotations.summary }}
• Cách xử lý: {{ .Annotations.xu_ly }}
• Bắt đầu: {{ template "smashcourt.time" .StartsAt }}
{{- end }}
{{ with .Annotations.dashboard_url }}• Dashboard: {{ . }}{{ end }}
{{- end }}

{{ define "smashcourt.message" -}}
{{ if .Alerts.Firing -}}
ĐANG CÓ SỰ CỐ ({{ len .Alerts.Firing }})
{{ range .Alerts.Firing }}{{ template "smashcourt.alert" . }}
{{ end }}{{ end -}}
{{ if .Alerts.Resolved -}}
ĐÃ ỔN LẠI ({{ len .Alerts.Resolved }})
{{ range .Alerts.Resolved }}{{ template "smashcourt.alert" . }}
{{ end }}{{ end -}}
Giờ theo giờ Việt Nam. Email tự động từ Grafana Cloud, bản deploy SmashCourt; còn sự cố thì nhắc lại sau 4 giờ.
{{- end }}
"""

_SERVICE_OF_BLACKBOX = '{{ reReplaceAll "^integrations/blackbox/" "" $labels.job | toUpper }}'


def _rule(uid: str, title: str, expr: str, op: str, threshold: float, duration: str, no_data: str,
          summary: str, action: str, dashboard: str, service: str | None = None) -> dict:
    labels = {"he_thong": "smashcourt-deploy"}
    if service:
        labels["dich_vu"] = service
    return {
        "uid": uid,
        "title": title,
        "ruleGroup": RULE_GROUP,
        "folderUID": FOLDER_UID,
        "condition": "C",
        "for": duration,
        "noDataState": no_data,
        "execErrState": "Error",
        "labels": labels,
        # dashboard_url: điền URL đầy đủ lúc push (Grafana chỉ nhận __dashboardUid__ khi kèm __panelId__)
        "annotations": {"summary": summary, "xu_ly": action, "dashboard_url": dashboard},
        "notification_settings": {"receiver": CONTACT_POINT, "repeat_interval": "4h"},
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


def alert_rules(prometheus_uid: str, grafana_url: str = "") -> list[dict]:
    def http_errors(job: str) -> str:
        return (
            f'(sum(rate(http_server_request_duration_seconds_count{{job="{job}",http_response_status_code=~"5.."}}[5m])) or vector(0))'
            f' / sum(rate(http_server_request_duration_seconds_count{{job="{job}"}}[5m]))'
        )

    error_ratio = "{{ humanizePercentage $values.A.Value }}"
    rules = [
        # Không có số liệu = máy deploy tắt, mất mạng hoặc Alloy dừng: cũng báo (email ghi rõ là mất số liệu)
        _rule("smashcourt-down", "Dịch vụ không phản hồi", "min by (job) (probe_success)", "lt", 1, "2m", "Alerting",
              f"{_SERVICE_OF_BLACKBOX} không trả lời health (2xx) quá 2 phút.",
              "xem container bằng \"docker ps\" và log bằng \"docker logs <tên container> --tail 100\"; "
              "chạy lại bằng \"bash deploy/deploy.sh <be|tadss|ai>\" trong SmashCourt-BE.",
              "smashcourt-van-hanh-be"),
        _rule("smashcourt-be-5xx", "BE lỗi 5xx > 5 %", http_errors("smashcourt-be"), "gt", 0.05, "5m", "OK",
              f"{error_ratio} request vào BE trả lỗi 5xx trong 5 phút gần nhất (ngưỡng 5 %).",
              "mở dashboard BE xem endpoint nào lỗi và hàng Log; nếu vừa deploy bản mới thì cân nhắc quay về bản trước.",
              "smashcourt-van-hanh-be", "BE"),
        _rule("smashcourt-ai-5xx", "AI lỗi 5xx > 5 %", http_errors("smashcourt-ai"), "gt", 0.05, "5m", "OK",
              f"{error_ratio} request vào AI trả lỗi 5xx trong 5 phút gần nhất (ngưỡng 5 %).",
              "mở dashboard AI xem lời gọi Gemini (lỗi, 429) và hàng Log.",
              "smashcourt-van-hanh-ai", "AI"),
        _rule("smashcourt-tadss-fallback", "TADSS dùng đường lùi > 20 %",
              '(sum(rate(tadss_fallbacks_total{job="tadss-runtime"}[10m])) or vector(0)) / sum(rate(tadss_decisions_total{job="tadss-runtime"}[10m]))',
              "gt", 0.2, "10m", "OK",
              f"{error_ratio} quyết định TADSS phải dùng đường lùi (cọc 100 %) trong 10 phút (ngưỡng 20 %). Khách vẫn đặt được sân.",
              "mở dashboard TADSS xem lý do đường lùi (strategy lỗi, quá thời gian); kiểm PostgreSQL và Redis của TADSS.",
              "smashcourt-van-hanh-tadss", "TADSS"),
        _rule("smashcourt-gemini-quota", "Gemini hết giới hạn (429) liên tục",
              'sum(rate(gemini_request_duration_seconds_count{job="smashcourt-ai",outcome="quota"}[5m]))',
              "gt", 0, "10m", "OK",
              "Gemini trả 429 (hết giới hạn gói miễn phí) liên tục 10 phút; chatbot và gợi ý AI đang lỗi.",
              "đợi hạn mức hồi lại, hoặc đổi GEMINI_MODEL / khóa Gemini trong file bí mật rồi \"bash deploy/deploy.sh ai\".",
              "smashcourt-van-hanh-ai", "AI"),
    ]
    for rule in rules:
        rule["data"][0]["datasourceUid"] = prometheus_uid
        rule["annotations"]["dashboard_url"] = f"{grafana_url.rstrip('/')}/d/{rule['annotations']['dashboard_url']}"
    return rules


# ── Đẩy lên Grafana ──────────────────────────────────────────────────────────

def read_env() -> dict[str, str]:
    # Cùng thứ tự với deploy/env-file.sh: SMASHCOURT_ENV_FILE ⇒ smashcourt-deploy.env ở gốc workspace ⇒ ~/.smashcourt-deploy/.env (trong runner)
    root_file = Path(__file__).resolve().parents[3] / "smashcourt-deploy.env"
    default = root_file if root_file.is_file() else Path.home() / ".smashcourt-deploy" / ".env"
    path = Path(os.environ.get("SMASHCOURT_ENV_FILE") or default)
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
    # Grafana Cloud: kho chính là grafanacloud-<stack>-prom (grafanacloud-usage là số liệu tính phí, không phải của app)
    prometheus_uid = next((d["uid"] for d in prometheus if d.get("name", "").endswith("-prom")), prometheus[0]["uid"])

    if grafana.call("GET", f"/api/folders/{FOLDER_UID}", ok_missing=True) is None:
        grafana.call("POST", "/api/folders", {"uid": FOLDER_UID, "title": FOLDER_TITLE})
    for dashboard in DASHBOARDS:
        grafana.call("POST", "/api/dashboards/db", {"dashboard": build(dashboard), "folderUid": FOLDER_UID, "overwrite": True})
        print(f"dashboard: {dashboard.title}")

    grafana.call("PUT", f"/api/v1/provisioning/templates/{EMAIL_TEMPLATE_NAME}", {"template": EMAIL_TEMPLATE})
    print(f"mẫu email: {EMAIL_TEMPLATE_NAME}")
    contact = {"uid": CONTACT_POINT, "name": CONTACT_POINT, "type": "email", "settings": {
        "addresses": email, "singleEmail": True,
        "subject": '{{ template "smashcourt.subject" . }}', "message": '{{ template "smashcourt.message" . }}'}}
    existing = [c for c in grafana.call("GET", f"/api/v1/provisioning/contact-points?name={CONTACT_POINT}") or [] if c.get("uid") == CONTACT_POINT]
    if existing:
        grafana.call("PUT", f"/api/v1/provisioning/contact-points/{CONTACT_POINT}", contact)
    else:
        grafana.call("POST", "/api/v1/provisioning/contact-points", contact)
    print(f"điểm nhận cảnh báo: {CONTACT_POINT} (email)")

    # Một lệnh PUT cả nhóm: tạo/cập nhật mọi luật theo uid, chu kỳ đánh giá 60 giây (khớp chu kỳ gửi số liệu)
    rules = alert_rules(prometheus_uid, url)
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
