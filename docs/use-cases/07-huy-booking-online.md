# Use Case UC-BOOKING-03: Hủy booking online

> **Thay đổi ở Phase 6 (Đồ án 2, thiết kế chốt 18/10/2026).** Đặc tả dưới đây mô tả hành vi Đồ án 1. Khách tự hủy được cả đơn chưa thanh toán (`PENDING`, `PENDING_APPROVAL`); khách **không** tự hủy được từ giờ bắt đầu chơi (400 `BOOKING_ALREADY_STARTED`, P6-23; nhân viên vẫn hủy hộ được); tiền hoàn = tổng **đã thanh toán** × % hoàn, % hoàn tính đúng múi giờ, luôn trả tiền mặt tại chi nhánh đặt sân; mọi lần hủy ghi lý do hủy. Link hủy trong email bị vô hiệu khi khách hủy trong app. Xem UC-DEPOSIT-04, UC-DEPOSIT-12 và `docs/api/DEPOSIT_APPROVAL_API.md` Mục 3.4. Use case UC-DEPOSIT-xx nằm ở `TADSS-Runtime/docs/report/USE_CASES.md`.

## Bảng 3.7. Đặc tả Use case Hủy booking online

| Thông tin | Nội dung |
|-----------|----------|
| **Tên Use case** | Hủy booking online |
| **Mã Use case** | UC-BOOKING-03 |
| **Mô tả Use case** | Là khách hàng hoặc khách vãng lai, tôi muốn hủy booking đã đặt online để nhận lại tiền hoàn (nếu đủ điều kiện) khi không thể sử dụng dịch vụ theo lịch đã đặt. |
| **Tác nhân** | Khách hàng đã đăng nhập / Khách vãng lai (qua link email) |
| **Tác nhân phụ** | Email Service |
| **Mức độ ưu tiên** | Cao |
| **Điều kiện kích hoạt** | Người dùng nhận được email xác nhận booking và chọn link "Hủy booking" trong email. |
| **Điều kiện tiên quyết** | • Booking ở trạng thái thanh toán online thành công (PAID_ONLINE).<br>• Booking chưa bị hủy trước đó.<br>• Booking chưa bắt đầu (thời gian bắt đầu chơi > hiện tại).<br>• Link hủy booking hợp lệ, chưa hết hạn và chưa được sử dụng. |
| **Điều kiện cuối** | • Booking được đánh dấu là đã hủy (trạng thái "Đã hủy" hoặc "Đã hủy - Chờ hoàn tiền").<br>• Slot đã đặt được giải phóng để người khác có thể đặt lại.<br>• Yêu cầu hoàn tiền được tạo để nhân viên xử lý thủ công tại quầy (nếu đủ điều kiện).<br>• Hệ thống gửi email thông báo hủy đến người dùng (nếu gửi thành công).<br>• Thông báo được gửi đến các khách hàng quan tâm slot này (nếu có). |

---

## Luồng chính

1. Người dùng nhận email xác nhận booking sau khi đặt sân thành công.
2. Người dùng mở email và chọn link "Hủy booking".
3. Hệ thống xác thực token hủy từ link email.
4. Hệ thống hiển thị thông tin booking gồm mã booking, thông tin sân, thời gian, tổng tiền.
5. Hệ thống kiểm tra trạng thái booking.
6. Hệ thống tính toán thời gian còn lại trước giờ chơi.
7. Hệ thống tra cứu chính sách hoàn tiền áp dụng:
   - Hủy trước từ 24 giờ trở lên: hoàn 100%
   - Hủy trước từ 2 giờ đến dưới 24 giờ: hoàn 50%
   - Hủy dưới 2 giờ: không hoàn tiền
8. Hệ thống hiển thị số tiền hoàn lại dự kiến theo chính sách.
9. Nếu có hoàn tiền, hệ thống hiển thị thông báo "Nhân viên sẽ xác nhận và hoàn tiền theo quy định của chi nhánh".
10. Nếu không hoàn tiền, hệ thống hiển thị cảnh báo "Không được hoàn tiền do hủy quá gần giờ chơi".
11. Người dùng xác nhận hủy booking.
12. Hệ thống cập nhật trạng thái booking:
    - Nếu có hoàn tiền: "Đã hủy - Chờ hoàn tiền"
    - Nếu không hoàn tiền: "Đã hủy"
