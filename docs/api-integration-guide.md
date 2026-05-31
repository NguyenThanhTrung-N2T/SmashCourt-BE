# Cẩm Nang Tích Hợp API SmashCourt - Toàn Bộ Hệ Thống (Comprehensive API Reference)

Tài liệu này được biên soạn chi tiết dành riêng cho đội ngũ phát triển Frontend. Toàn bộ các API, đầu vào (Input), đầu ra (Output), phân quyền (Role) và ví dụ JSON của tất cả **23 Controller** trên hệ thống Backend được tổng hợp đầy đủ tại đây.

---

## 📌 QUY ƯỚC CHUNG

### 1. Định dạng phản hồi mặc định (`ApiResponse<T>`)
```json
{
  "success": true, // true nếu thành công, false nếu gặp lỗi
  "data": { ... }, // Payload kết quả (DTO) hoặc null
  "message": "Thông điệp phản hồi từ hệ thống"
}
```

### 2. Định dạng phân trang (`PagedResult<T>`)
```json
{
  "items": [ ... ], 
  "page": 1,        
  "pageSize": 10,   
  "totalItems": 100,
  "totalPages": 10, 
  "hasNext": true,  
  "hasPrev": false  
}
```

### 3. Header bắt buộc cho API cần xác thực (Authenticated API)
```http
Authorization: Bearer <JWT_TOKEN>
Accept: application/json
Content-Type: application/json
```

---

## 🔑 PHẦN 1: XÁC THỰC & TÀI KHOẢN (AUTHENTICATION & PROFILE)

### 1. `AuthController` (Xác thực tài khoản hệ thống)
* **`POST /api/auth/register`** (Khách đăng ký)
  - **Request Body:** `{ "email", "password", "fullName", "phone" }`
* **`POST /api/auth/login`** (Đăng nhập)
  - **Request Body:** `{ "email", "password" }`
  - **Response (`data`):** `{ "token", "email", "role" ("CUSTOMER"|"STAFF"|"BRANCH_MANAGER"|"OWNER"), "fullName" }`
* **`POST /api/auth/forgot-password`** (Yêu cầu khôi phục mật khẩu qua Email)
* **`POST /api/auth/reset-password`** (Thiết lập lại mật khẩu với token từ email)
  - **Request Body:** `{ "email", "token", "newPassword" }`

### 2. `GoogleAuthController` (Xác thực bên thứ ba)
* **`POST /api/auth/google-login`** (Đăng nhập bằng Google)
  - **Request Body:** `{ "idToken" }`
  - **Response:** Trả về JWT Token và thông tin phân quyền tương tự đăng nhập thường.

### 3. `ProfileController` (Thông tin cá nhân người dùng hiện tại)
* **`GET /api/profile`** (Lấy thông tin cá nhân hiện tại)
* **`PUT /api/profile`** (Cập nhật thông tin cá nhân)
  - **Request Body:** `{ "fullName", "phone", "avatarUrl" }`
* **`POST /api/profile/change-password`** (Đổi mật khẩu)
  - **Request Body:** `{ "currentPassword", "newPassword" }`

---

## 🏢 PHẦN 2: CHI NHÁNH & QUẢN LÝ DỊCH VỤ (BRANCHES & SERVICES)

### 1. `BranchController` (Quản lý chi nhánh)
* **`GET /api/branches`** (Lấy danh sách chi nhánh)
  - **Query Params:** `?page=1&pageSize=10` (Trả về `PagedResult`)
* **`GET /api/branches/{id}`** (Chi tiết một chi nhánh kèm quản lý phụ trách)
* **`POST /api/branches`** [OWNER] (Tạo chi nhánh mới)
  - **Request Body:** `{ "name", "address", "phone", "avatarUrl", "latitude", "longitude", "openTime" (hh:mm:ss), "closeTime", "managerId" }`
