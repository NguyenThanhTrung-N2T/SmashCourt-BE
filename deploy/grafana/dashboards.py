# Dashboard "Vận hành" của bản deploy (Epic 6C, gói 6C-3a; P6-49): 3 dashboard BE / TADSS / AI nối với nhau bằng link như 3 tab.
# Nguồn chuẩn là file này (không sửa JSON trên Grafana rồi quên đưa về); grafana_cloud.py dựng JSON, kiểm và đẩy lên Grafana Cloud.
# Mỗi panel có mô tả 6 ý (P6-49, plan 6C Mục 5.1): đo gì, đơn vị, cách đọc, ngưỡng + nguồn ngưỡng, metric nguồn, mã test.
from dataclasses import dataclass, field

TAG = "smashcourt-van-hanh"
FOLDER_TITLE = "SmashCourt - Vận hành"

# Tên metric trên Grafana Cloud (OTLP ⇒ Prometheus: dấu chấm thành gạch dưới, đơn vị giây thêm _seconds, counter thêm _total)
# và instrument gốc. grafana_cloud.py check từ chối PromQL dùng metric ngoài danh sách này (OB-BE-3).
KNOWN_METRICS = {
    # BE + AI: ASP.NET Core / FastAPI (semconv HTTP ổn định)
    "http_server_request_duration_seconds": "http.server.request.duration (BE: Microsoft.AspNetCore.Hosting; AI: opentelemetry-instrumentation-fastapi)",
    "http_client_request_duration_seconds": "http.client.request.duration (BE: System.Net.Http; AI: opentelemetry-instrumentation-httpx)",
    # BE: Npgsql
    "db_client_connections_usage": "db.client.connections.usage (Npgsql 8)",
    "db_client_connections_max": "db.client.connections.max (Npgsql 8)",
    # BE: runtime .NET (OpenTelemetry.Instrumentation.Runtime)
    "process_runtime_dotnet_gc_heap_size_bytes": "process.runtime.dotnet.gc.heap.size",
    "process_runtime_dotnet_thread_pool_queue_length": "process.runtime.dotnet.thread_pool.queue.length",
    # TADSS (TADSS.Application/Common/TadssMetricNames)
    "tadss_decisions_total": "tadss.decisions",
    "tadss_fallbacks_total": "tadss.fallbacks",
    "tadss_decision_duration_milliseconds": "tadss.decision.duration",
    "tadss_circuit_state": "tadss.circuit.state",
    "tadss_cache_requests_total": "tadss.cache.requests",
    "tadss_pending_approvals": "tadss.pending_approvals",
    # AI (app/services/telemetry.py)
    "gemini_request_duration_seconds": "gemini.request.duration",
    "gemini_tokens_total": "gemini.tokens",
    # Alloy: cAdvisor + blackbox (deploy/docker-compose.yml, configs.alloy)
    "container_cpu_usage_seconds_total": "cAdvisor",
    "container_memory_working_set_bytes": "cAdvisor",
    "container_start_time_seconds": "cAdvisor",
    "probe_success": "blackbox (health)",
    "probe_duration_seconds": "blackbox (health)",
}


@dataclass
class Description:
    what: str
    unit: str
    read: str
    threshold: str
    source: str
    tests: str

    def render(self) -> str:
        return "\n".join([
            f"**Đo gì:** {self.what}",
            f"**Đơn vị:** {self.unit}",
            f"**Cách đọc:** {self.read}",
            f"**Ngưỡng:** {self.threshold}",
            f"**Nguồn:** {self.source}",
            f"**Mã test:** {self.tests}",
        ])


@dataclass
class Panel:
    title: str
    kind: str  # stat | timeseries | logs
    targets: list[tuple[str, str]]  # (biểu thức, nhãn chú giải)
    unit: str
    description: Description
    width: int = 8
    height: int = 7
    thresholds: list[tuple[float, str]] = field(default_factory=list)  # (từ giá trị, màu)
    text_mode: str = "value"  # stat: value | name (hiện nhãn, vd phiên bản image)
    mappings: list[dict] = field(default_factory=list)


@dataclass
class Dashboard:
    uid: str
    title: str
    guide: str  # panel chữ "Cách đọc tab này"
    panels: list[Panel]


