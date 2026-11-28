# Dashboard "Vận hành" của bản deploy (Epic 6C, gói 6C-3a; P6-49): 3 dashboard BE / TADSS / AI nối với nhau bằng link như 3 tab.
# Nguồn chuẩn là file này (không sửa JSON trên Grafana rồi quên đưa về); grafana_cloud.py dựng JSON, kiểm và đẩy lên Grafana Cloud.
# Mỗi panel có mô tả 6 ý (P6-49, plan 6C Mục 5.1): đo gì, đơn vị, cách đọc, ngưỡng + nguồn ngưỡng, metric nguồn, mã test.
#
# Bố cục chung của cả 3 tab (đọc từ trên xuống, chuyển tab vẫn cùng thứ tự):
#   1. Hàng KPI (luôn hiện): 6 ô bằng nhau — Sẵn sàng, Phiên bản, Khởi động, rồi 3 con số chính của service, có đường xu hướng.
#   2. Các nhóm chi tiết có tiêu đề (hàng chia đều độ rộng): lưu lượng/độ trễ, nghiệp vụ hoặc phụ thuộc, tài nguyên.
#   3. "Log" và "Cách đọc" thu gọn mặc định, bấm mới mở.
# Request và độ trễ bỏ /health (Docker và Alloy gọi liên tục, không phải người dùng).
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
    # BE: mạch ngắt lệnh đánh giá cọc (Integrations/Tadss/TadssCircuit, meter SmashCourt.Tadss, P6-57)
    "smashcourt_tadss_circuit_state": "smashcourt.tadss.circuit.state",
    "smashcourt_tadss_circuit_transitions_total": "smashcourt.tadss.circuit.transitions",
    # BE: kiểm tính đúng dữ liệu đặt sân (Jobs/BookingInvariantJob, meter SmashCourt.Booking)
    "smashcourt_booking_invariant_violations": "smashcourt.booking.invariant_violations",
    "smashcourt_booking_invariant_checked_bookings": "smashcourt.booking.invariant_checked_bookings",
    "smashcourt_booking_invariant_checks_total": "smashcourt.booking.invariant_checks",
    # BE: counter nghiệp vụ (Infrastructure/Telemetry/BookingFlowMetrics, meter SmashCourt.Booking)
    "smashcourt_booking_deposit_decisions_total": "smashcourt.booking.deposit_decisions",
    "smashcourt_payment_results_total": "smashcourt.payment.results",
    "smashcourt_booking_expirations_total": "smashcourt.booking.expirations",
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

NOT_HEALTH = 'http_route!~"/health.*"'
NO_REQUESTS = "Chưa có request"
TADSS_OFF = "Chưa có quyết định (cờ TADSS đang tắt)"


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
    kind: str  # stat | timeseries | state-timeline | logs
    targets: list[tuple[str, str]]  # (biểu thức, nhãn chú giải)
    unit: str
    description: Description
    width: int = 8
    height: int = 8
    thresholds: list[tuple[float, str]] = field(default_factory=list)  # (từ giá trị, màu); có ⇒ ô KPI tô màu theo ngưỡng
    text_mode: str = "value"  # stat: value | name (hiện nhãn, vd tag image) | value_and_name
    mappings: list[dict] = field(default_factory=list)
    soft_max: float | None = None  # trục Y từ 0 tới giá trị này (tự giãn khi vượt): tỉ lệ 0 % không bị vẽ trục vô nghĩa
    sparkline: bool = False  # stat: vẽ đường xu hướng mờ phía sau con số
    no_value: str = ""  # chữ hiện khi chưa có số liệu (thay "No data"), nói rõ vì sao
    instant: bool = False  # stat: truy vấn tức thời (chỉ chuỗi còn sống lúc xem), không cả khoảng thời gian của dashboard


@dataclass
class Section:
    title: str | None  # None: hàng KPI đầu trang, không có tiêu đề nhóm
    panels: list[Panel]
    collapsed: bool = False


@dataclass
class Dashboard:
    uid: str
    title: str
    guide: str  # nội dung nhóm thu gọn "Cách đọc tab này"
    sections: list[Section]

    @property
    def panels(self) -> list[Panel]:
        return [panel for section in self.sections for panel in section.panels]


# ── Khối dùng chung ──────────────────────────────────────────────────────────

KPI_WIDTH = 4
KPI_HEIGHT = 4