* **`PUT /api/branches/{id}`** [OWNER] (Cập nhật chi nhánh)
* **`POST /api/branches/{id}/suspend`** [OWNER] (Tạm khóa hoạt động)
* **`POST /api/branches/{id}/activate`** [OWNER] (Mở khóa hoạt động)
* **`DELETE /api/branches/{id}`** [OWNER] (Vô hiệu hóa hoạt động chi nhánh)

### 2. `ServiceController` (Quản lý dịch vụ - Nước uống, thuê vợt, thuê bóng...)
* **`GET /api/services`** (Lấy danh sách dịch vụ hệ thống)
* **`GET /api/branches/{branchId}/services`** (Lấy dịch vụ thực tế tại chi nhánh)
* **`POST /api/branches/{branchId}/services`** [OWNER, BRANCH_MANAGER] (Bật dịch vụ + set giá riêng)
  - **Request Body:** `{ "serviceId", "price" }`
* **`PUT /api/branches/{branchId}/services/{serviceId}`** [OWNER, BRANCH_MANAGER] (Sửa giá dịch vụ tại quầy chi nhánh)
  - **Request Body:** `{ "price" }`
* **`DELETE /api/branches/{branchId}/services/{serviceId}`** [OWNER, BRANCH_MANAGER] (Tắt dịch vụ tại chi nhánh)

### 3. `UploadController` (Đăng tải ảnh lên máy chủ)
* **`POST /api/upload`** (Upload ảnh lẻ)
  - **Request:** FormData chứa trường `file` (File nhị phân).
  - **Response (`data`):** Trả về String URL (ví dụ: `"https://smashcourt.com/uploads/abc.jpg"`).

---

## 🏸 PHẦN 3: SÂN & PHÂN BỔ THỜI GIAN (COURTS, TYPES & TIME GRIDS)

### 1. `CourtTypeController` (Quản lý Loại sân - Sân trong nhà, sân đất nện, sân cỏ...)
* **`GET /api/court-types`** (Danh sách các loại sân hệ thống)
* **`POST /api/court-types`** [OWNER] (Tạo loại sân mới)
  - **Request Body:** `{ "name", "description" }`
* **`PUT /api/court-types/{id}`** [OWNER] (Cập nhật tên/mô tả loại sân)
* **`DELETE /api/court-types/{id}`** [OWNER] (Vô hiệu hóa loại sân)

### 2. `CourtController` (Quản lý Sân chơi & Sơ đồ trạng thái)
* **`GET /api/courts`** (Khách hàng xem danh sách sân trống của một chi nhánh)
  - **Query:** `?requestedBranchId=...&typeId=...`
* **`GET /api/courts/management-dashboard/stats`** [ADMIN/STAFF] (Thống kê số lượng sân theo trạng thái - Dùng cho Polling nhanh)
* **`GET /api/courts/management-dashboard/courts`** [ADMIN/STAFF] (Danh sách thẻ sân phân trang phục vụ vẽ sơ đồ sân)
  - **Query:** `?branchId=...&search=...&typeId=...&page=1&pageSize=10`
* **`GET /api/courts/management-timeline`** [ADMIN/STAFF] (Xem timeline ca chơi đầy đủ phục vụ thao tác tại quầy)
  - **Query:** `?branchId=...&date=2026-05-31&typeId=...`
* **`GET /api/courts/{id}/management-details`** [ADMIN/STAFF] (Chi tiết các ca đặt của sân theo ngày)
  - **Query:** `?date=2026-05-31`
* **`POST /api/courts`** [OWNER, BRANCH_MANAGER] (Tạo sân mới)
* **`PUT /api/courts/{id}`** [OWNER, BRANCH_MANAGER] (Sửa sân)
* **`POST /api/courts/{id}/suspend`** [OWNER, BRANCH_MANAGER] (Tạm ngưng sân)
* **`POST /api/courts/{id}/activate`** [OWNER, BRANCH_MANAGER] (Mở lại sân)
* **`DELETE /api/courts/{id}`** [OWNER, BRANCH_MANAGER] (Xóa sân)