def _up_panel(job: str, container: str, tests: str) -> list[Panel]:
    """3 panel tổng quan giống nhau ở mọi tab: sẵn sàng, phiên bản đang chạy, lần khởi động gần nhất"""
    return [
        Panel(
            "Sẵn sàng",
            "stat",
            [(f'max(probe_success{{job="integrations/blackbox/{job}"}})', "")],
            "none",
            Description(
                what="Alloy gọi endpoint health của service từ trong mạng Docker mỗi 60 giây (không qua ngrok).",
                unit="1 = sẵn sàng, 0 = không trả 2xx trong 5 giây.",
                read="Xanh SẴN SÀNG là bình thường. Đỏ = service dừng hoặc DB của nó mất. Không có số = máy deploy tắt hoặc Alloy dừng.",
                threshold="0 trong 2 phút ⇒ cảnh báo email 'không sẵn sàng' (luật smashcourt-down-*).",
                source="probe_success (Alloy prometheus.exporter.blackbox)",
                tests=tests,
            ),
            width=4,
            height=5,
            thresholds=[(0, "red"), (1, "green")],
            mappings=[{"type": "value", "options": {"0": {"text": "KHÔNG SẴN SÀNG"}, "1": {"text": "SẴN SÀNG"}}}],
        ),
        Panel(
            "Image đang chạy",
            "stat",
            [(f'max by (image) (container_start_time_seconds{{name="{container}"}})', "{{image}}")],
            "none",
            Description(
                what="Image của container đang chạy (đọc từ Docker, không do app tự báo).",
                unit="Tên image kèm tag; tag sha-<7> là commit đã build.",
                read="So tag với commit mới nhất của nhánh trên GitHub: lệch nghĩa là CD chưa chạy hoặc đã quay về bản trước.",
                threshold="Không có ngưỡng; CD tự quay về nếu bản mới hỏng (deploy/cd.sh).",
                source="container_start_time_seconds, nhãn image (Alloy prometheus.exporter.cadvisor)",
                tests="OPS-05, OPS-08",
            ),
            width=6,
            height=5,
            text_mode="name",
        ),
        Panel(
            "Khởi động lúc",
            "stat",
            [(f'max(container_start_time_seconds{{name="{container}"}}) * 1000', "")],
            "dateTimeFromNow",
            Description(
                what="Thời điểm container khởi động gần nhất (mỗi lần CD triển khai hoặc Docker khởi động lại).",
                unit="Thời gian tương đối (vd '5 phút trước').",
                read="Khởi động lại liên tục mà không có lần push nào ⇒ service bị sập rồi Docker bật lại (xem log).",
                threshold="Không có ngưỡng.",
                source="container_start_time_seconds (cAdvisor)",
                tests="OPS-09",
            ),
            width=4,
            height=5,
        ),
    ]


def _resource_panels(container: str, tests: str) -> list[Panel]:
    return [
        Panel(
            "CPU",
            "timeseries",
            [(f'sum(rate(container_cpu_usage_seconds_total{{name="{container}"}}[5m]))', "CPU")],
            "short",
            Description(
                what="CPU container dùng, trung bình 5 phút.",
                unit="Số lõi (1 = một lõi chạy hết công suất).",
                read="Tăng theo lượng request là bình thường; cao kéo dài khi không có request ⇒ vòng lặp hoặc job nặng.",
                threshold="Theo dõi; > 1 lõi kéo dài là bất thường với tải demo.",
                source="container_cpu_usage_seconds_total (cAdvisor)",
                tests=tests,
            ),
            width=6,
        ),
        Panel(
            "RAM",
            "timeseries",
            [(f'max(container_memory_working_set_bytes{{name="{container}"}})', "RAM")],
            "bytes",
            Description(
                what="Bộ nhớ container đang dùng (working set, cách Docker tính để giới hạn).",
                unit="Byte.",
                read="Đi ngang là bình thường; tăng đều không giảm ⇒ nghi rò bộ nhớ.",
                threshold="Theo dõi; máy deploy có 8 GB cho Docker (plan 6C Mục 6).",
                source="container_memory_working_set_bytes (cAdvisor)",
                tests=tests,
            ),
            width=6,
        ),
    ]


def _logs_panel(service: str, tests: str) -> Panel:
    return Panel(
        "Log Warning / Error",
        "logs",
        [(f'{{service_name="{service}"}} | detected_level=~"warn|error|fatal|critical"', "")],
        "none",
        Description(
            what="Log mức Warning trở lên của service, gửi qua OTLP.",
            unit="Dòng log.",
            read="Bấm một dòng để xem đủ thuộc tính (trace_id: mở trace tương ứng). Toàn bộ log: Explore ⇒ Loki ⇒ service_name.",
            threshold="Không có ngưỡng; dùng khi panel khác báo bất thường.",
            source=f'Loki, service_name="{service}"',
            tests=tests,
        ),
        width=24,
        height=10,
    )