def _kpi_identity(job: str, container: str) -> list[Panel]:
    """3 ô đầu hàng KPI, giống nhau ở mọi tab: sẵn sàng, phiên bản, khởi động"""
    return [
        Panel(
            "Sẵn sàng",
            "stat",
            [(f'max(probe_success{{job="integrations/blackbox/{job}"}})', "")],
            "none",
            Description(
                what="Alloy gọi endpoint health của service từ trong mạng Docker mỗi 60 giây (không qua ngrok).",
                unit="SẴN SÀNG = trả 2xx; LỖI = không trả 2xx trong 5 giây.",
                read="Xanh là bình thường. Đỏ = service dừng hoặc DB của nó mất. 'Mất số liệu' = máy deploy tắt hoặc Alloy dừng.",
                threshold="Lỗi trong 2 phút ⇒ email 'Service không sẵn sàng' (luật smashcourt-down).",
                source="probe_success (Alloy prometheus.exporter.blackbox)",
                tests="OPS-04",
            ),
            width=KPI_WIDTH,
            height=KPI_HEIGHT,
            thresholds=[(0, "red"), (1, "green")],
            mappings=[{"type": "value", "options": {"0": {"text": "LỖI"}, "1": {"text": "SẴN SÀNG"}}}],
            no_value="Mất số liệu",
        ),
        Panel(
            "Phiên bản",
            "stat",
            # Chỉ hiện tag (sha-<7>): tách phần sau dấu ':' của nhãn image. Tức thời + topk(1) theo lúc khởi động: sau một lần CD, khoảng
            # thời gian của dashboard còn chuỗi của container cũ (image khác) ⇒ ô từng hiện hai tag (TADSS 04/11: sha-3b38baf sha-c5cc1a7)
            [(f'label_replace(topk(1, max by (image) (container_start_time_seconds{{name="{container}"}})), "tag", "$1", "image", ".*:(.*)")',
              "{{tag}}")],
            "none",
            Description(
                what="Tag image của container đang chạy (đọc từ Docker, không do app tự báo).",
                unit="sha-<7> = 7 ký tự đầu của commit đã build.",
                read="So với commit mới nhất của nhánh trên GitHub: lệch nghĩa là CD chưa chạy xong hoặc đã quay về bản trước.",
                threshold="Không có ngưỡng; CD tự quay về khi bản mới hỏng (deploy/cd.sh).",
                source="container_start_time_seconds, nhãn image (Alloy prometheus.exporter.cadvisor)",
                tests="OPS-05, OPS-08",
            ),
            width=KPI_WIDTH,
            height=KPI_HEIGHT,
            text_mode="name",
            instant=True,
            no_value="Container không chạy",
        ),
        Panel(
            "Khởi động",
            "stat",
            [(f'max(container_start_time_seconds{{name="{container}"}}) * 1000', "")],
            "dateTimeFromNow",
            Description(
                what="Lần container khởi động gần nhất (mỗi lần CD triển khai hoặc Docker khởi động lại).",
                unit="Thời gian tương đối.",
                read="Khởi động lại liên tục mà không có lần push nào ⇒ service sập rồi Docker bật lại (mở nhóm Log).",
                threshold="Không có ngưỡng.",
                source="container_start_time_seconds (cAdvisor)",
                tests="OPS-09",
            ),
            width=KPI_WIDTH,
            height=KPI_HEIGHT,
            no_value="Container không chạy",
        ),
    ]


def _kpi_http(job: str, tests: str, p95_thresholds: list[tuple[float, str]], p95_source: str) -> list[Panel]:
    """3 ô KPI lưu lượng của service HTTP (BE, AI): request/phút, lỗi 5xx, p95"""
    sel = f'job="{job}",{NOT_HEALTH}'
    return [
        Panel(
            "Request / phút",
            "stat",
            [(f"sum(rate(http_server_request_duration_seconds_count{{{sel}}}[5m])) * 60", "")],
            "reqpm",
            Description(
                what="Số request người dùng gửi tới service, trung bình 5 phút (không tính /health).",
                unit="Request mỗi phút.",
                read="Cho biết có ai đang dùng; lỗi và độ trễ chỉ có nghĩa khi con số này khác 0.",
                threshold="Không có ngưỡng.",
                source="http_server_request_duration_seconds_count",
                tests=tests,
            ),
            width=KPI_WIDTH,
            height=KPI_HEIGHT,
            sparkline=True,
            no_value="0",
        ),
        Panel(
            "Lỗi 5xx",
            "stat",
            [(
                f'(sum(rate(http_server_request_duration_seconds_count{{{sel},http_response_status_code=~"5.."}}[5m])) or vector(0))'
                f" / clamp_min(sum(rate(http_server_request_duration_seconds_count{{{sel}}}[5m])), 1e-9)",
                "",
            )],
            "percentunit",
            Description(
                what="Phần trăm request trả lỗi 5xx (lỗi phía server), cửa sổ 5 phút.",
                unit="Phần trăm.",
                read="0 % là bình thường. Tăng ⇒ xem 'Lỗi theo mã' rồi mở nhóm Log.",
                threshold="Vàng > 1 %; đỏ > 5 % và > 5 % trong 5 phút ⇒ email (plan 6C Mục 5.2).",
                source="http_server_request_duration_seconds_count, nhãn http_response_status_code",
                tests=tests,
            ),
            width=KPI_WIDTH,
            height=KPI_HEIGHT,
            thresholds=[(0, "green"), (0.01, "yellow"), (0.05, "red")],
            sparkline=True,
            no_value="0 %",
        ),
        Panel(
            "Độ trễ p95",
            "stat",
            [(f"histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket{{{sel}}}[5m])))", "")],
            "s",
            Description(
                what="95 % request (mọi endpoint, trừ /health) xong trong thời gian này, đo ở server (không gồm mạng/ngrok).",
                unit="Giây; ước lượng từ bucket histogram.",
                read="Ít request thì p95 dao động mạnh; xem 'Độ trễ p95 theo endpoint' để biết endpoint nào chậm.",
                threshold=p95_source,
                source="http_server_request_duration_seconds_bucket",
                tests=tests,
            ),
            width=KPI_WIDTH,
            height=KPI_HEIGHT,
            thresholds=p95_thresholds,
            sparkline=True,
            no_value="Chưa có",
        ),
    ]


