# SmashCourt-BE — Backend API

ASP.NET Core 8 Web API (monolith) cho hệ thống đặt sân cầu lông SmashCourt. Thuộc workspace 4 repo — xem `../CLAUDE.md`.

## Stack
.NET 8 · EF Core 8 + Npgsql (PostgreSQL, enum map sang PG enum) · JWT Bearer (access token FE giữ trong bộ nhớ; refresh token trong cookie HTTP-only `RefreshToken`) + Refresh Token rotation · Google OAuth ·
Hangfire (PostgreSQL storage) · SignalR (`/hubs/notifications`) · Cloudinary · VNPay (BE tự dựng và ký URL bằng `Helpers/VnPayHelper`, sau `IPaymentGateway`; `VNPAY.NET` chỉ còn trong project test để đối chứng chữ ký) · SMTP Gmail · AspNetCoreRateLimit · Polly ·
xUnit + Moq + Testcontainers + WireMock.Net (TADSS giả trong test HTTP). Root namespace `SmashCourt_BE`.

## Lệnh
```bash
dotnet restore
dotnet build SmashCourt-BE.slnx -c Release -nologo -v q        # build cả app + test project
dotnet test  SmashCourt-BE.slnx -c Release --no-build --filter "Category!=Integration"   # unit tests
dotnet test  SmashCourt-BE.slnx -c Release --no-build --filter "FullyQualifiedName~BookingServiceTests"
dotnet test  SmashCourt-BE.slnx -c Release --filter "Category=Integration"   # cần Docker Desktop
dotnet run                                                      # http://localhost:5179, Swagger /swagger
docker compose -f docker-compose.yml -f docker-compose.mailpit.yml up -d --build   # BE + ngrok + Mailpit cho đợt E2E/LT-04: email vào http://localhost:8025 thay Gmail (Smtp:EnableSsl=false chỉ ở file phụ)
```
CI (`.github/workflows/ci.yml`): restore → build Release → test → build Docker image.
Build hiện **0 warning** (đã dọn 20/10/2026: MSB3277 EF Relational và CS8629) — không thêm warning mới.
Thư mục test chia theo loại: `Unit/` (phản chiếu cây nguồn), `Integration/` (+ `Http/`), `TestData/` (hạ tầng dùng chung).
Integration test (`SmashCourt-BE.Tests/Integration/`) chạy trên **PostgreSQL 17 thật nạp đúng schema production** (Testcontainers, `postgres:17.6-alpine`, nạp bằng `psql` trong container) nên ràng buộc CHECK, exclusion, enum có mặt; fixture dùng chung ánh xạ enum với app (`NpgsqlEnumMappingExtensions.MapSmashCourtEnums`). Schema lấy từ bản sao `SmashCourt-BE.Tests/TestData/Database/schema_smashcourt.sql`; bản chuẩn là `TADSS-Runtime/docs/database/schema_smashcourt.sql` (dump từ Neon `tadss-dev`). Đổi schema DB ⇒ dump lại bản chuẩn rồi chép đè bản sao (`SchemaCopySyncTests` đỏ nếu lệch khi có thư mục TADSS-Runtime cạnh repo). Toàn bộ suite (unit + integration + HTTP, khoảng 2230 test ở cuối 6A-5) chạy khoảng 2,5 phút khi image đã tải; lần đầu tải image PostgreSQL. Một số test đối chiếu kết quả với bản gốc ở TADSS-Runtime (`export_smashcourt_bookings.sql`, JSON schema v2) và tự bỏ qua khi không có thư mục `TADSS-Runtime` cạnh repo (CI).

