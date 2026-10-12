# Use Case UC-AUTH-02: Đăng nhập tài khoản

## Bảng 3.2. Đặc tả Use case Đăng nhập tài khoản

| Thông tin | Nội dung |
|-----------|----------|
| **Tên Use case** | Đăng nhập tài khoản |
| **Mã Use case** | UC-AUTH-02 |
| **Mô tả Use case** | Là người dùng, tôi muốn đăng nhập vào hệ thống bằng email và mật khẩu để sử dụng các chức năng phù hợp với vai trò của mình. |
| **Tác nhân** | Khách truy cập / Người dùng |
| **Tác nhân phụ** | Email Service (nếu tài khoản bật xác thực hai lớp) |
| **Mức độ ưu tiên** | Cao |
| **Điều kiện kích hoạt** | Người dùng chọn chức năng "Đăng nhập" trên giao diện hệ thống. |
| **Điều kiện tiên quyết** | • Người dùng đã có tài khoản trong hệ thống.<br>• Email đã được xác thực.<br>• Tài khoản không bị khóa hoặc tạm khóa do vi phạm chính sách đăng nhập. |
| **Điều kiện cuối** | Người dùng đăng nhập thành công, hệ thống cấp phiên đăng nhập và điều hướng người dùng đến giao diện phù hợp với vai trò. |

---

## Luồng chính

1. Người dùng truy cập màn hình Đăng nhập.
2. Hệ thống hiển thị biểu mẫu đăng nhập gồm email và mật khẩu.
3. Người dùng nhập email và mật khẩu.
4. Người dùng bấm nút "Đăng nhập".
5. Hệ thống kiểm tra tính hợp lệ của dữ liệu đầu vào.
6. Hệ thống tìm tài khoản theo email.
7. Hệ thống kiểm tra:
   - Tài khoản có phải OAuth account không (AuthProvider).
   - Email đã xác thực chưa (IsEmailVerified).
   - Tài khoản bị khóa vĩnh viễn không (Status = LOCKED).
   - Tài khoản bị khóa tạm thời không (LockedUntil).
8. Hệ thống kiểm tra nếu LastFailedLoginAt > 15 phút thì reset FailedLoginCount về 0.
9. Hệ thống xác thực mật khẩu.
10. Nếu mật khẩu đúng, hệ thống reset FailedLoginCount = 0, LockedUntil = null, LastFailedLoginAt = null.
11. Nếu tài khoản không yêu cầu 2FA hoặc đổi mật khẩu bắt buộc, hệ thống tạo access token, refresh token và lưu thông tin phiên đăng nhập.
12. Hệ thống đăng nhập thành công và điều hướng người dùng đến màn hình phù hợp với vai trò.

---

## Luồng thay thế

### (4a) Người dùng bấm vào "Đăng ký"
- Hệ thống chuyển sang màn hình Đăng ký.

### (4b) Người dùng bấm vào "Quên mật khẩu"
- Hệ thống chuyển sang màn hình Quên mật khẩu.

### (5a) Người dùng nhập thiếu email hoặc mật khẩu
- Hệ thống hiển thị thông báo yêu cầu nhập đầy đủ thông tin.

### (5b) Email không đúng định dạng
- Hệ thống hiển thị thông báo email không hợp lệ.

### (11a) Tài khoản bật xác thực hai lớp (Is2faEnabled = true)
1. Hệ thống tạo OTP 6 chữ số và lưu vào database với thời hạn 5 phút.
2. Hệ thống gửi OTP đến email người dùng qua Email Service.
3. Hệ thống tạo temp token (JWT type = "2fa_temp", thời hạn 10 phút).
4. Hệ thống trả về Status = "2fa_required" và TempToken.
5. Người dùng được chuyển sang màn hình xác thực 2FA.
6. Người dùng nhập OTP 6 chữ số.
7. Hệ thống gọi API `POST /api/auth/login/2fa` với TempToken và OtpCode.
8. Hệ thống validate temp token và OTP (tối đa 3 lần thử).
9. Nếu OTP hợp lệ, hệ thống cấp access token và refresh token.
10. Người dùng đăng nhập thành công.

### (11b) Tài khoản bắt buộc đổi mật khẩu (MustChangePassword = true)
- Hệ thống tạo temp token (JWT type = "change_password_temp", thời hạn 15 phút).
- Hệ thống trả về Status = "must_change_password" và TempToken.
- Hệ thống chuyển người dùng sang màn hình đổi mật khẩu bắt buộc trước khi cấp phiên đăng nhập chính thức.

---

## Luồng ngoại lệ

### (6a) Tài khoản không tồn tại
- Hệ thống thông báo "Email hoặc mật khẩu không đúng".