13. Hệ thống ghi nhận thời điểm hủy và nguồn hủy.
14. Hệ thống đánh dấu token hủy đã sử dụng để tránh dùng lại.
15. Hệ thống giải phóng các slot đã đặt trong booking.
16. Nếu có hoàn tiền, hệ thống tạo yêu cầu hoàn tiền để nhân viên xử lý thủ công tại quầy.
17. Hệ thống gửi email thông báo hủy booking thành công với thông tin hoàn tiền (nếu có).
18. Hệ thống thông báo cho các khách hàng đã đăng ký quan tâm slot này (nếu có).
19. Hệ thống hiển thị thông báo "Hủy booking thành công" và thông tin hoàn tiền.

---

## Luồng thay thế

### (1a) Khách hàng đã đăng nhập hủy từ lịch sử booking
- Khách hàng đăng nhập vào hệ thống.
- Khách hàng truy cập danh sách lịch sử booking.
- Hệ thống hiển thị danh sách booking với các thông tin cơ bản.
- Khách hàng chọn booking cần hủy.
- Hệ thống hiển thị chi tiết booking.
- Hệ thống kiểm tra và hiển thị nút "Hủy đặt sân" nếu booking ở trạng thái phù hợp.
- Khách hàng bấm nút "Hủy đặt sân".
- Hệ thống kiểm tra quyền sở hữu booking.
- Use case tiếp tục từ bước 5 trong luồng chính.

### (11a) Người dùng chọn "Quay lại" khi xem thông tin hoàn tiền
- Người dùng xem thông tin hoàn tiền nhưng quyết định không hủy.
- Người dùng bấm nút "Quay lại".
- Hệ thống quay về màn hình trước đó.
- Use case kết thúc.

---

## Luồng ngoại lệ

### (3a) Token hủy không hợp lệ hoặc đã được sử dụng
- Hệ thống phát hiện token không tồn tại hoặc đã được sử dụng trước đó.
- Hệ thống hiển thị thông báo "Link hủy không hợp lệ hoặc đã được sử dụng".
- Use case kết thúc.

### (3b) Token hủy đã hết hạn
- Hệ thống phát hiện token đã quá thời hạn cho phép.
- Hệ thống hiển thị thông báo "Link hủy đã hết hạn, vui lòng liên hệ nhân viên để được hỗ trợ".
- Use case kết thúc.

### (5a) Booking không ở trạng thái cho phép hủy
- Hệ thống phát hiện booking không ở trạng thái PAID_ONLINE hoặc đã bị hủy.
- Hệ thống hiển thị thông báo "Không thể hủy booking này" kèm lý do cụ thể.
- Use case kết thúc.

### (5b) Booking đã bắt đầu hoặc đã kết thúc
- Hệ thống phát hiện thời gian bắt đầu chơi đã qua.
- Hệ thống hiển thị thông báo "Không thể hủy booking đã bắt đầu hoặc đã kết thúc".
- Use case kết thúc.

### (1a.1) Booking không thuộc về khách hàng
- Hệ thống phát hiện booking không thuộc về khách hàng đang đăng nhập.
- Hệ thống hiển thị thông báo "Bạn không có quyền hủy booking này".
- Use case kết thúc.

### (1a.2) Tài khoản khách hàng bị khóa
- Hệ thống phát hiện tài khoản khách hàng bị khóa.
- Hệ thống hiển thị thông báo "Tài khoản bị khóa, vui lòng liên hệ nhân viên".
- Use case kết thúc.

### (1a.3) Booking không hiển thị nút "Hủy đặt sân"
- Hệ thống kiểm tra trạng thái booking không phù hợp.
- Hệ thống không hiển thị nút "Hủy đặt sân" trong chi tiết booking.
- Khách hàng chỉ có thể xem thông tin booking.
- Use case kết thúc.

### (1a.4) Booking đã bị hủy trước đó
- Hệ thống phát hiện booking đã ở trạng thái đã hủy.
- Hệ thống hiển thị trạng thái booking đã hủy.
- Use case kết thúc.

### (7a) Không tra cứu được chính sách hoàn tiền
- Hệ thống gặp lỗi khi tải chính sách hoàn tiền từ server.
- Hệ thống sử dụng chính sách mặc định: ≥24h = 100%, ≥2h = 50%, <2h = 0%.
- Use case tiếp tục từ bước 8.

### (17a) Không gửi được email thông báo hủy
- Hệ thống gặp lỗi khi gửi email.
- Hệ thống ghi log lỗi nhưng không rollback giao dịch.
- Booking vẫn được hủy thành công.
- Use case tiếp tục từ bước 18.

