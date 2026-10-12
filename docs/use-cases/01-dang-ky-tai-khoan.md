# UC-01: Đăng ký tài khoản

## Bảng Đặc tả Use Case

### Thông tin cơ bản

| Thông tin | Nội dung |
|-----------|----------|
| **Tên Use case** | Đăng ký tài khoản |
| **Mã Use case** | UC-AUTH-01 |
| **Mô tả Use case** | Là khách truy cập, tôi muốn tạo mới tài khoản để có thể đặt sân, tích điểm loyalty và sử dụng các tính năng dành cho khách hàng của hệ thống. |
| **Tác nhân** | Khách truy cập (Guest) |
| **Mức độ ưu tiên** | Cao |
| **Điều kiện kích hoạt** | Người dùng chọn chức năng "Đăng ký" trên giao diện hệ thống. |

### Điều kiện tiên quyết
- Người dùng chưa đăng nhập vào hệ thống
- Người dùng có email hợp lệ chưa được sử dụng trong hệ thống
- Hệ thống email service đang hoạt động (để gửi OTP)

### Điều kiện cuối
- Tài khoản đã được tạo thành công trong hệ thống với trạng thái chưa xác thực email
- OTP xác thực đã được gửi đến email người dùng
- Sau khi xác thực OTP thành công, người dùng có thể đăng nhập với vai trò "Khách hàng" (Customer)

---

## Luồng chính

1. Người dùng truy cập màn hình **Đăng ký**.

2. Hệ thống hiển thị biểu mẫu đăng ký gồm các trường:
   - **Email** (*bắt buộc*)
   - **Mật khẩu** (*bắt buộc*)
   - **Họ và tên** (*bắt buộc*)
   - **Số điện thoại** (*tùy chọn*)
   - Link "Đã có tài khoản? Đăng nhập"

3. Người dùng nhập đầy đủ thông tin vào form.

4. Người dùng bấm nút **"Đăng ký"**.

5. Hệ thống kiểm tra tính hợp lệ của dữ liệu đầu vào:
   - Email đúng định dạng
   - Mật khẩu đạt yêu cầu (tối thiểu 8 ký tự, có ít nhất 1 chữ hoa, 1 số, 1 ký tự đặc biệt)
   - Họ tên không để trống
   - Số điện thoại đúng định dạng (nếu có nhập)

6. Hệ thống kiểm tra email chưa tồn tại trong database.

7. Hệ thống tạo tài khoản mới:
   - Normalize email (lowercase, trim)
   - Hash mật khẩu bằng BCrypt (cost factor = 12)
   - Normalize họ tên tiếng Việt (bỏ dấu)
   - Gán vai trò mặc định: `CUSTOMER`
   - Gán trạng thái: `ACTIVE`
   - Đặt `IsEmailVerified = false`

8. Hệ thống tạo OTP 6 chữ số ngẫu nhiên:
   - Hash OTP bằng HMAC-SHA256
   - Lưu OTP vào bảng `otp_codes` với thời gian hết hạn 5 phút
   - Loại OTP: `EMAIL_VERIFY`

9. Hệ thống gửi email chứa OTP đến địa chỉ email người dùng vừa đăng ký.

10. Hệ thống hiển thị thông báo: *"OTP đã được gửi đến email của bạn, có hiệu lực trong 5 phút"*

11. Hệ thống chuyển người dùng sang màn hình **Xác thực OTP**.

---

## Luồng thay thế

### (4a) Người dùng bấm vào "Đã có tài khoản? Đăng nhập"
→ Hệ thống chuyển sang màn hình **Đăng nhập** (UC-AUTH-02)

### (5a) Người dùng nhập thiếu trường thông tin bắt buộc
→ Hệ thống hiển thị thông báo lỗi validation cho từng trường:
- "Email không được để trống"
- "Mật khẩu không được để trống"  
- "Họ tên không được để trống"

→ Người dùng sửa lại thông tin và quay lại bước 4.

### (5b) Email không đúng định dạng
→ Hệ thống hiển thị: *"Email không hợp lệ"*
→ Người dùng sửa lại email và quay lại bước 4.

### (5c) Mật khẩu không đạt yêu cầu
→ Hệ thống hiển thị: *"Mật khẩu tối thiểu 8 ký tự, có ít nhất 1 chữ hoa, 1 số, 1 ký tự đặc biệt"*
→ Người dùng sửa lại mật khẩu và quay lại bước 4.