### 3. `TimeSlotController` (Quản lý Ca chơi / Khung giờ)
* **`GET /api/timeslots`** (Lấy toàn bộ khung giờ hệ thống)
* **`POST /api/timeslots`** [OWNER] (Cấu hình thêm ca chơi mới)
  - **Request Body:** `{ "startTime": "06:00", "endTime": "07:00", "dayType": "WEEKDAY" }` // WEEKDAY, WEEKEND

### 4. `TimeGridController` (Tính toán khung giờ khả dụng cho đặt sân)
* **`GET /api/time-grid`** (Lấy danh sách các ô giờ trống để đặt sân)
  - **Query:** `?branchId=...&courtTypeId=...&date=2026-05-31`

---

## 💰 PHẦN 4: THIẾT LẬP CẤU HÌNH GIÁ SÂN (PRICE MANAGEMENT)

Hệ thống SmashCourt hỗ trợ mô hình giá phân tầng: **Giá Hệ Thống (System Price)** làm mặc định, và **Giá Chi Nhánh (Branch Price)** đè lên (Override).

### 1. `SystemPriceController` (Cấu hình giá chung cho toàn hệ thống)
* **`GET /api/system-prices/current`** (Lấy bảng giá hệ thống đang có hiệu lực)
* **`POST /api/system-prices`** [OWNER] (Thiết lập giá mới cho từng khung giờ và loại sân)
  - **Request Body:**
    ```json
    {
      "courtTypeId": "guid-loại-sân",
      "effectiveFrom": "2026-06-01",
      "prices": [
        { "timeSlotId": "guid-ca-chơi", "price": 120000.00 }
      ]
    }
    ```

### 2. `BranchPriceController` (Cấu hình giá riêng biệt cho từng chi nhánh)
* **`GET /api/branches/{branchId}/prices`** [OWNER] (Lịch sử cấu hình giá đè tại chi nhánh)
* **`GET /api/branches/{branchId}/prices/current`** [STAFF/MANAGER/OWNER] (Bảng giá thực tế đang áp dụng của chi nhánh sau khi fallback)
* **`GET /api/branches/{branchId}/prices/resolved`** (Lấy bảng giá thực tế theo một ngày cụ thể)
* **`POST /api/branches/{branchId}/prices`** [OWNER] (Thiết lập giá đè cho chi nhánh)
  - **Request Body:** Tương tự cấu hình giá hệ thống nhưng bổ sung ID chi nhánh ở URL.
* **`POST /api/branches/{branchId}/prices/calculate`** (Khách xem trước giá khi chọn đặt ca)
  - **Request Body:** `{ "courtTypeId", "date", "startTime", "endTime" }`
  - **Response (`data`):** `{ "totalPrice", "isPeakHours", "slots": [ ... ] }`

---

## 📅 PHẦN 5: NGHIỆP VỤ ĐẶT SÂN & THANH TOÁN (BOOKINGS, POLICIES & PAYMENTS)

### 1. `CustomerBookingController` (Khách hàng đặt sân qua Mobile/Web FE)
* **`GET /api/customer/bookings`** (Lịch sử đặt sân của tài khoản đang đăng nhập)
  - **Query:** `?page=1&pageSize=10`
* **`GET /api/customer/bookings/{id}`** (Chi tiết đơn đặt của khách hàng)
* **`POST /api/customer/bookings`** (Tạo đơn đặt sân trực tuyến)
  - **Request Body:**
    ```json
    {
      "branchId": "guid-chi-nhánh",
      "courtId": "guid-sân",
      "date": "2026-05-31",
      "startTime": "08:00",
      "endTime": "10:00",
      "paymentTiming": "PREPAID",
      "promotionCode": "GIAM20"
    }
    ```

### 2. `BookingController` (Quản trị đặt sân dành cho Staff/Manager làm tại quầy)
* **`GET /api/bookings`** [STAFF/MANAGER/OWNER] (Tìm kiếm và lọc toàn bộ đơn đặt sân)
  - **Query:** `?branchId=...&search=...&status=...&page=1&pageSize=10`