### (19a) Lỗi hệ thống hoặc lỗi kết nối cơ sở dữ liệu
- Hệ thống gặp lỗi không mong muốn trong quá trình xử lý.
- Hệ thống hiển thị thông báo "Đã xảy ra lỗi, vui lòng thử lại sau".
- Hệ thống rollback giao dịch nếu đang trong quá trình hủy booking.
- Use case kết thúc.

---

## Thông tin bổ sung

### Quy tắc nghiệp vụ

1. **Chính sách hoàn tiền**: Số tiền hoàn phụ thuộc vào thời gian hủy trước giờ chơi theo chính sách: Hủy trước từ 24 giờ trở lên hoàn 100%, hủy trước từ 2 giờ đến dưới 24 giờ hoàn 50%, hủy dưới 2 giờ không hoàn tiền.

2. **Chỉ booking đã thanh toán online mới được hủy qua link**: Link hủy trong email chỉ áp dụng cho booking ở trạng thái PAID_ONLINE (đã thanh toán online qua VNPay). Booking thanh toán tại quầy không có link hủy.

3. **Link hủy có thời hạn**: Link hủy trong email có thời hạn sử dụng hoặc đến trước giờ chơi (tùy cái nào đến trước). Sau thời hạn này, link không còn hiệu lực.

4. **Link hủy chỉ dùng một lần**: Mỗi link hủy chỉ có thể sử dụng một lần. Hệ thống đánh dấu token đã sử dụng để đảm bảo link không bị lạm dụng.

5. **Trạng thái booking sau khi hủy**: Nếu có hoàn tiền, trạng thái booking là "Đã hủy - Chờ hoàn tiền"; nếu không hoàn tiền, trạng thái là "Đã hủy".

6. **Giải phóng slot để đặt lại**: Khi hủy booking, các slot đã đặt được giải phóng và người khác có thể đặt lại ngay lập tức.

7. **Hoàn tiền qua nhân viên tại quầy**: Tiền hoàn không tự động qua VNPay, mà hệ thống tạo yêu cầu hoàn tiền để nhân viên xử lý thủ công tại quầy. Khách hàng cần đến chi nhánh để nhận tiền mặt trong thời gian xử lý theo quy định của chi nhánh.

8. **Cập nhật lượt sử dụng khuyến mãi**: Khi hủy booking có sử dụng khuyến mãi, hệ thống tự động cập nhật lại lượt sử dụng khuyến mãi theo quy định để giải phóng slot cho khách hàng khác.

9. **Thông báo khách hàng quan tâm**: Khi slot được giải phóng, hệ thống tự động thông báo cho các khách hàng đã đăng ký quan tâm đến slot đó (nếu có).

10. **Khách hàng đã đăng nhập có thể hủy từ lịch sử**: Ngoài link email, khách hàng đã đăng nhập còn có thể hủy booking từ trang lịch sử đặt sân. Hệ thống kiểm tra quyền sở hữu booking trước khi cho phép hủy. Nếu booking đã bị hủy trước đó, hệ thống chỉ hiển thị trạng thái đã hủy.

11. **Cảnh báo khi không hoàn tiền**: Nếu không hoàn tiền (hủy dưới 2 giờ), hệ thống hiển thị cảnh báo rõ ràng nhưng vẫn cho phép người dùng tiếp tục hủy nếu muốn.

### Yêu cầu phi chức năng

1. **Thời gian phản hồi**: Thao tác hủy booking cần hoàn thành trong thời gian hợp lý để đảm bảo trải nghiệm người dùng.

2. **Xử lý giao dịch an toàn**: Toàn bộ quá trình hủy booking phải nằm trong một giao dịch để đảm bảo tính nhất quán dữ liệu.

3. **Bảo vệ chống sử dụng lại token**: Hệ thống phải đảm bảo mỗi link hủy chỉ được sử dụng một lần để tránh lạm dụng.

4. **Ghi nhận hoạt động**: Hệ thống ghi log tất cả các lần hủy booking với thông tin người dùng, thời gian, nguồn hủy để hỗ trợ kiểm tra và đối soát.

5. **Email không ảnh hưởng nghiệp vụ**: Lỗi gửi email không được ảnh hưởng đến việc hủy booking. Hệ thống phải hoàn thành hủy booking trước khi gửi email.

6. **Xử lý trùng lặp**: Hệ thống phải hỗ trợ xử lý khi người dùng thực hiện hành động hủy nhiều lần (do lỗi mạng hoặc click nhiều lần).

7. **Thông báo realtime**: Hệ thống cần cập nhật trạng thái sân realtime để nhân viên và khách hàng khác có thể thấy slot vừa được giải phóng.