### (5d) Số điện thoại không đúng định dạng
→ Hệ thống hiển thị: *"Số điện thoại không hợp lệ"*
→ Người dùng sửa lại số điện thoại và quay lại bước 4.

### (11a) Người dùng chưa nhận được OTP
→ Người dùng bấm **"Gửi lại OTP"**
→ Hệ thống tạo OTP mới và gửi lại email
→ Hệ thống hiển thị: *"OTP đã được gửi lại, có hiệu lực trong 5 phút"*

---

## Luồng ngoại lệ

### (6a) Email đã tồn tại và đã được xác thực
→ Hệ thống trả về mã lỗi **409 Conflict**
→ Hệ thống hiển thị: *"Email đã được sử dụng"*
→ Người dùng có thể:
- Chọn "Đăng nhập" để truy cập tài khoản hiện có
- Hoặc sử dụng email khác

### (6b) Email đã tồn tại nhưng chưa xác thực (đăng ký dở dang)
→ Hệ thống **CẬP NHẬT** thông tin tài khoản cũ:
- Cập nhật password hash mới
- Cập nhật họ tên, số điện thoại
- Đặt `UpdatedAt = hiện tại`

→ Hệ thống **GỬI LẠI OTP** mới (không tạo user mới)
→ Tiếp tục từ bước 9 (gửi email OTP)

### (6c) Email đã được đăng ký bằng Google OAuth (không có password)
→ Hệ thống trả về mã lỗi **409 Conflict**
→ Hệ thống hiển thị: *"Email này đã được đăng ký bằng Google, vui lòng đăng nhập bằng Google"*
→ Người dùng có thể:
- Chọn "Đăng nhập bằng Google"
- Hoặc sử dụng email khác

### (7a) Lỗi trùng lặp email khi lưu database (race condition)
→ Hệ thống bắt exception `DbUpdateException`
→ Hệ thống trả về mã lỗi **400 Bad Request**
→ Hệ thống hiển thị: *"Email đã được sử dụng"*

### (9a) Lỗi gửi email (SMTP server down, network issue)
→ Hệ thống log error chi tiết
→ Hệ thống vẫn tạo tài khoản thành công
→ Hệ thống hiển thị: *"Đã tạo tài khoản nhưng không thể gửi OTP, vui lòng thử gửi lại sau"*
→ Người dùng có thể bấm "Gửi lại OTP" để thử lại

### (9b) Lỗi hệ thống hoặc lỗi database khi tạo tài khoản
→ Hệ thống trả về mã lỗi **500 Internal Server Error**
→ Hệ thống hiển thị: *"Đã có lỗi xảy ra, vui lòng thử lại sau"*
→ Hệ thống rollback transaction (nếu có)

### (10a) Rate limit exceeded (quá nhiều request đăng ký)
→ Hệ thống trả về mã lỗi **429 Too Many Requests**
→ Hệ thống hiển thị: *"Bạn gửi quá nhiều request, vui lòng thử lại sau"*
→ Người dùng phải chờ 1 phút trước khi thử lại

---

## Thông tin bổ sung

### Quy tắc nghiệp vụ

1. **Email duy nhất**: Mỗi email chỉ được sử dụng cho một tài khoản duy nhất.

2. **Vai trò mặc định**: Tài khoản mới tạo mặc định có vai trò `CUSTOMER`.

3. **Xác thực email bắt buộc**: Người dùng phải xác thực email qua OTP trước khi đăng nhập.

