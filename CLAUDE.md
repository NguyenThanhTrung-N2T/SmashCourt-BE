# SmashCourt-BE — Backend API

ASP.NET Core 8 Web API (monolith) cho hệ thống đặt sân cầu lông SmashCourt. Thuộc workspace 4 repo — xem `../CLAUDE.md`.

## Stack
.NET 8 · EF Core 8 + Npgsql (PostgreSQL, enum map sang PG enum) · JWT trong HTTP-only cookie + Refresh Token rotation · Google OAuth ·
Hangfire (PostgreSQL storage) · SignalR (`/hubs/notifications`) · Cloudinary · VNPay (`VNPAY.NET`) · SMTP Gmail · AspNetCoreRateLimit · Polly ·
xUnit + Moq + Testcontainers. Root namespace `SmashCourt_BE`.

## Lệnh
```bash
dotnet restore
dotnet build SmashCourt-BE.slnx -c Release -nologo -v q        # build cả app + test project
dotnet test  SmashCourt-BE.slnx -c Release --no-build --filter "Category!=Integration"   # unit tests
dotnet test  SmashCourt-BE.slnx -c Release --no-build --filter "FullyQualifiedName~BookingServiceTests"
dotnet test  SmashCourt-BE.slnx -c Release --filter "Category=Integration"   # cần Docker Desktop
dotnet run                                                      # http://localhost:5179, Swagger /swagger
```
CI (`.github/workflows/ci.yml`): restore → build Release → test → build Docker image.
Baseline hiện có 2 warning (MSB3277 xung đột EF Relational trong test project, CS8629 trong `BookingServiceTests`) — không thêm warning mới.

## Kiến trúc (chi tiết: skill `smashcourt-be-architecture`)
```
Controllers/            → nhận request, lấy claim, gọi service, trả ApiResponse<T>
Services/ + IService/   → nghiệp vụ, validate, ném AppException(status, msg, ErrorCodes.X), map entity → DTO
  AccessControl/        → IBranchScopeResolver: phân quyền dữ liệu theo chi nhánh
  Internal/             → DTO nội bộ (dữ liệu chuẩn bị cho AI)
Repositories/ + IRepository/ → truy vấn EF Core, SaveChangesAsync
Data/                   → SmashCourtContext (DbSet theo Module 1–8, HasPostgresEnum), UnitOfWork
Models/Entities|Enums|ViewModels|Promotions
DTOs/{Feature}/         → Create{X}Dto, Update{X}Dto, {X}Dto, {X}ListQuery : PaginationQuery
Common/                 → ApiResponse<T>, AppException, ErrorCodes, PagedResult, PaginationQuery, Constants/SignalREvents
Configurations/         → AuthorizationPolicies (OwnerOnly, OwnerOrManager, ManagerOnly, StaffAndAbove, CustomerOnly, AnyAuthenticated), *Settings
Jobs/ + Interfaces/     → Hangfire jobs; lịch đăng ký trong Extensions/HangfireExtensions.cs (giờ VN)
Integrations/AI/        → IFastApiClient/FastApiClient (JSON snake_case) gọi SmashCourt-AI
Hubs/ Middlewares/ Helpers/ Factories/ Infrastructure/ Templates/
Program.cs              → toàn bộ DI (AddScoped<IX, X>), auth, CORS "AllowFrontend", rate limit, middleware
```
Module nghiệp vụ: Auth & User · Branch & Court · Pricing & TimeSlot · Service · Loyalty · Promotion · Booking & TimeGrid (SlotLock chống double booking) · Payment & Invoice (VNPay IPN) · Report · AI proxy.

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