* **`GET /api/bookings/{id}`** [STAFF/MANAGER/OWNER] (Xem chi tiết đơn và hóa đơn kèm dịch vụ)
* **`POST /api/bookings`** [STAFF/MANAGER/OWNER] (Đặt sân hộ khách vãng lai tại quầy)
* **`POST /api/bookings/lock`** (Khóa giữ chỗ sân tạm thời trong 15 phút để làm thủ tục)
* **`POST /api/bookings/{id}/check-in`** [STAFF/MANAGER] (Xác nhận khách có mặt, chuyển đơn sang `IN_PROGRESS`)
* **`POST /api/bookings/{id}/cancel`** (Yêu cầu hủy đơn đặt sân)
* **`POST /api/bookings/{id}/checkout-postpaid`** [STAFF/MANAGER] (Thanh toán sau tại quầy cho khách)

### 3. `CancelPoliciesController` (Cấu hình chính sách hoàn trả tiền)
* **`GET /api/cancel-policies`** (Chính sách hủy sân: Hủy trước bao lâu được hoàn bao nhiêu %)
* **`POST /api/cancel-policies`** [OWNER] (Thiết lập tỷ lệ hoàn tiền)
  - **Request Body:** `{ "refundPercentage", "hoursBefore" }`

### 4. `PaymentController` (Xử lý giao dịch qua VNPay)
* **`POST /api/payments/create-vnpay-url`** (Tạo liên kết thanh toán sang ứng dụng VNPay)
  - **Request Body:** `{ "bookingId", "returnUrl" }`
* **`GET /api/payments/vnpay-ipn`** (VNPay gọi ngầm để đồng bộ hóa đơn - Chỉ Backend xử lý)
* **`GET /api/payments/vnpay-return`** (Redirect kết quả hiển thị trên FE)

---

## 👥 PHẦN 6: KHÁCH HÀNG & THÀNH VIÊN THÂN THIẾT (CUSTOMERS & LOYALTY)

### 1. `CustomerManagementController` (Quản lý khách hàng toàn hệ thống)
* **`GET /api/customers`** [STAFF/MANAGER/OWNER] (Danh sách khách hàng đăng ký thành viên)
  - **Query:** `?search=...&page=1&pageSize=10`
* **`GET /api/customers/{id}`** [STAFF/MANAGER/OWNER] (Xem chi tiết thông tin khách hàng kèm điểm tích lũy và thứ hạng)

### 2. `LoyaltyController` (Tích điểm và đổi thưởng)
* **`GET /api/loyalty/points-history`** (Lịch sử thay đổi điểm tích lũy của user hiện tại)
* **`POST /api/loyalty/redeem`** (Yêu cầu đổi điểm tích lũy lấy mã giảm giá)
  - **Request Body:** `{ "pointsToRedeem" }`

### 3. `LoyaltyTierController` (Cấu hình hạng thành viên - Đồng, Bạc, Vàng, Kim Cương)
* **`GET /api/loyalty/tiers`** (Danh sách các hạng thành viên đang cấu hình trên hệ thống)
* **`POST /api/loyalty/tiers`** [OWNER] (Thiết lập hạng thành viên mới)
  - **Request Body:** `{ "name", "minPoints", "discountPercentage" }`

---

## 🎫 PHẦN 7: KHUYẾN MÃI & BÁO CÁO (PROMOTIONS & REPORTS)

### 1. `PromotionController` (Quản lý mã giảm giá)
* **`GET /api/promotions`** [OWNER, BRANCH_MANAGER] (Danh sách mã khuyến mãi)
* **`POST /api/promotions`** [OWNER] (Tạo mã giảm giá mới)
  - **Request Body:** `{ "code", "discountPercentage", "maxDiscountAmount", "startDate", "endDate", "minOrderAmount", "branchId" }`
