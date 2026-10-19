# Use Case UC-AUTH-04: Quên mật khẩu

## Bảng 3.4. Đặc tả Use case Quên mật khẩu

| Thông tin | Nội dung |
|-----------|----------|
| **Tên Use case** | Quên mật khẩu |
| **Mã Use case** | UC-AUTH-04 |
| **Mô tả Use case** | Là người dùng, tôi muốn đặt lại mật khẩu khi quên mật khẩu cũ để có thể đăng nhập lại vào hệ thống. |
| **Tác nhân** | Người dùng chưa xác thực |
| **Tác nhân phụ** | Email Service |
| **Mức độ ưu tiên** | Cao |
| **Điều kiện kích hoạt** | Người dùng chọn chức năng "Quên mật khẩu" trên màn hình đăng nhập. |
| **Điều kiện tiên quyết** | • Người dùng có email đã đăng ký trong hệ thống.<br>• Tài khoản hợp lệ để đặt lại mật khẩu là tài khoản email/mật khẩu, đã xác thực email và không bị khóa. |
| **Điều kiện cuối** | Người dùng đặt lại mật khẩu thành công. Tất cả phiên đăng nhập cũ bị thu hồi. Người dùng có thể đăng nhập bằng mật khẩu mới. |

---

## Luồng chính

1. Người dùng truy cập màn hình Đăng nhập.
2. Người dùng chọn "Quên mật khẩu".
3. Hệ thống hiển thị màn hình nhập email.
4. Người dùng nhập email đã đăng ký.
5. Người dùng bấm nút "Gửi mã xác thực".
6. Hệ thống kiểm tra tính hợp lệ của email.
7. Hệ thống kiểm tra email trong hệ thống và điều kiện đặt lại mật khẩu.
8. Nếu tài khoản hợp lệ, hệ thống tạo mã OTP và gửi đến email người dùng qua Email Service.
9. Hệ thống hiển thị thông báo "Nếu email tồn tại trong hệ thống, mã xác thực đã được gửi đến email của bạn".
10. Hệ thống chuyển người dùng sang màn hình nhập OTP.
11. Người dùng nhập OTP 6 chữ số nhận được từ email.
12. Người dùng bấm nút "Xác nhận".
13. Hệ thống xác thực OTP.
14. Nếu OTP hợp lệ, hệ thống tạo mã xác nhận đặt lại mật khẩu có thời hạn ngắn.
15. Hệ thống chuyển người dùng sang màn hình đặt mật khẩu mới.
16. Người dùng nhập mật khẩu mới và xác nhận mật khẩu.
17. Người dùng bấm nút "Đặt lại mật khẩu".
18. Hệ thống kiểm tra tính hợp lệ của mật khẩu mới và mã xác nhận.
19. Hệ thống cập nhật mật khẩu mới cho tài khoản.
20. Hệ thống thu hồi tất cả phiên đăng nhập cũ của người dùng.
21. Hệ thống hiển thị thông báo "Đặt lại mật khẩu thành công".
22. Hệ thống chuyển người dùng về màn hình Đăng nhập.

---

## Luồng thay thế

### (10a) Người dùng yêu cầu gửi lại OTP
- Người dùng chọn "Gửi lại OTP".
- Hệ thống kiểm tra khoảng thời gian chờ giữa các lần gửi.
- Nếu đã đủ thời gian chờ, hệ thống tạo OTP mới và gửi lại email.
- Use case quay lại bước 9.

### (16a) Người dùng muốn hủy đặt lại mật khẩu
- Người dùng chọn "Quay lại đăng nhập".
- Hệ thống chuyển về màn hình Đăng nhập.
- Use case kết thúc.

---

## Luồng ngoại lệ

### (6a) Email không đúng định dạng
- Hệ thống hiển thị thông báo "Email không hợp lệ".
- Use case quay lại bước 4.

### (7a) Email không tồn tại, chưa xác thực hoặc là tài khoản Google OAuth
- Hệ thống không báo lỗi để tránh tiết lộ thông tin tài khoản.
- Hệ thống vẫn hiển thị thông báo chung như bước 10.

### (7b) Tài khoản bị khóa
- Hệ thống không báo lỗi để tránh tiết lộ trạng thái tài khoản.
- Hệ thống vẫn hiển thị thông báo chung như bước 9.

### (8a) Không gửi được email OTP do lỗi hệ thống
- Hệ thống hiển thị thông báo "Không thể gửi email OTP, vui lòng thử lại".
- Use case kết thúc.

### (10a.1) Người dùng yêu cầu gửi lại OTP quá nhanh
- Hệ thống hiển thị thông báo "Vui lòng chờ trước khi gửi lại".
- Use case quay lại màn hình nhập OTP.