Test HTTP (`SmashCourt-BE.Tests/Integration/Http/`, Phase 6A-2a): chạy đúng `Program.cs` bằng `WebApplicationFactory<Program>` (môi trường `Testing`) trên **database riêng** `smashcourt_http_*` tạo từ cùng schema trong cùng container và xóa khi xong, nên dữ liệu commit thật không lẫn vào test rollback. Cấu hình đọc sớm (chuỗi kết nối, JWT, VNPay) đặt bằng biến môi trường trong factory; factory từ chối máy chủ không phải cục bộ. Hangfire tắt bằng `Hangfire:Enabled=false`; email ghi lại bằng `RecordingEmailService` (`EmailService.SendAsync` là `protected virtual`); TADSS là WireMock (`TadssStub`, response dựng từ mẫu schema v2); đăng nhập bằng JWT thật từ `ITokenService` (`HttpTestBase.CreateClientForAsync`). Giới hạn rate limit của host test rất cao, test về rate limit tự hạ bằng `SetSetting`. Host dựng một lần cho cả collection, mất vài giây ở test HTTP đầu tiên.

## Kiến trúc (chi tiết: skill `smashcourt-be-architecture`)
```
Controllers/            → nhận request, lấy claim, gọi service, trả ApiResponse<T>
Services/ + IService/   → nghiệp vụ, validate, ném AppException(status, msg, ErrorCodes.X), map entity → DTO
  AccessControl/        → IBranchScopeResolver: phân quyền dữ liệu theo chi nhánh
  Internal/             → DTO nội bộ (dữ liệu chuẩn bị cho AI)
  Deposit/              → luật thuần của luồng cọc (resolver, policy giữ chỗ, mapper), không I/O
  Cancellation/         → luật thuần của hủy (tiền hoàn, đã tới giờ, trả lượt khuyến mãi)
  (gốc)                 → thanh toán: IPaymentGateway (VnPayService), OnlinePaymentStarter, PaymentResultService dùng chung mọi cổng
Repositories/ + IRepository/ → truy vấn EF Core, SaveChangesAsync
Data/                   → SmashCourtContext (DbSet theo Module 1–8, HasPostgresEnum), UnitOfWork
Models/Entities|Enums|ViewModels|Promotions|Deposit|Cancellation|Payments
DTOs/{Feature}/         → Create{X}Dto, Update{X}Dto, {X}Dto, {X}ListQuery : PaginationQuery
Common/                 → ApiResponse<T>, AppException, ErrorCodes, PagedResult, PaginationQuery, Constants/SignalREvents
Configurations/         → AuthorizationPolicies (OwnerOnly, OwnerOrManager, ManagerOnly, StaffAndAbove, CustomerOnly, AnyAuthenticated), *Settings
Jobs/ + Interfaces/     → Hangfire jobs; lịch đăng ký trong Extensions/HangfireExtensions.cs (giờ VN)
Integrations/AI/        → IFastApiClient/FastApiClient (JSON snake_case) gọi SmashCourt-AI
Integrations/Tadss/     → ITadssClient/TadssClient (JSON camelCase, DateTimeOffset, không Polly) gọi TADSS v2; DTO riêng, kiểm theo JSON schema (bản sao ở Tests/TestData/Tadss/v2)
Hubs/ Middlewares/ Helpers/ Factories/ Infrastructure/ Templates/
Program.cs              → toàn bộ DI (AddScoped<IX, X>), auth, CORS "AllowFrontend", rate limit, middleware
```
Module nghiệp vụ: Auth & User · Branch & Court · Pricing & TimeSlot · Service · Loyalty · Promotion · Booking & TimeGrid (SlotLock chống double booking) · Cọc & duyệt cọc (TADSS, Phase 6) · Hủy (`BookingCancellationService` dùng chung mọi đường hủy) · Payment & Invoice (cổng thanh toán dùng chung, hiện VNPay; hoàn tiền mặt tại chi nhánh) · Report · AI proxy.