def _red_panels(job: str, tests: str, slo: str) -> list[Panel]:
    sel = f'job="{job}"'
    return [
        Panel(
            "Request mỗi giây",
            "timeseries",
            [(f"sum(rate(http_server_request_duration_seconds_count{{{sel}}}[5m]))", "request/giây")],
            "reqps",
            Description(
                what="Số request HTTP service nhận, trung bình 5 phút (BE tính cả /health do Docker và Alloy gọi; AI không tính /health).",
                unit="Request/giây.",
                read="Cho biết có ai đang dùng; các panel lỗi, độ trễ chỉ có nghĩa khi có request.",
                threshold="Không có ngưỡng.",
                source="http_server_request_duration_seconds_count",
                tests=tests,
            ),
        ),
        Panel(
            "Tỉ lệ lỗi 5xx",
            "timeseries",
            [(
                f'(sum(rate(http_server_request_duration_seconds_count{{{sel},http_response_status_code=~"5.."}}[5m])) or vector(0))'
                f" / sum(rate(http_server_request_duration_seconds_count{{{sel}}}[5m]))",
                "5xx",
            )],
            "percentunit",
            Description(
                what="Phần trăm request trả lỗi 5xx, cửa sổ 5 phút.",
                unit="Phần trăm.",
                read="0 % là bình thường. Tăng ⇒ mở panel log bên dưới tìm lỗi.",
                threshold="> 5 % trong 5 phút ⇒ cảnh báo email (plan 6C Mục 5.2).",
                source="http_server_request_duration_seconds_count, nhãn http_response_status_code",
                tests=tests,
            ),
            thresholds=[(0, "green"), (0.05, "red")],
        ),
        Panel(
            "Độ trễ p95 theo endpoint",
            "timeseries",
            [(
                f"histogram_quantile(0.95, sum by (le, http_route) (rate(http_server_request_duration_seconds_bucket{{{sel}}}[5m])))",
                "{{http_route}}",
            )],
            "s",
            Description(
                what="95 % request của mỗi endpoint xong trong thời gian này (đo ở server, không gồm mạng/ngrok).",
                unit="Giây; ước lượng từ bucket histogram nên chỉ chính xác tới biên bucket gần nhất.",
                read="So các endpoint với nhau; một endpoint vọt lên ⇒ xem trace của endpoint đó. Ít request thì p95 dao động mạnh.",
                threshold=slo,
                source="http_server_request_duration_seconds_bucket, nhãn http_route",
                tests=tests,
            ),
            width=24,
            height=8,
        ),
    ]