def _traffic_section(job: str, tests: str) -> Section:
    sel = f'job="{job}",{NOT_HEALTH}'
    return Section("Lưu lượng và độ trễ theo endpoint", [
        Panel(
            "Request theo endpoint",
            "timeseries",
            [(f"sum by (http_route) (rate(http_server_request_duration_seconds_count{{{sel}}}[5m])) * 60", "{{http_route}}")],
            "reqpm",
            Description(
                what="Số request mỗi endpoint, trung bình 5 phút (không tính /health).",
                unit="Request mỗi phút.",
                read="Biết endpoint nào được dùng nhiều nhất; dòng không tên là request tới đường dẫn không tồn tại (404).",
                threshold="Không có ngưỡng.",
                source="http_server_request_duration_seconds_count, nhãn http_route",
                tests=tests,
            ),
            no_value=NO_REQUESTS,
        ),
        Panel(
            "Độ trễ p95 theo endpoint",
            "timeseries",
            [(f"histogram_quantile(0.95, sum by (le, http_route) (rate(http_server_request_duration_seconds_bucket{{{sel}}}[5m])))", "{{http_route}}")],
            "s",
            Description(
                what="p95 thời gian xử lý của từng endpoint, đo ở server.",
                unit="Giây.",
                read="Một endpoint vọt lên ⇒ xem trace của endpoint đó (Explore › Tempo).",
                threshold="Như ô 'Độ trễ p95' của hàng KPI.",
                source="http_server_request_duration_seconds_bucket, nhãn http_route",
                tests=tests,
            ),
            no_value=NO_REQUESTS,
        ),
        Panel(
            "Lỗi theo mã",
            "timeseries",
            [(
                f'sum by (http_response_status_code) (rate(http_server_request_duration_seconds_count{{{sel},http_response_status_code=~"4..|5.."}}[5m])) * 60',
                "{{http_response_status_code}}",
            )],
            "reqpm",
            Description(
                what="Request trả 4xx (lỗi phía người gọi: chưa đăng nhập, sai dữ liệu) và 5xx (lỗi server), theo mã.",
                unit="Request mỗi phút.",
                read="401/400 rải rác là bình thường (hết phiên, nhập sai); 5xx là lỗi cần xem log.",
                threshold="5xx: như ô 'Lỗi 5xx'.",
                source="http_server_request_duration_seconds_count, nhãn http_response_status_code",
                tests=tests,
            ),
            no_value="Không có lỗi",
        ),
    ])


def _resource_panels(container: str, tests: str, width: int) -> list[Panel]:
    return [
        Panel(
            "CPU",
            "timeseries",
            [(f'sum(rate(container_cpu_usage_seconds_total{{name="{container}"}}[5m]))', "CPU")],
            "short",
            Description(
                what="CPU container dùng, trung bình 5 phút.",
                unit="Số lõi (1 = một lõi chạy hết công suất).",
                read="Tăng theo request là bình thường; cao kéo dài khi không có request ⇒ vòng lặp hoặc job nặng.",
                threshold="Theo dõi; > 1 lõi kéo dài là bất thường với tải demo.",
                source="container_cpu_usage_seconds_total (cAdvisor)",
                tests=tests,
            ),
            width=width,
            height=6,
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
            width=width,
            height=6,
        ),
    ]


def _logs_section(service: str, tests: str) -> Section:
    return Section("Log Warning / Error", [
        Panel(
            "Log Warning / Error",
            "logs",
            [(f'{{service_name="{service}"}} | detected_level=~"warn|error|fatal|critical"', "")],
            "none",
            Description(
                what="Log mức Warning trở lên của service, gửi qua OTLP.",
                unit="Dòng log.",
                read="Bấm một dòng để xem đủ thuộc tính (trace_id: mở trace tương ứng). Toàn bộ log: Explore › Loki › service_name.",
                threshold="Không có ngưỡng; dùng khi ô KPI báo bất thường.",
                source=f'Loki, service_name="{service}"',
                tests=tests,
            ),
            width=24,
            height=12,
        ),
    ], collapsed=True)


# ── BE ───────────────────────────────────────────────────────────────────────