## Phase 6 (đang làm)
- Nhánh `feat/phase-6-deposit`; tài liệu duy nhất của phase: `../TADSS-Runtime/spec/PHASE6_DESIGN_SPEC.md`, plan từng gói `../TADSS-Runtime/spec/PHASE6_PLAN_6A-*.md`. Đặc tả API BE ↔ FE: `docs/api/DEPOSIT_APPROVAL_API.md`. Script DB: `docs/database/phase6_*.sql` (người dùng tự chạy lên Neon; bản dump schema đã cập nhật sẵn).
- Đã xong 6A-1…6A-5 (nền, đặt online hai transaction, duyệt cọc, thanh toán / hủy / check-out; 6A-5 ngày 27/10, commit `fd29a51`: Job-01 và job hết hạn duyệt `BookingApprovalJob` qua dịch vụ hủy chung (`ExpireUnpaidAsync` / `ExpireApprovalAsync`), job báo kết quả `TadssOutcomeJob` (20 đơn / 5 phút, `TadssOutcomeMapper`, `TadssOutcomeDelivery`), Job-02/Job-04 ghi có điều kiện, kiểm hạn duyệt lúc nhận trong `DepositDecisionResolver`). 6A-6 xong 31/10 (P6-27…P6-30): link VNPay hết hạn trước hạn thanh toán `PaymentLinkBufferMinutes` (2 phút), BE tự ký URL (`Helpers/VnPayHelper`, giờ VN), thanh toán lại còn dưới 3 phút ⇒ 400 `PAYMENT_HOLD_EXPIRED`; `vnpay/confirm` trả `bookingStatus` (N-36); bộ kiểm SC6 `SmashCourt-BE.Tests/Integration/Invariants/` tự chạy sau mỗi test HTTP (`HttpTestBase`; chỉ override `SkipInvariantCheckReason` khi cố ý để lại dữ liệu vi phạm) và chạy trên staging (`dotnet test --filter Category=Staging` kèm `SC6_CONNECTION_STRING`, `SC6_FROM_UTC`, `SC6_TO_UTC`, `TADSS_BASE_URL`, `TADSS_API_KEY`, `SC6_OUTPUT`); container đặt `DOTNET_SYSTEM_NET_DISABLEIPV6=1` (mạng Docker không có IPv6). Thử VNPay sandbox 28/10 và 30/10 (spec 10.1 bước 2). `docker-compose.yml` truyền mã terminal và HashSecret VNPay từ `.env`, đè `appsettings.Development.json`: hai nơi phải cùng giá trị; từ 31/10 truyền thêm `TADSS_BASE_URL` (URL ngrok của TADSS) và `TADSS_API_KEY` thành `TadssConfig:BaseUrl`/`ApiKey` (khi `dotnet run` thì `TadssConfig:ApiKey` nằm ở `appsettings.Development.json`). P6-31 (31/10): `BookingDto` thêm `CancelSource`, `CancelledByName` (chỉ khi `includeStaffOnlyFields`, nạp `CancelledByUser` trong `GetAllAsync`/`GetByIdWithDetailsAsync`) và `CancelledAt`. Hướng dẫn cho người làm FE: `../TADSS-Runtime/spec/PHASE6_FE_HANDOFF.md`. **Epic 6C gói 6C-1 (31/10):** image không chứa `appsettings.Development.json` (`.dockerignore`), chạy non-root (`USER $APP_UID`), có nhãn OCI `revision` = sha (`--build-arg GIT_SHA`), tên `ghcr.io/nguyenthanhtrung-n2t/smashcourt-be:${IMAGE_TAG:-local}`; compose local mount chỉ đọc file Development; `tools/ci/check-image.sh <image>` kiểm image; `Configurations/RequiredConfiguration` chặn khởi động khi thiếu DB/JWT/OTP (chỉ nêu tên khóa), cảnh báo khi thiếu khóa tính năng phụ; `/health/live`, `/health/ready` (PostgreSQL), `/health/dependencies` (TADSS, AI; luôn 200) ở `Extensions/HealthCheckExtensions.cs`. Cờ `TadssConfig:EnableTadssDecision` vẫn **tắt** (đủ điều kiện kỹ thuật; bật là quyết định của người dùng).
- Luật đã chốt cần nhớ khi sửa luồng booking/thanh toán:
  - Hoàn tiền luôn tiền mặt tại chi nhánh đặt sân; không gọi API hoàn của cổng. Mỗi Payment `SUCCESS` một Refund.
  - Nghiệp vụ thanh toán không gắn VNPay: cổng mới chỉ hiện thực `IPaymentGateway`; quy tắc thất bại / thành công / tiền về sau khi hủy / trả hai lần nằm ở `PaymentResultService`.
  - Mọi luồng chạm đơn **khóa dòng booking trước** Payment (`IBookingRepository.LockAsync` hoặc câu cập nhật có điều kiện đầu tiên) — sai thứ tự từng gây deadlock `40P01`.
  - Transaction dùng `TransactionScopeHelper.ReadCommitted()` kèm câu cập nhật có điều kiện (`ExecuteUpdateAsync`, không SQL thô); scope lồng phải cùng mức cô lập với scope ngoài.
  - Job đổi trạng thái booking xử lý từng đơn trong một transaction, câu điều kiện (hoặc khóa rồi đọc lại) chạy đầu tiên; thua thì bỏ qua, không ghi gì (P6-25). Không `SaveChanges` cả lô entity đã nạp từ trước: đè được thao tác của nhân viên vừa commit.
  - Outcome gửi TADSS là nhãn đánh giá: nhãn khách đến / không đến phải khớp `checked_in_at` (lệch thì giữ lại, log Error); `occurredAt` cố định (giờ kết thúc chơi, hoặc `cancelled_at`) để gửi lại cùng nội dung; lỗi hệ thống dừng cả chu kỳ (P6-26).
  - Đặt online: quyết định cọc mang mốc nhận `DecidedAtUtc`; `NEED_APPROVAL` có hạn đã qua lúc nhận hoặc sau giờ chơi ⇒ cọc 100 % (P6-24); transaction 2 ghi đơn bằng đúng mốc đó.
  - Thời gian: "bây giờ" lấy từ `TimeProvider`; giờ chơi (giờ tường VN) đổi bằng `DateTimeHelper.ToUtcFromVietnam`; DTO TADSS dùng `DateTimeOffset` có offset (P6-22).