* **`POST /api/promotions/validate`** (Kiểm tra xem mã giảm giá có hợp lệ với giỏ hàng hiện tại không)
  - **Request Body:** `{ "code", "branchId", "orderAmount" }`
  - **Response (`data`):** `{ "isValid", "discountAmount", "message" }`

### 2. `ReportController` (Thống kê chi tiết)
* **`GET /api/reports/dashboard/owner`** [OWNER] (Dashboard tổng của chủ hệ thống)
* **`GET /api/reports/dashboard/manager`** [BRANCH_MANAGER, OWNER] (Dashboard của chi nhánh)
* **`GET /api/reports/revenue`** [BRANCH_MANAGER, OWNER] (Báo cáo chi tiết doanh thu sân và dịch vụ)
* **`GET /api/reports/bookings`** [BRANCH_MANAGER, OWNER] (Báo cáo tỷ lệ đặt sân, tỷ lệ hủy)
* **`GET /api/reports/courts/utilization`** [BRANCH_MANAGER, OWNER] (Báo cáo tỷ lệ trống và lấp đầy sân theo giờ)
* **`GET /api/reports/customers`** [BRANCH_MANAGER, OWNER] (Báo cáo tăng trưởng khách hàng)
* **`GET /api/reports/customers/top-spenders`** [BRANCH_MANAGER, OWNER] (Danh sách khách hàng chi tiêu lớn nhất)
* **`GET /api/reports/services`** [BRANCH_MANAGER, OWNER] (Báo cáo doanh số bán kèm dịch vụ)
* **`GET /api/reports/promotions`** [BRANCH_MANAGER, OWNER] (Báo cáo đánh giá hiệu quả khuyến mãi)

---

## 🤖 PHẦN 8: TRỢ LÝ AI (AI ASSISTANT SERVICES)

### 1. `AIController` (Trợ lý tư vấn và đề xuất)
* **`POST /api/ai/chat`** (Hỏi đáp tự nhiên với Chatbot AI)
  - **Request Body:** `{ "message", "sessionId" }`
* **`POST /api/ai/suggest/booking`** (AI đề xuất ca đặt sân tối ưu theo thói quen lịch sử)
  - **Request Body:** `{ "branchId" }`
* **`POST /api/ai/suggest/pricing`** [OWNER] (AI đề xuất điều chỉnh biểu phí giá để tối ưu hóa doanh thu)
  - **Request Body:** `{ "branchId", "fromDate", "toDate" }`
* **`POST /api/ai/suggest/promotions`** [OWNER] (AI đề xuất chiến dịch khuyến mãi cho khung giờ thấp điểm)
  - **Request Body:** `{ "branchId", "fromDate", "toDate" }`
* **`POST /api/ai/analytics/summary`** [OWNER, BRANCH_MANAGER] (AI phân tích và tóm tắt hiệu suất hoạt động kinh doanh)
* **`POST /api/ai/analytics/strategic`** [OWNER] (AI phân tích chiến lược phát triển hệ thống chi nhánh)

---

## 👥 PHẦN 9: QUẢN TRỊ TÀI KHOẢN (USER MANAGEMENT)

### 1. `UserManagementController` (Quản trị nhân sự toàn hệ thống)
* **`GET /api/users`** [OWNER] (Danh sách toàn bộ tài khoản nhân sự Admin/Manager/Staff)
  - **Query:** `?search=...&role=...&page=1&pageSize=10`
* **`POST /api/users`** [OWNER] (Tạo tài khoản nhân sự mới)
  - **Request Body:** `{ "email", "password", "fullName", "phone", "role" ("STAFF"|"BRANCH_MANAGER") }`
* **`POST /api/users/{id}/assign-branch`** [OWNER] (Gán nhân sự vào chi nhánh làm việc)
  - **Request Body:** `{ "branchId" }`
* **`DELETE /api/users/{id}`** [OWNER] (Khóa/Vô hiệu hóa tài khoản nhân viên)