BE = Dashboard(
    uid="smashcourt-van-hanh-be",
    title="SmashCourt Vận hành - BE",
    guide=(
        "1. **Hàng KPI**: Sẵn sàng phải xanh, Phiên bản phải đúng commit mới nhất; 3 ô sau chỉ có ý nghĩa khi có người dùng.\n"
        "2. **Lưu lượng**: endpoint nào được dùng, chậm hay lỗi.\n"
        "3. **Phụ thuộc**: BE gọi TADSS và AI chậm hoặc lỗi thì đặt sân vẫn chạy (cọc 100 %), gợi ý AI báo lỗi; pool PostgreSQL đầy thì mọi request chậm.\n"
        "4. Số liệu gửi 60 giây một lần nên trễ 1–2 phút. Rê chuột vào ⓘ cạnh tên panel để đọc mô tả đầy đủ.\n"
        "5. Request ngrok trong tháng (giới hạn 20.000): xem dashboard.ngrok.com (ngrok không có metric)."
    ),
    sections=[
        Section(None, [
            *_kpi_identity("be", "smashcourt-be"),
            *_kpi_http(
                "smashcourt-be", "OB-BE-1",
                [(0, "green"), (0.5, "yellow"), (1, "red")],
                "Vàng > 0,5 s, đỏ > 1 s: mức tham khảo tới khi chốt SLO BE (task 2.18, P6-52).",
            ),
        ]),
        _traffic_section("smashcourt-be", "OB-BE-1"),
        Section("Tính đúng dữ liệu đặt sân", [
            Panel(
                "Vi phạm tính đúng dữ liệu",
                "stat",
                [('sum(smashcourt_booking_invariant_violations{job="smashcourt-be"})', "vi phạm")],
                "none",
                Description(
                    what="Tổng số vi phạm ở lượt kiểm gần nhất của job kiểm tính đúng (10 phút một lần; mọi đơn còn hiệu lực cộng đơn tạo / sửa trong 24 giờ; bỏ đơn vừa đổi dưới 2 phút). "
                         "Cùng bộ luật với bộ test: đơn xác nhận đủ cọc, hóa đơn đã trả thu đủ, đơn chờ duyệt giữ sân, đơn hủy không giữ sân và có lý do, "
                         "dòng sân của đơn hiệu lực đang bật, không trùng giờ, đơn chờ thanh toán có hạn giữ chỗ.",
                    unit="Vi phạm (số đơn × điều).",
                    read="0 là dữ liệu đúng. Khác 0 ⇒ panel 'Lỗi dữ liệu đang có' cho biết loại lỗi, panel 'Đơn đang vi phạm' ngay dưới nêu mã đơn (BK-…) và lý do.",
                    threshold="Khác 0 ⇒ email 'Dữ liệu đặt sân sai' (đỏ). Chỉ tiêu của đề cương: 0 đơn hỏng.",
                    source="smashcourt_booking_invariant_violations (meter SmashCourt.Booking, BookingInvariantJob)",
                    tests="OB-BE-5, BE-IT-20",
                ),
                width=6,
                thresholds=[(0, "green"), (1, "red")],
                no_value="Chưa có lượt kiểm (job chạy 10 phút một lần)",
                instant=True,
            ),
            Panel(
                "Lỗi dữ liệu đang có",
                "timeseries",
                # Chỉ loại lỗi đang khác 0, chú giải là tên dễ hiểu (nhãn rule_name do BE gắn từ InvariantRules.Titles); rule_name!="" bỏ chuỗi cũ chưa có tên
                [('max by (rule_name) (smashcourt_booking_invariant_violations{job="smashcourt-be",rule_name!=""}) > 0', "{{rule_name}}")],
                "none",
                Description(
                    what="Số đơn đang sai theo từng loại lỗi ở lượt kiểm gần nhất, chỉ hiện loại đang có lỗi. Các loại: đơn đã xác nhận nhưng chưa thu đủ tiền cọc; "
                         "hóa đơn ghi đã trả nhưng thu chưa đủ; đơn chờ duyệt không còn giữ sân; đơn đã hủy vẫn giữ sân; đơn đã hủy không có lý do hủy; "
                         "đơn còn hiệu lực nhưng có sân bị tắt; hai đơn trùng sân, trùng giờ; đơn chờ thanh toán không có hạn giữ chỗ.",
                    unit="Vi phạm (một đơn có thể sai nhiều chỗ).",
                    read="Trống ('Không có lỗi nào') là dữ liệu đúng. Có đường nào là loại lỗi đó đang tồn tại; mã đơn và lý do cụ thể ở panel 'Đơn đang vi phạm' ngay dưới. "
                         "Sửa xong thì đường biến mất sau tối đa 10 phút. Đơn đã kết thúc hoặc đã hủy chỉ được soát trong 24 giờ sau lần sửa cuối, "
                         "nên lỗi của chúng có thể biến mất mà chưa được sửa: tra panel 'Đơn đang vi phạm' để chắc.",
                    threshold="Không có đường nào.",
                    source="smashcourt_booking_invariant_violations, nhãn rule_name (tên lỗi) và rule (mã điều, dùng cho cảnh báo)",
                    tests="OB-BE-5",
                ),
                width=6,
                soft_max=1,
                no_value="Không có lỗi nào",
            ),
            Panel(
                "Đơn trong phạm vi soát",
                "stat",
                [('max(smashcourt_booking_invariant_checked_bookings{job="smashcourt-be"})', "đơn")],
                "none",
                Description(
                    what="Số đơn lượt kiểm gần nhất đã soát: mọi đơn còn hiệu lực cộng đơn tạo hoặc sửa trong 24 giờ.",
                    unit="Đơn.",
                    read="0 vi phạm với 0 đơn nghĩa là chưa có gì để soát, không phải bằng chứng dữ liệu đúng.",
                    threshold="Không có ngưỡng.",
                    source="smashcourt_booking_invariant_checked_bookings",
                    tests="OB-BE-5",
                ),
                width=6,
                no_value="Chưa có lượt kiểm",
                instant=True,
            ),
            Panel(
                "Lượt kiểm theo kết quả (1 giờ)",
                "timeseries",
                [('sum by (outcome) (increase(smashcourt_booking_invariant_checks_total{job="smashcourt-be"}[1h]))', "{{outcome}}")],
                "none",
                Description(
                    what="Số lượt kiểm trong 1 giờ theo kết quả: clean (sạch), violations (có vi phạm), error (không đọc được DB).",
                    unit="Lượt mỗi giờ (bình thường khoảng 6).",
                    read="error > 0 ⇒ job không kiểm được, số vi phạm đang giữ của lượt trước; xem log '[Invariant] Không kiểm được'.",
                    threshold="error = 0; tổng khoảng 6 lượt mỗi giờ. 30 phút không có lượt thành công (BE vẫn chạy) ⇒ email 'Kiểm tính đúng dữ liệu không chạy được'.",
                    source="smashcourt_booking_invariant_checks_total, nhãn outcome",
                    tests="OB-BE-5",
                ),
                width=6,
                no_value="Chưa có lượt kiểm",
            ),
            Panel(
                "Đơn đang vi phạm",
                "logs",
                [('{service_name="smashcourt-be"} |= "[Invariant]" | detected_level=~"warn|error"', "")],
                "none",
                Description(
                    what="Log của job kiểm tính đúng khi có vi phạm: dòng 'Đơn BK-… (id) vi phạm <mã>: <tên lỗi>. Chi tiết: <lý do>' khi một vi phạm mới xuất hiện, "
                         "và mỗi lượt kiểm (10 phút) một dòng tổng 'Đơn đang vi phạm: <tên lỗi> (<mã>): BK-…, BK-…' nêu lại mọi đơn còn sai (tối đa 20 mã, còn lại chỉ đếm). "
                         "Dòng 'Không kiểm được' là lượt kiểm lỗi (không đọc được DB).",
                    unit="Dòng log, mới nhất ở trên.",
                    read="Dòng tổng mới nhất là danh sách đơn đang sai; tra mã đơn ở trang quản lý đặt sân rồi sửa theo lý do ở dòng 'vi phạm' của đơn đó. "
                         "Không có dòng nào trong khoảng thời gian đang xem nghĩa là không có vi phạm (lượt sạch ghi mức Information, không hiện ở đây). "
                         "Vi phạm cũ hơn khoảng đang xem vẫn có trong dòng tổng mới nhất; chi tiết lý do thì mở rộng khoảng thời gian.",
                    threshold="Không có dòng nào. Có dòng 'vi phạm' ⇒ cùng lúc có email 'Dữ liệu đặt sân sai'.",
                    source='Loki, service_name="smashcourt-be", dòng có "[Invariant]" mức Warning trở lên (Jobs/BookingInvariantJob)',
                    tests="OB-BE-5",
                ),
                width=24,
            ),
        ]),
        Section("Nghiệp vụ đặt sân", [
            Panel(
                "Quyết định cọc theo nhánh (1 giờ)",
                "timeseries",
                [('sum by (branch) (increase(smashcourt_booking_deposit_decisions_total{job="smashcourt-be"}[1h]))', "{{branch}}")],
                "none",
                Description(
                    what="Số đơn online theo nhánh quyết định cọc: tadss_deposit (cọc theo TADSS), need_approval (chờ Manager duyệt), rejected (TADSS từ chối), "
                         "full_payment_fallback (cọc 100 % do tắt cờ hoặc TADSS lỗi), confirm_without_online_payment (dưới mức tối thiểu, thu tại quầy).",
                    unit="Đơn mỗi giờ.",
                    read="full_payment_fallback tăng trong khi cờ đang bật ⇒ TADSS có sự cố (xem tab TADSS, mạch ngắt); rejected tăng vọt ⇒ xem dữ liệu lịch sử khách.",
                    threshold="Theo dõi xu hướng; không có ngưỡng cứng (bản deploy ít lưu lượng).",
                    source="smashcourt_booking_deposit_decisions_total, nhãn branch (meter SmashCourt.Booking)",
                    tests="OB-BE-6",
                ),
                width=8,
                no_value="Chưa có đơn online",
            ),
            Panel(
                "Kết quả thanh toán từ cổng (1 giờ)",
                "timeseries",
                [('sum by (outcome) (increase(smashcourt_payment_results_total{job="smashcourt-be"}[1h]))', "{{outcome}}")],
                "none",
                Description(
                    what="Kết quả IPN theo cách BE xử lý: succeeded, failed, already_recorded / stale_failure (gửi lặp hoặc link cũ), after_cancellation (tiền về sau khi hủy, "
                         "tạo hoàn 100 %), extra_payment (trả thêm), invalid_signature, amount_mismatch, unknown_transaction.",
                    unit="Kết quả mỗi giờ.",
                    read="invalid_signature hoặc amount_mismatch khác 0 ⇒ có request giả mạo hoặc cấu hình khóa VNPay sai; after_cancellation khác 0 ⇒ có tiền phải hoàn tại chi nhánh.",
                    threshold="invalid_signature = 0, amount_mismatch = 0.",
                    source="smashcourt_payment_results_total, nhãn outcome",
                    tests="OB-BE-6",
                ),
                width=8,
                no_value="Chưa có kết quả thanh toán",
            ),
            Panel(
                "Đơn bị job hủy (1 giờ)",
                "timeseries",
                [('sum by (reason) (increase(smashcourt_booking_expirations_total{job="smashcourt-be"}[1h]))', "{{reason}}")],
                "none",
                Description(
                    what="Số đơn job hủy vì hết hạn: payment_expired (giữ chỗ mà không thanh toán), approval_expired (Manager không duyệt kịp; không bao giờ tự duyệt).",
                    unit="Đơn mỗi giờ.",
                    read="approval_expired khác 0 ⇒ Manager đang để lỡ hạn duyệt; payment_expired tăng ⇒ xem trang thanh toán và VNPay.",
                    threshold="approval_expired nên bằng 0.",
                    source="smashcourt_booking_expirations_total, nhãn reason",
                    tests="OB-BE-6",
                ),
                width=8,
                no_value="Chưa có đơn bị hủy do hết hạn",
            ),
        ]),
        Section("Phụ thuộc: TADSS, AI, PostgreSQL", [
            Panel(
                "BE chờ TADSS / AI (p95)",
                "timeseries",
                [(
                    'histogram_quantile(0.95, sum by (le, server_address) (rate(http_client_request_duration_seconds_bucket{job="smashcourt-be",server_address=~"tadss|ai"}[5m])))',
                    "{{server_address}}",
                )],
                "s",
                Description(
                    what="Thời gian BE chờ TADSS (quyết định cọc) và AI (chat, gợi ý) trả lời, p95.",
                    unit="Giây.",
                    read="TADSS > 0,8 giây ⇒ BE hết thời gian chờ và cọc 100 %. AI chậm chủ yếu do Gemini (tab AI).",
                    threshold="TADSS: TadssConfig:TimeoutMs = 800 ms (BE tự chuyển sang cọc 100 %).",
                    source="http_client_request_duration_seconds_bucket, nhãn server_address",
                    tests="OB-BE-1, OB-BE-2",
                ),
                no_value="Chưa có lời gọi (TADSS chỉ được gọi khi bật cờ)",
                width=6,
            ),
            Panel(
                "Lời gọi TADSS / AI thất bại",
                "timeseries",
                [(
                    'sum by (server_address) (rate(http_client_request_duration_seconds_count{job="smashcourt-be",server_address=~"tadss|ai",error_type!=""}[5m])) * 60',
                    "{{server_address}}",
                )],
                "reqpm",
                Description(
                    what="Lời gọi TADSS / AI thất bại (không kết nối được, hết thời gian, trả 5xx).",
                    unit="Lần mỗi phút.",
                    read="Khác 0 ⇒ xem tab TADSS hoặc AI cùng thời điểm.",
                    threshold="Khác 0 kéo dài ⇒ kiểm service đó; service dừng hẳn thì có email 'không sẵn sàng'.",
                    source="http_client_request_duration_seconds_count, nhãn error_type",
                    tests="OB-BE-1",
                ),
                no_value="Không có lỗi",
                width=6,
            ),
            Panel(
                "Mạch gọi TADSS (0 đóng, 1 mở, 2 nửa mở)",
                "timeseries",
                [('max(smashcourt_tadss_circuit_state{job="smashcourt-be"})', "trạng thái")],
                "none",
                Description(
                    what="Trạng thái mạch ngắt lệnh đánh giá cọc của BE (P6-57): 0 đóng (gọi TADSS bình thường), 1 mở (TADSS không trả lời, đơn online cọc 100 % ngay), 2 nửa mở (một đơn đang được phép đi thử).",
                    unit="Mã trạng thái 0 / 1 / 2.",
                    read="Khác 0 ⇒ khách vẫn đặt được nhưng mọi đơn online cọc 100 %, không có quyết định của TADSS; xem tab TADSS và log 'Mạch gọi TADSS' của BE.",
                    threshold="Khác 0 quá 2 phút ⇒ email 'Mạch gọi TADSS của BE không đóng'. Mở khi 1 lần gọi lỗi và /health/ready cũng lỗi (300 ms), hoặc 3 lần gọi lỗi liên tiếp.",
                    source="smashcourt_tadss_circuit_state (meter SmashCourt.Tadss)",
                    tests="BE-UT-14, BE-IT-27",
                ),
                no_value="Chưa có số liệu (BE chưa xuất metric)",
                width=6,
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
                    what="Kết nối trong pool Npgsql của BE: đang dùng (used), rảnh (idle) và mức tối đa.",
                    unit="Kết nối.",
                    read="used chạm 'tối đa' ⇒ request phải chờ kết nối, độ trễ tăng.",
                    threshold="used gần tối đa (mặc định 100) là dấu hiệu nghẽn.",
                    source="db_client_connections_usage, db_client_connections_max",
                    tests="OB-BE-1",
                ),
                width=6,
            ),
        ]),
        Section("Tài nguyên", [
            *_resource_panels("smashcourt-be", "OB-BE-1", 6),
            Panel(
                "Heap .NET",
                "timeseries",
                [('sum(process_runtime_dotnet_gc_heap_size_bytes{job="smashcourt-be"})', "heap")],
                "bytes",
                Description(
                    what="Bộ nhớ heap .NET của BE sau lần GC gần nhất.",
                    unit="Byte.",
                    read="Tăng đều không giảm sau GC ⇒ nghi rò bộ nhớ (so với RAM).",
                    threshold="Theo dõi.",
                    source="process_runtime_dotnet_gc_heap_size_bytes",
                    tests="OB-BE-1",
                ),
                width=6,
                height=6,
            ),
            Panel(
                "Hàng đợi thread pool",
                "timeseries",
                [('max(process_runtime_dotnet_thread_pool_queue_length{job="smashcourt-be"})', "hàng đợi")],
                "short",
                Description(
                    what="Số việc đang chờ thread của .NET.",
                    unit="Việc.",
                    read="Khác 0 kéo dài ⇒ thiếu thread (thường do chờ đồng bộ), request chậm dần.",
                    threshold="> 0 kéo dài là bất thường.",
                    source="process_runtime_dotnet_thread_pool_queue_length",
                    tests="OB-BE-1",
                ),
                width=6,
                height=6,
                soft_max=10,
            ),
        ]),
        _logs_section("smashcourt-be", "OB-BE-1"),
    ],
)