## Điểm cần nhớ
- Mọi response bọc `ApiResponse<T>.Ok(data, "message tiếng Việt")`; lỗi nghiệp vụ = `AppException` + hằng trong `Common/ErrorCodes.cs` (FE xử lý theo `code`).
- Service/repo mới **phải** đăng ký DI trong `Program.cs` (build vẫn xanh nếu quên → chỉ lỗi lúc runtime).
- Route `api/{kebab-case-plural}`; role: `OWNER`, `BRANCH_MANAGER`, `STAFF`, `CUSTOMER`.
- `Migrations/` đang trống — schema thật ở `../TADSS-Runtime/docs/database/schema_smashcourt.sql`. Đổi entity/enum ⇒ báo người dùng cập nhật DB; không tự chạy `dotnet ef`.
- Đổi DTO/route ⇒ cập nhật `smashcourt-fe/src/api/*.api.ts` + types; đổi DTO AI ⇒ cập nhật `SmashCourt-AI/app/models/ai_schemas.py`.
- Không đọc/sửa `.env`, `appsettings.Development.json`, User Secrets.

## Quy tắc làm việc
- **Không tự ý `git commit` / `push` / `merge` / `rebase` / `reset`** — chỉ khi người dùng yêu cầu rõ ràng (xem `../.claude/rules/git-safety.md`).
- Sửa code xong **phải build lại + chạy test liên quan** và sửa hết lỗi trước khi báo xong (skill `build-verify`).
- Tạo file/class mới theo đúng bảng đặt tên trong skill `smashcourt-be-architecture`; bắt chước file cùng thư mục.