### (7a) Email chưa được xác thực
- Hệ thống từ chối đăng nhập và yêu cầu người dùng xác thực email.

### (7b) Tài khoản đã đăng ký bằng Google OAuth
- Hệ thống thông báo người dùng cần đăng nhập bằng Google.

### (7c) Tài khoản bị khóa
- Hệ thống từ chối đăng nhập và hiển thị thông báo tài khoản bị khóa.

### (7d) Tài khoản bị khóa tạm thời do nhập sai mật khẩu nhiều lần
- Hệ thống thông báo người dùng thử lại sau (kèm số phút còn lại).

### (9a) Mật khẩu không đúng
1. Hệ thống tăng FailedLoginCount += 1.
2. Hệ thống cập nhật LastFailedLoginAt = DateTime.UtcNow.
3. Nếu FailedLoginCount >= 5:
   - Hệ thống set LockedUntil = DateTime.UtcNow + 15 phút.
   - Hệ thống reset FailedLoginCount = 0.
   - Hệ thống thông báo "Tài khoản tạm khóa 15 phút do nhập sai mật khẩu quá 5 lần".
4. Nếu FailedLoginCount < 5:
   - Hệ thống thông báo "Email hoặc mật khẩu không đúng" (không tiết lộ số lần còn lại).

### (11a.2) Không gửi được OTP xác thực hai lớp
- Hệ thống thông báo lỗi và yêu cầu người dùng thử lại sau.

### (11a.7) Temp token không hợp lệ hoặc hết hạn
- Hệ thống yêu cầu đăng nhập lại.

### (11a.8) OTP không hợp lệ hoặc hết hạn
- Hệ thống yêu cầu đăng nhập lại.

### (11a.8) OTP nhập sai quá 3 lần
- Hệ thống khóa OTP và yêu cầu đăng nhập lại.

### (12a) Lỗi hệ thống hoặc lỗi kết nối cơ sở dữ liệu
- Hệ thống hiển thị thông báo lỗi chung và yêu cầu người dùng thử lại sau.

---

## Thông tin bổ sung

### Quy tắc nghiệp vụ

1. Người dùng phải xác thực email trước khi đăng nhập.
2. Hệ thống không tiết lộ cụ thể email có tồn tại hay không khi đăng nhập thất bại.
3. Tài khoản đăng ký bằng Google OAuth không đăng nhập bằng mật khẩu thường.
4. Email luôn được chuẩn hóa trước khi tìm kiếm (trim, lowercase, remove Unicode marks).
5. Mật khẩu được xác thực bằng BCrypt với cost factor = 12.
6. Sau 5 lần nhập sai mật khẩu, tài khoản bị khóa tạm thời 15 phút.
7. FailedLoginCount được reset về 0 nếu LastFailedLoginAt > 15 phút.
8. Khi đăng nhập thành công, tất cả refresh token cũ bị thu hồi (revoke).
9. Refresh token có thời hạn 7 ngày.
10. Access token chứa thông tin UserId, Email, Role, FullName, BranchId.
11. Temp token cho 2FA có thời hạn 10 phút.
12. Temp token cho đổi mật khẩu bắt buộc có thời hạn 15 phút.
13. OTP cho 2FA: 6 chữ số, hash bằng HMAC-SHA256, thời hạn 5 phút, tối đa 3 lần thử.
14. Hệ thống lưu thông tin phiên: device name, IP address, user agent.

### Yêu cầu phi chức năng

1. **Thời gian phản hồi**: < 2 giây cho trường hợp bình thường.
2. **Rate limiting**: 10 request/phút/IP để chống brute-force.
3. **Bảo mật dữ liệu**: Mật khẩu, OTP, refresh token phải được hash/mã hóa trước khi lưu database.
4. **HTTPS**: Access token và refresh token phải được truyền qua HTTPS.
5. **Token storage**: Refresh token phải được lưu trữ an toàn ở client (httpOnly cookie hoặc secure storage).
6. **Audit logging**: Hệ thống phải ghi log tất cả lần đăng nhập (thành công và thất bại) với timestamp, IP, user agent.

### API Endpoints

#### 1. Đăng nhập (Bước đầu tiên)
```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "SecurePassword123"
}
```

**Response (Thành công không 2FA)**:
```json
{
  "status": "Success",
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "a1b2c3d4e5f6...",
  "user": {
    "id": 1,
    "email": "user@example.com",
    "fullName": "Nguyễn Văn A",
    "role": "CUSTOMER",
    "branchId": null
  }
}
```

**Response (Cần đổi mật khẩu)**:
```json
{
  "status": "must_change_password",
  "tempToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "message": "Bạn cần đổi mật khẩu trước khi tiếp tục sử dụng hệ thống"
}
```