4. **Mật khẩu mạnh**: 
   - Tối thiểu 8 ký tự
   - Có ít nhất 1 chữ hoa (A-Z)
   - Có ít nhất 1 chữ số (0-9)
   - Có ít nhất 1 ký tự đặc biệt (!@#$%^&*...)

5. **Hash mật khẩu**: Mật khẩu phải được hash bằng BCrypt với cost factor 12.

6. **Normalize dữ liệu**:
   - Email: lowercase, trim spaces
   - Họ tên: trim spaces, normalize tiếng Việt (bỏ dấu) để hỗ trợ tìm kiếm

7. **OTP có thời hạn**: OTP chỉ có hiệu lực trong **5 phút**.

8. **OTP hash**: OTP được hash bằng HMAC-SHA256 trước khi lưu vào database.

9. **Xử lý đăng ký dở dang**: 
   - Nếu email đã tồn tại nhưng chưa xác thực, cho phép cập nhật thông tin và gửi lại OTP
   - Không tạo tài khoản mới, tránh spam database

10. **Rate limiting**: 
    - Giới hạn 10 request/phút cho endpoint đăng ký
    - Sử dụng policy `sensitive` rate limiter

### Yêu cầu phi chức năng

1. **Hiệu năng**:
   - Thời gian phản hồi API đăng ký: < 2 giây
   - Thời gian gửi email OTP: < 5 giây
   - Hỗ trợ đồng thời 100 request đăng ký cùng lúc

2. **Bảo mật**:
   - Mật khẩu không được hiển thị ra ngoài hệ thống
   - OTP hash, không lưu plain text
   - HTTPS bắt buộc cho tất cả API endpoints
   - Rate limiting để chống spam/brute force

3. **Khả dụng**:
   - Email service có retry mechanism khi gửi thất bại
   - Graceful error handling, không crash server
   - Log đầy đủ để troubleshoot

4. **Trải nghiệm người dùng**:
   - Validation realtime trên frontend
   - Thông báo lỗi rõ ràng, dễ hiểu (tiếng Việt)
   - Tự động focus vào trường lỗi
   - Loading indicator khi đang xử lý

5. **Tuân thủ**:
   - GDPR compliance: cho phép người dùng xóa tài khoản
   - Email marketing: opt-in cho newsletter (tương lai)

### API Endpoint

```
POST /api/auth/register
Content-Type: application/json

Request Body:
{
  "email": "user@example.com",
  "password": "SecurePass123!",
  "fullName": "Nguyễn Văn A",
  "phone": "0901234567"  // optional
}

Success Response (200 OK):
{
  "success": true,
  "message": "OTP đã được gửi đến email của bạn, có hiệu lực trong 5 phút",
  "code": "OK",
  "data": null
}

Error Response (409 Conflict):
{
  "success": false,
  "message": "Email đã được sử dụng",
  "code": "EMAIL_EXISTS",
  "errors": null
}
```

### Database Schema Liên quan

**Bảng users**:
```sql
CREATE TABLE users (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  email VARCHAR(255) UNIQUE NOT NULL,
  password_hash VARCHAR(255),
  full_name VARCHAR(255) NOT NULL,
  full_name_normalized VARCHAR(255),
  phone VARCHAR(20),
  role user_role NOT NULL DEFAULT 'CUSTOMER',
  status user_status NOT NULL DEFAULT 'ACTIVE',
  is_email_verified BOOLEAN DEFAULT FALSE,
  created_at TIMESTAMP DEFAULT NOW(),
  updated_at TIMESTAMP DEFAULT NOW()
);
```

**Bảng otp_codes**:
```sql
CREATE TABLE otp_codes (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID REFERENCES users(id),
  type otp_type NOT NULL,
  code_hash VARCHAR(255) NOT NULL,
  attempt_count INTEGER DEFAULT 0,
  expires_at TIMESTAMP NOT NULL,
  used_at TIMESTAMP,
  created_at TIMESTAMP DEFAULT NOW()
);
```

### Use Cases liên quan

- **UC-AUTH-02**: Đăng nhập
- **UC-AUTH-03**: Xác thực OTP đăng ký
- **UC-AUTH-04**: Gửi lại OTP
- **UC-GOOGLE-01**: Đăng nhập bằng Google OAuth

---

## Ghi chú kỹ thuật

### Frontend Implementation
- Framework: Next.js 16 + React + TypeScript
- Form validation: React Hook Form + Zod schema
- HTTP client: Fetch API / Axios
- State management: React Context hoặc Zustand

### Backend Implementation  
- Framework: ASP.NET Core 8 Web API
- ORM: Entity Framework Core
- Database: PostgreSQL 15+
- Password hashing: BCrypt.Net (cost factor 12)
- OTP hashing: HMAC-SHA256
- Email service: SMTP (Gmail)
- Rate limiting: AspNetCoreRateLimit

### Security Considerations
- Input validation ở cả client và server
- SQL injection protection (EF Core parameterized queries)
- XSS protection (HTML encode user input)
- CSRF protection (SameSite cookies)
- Rate limiting (10 req/min per IP)

---

**Người viết**: [Tên sinh viên]  
**Ngày tạo**: [Ngày tháng năm]  
**Phiên bản**: 1.0