### (13a) OTP không tồn tại hoặc đã hết hạn
- Hệ thống hiển thị thông báo "OTP không hợp lệ hoặc đã hết hạn, vui lòng yêu cầu mã mới".
- Use case quay lại bước 3.

### (13b) OTP nhập sai
- Hệ thống ghi nhận số lần nhập sai.
- Nếu chưa vượt quá số lần cho phép, hệ thống hiển thị thông báo "OTP không đúng, còn X lần thử".
- Nếu vượt quá số lần cho phép, hệ thống vô hiệu hóa OTP và yêu cầu người dùng yêu cầu mã mới.
- Use case quay lại bước 11 hoặc bước 3.

### (18a) Mật khẩu mới không đủ mạnh
- Hệ thống hiển thị thông báo yêu cầu về độ mạnh mật khẩu.
- Use case quay lại bước 16.

### (18b) Mật khẩu xác nhận không khớp
- Hệ thống hiển thị thông báo "Mật khẩu xác nhận không khớp".
- Use case quay lại bước 16.

### (18c) Mã xác nhận đặt lại mật khẩu không hợp lệ hoặc đã hết hạn
- Hệ thống hiển thị thông báo "Phiên đặt lại mật khẩu không hợp lệ hoặc đã hết hạn, vui lòng thực hiện lại".
- Use case quay lại bước 3.

### (19a) Tài khoản bị khóa khi đặt mật khẩu mới
- Hệ thống hiển thị thông báo "Tài khoản của bạn đã bị khóa, vui lòng liên hệ hỗ trợ".
- Use case kết thúc.

### (E1) Lỗi hệ thống hoặc lỗi kết nối cơ sở dữ liệu
- Hệ thống hiển thị thông báo "Đã xảy ra lỗi, vui lòng thử lại sau".
- Use case kết thúc.

---

## Thông tin bổ sung

### Quy tắc nghiệp vụ

1. **Bảo mật thông tin tài khoản**: Hệ thống không tiết lộ thông tin về việc email có tồn tại, đã xác thực hay loại tài khoản khi người dùng yêu cầu đặt lại mật khẩu (silent return).
2. **Tài khoản Google OAuth**: Tài khoản đăng ký bằng Google OAuth không được phép đặt lại mật khẩu qua flow này.
3. **Email phải đã xác thực**: Chỉ tài khoản có email đã xác thực mới được phép đặt lại mật khẩu.
4. **Tài khoản bị khóa**: Tài khoản bị khóa không được phép đặt lại mật khẩu (hệ thống không báo lỗi để tránh tiết lộ trạng thái).
5. **OTP có thời hạn và giới hạn**: OTP có thời hạn 5 phút, chỉ sử dụng một lần, và giới hạn tối đa 3 lần nhập sai. Vượt quá giới hạn, OTP bị vô hiệu hóa.
6. **Cooldown giữa các lần gửi OTP**: Người dùng phải chờ một khoảng thời gian nhất định giữa các lần yêu cầu gửi OTP để tránh lạm dụng.
7. **Mã xác nhận có thời hạn ngắn**: Mã xác nhận đặt lại mật khẩu (sau khi xác thực OTP thành công) có thời hạn ngắn để đảm bảo bảo mật.
8. **Mật khẩu mới phải đủ mạnh**: Mật khẩu mới phải đáp ứng yêu cầu (tối thiểu 8 ký tự, có chữ hoa, số, ký tự đặc biệt). Mật khẩu được hash bằng BCrypt với cost factor 12.
9. **Thu hồi phiên đăng nhập cũ**: Sau khi đặt mật khẩu mới thành công, tất cả phiên đăng nhập cũ trên các thiết bị khác bị thu hồi để bảo vệ tài khoản.
10. **Reset trạng thái đăng nhập thất bại**: Khi đặt mật khẩu mới thành công, các trạng thái liên quan đến đăng nhập thất bại (FailedLoginCount, LockedUntil) được reset về trạng thái ban đầu.

### Yêu cầu phi chức năng

1. **Thời gian phản hồi**: Các thao tác cần phản hồi trong thời gian hợp lý.
2. **Bảo mật thông tin**: Không tiết lộ thông tin nhạy cảm về tài khoản người dùng.
3. **Giới hạn tần suất**: Hệ thống giới hạn tần suất yêu cầu OTP để hạn chế lạm dụng.
4. **Gửi email nhanh chóng**: OTP phải được gửi đến email người dùng trong thời gian ngắn.
5. **Ghi nhận hoạt động**: Hệ thống ghi nhận các lần yêu cầu đặt lại mật khẩu để hỗ trợ kiểm tra và xử lý sự cố.
6. **Bảo vệ dữ liệu nhạy cảm**: Mật khẩu, OTP và các mã xác nhận phải được bảo vệ an toàn.