# ── TADSS ────────────────────────────────────────────────────────────────────

_TADSS = 'job="tadss-runtime"'
_DECISIONS = f'{_TADSS},endpoint="POST api/v2/decisions"'

TADSS = Dashboard(
    uid="smashcourt-van-hanh-tadss",
    title="SmashCourt Vận hành - TADSS",
    guide=(
        "1. Số liệu quyết định chỉ có khi BE bật `TadssConfig:EnableTadssDecision` (đang **tắt**); khi tắt, chỉ hàng KPI đầu, breaker và tài nguyên có số.\n"
        "2. **Đường lùi** cao nghĩa là strategy lỗi/chậm và TADSS trả cọc 100 %.\n"
        "3. SLO của TADSS: SC1 p95 < 150 ms, SC2 p99 ≤ 700 ms (P6-52).\n"
        "4. Dashboard chi tiết hơn (shadow, override, k6) chạy local: `docker compose up -d` trong TADSS-Runtime."
    ),
    sections=[
        Section(None, [
            *_kpi_identity("tadss", "tadss-runtime"),
            Panel(
                "Quyết định / phút",
                "stat",
                [(f"sum(rate(tadss_decisions_total{{{_TADSS}}}[5m])) * 60", "")],
                "short",
                Description(
                    what="Số quyết định cọc TADSS trả, trung bình 5 phút.",
                    unit="Quyết định mỗi phút.",
                    read="0 khi cờ TADSS tắt hoặc không có ai đặt sân online.",
                    threshold="Không có ngưỡng.",
                    source="tadss_decisions_total",
                    tests="OB-1",
                ),
                width=KPI_WIDTH,
                height=KPI_HEIGHT,
                sparkline=True,
                no_value="Cờ tắt",
            ),
            Panel(
                "Đường lùi",
                "stat",
                [(
                    f"(sum(rate(tadss_fallbacks_total{{{_TADSS}}}[10m])) or vector(0)) / clamp_min(sum(rate(tadss_decisions_total{{{_TADSS}}}[10m])), 1e-9)",
                    "",
                )],
                "percentunit",
                Description(
                    what="Phần trăm quyết định phải dùng đường lùi cọc 100 % (strategy lỗi, quá ngân sách 500 ms, breaker mở).",
                    unit="Phần trăm, cửa sổ 10 phút.",
                    read="Gần 0 % là bình thường; tăng ⇒ xem nhóm 'Chịu lỗi'.",
                    threshold="Vàng > 5 %; đỏ > 20 % và > 20 % trong 10 phút ⇒ email (plan 6C Mục 5.2).",
                    source="tadss_fallbacks_total / tadss_decisions_total",
                    tests="OB-1, FI-01…10",
                ),
                width=KPI_WIDTH,
                height=KPI_HEIGHT,
                thresholds=[(0, "green"), (0.05, "yellow"), (0.2, "red")],
                sparkline=True,
                no_value="Cờ tắt",
            ),
            Panel(
                "Độ trễ p95",
                "stat",
                [(f"histogram_quantile(0.95, sum by (le) (rate(tadss_decision_duration_milliseconds_bucket{{{_DECISIONS}}}[5m])))", "")],
                "ms",
                Description(
                    what="95 % lần gọi POST /api/v2/decisions xong trong thời gian này (đo trong TADSS).",
                    unit="Mili giây.",
                    read="Dưới 150 ms là đạt SC1.",
                    threshold="SC1: p95 < 150 ms (P6-52, methodology).",
                    source="tadss_decision_duration_milliseconds_bucket",
                    tests="SC1, OB-1",
                ),
                width=KPI_WIDTH,
                height=KPI_HEIGHT,
                thresholds=[(0, "green"), (150, "red")],
                sparkline=True,
                no_value="Cờ tắt",
            ),
        ]),
        Section("Quyết định", [
            Panel(
                "Quyết định theo trạng thái",
                "timeseries",
                [(f"sum by (status) (rate(tadss_decisions_total{{{_TADSS}}}[5m])) * 60", "{{status}}")],
                "short",
                Description(
                    what="Số quyết định mỗi phút theo trạng thái (APPROVED, NEED_APPROVAL…).",
                    unit="Quyết định mỗi phút.",
                    read="NEED_APPROVAL tăng ⇒ Manager có thêm đơn phải duyệt (panel bên phải).",
                    threshold="Không có ngưỡng.",
                    source="tadss_decisions_total, nhãn status",
                    tests="OB-1",
                ),
                no_value=TADSS_OFF,
            ),
            Panel(
                "Độ trễ p95 / p99",
                "timeseries",
                [
                    (f"histogram_quantile(0.95, sum by (le) (rate(tadss_decision_duration_milliseconds_bucket{{{_DECISIONS}}}[5m])))", "p95"),
                    (f"histogram_quantile(0.99, sum by (le) (rate(tadss_decision_duration_milliseconds_bucket{{{_DECISIONS}}}[5m])))", "p99"),
                ],
                "ms",
                Description(
                    what="Thời gian TADSS xử lý POST /api/v2/decisions, p95 và p99.",
                    unit="Mili giây.",
                    read="p95 dưới 150 ms là đạt SC1; p99 dưới 700 ms là đạt SC2.",
                    threshold="SC1 p95 < 150 ms; SC2 p99 ≤ 700 ms (P6-52, methodology).",
                    source="tadss_decision_duration_milliseconds_bucket",
                    tests="SC1, SC2, OB-1",
                ),
                no_value=TADSS_OFF,
            ),
            Panel(
                "Đơn chờ Manager duyệt",
                "timeseries",
                [(f"max by (branch) (tadss_pending_approvals{{{_TADSS}}})", "{{branch}}")],
                "short",
                Description(
                    what="Số quyết định NEED_APPROVAL chưa được duyệt, theo chi nhánh.",
                    unit="Đơn.",
                    read="Tăng mà không giảm ⇒ Manager chưa xử lý; hết hạn duyệt thì đơn cọc 100 %.",
                    threshold="Không có ngưỡng.",
                    source="tadss_pending_approvals",
                    tests="OB-1",
                ),
                no_value="Không có đơn chờ duyệt",
            ),
        ]),
        Section("Chịu lỗi và cache", [
            Panel(
                "Circuit breaker của strategy",
                "state-timeline",
                [(f"max by (strategy) (tadss_circuit_state{{{_TADSS}}})", "{{strategy}}")],
                "none",
                Description(
                    what="Trạng thái breaker của từng strategy theo thời gian.",
                    unit="Đóng = bình thường; Mở = bỏ qua strategy, dùng đường lùi; Nửa mở = đang thử lại.",
                    read="Dải đỏ (Mở) kéo dài ⇒ strategy lỗi liên tục, mọi quyết định cọc 100 %.",
                    threshold="Khác 'Đóng' là bất thường.",
                    source="tadss_circuit_state",
                    tests="FI-03, OB-1",
                ),
                mappings=[{"type": "value", "options": {
                    "0": {"text": "Đóng", "color": "green"},
                    "1": {"text": "Mở", "color": "red"},
                    "2": {"text": "Nửa mở", "color": "yellow"},
                }}],
            ),
            Panel(
                "Đường lùi theo lý do",
                "timeseries",
                [(f"sum by (reason) (rate(tadss_fallbacks_total{{{_TADSS}}}[5m])) * 60", "{{reason}}")],
                "short",
                Description(
                    what="Số lần dùng đường lùi mỗi phút, theo lý do (lỗi strategy, quá ngân sách thời gian, breaker mở…).",
                    unit="Lần mỗi phút.",
                    read="Cho biết vì sao ô 'Đường lùi' tăng.",
                    threshold="Như ô 'Đường lùi'.",
                    source="tadss_fallbacks_total, nhãn reason",
                    tests="OB-1, FI-01…10",
                ),
                no_value="Không có đường lùi",
            ),
            Panel(
                "Tỉ lệ cache hit",
                "timeseries",
                [(
                    f'(sum(rate(tadss_cache_requests_total{{{_TADSS},result="hit"}}[10m])) or vector(0)) / sum(rate(tadss_cache_requests_total{{{_TADSS}}}[10m]))',
                    "hit",
                )],
                "percentunit",
                Description(
                    what="Phần trăm lần tra cache Redis có sẵn kết quả strategy. Bản deploy tắt cache của hai strategy quy tắc (P6-58: khóa cache chứa mã đơn nên không bao giờ trúng), nên panel trống là đúng.",
                    unit="Phần trăm, cửa sổ 10 phút.",
                    read="Trống: cache đang tắt (P6-58). Chỉ có số khi bật lại cache cho một strategy tính toán đắt; khi đó tụt về 0 đột ngột ⇒ Redis mất (TADSS vẫn chạy).",
                    threshold="Không có ngưỡng.",
                    source="tadss_cache_requests_total, nhãn result",
                    tests="OB-1",
                ),
                soft_max=1,
                no_value="Cache tắt cho strategy quy tắc (P6-58)",
            ),
        ]),
        Section("Tài nguyên", _resource_panels("tadss-runtime", "OB-1", 12)),
        _logs_section("tadss-runtime", "OB-1"),
    ],
)