**Response (Cần 2FA)**:
```json
{
  "status": "2fa_required",
  "tempToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

#### 2. Xác thực 2FA
```http
POST /api/auth/login/2fa
Content-Type: application/json

{
  "tempToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "otpCode": "123456"
}
```

**Response (Thành công)**:
```json
{
  "status": "Success",
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "a1b2c3d4e5f6...",
  "user": {
    "id": 1,
    "email": "user@example.com",
    "fullName": "Nguyễn Văn A",
    "role": "CUSTOMER",
    "branchId": null
  }
}
```

### Database Schema

**Bảng Users** (các trường liên quan đến đăng nhập):
```sql
CREATE TABLE Users (
    Id SERIAL PRIMARY KEY,
    Email VARCHAR(255) NOT NULL UNIQUE,
    NormalizedEmail VARCHAR(255) NOT NULL UNIQUE,
    PasswordHash VARCHAR(255),
    FullName VARCHAR(255) NOT NULL,
    Role VARCHAR(50) NOT NULL,  -- CUSTOMER, BRANCH_STAFF, BRANCH_MANAGER, SYSTEM_ADMIN
    Status VARCHAR(50) NOT NULL DEFAULT 'ACTIVE',  -- ACTIVE, LOCKED
    IsEmailVerified BOOLEAN NOT NULL DEFAULT FALSE,
    AuthProvider VARCHAR(50) NOT NULL DEFAULT 'LOCAL',  -- LOCAL, GOOGLE, FACEBOOK
    FailedLoginCount INT NOT NULL DEFAULT 0,
    LockedUntil TIMESTAMP,
    LastFailedLoginAt TIMESTAMP,
    MustChangePassword BOOLEAN NOT NULL DEFAULT FALSE,
    Is2faEnabled BOOLEAN NOT NULL DEFAULT FALSE,
    BranchId INT,
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW()
);
```

**Bảng RefreshTokens**:
```sql
CREATE TABLE RefreshTokens (
    Id SERIAL PRIMARY KEY,
    UserId INT NOT NULL REFERENCES Users(Id),
    TokenHash VARCHAR(255) NOT NULL UNIQUE,
    ExpiresAt TIMESTAMP NOT NULL,
    RevokedAt TIMESTAMP,
    RotatedFromId INT REFERENCES RefreshTokens(Id),
    DeviceName VARCHAR(255),
    IpAddress VARCHAR(50),
    UserAgent TEXT,
    LastUsedAt TIMESTAMP NOT NULL,
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW()
);
```

**Bảng OtpCodes**:
```sql
CREATE TABLE OtpCodes (
    Id SERIAL PRIMARY KEY,
    UserId INT NOT NULL REFERENCES Users(Id),
    Type VARCHAR(50) NOT NULL,  -- EMAIL_VERIFY, FORGOT_PASSWORD, TWO_FA
    CodeHash VARCHAR(255) NOT NULL,
    AttemptCount INT NOT NULL DEFAULT 0,
    ExpiresAt TIMESTAMP NOT NULL,
    UsedAt TIMESTAMP,
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW()
);
```

### Sơ đồ trạng thái đăng nhập

```
[Nhập email/password] 
    ↓
[Validate input] 
    ↓
[Tìm user + Kiểm tra OAuth/Email verified/Locked] 
    ↓
[Verify password] 
    ↓ (success)
[Kiểm tra MustChangePassword]
    ↓ (false)
[Kiểm tra Is2faEnabled]
    ↓ (false)                     ↓ (true)
[Cấp access + refresh token]  [Gửi OTP → Nhập OTP → Verify OTP → Cấp token]
    ↓
[Đăng nhập thành công]
```

### Race condition và Edge cases

1. **Email đã tồn tại nhưng chưa verify**: User có thể đăng nhập nhưng sẽ bị chặn ở bước kiểm tra IsEmailVerified.
2. **User bị khóa trong lúc nhập OTP 2FA**: Hệ thống kiểm tra lại status và LockedUntil trong `Login2FAAsync` để đảm bảo không cấp token cho user bị khóa.
3. **Temp token hết hạn trong lúc nhập OTP**: User phải đăng nhập lại từ đầu.
4. **OTP hết hạn trong lúc nhập**: User phải đăng nhập lại từ đầu (không có chức năng "gửi lại OTP" trong luồng 2FA login).
5. **Multiple concurrent login từ cùng một user**: Refresh token cũ bị revoke khi login mới thành công → các session cũ sẽ phải refresh token hoặc đăng nhập lại.
6. **FailedLoginCount reset logic**: Nếu user không thử đăng nhập trong 15 phút thì FailedLoginCount được reset về 0 → tránh tình trạng bị khóa vĩnh viễn nếu ai đó tấn công brute-force rồi bỏ đi.