BE = Dashboard(
    uid="smashcourt-van-hanh-be",
    title="SmashCourt Vận hành - BE",
    guide=(
        "### Cách đọc tab BE\n"
        "1. Hàng đầu: **Sẵn sàng** phải xanh, **Image đang chạy** phải đúng commit mới nhất.\n"
        "2. **Request / lỗi / độ trễ**: chỉ có ý nghĩa khi có người dùng.\n"
        "3. **Phụ thuộc**: BE gọi TADSS và AI chậm hoặc lỗi thì đặt sân vẫn chạy (cọc 100 %), gợi ý AI báo lỗi.\n"
        "4. Số liệu gửi 60 giây một lần nên trễ tối đa khoảng 1–2 phút.\n"
        "Request ngrok trong tháng (giới hạn 20.000): xem trang dashboard.ngrok.com, ngrok không có metric để đưa vào đây."
    ),
    panels=[
        *_up_panel("be", "smashcourt-be", "OPS-04"),
        *_red_panels("smashcourt-be", "OB-BE-1", "SLO BE ở task 2.18 (P6-52), chưa chốt; dùng để so sánh tương đối."),
        Panel(
            "BE gọi TADSS / AI: p95",
            "timeseries",
            [(
                'histogram_quantile(0.95, sum by (le, server_address) (rate(http_client_request_duration_seconds_bucket{job="smashcourt-be",server_address=~"tadss|ai"}[5m])))',
                "{{server_address}}",
            )],
            "s",
            Description(
                what="Thời gian BE chờ TADSS (quyết định cọc) và AI (gợi ý, chat) trả lời, p95.",
                unit="Giây.",
                read="TADSS > 0,8 giây ⇒ BE hết thời gian chờ và cọc 100 %. AI chậm chủ yếu do Gemini (xem tab AI).",
                threshold="TADSS: TadssConfig:TimeoutMs = 800 ms (BE tự chuyển sang cọc 100 %).",
                source="http_client_request_duration_seconds_bucket, nhãn server_address",
                tests="OB-BE-1, OB-BE-2",
            ),
            width=12,
        ),
        Panel(
            "BE gọi TADSS / AI: lỗi",
            "timeseries",
            [(
                'sum by (server_address) (rate(http_client_request_duration_seconds_count{job="smashcourt-be",server_address=~"tadss|ai",error_type!=""}[5m]))',
                "{{server_address}}",
            )],
            "reqps",
            Description(
                what="Lời gọi TADSS / AI thất bại (không kết nối được, hết thời gian, trả 5xx).",
                unit="Lần/giây.",
                read="Khác 0 ⇒ xem tab TADSS hoặc AI cùng thời điểm.",
                threshold="Khác 0 kéo dài ⇒ kiểm service đó; cảnh báo 'không sẵn sàng' bắt trường hợp service dừng hẳn.",
                source="http_client_request_duration_seconds_count, nhãn error_type",
                tests="OB-BE-1",
            ),
            width=12,
        ),
        Panel(
            "Kết nối PostgreSQL",
            "timeseries",
            [
                ('sum by (state) (db_client_connections_usage{job="smashcourt-be"})', "{{state}}"),
                ('max(db_client_connections_max{job="smashcourt-be"})', "tối đa"),
            ],
            "short",
            Description(
                what="Số kết nối trong pool Npgsql của BE: đang dùng (used) và rảnh (idle).",
                unit="Kết nối.",
                read="used chạm 'tối đa' ⇒ request phải chờ kết nối, độ trễ tăng.",
                threshold="used gần tối đa (mặc định 100) là dấu hiệu nghẽn.",
                source="db_client_connections_usage, db_client_connections_max",
                tests="OB-BE-1",
            ),
            width=8,
        ),
        Panel(
            "Heap .NET",
            "timeseries",
            [
                ('sum(process_runtime_dotnet_gc_heap_size_bytes{job="smashcourt-be"})', "heap"),
            ],
            "bytes",
            Description(
                what="Bộ nhớ heap .NET của BE sau lần GC gần nhất.",
                unit="Byte.",
                read="Tăng đều không giảm sau GC ⇒ nghi rò bộ nhớ (so với panel RAM).",
                threshold="Theo dõi.",
                source="process_runtime_dotnet_gc_heap_size_bytes",
                tests="OB-BE-1",
            ),
            width=8,
        ),
        Panel(
            "Hàng đợi thread pool",
            "timeseries",
            [('max(process_runtime_dotnet_thread_pool_queue_length{job="smashcourt-be"})', "hàng đợi")],
            "short",
            Description(
                what="Số việc chờ thread của .NET.",
                unit="Việc.",
                read="Khác 0 kéo dài ⇒ thiếu thread (thường do chờ đồng bộ), request chậm dần.",
                threshold="> 0 kéo dài là bất thường.",
                source="process_runtime_dotnet_thread_pool_queue_length",
                tests="OB-BE-1",
            ),
            width=8,
        ),
        *_resource_panels("smashcourt-be", "OB-BE-1"),
        _logs_panel("smashcourt-be", "OB-BE-1"),
    ],
)