# ── AI ───────────────────────────────────────────────────────────────────────

_AI = 'job="smashcourt-ai"'

AI = Dashboard(
    uid="smashcourt-van-hanh-ai",
    title="SmashCourt Vận hành - AI",
    guide=(
        "1. AI chỉ nhận request từ BE (chat, gợi ý, phân tích); mỗi request gọi Gemini một lần.\n"
        "2. **Gemini** chiếm gần hết thời gian xử lý; 'quota' là bị Google giới hạn (429), người dùng thấy AI tạm không khả dụng.\n"
        "3. Giới hạn gói miễn phí Gemini xem ở AI Studio; ô token cho biết đã dùng bao nhiêu trong 24 giờ."
    ),
    sections=[
        Section(None, [
            *_kpi_identity("ai", "smashcourt-ai"),
            *_kpi_http(
                "smashcourt-ai", "OB-AI-1",
                [(0, "green"), (10, "yellow"), (30, "red")],
                "Vàng > 10 s, đỏ > 30 s (gồm cả thời gian chờ Gemini). SLO phần xử lý riêng của AI (p95 < 300 ms, P6-52) đo ở bộ test với LLM giả.",
            ),
        ]),
        _traffic_section("smashcourt-ai", "OB-AI-1"),
        Section("Gemini", [
            Panel(
                "Lời gọi Gemini theo kết quả",
                "timeseries",
                [(f"sum by (outcome) (rate(gemini_request_duration_seconds_count{{{_AI}}}[5m])) * 60", "{{outcome}}")],
                "short",
                Description(
                    what="Số lời gọi Gemini mỗi phút theo kết quả: ok, quota (bị giới hạn 429), error (lỗi khác).",
                    unit="Lời gọi mỗi phút.",
                    read="quota xuất hiện ⇒ đã chạm giới hạn gói miễn phí, chờ vài phút hoặc đổi model.",
                    threshold="quota liên tục 10 phút ⇒ email (plan 6C Mục 5.2).",
                    source="gemini_request_duration_seconds_count, nhãn outcome",
                    tests="OB-AI-1",
                ),
                no_value="Chưa có lời gọi Gemini",
            ),
            Panel(
                "Độ trễ Gemini p95",
                "timeseries",
                [(
                    f'histogram_quantile(0.95, sum by (le, gemini_operation) (rate(gemini_request_duration_seconds_bucket{{{_AI},outcome="ok"}}[15m])))',
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
                no_value="Chưa có lời gọi Gemini",
            ),
            Panel(
                "Token 24 giờ",
                "stat",
                [(f"sum by (token_type) (increase(gemini_tokens_total{{{_AI}}}[24h]))", "{{token_type}}")],
                "short",
                Description(
                    what="Số token đã gửi (input) và nhận (output) trong 24 giờ qua.",
                    unit="Token.",
                    read="So với giới hạn ngày của model trong AI Studio để biết còn bao nhiêu.",
                    threshold="Giới hạn gói miễn phí theo model (AI Studio).",
                    source="gemini_tokens_total, nhãn token_type",
                    tests="OB-AI-1",
                ),
                width=4,
                text_mode="value_and_name",
                no_value="0",
            ),
            Panel(
                "Model (24 giờ)",
                "stat",
                [(f"sum by (gemini_model) (increase(gemini_request_duration_seconds_count{{{_AI}}}[24h]))", "{{gemini_model}}")],
                "short",
                Description(
                    what="Model Gemini được gọi trong 24 giờ qua và số lời gọi.",
                    unit="Lời gọi.",
                    read="Đổi model ở GEMINI_MODEL trong file bí mật rồi triển khai lại AI; ô đổi theo.",
                    threshold="Không có ngưỡng.",
                    source="gemini_request_duration_seconds_count, nhãn gemini_model",
                    tests="OB-AI-1",
                ),
                width=4,
                text_mode="value_and_name",
                no_value="Chưa có lời gọi",
            ),
        ]),
        Section("Tài nguyên", _resource_panels("smashcourt-ai", "OB-AI-1", 12)),
        _logs_section("smashcourt-ai", "OB-AI-1"),
    ],
)

DASHBOARDS = [BE, TADSS, AI]