TADSS = Dashboard(
    uid="smashcourt-van-hanh-tadss",
    title="SmashCourt Vận hành - TADSS",
    guide=(
        "### Cách đọc tab TADSS\n"
        "1. Chỉ có số liệu quyết định khi BE bật `TadssConfig:EnableTadssDecision` (đang **tắt**); khi tắt, chỉ hàng đầu và tài nguyên có số.\n"
        "2. **Đường lùi** cao nghĩa là strategy lỗi/chậm và TADSS trả cọc 100 %.\n"
        "3. SLO của TADSS: SC1 p95 < 150 ms, SC2 p99 ≤ 700 ms (P6-52).\n"
        "Dashboard chi tiết hơn (shadow, override, k6) chạy local: `docker compose up -d` trong TADSS-Runtime."
    ),
    panels=[
        *_up_panel("tadss", "tadss-runtime", "OPS-04"),
        Panel(
            "Quyết định theo trạng thái",
            "timeseries",
            [('sum by (status) (rate(tadss_decisions_total{job="tadss-runtime"}[5m]))', "{{status}}")],
            "ops",
            Description(
                what="Số quyết định TADSS trả mỗi giây, theo trạng thái (APPROVED, NEED_APPROVAL…).",
                unit="Quyết định/giây.",
                read="NEED_APPROVAL tăng ⇒ Manager có thêm đơn phải duyệt (xem panel hàng chờ).",
                threshold="Không có ngưỡng.",
                source="tadss_decisions_total, nhãn status",
                tests="OB-1",
            ),
        ),
        Panel(
            "Tỉ lệ đường lùi (cọc 100 %)",
            "timeseries",
            [(
                '(sum(rate(tadss_fallbacks_total{job="tadss-runtime"}[10m])) or vector(0)) / sum(rate(tadss_decisions_total{job="tadss-runtime"}[10m]))',
                "đường lùi",
            )],
            "percentunit",
            Description(
                what="Phần trăm quyết định phải dùng đường lùi (strategy lỗi, quá ngân sách 500 ms, breaker mở).",
                unit="Phần trăm, cửa sổ 10 phút.",
                read="Gần 0 % là bình thường; tăng ⇒ xem panel breaker và log.",
                threshold="> 20 % trong 10 phút ⇒ cảnh báo email (plan 6C Mục 5.2).",
                source="tadss_fallbacks_total / tadss_decisions_total",
                tests="OB-1, FI-01…10",
            ),
            thresholds=[(0, "green"), (0.2, "red")],
        ),
        Panel(
            "Độ trễ quyết định p95 / p99",
            "timeseries",
            [
                ('histogram_quantile(0.95, sum by (le) (rate(tadss_decision_duration_milliseconds_bucket{job="tadss-runtime",endpoint="POST api/v2/decisions"}[5m])))', "p95"),
                ('histogram_quantile(0.99, sum by (le) (rate(tadss_decision_duration_milliseconds_bucket{job="tadss-runtime",endpoint="POST api/v2/decisions"}[5m])))', "p99"),
            ],
            "ms",
            Description(
                what="Thời gian TADSS xử lý POST /api/v2/decisions (đo trong TADSS).",
                unit="Mili giây.",
                read="p95 dưới 150 ms là đạt SC1; p99 dưới 700 ms là đạt SC2.",
                threshold="SC1 p95 < 150 ms; SC2 p99 ≤ 700 ms (P6-52, methodology).",
                source="tadss_decision_duration_milliseconds_bucket",
                tests="SC1, SC2, OB-1",
            ),
            thresholds=[(0, "green"), (150, "yellow"), (700, "red")],
        ),
        Panel(
            "Circuit breaker của strategy",
            "timeseries",
            [('max by (strategy) (tadss_circuit_state{job="tadss-runtime"})', "{{strategy}}")],
            "short",
            Description(
                what="Trạng thái breaker của từng strategy.",
                unit="0 = đóng (bình thường), 1 = mở (bỏ qua strategy, dùng đường lùi), 2 = nửa mở (đang thử lại).",
                read="Mở kéo dài ⇒ strategy lỗi liên tục, mọi quyết định cọc 100 %.",
                threshold="Khác 0 là bất thường.",
                source="tadss_circuit_state",
                tests="FI-03, OB-1",
            ),
        ),
        Panel(
            "Tỉ lệ cache hit",
            "timeseries",
            [(
                '(sum(rate(tadss_cache_requests_total{job="tadss-runtime",result="hit"}[10m])) or vector(0)) / sum(rate(tadss_cache_requests_total{job="tadss-runtime"}[10m]))',
                "hit",
            )],
            "percentunit",
            Description(
                what="Phần trăm lần tra cache Redis có sẵn kết quả strategy.",
                unit="Phần trăm, cửa sổ 10 phút.",
                read="Thấp khi mỗi đơn khác nhau là bình thường; tụt về 0 đột ngột ⇒ Redis mất (TADSS vẫn chạy).",
                threshold="Không có ngưỡng.",
                source="tadss_cache_requests_total, nhãn result",
                tests="OB-1",
            ),
        ),
        Panel(
            "Đơn chờ Manager duyệt",
            "timeseries",
            [('max by (branch) (tadss_pending_approvals{job="tadss-runtime"})', "{{branch}}")],
            "short",
            Description(
                what="Số quyết định NEED_APPROVAL chưa được duyệt, theo chi nhánh.",
                unit="Đơn.",
                read="Tăng mà không giảm ⇒ Manager chưa xử lý; hết hạn duyệt thì đơn cọc 100 %.",
                threshold="Không có ngưỡng.",
                source="tadss_pending_approvals",
                tests="OB-1",
            ),
        ),
        *_resource_panels("tadss-runtime", "OB-1"),
        _logs_panel("tadss-runtime", "OB-1"),
    ],
)

AI = Dashboard(
    uid="smashcourt-van-hanh-ai",
    title="SmashCourt Vận hành - AI",
    guide=(
        "### Cách đọc tab AI\n"
        "1. AI chỉ nhận request từ BE (chat, gợi ý, phân tích); mỗi request gọi Gemini một lần.\n"
        "2. **Gemini** chiếm gần hết thời gian xử lý; 'quota' là bị Google giới hạn (429), người dùng thấy AI tạm không khả dụng.\n"
        "3. Giới hạn gói miễn phí Gemini xem ở AI Studio; panel token cho biết đã dùng bao nhiêu."
    ),
    panels=[
        *_up_panel("ai", "smashcourt-ai", "OPS-04"),
        *_red_panels("smashcourt-ai", "OB-AI-1", "AI đề xuất p95 < 300 ms cho phần xử lý của AI, không gồm Gemini (P6-52, chốt ở 2.18)."),
        Panel(
            "Lời gọi Gemini theo kết quả",
            "timeseries",
            [('sum by (outcome) (rate(gemini_request_duration_seconds_count{job="smashcourt-ai"}[5m]))', "{{outcome}}")],
            "ops",
            Description(
                what="Số lời gọi Gemini mỗi giây theo kết quả: ok, quota (bị giới hạn 429), error (lỗi khác).",
                unit="Lời gọi/giây.",
                read="quota xuất hiện ⇒ đã chạm giới hạn gói miễn phí, chờ vài phút hoặc đổi model.",
                threshold="quota liên tục 10 phút ⇒ cảnh báo email (plan 6C Mục 5.2).",
                source="gemini_request_duration_seconds_count, nhãn outcome",
                tests="OB-AI-1",
            ),
            width=12,
        ),
        Panel(
            "Độ trễ Gemini p95",
            "timeseries",
            [(
                'histogram_quantile(0.95, sum by (le, gemini_operation) (rate(gemini_request_duration_seconds_bucket{job="smashcourt-ai",outcome="ok"}[15m])))',
                "{{gemini_operation}}",
            )],
            "s",
            Description(
                what="Thời gian Gemini trả lời các lời gọi thành công, p95, theo kiểu gọi (structured = JSON, text).",
                unit="Giây.",
                read="Vài giây là bình thường với LLM; tăng mạnh ⇒ phía Google chậm, không phải lỗi của AI.",
                threshold="Không có ngưỡng (phụ thuộc Google).",
                source="gemini_request_duration_seconds_bucket",
                tests="OB-AI-1",
            ),
            width=12,
        ),
        Panel(
            "Token Gemini trong 24 giờ",
            "stat",
            [('sum by (token_type) (increase(gemini_tokens_total{job="smashcourt-ai"}[24h]))', "{{token_type}}")],
            "short",
            Description(
                what="Số token đã gửi (input) và nhận (output) trong 24 giờ qua.",
                unit="Token.",
                read="So với giới hạn ngày của model trong AI Studio để biết còn bao nhiêu.",
                threshold="Giới hạn gói miễn phí theo model (xem AI Studio).",
                source="gemini_tokens_total, nhãn token_type",
                tests="OB-AI-1",
            ),
            width=12,
            text_mode="value_and_name",
        ),
        Panel(
            "Model đang dùng",
            "stat",
            [('sum by (gemini_model) (increase(gemini_request_duration_seconds_count{job="smashcourt-ai"}[24h]))', "{{gemini_model}}")],
            "short",
            Description(
                what="Model Gemini được gọi trong 24 giờ qua và số lời gọi.",
                unit="Lời gọi.",
                read="Đổi model trong file bí mật (GEMINI_MODEL) rồi triển khai lại AI; panel đổi theo.",
                threshold="Không có ngưỡng.",
                source="gemini_request_duration_seconds_count, nhãn gemini_model",
                tests="OB-AI-1",
            ),
            width=12,
            text_mode="value_and_name",
        ),
        *_resource_panels("smashcourt-ai", "OB-AI-1"),
        _logs_panel("smashcourt-ai", "OB-AI-1"),
    ],
)

DASHBOARDS = [BE, TADSS, AI]
