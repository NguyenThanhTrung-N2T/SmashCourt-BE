# Use Case UC-BOOKING-01: Đặt sân online

## Bảng 3.5. Đặc tả Use case Đặt sân online

| Thông tin | Nội dung |
|-----------|----------|
| **Tên Use case** | Đặt sân online |
| **Mã Use case** | UC-BOOKING-01 |
| **Mô tả Use case** | Là người dùng (khách hàng hoặc khách vãng lai), tôi muốn đặt sân badminton online để có thể thuê sân vào thời gian mong muốn và thanh toán trực tuyến qua VNPay. |
| **Tác nhân** | Khách hàng (đã đăng nhập) hoặc Khách vãng lai (chưa đăng nhập) |
| **Tác nhân phụ** | VNPay Payment Gateway |
| **Mức độ ưu tiên** | Cao |
| **Điều kiện kích hoạt** | Người dùng truy cập chức năng "Đặt sân mới" hoặc "Booking" trên giao diện hệ thống. |
| **Điều kiện tiên quyết** | • Người dùng truy cập được chức năng đặt sân.<br>• Hệ thống có ít nhất một chi nhánh đang hoạt động với sân khả dụng.<br>• VNPay Payment Gateway đang hoạt động. |
| **Điều kiện cuối** | Booking được tạo thành công ở trạng thái PENDING với thời hạn thanh toán 10 phút. Slot đã chọn bị khóa tạm thời. Người dùng được chuyển hướng đến VNPay Payment Gateway với URL thanh toán hợp lệ. |

---

## Luồng chính

1. Người dùng truy cập màn hình Đặt sân mới.
2. Hệ thống hiển thị bước 1: Chọn chi nhánh.
3. Người dùng chọn chi nhánh muốn đặt sân.
4. Hệ thống chuyển sang bước 2: Chọn loại sân.
5. Người dùng chọn loại sân (đơn hoặc đôi).
6. Hệ thống chuyển sang bước 3: Chọn sân và thời gian.
7. Người dùng chọn sân cụ thể từ danh sách sân khả dụng.
8. Người dùng chọn ngày đặt sân.
9. Người dùng chọn các khung giờ (slot) muốn đặt.
10. Hệ thống tính toán và hiển thị giá tạm tính cho các slot đã chọn.
11. Người dùng bấm "Tiếp tục".
12. Hệ thống chuyển sang bước 4: Xác nhận thông tin đặt sân.
13. Hệ thống hiển thị tóm tắt booking (chi nhánh, sân, ngày, giờ, giá tiền).
14. Nếu người dùng đã đăng nhập, hệ thống hiển thị thông tin loyalty (hạng thành viên, % chiết khấu, điểm hiện có) và danh sách khuyến mãi khả dụng (nếu có).
15. Nếu người dùng chưa đăng nhập, hệ thống yêu cầu nhập thông tin khách vãng lai (họ tên, SĐT, email).
16. Nếu là khách vãng lai, người dùng nhập đầy đủ thông tin bắt buộc.
17. Nếu người dùng đã đăng nhập, người dùng có thể chọn một khuyến mãi hoặc bỏ qua.
18. Hệ thống tính toán giá cuối cùng (giá gốc - chiết khấu loyalty - chiết khấu khuyến mãi).
19. Người dùng có thể nhập ghi chú (tùy chọn).
20. Người dùng bấm "Xác nhận đặt sân".
21. Hệ thống kiểm tra tính hợp lệ của thông tin (email, SĐT đúng định dạng).
22. Hệ thống kiểm tra slot còn khả dụng (chưa bị đặt hoặc khóa thanh toán).
23. Hệ thống tạo booking với trạng thái PENDING.
24. Hệ thống tạo invoice với thời hạn thanh toán 10 phút.
25. Hệ thống tạo payment record với trạng thái PENDING.
26. Hệ thống tạo slot lock (khóa slot trong 10 phút) để tránh double-booking.
27. Hệ thống tạo URL thanh toán VNPay.
28. Hệ thống chuyển hướng người dùng đến VNPay Payment Gateway.

---

## Luồng thay thế

### (9a) Người dùng chọn nhiều slot liên tiếp
- Người dùng có thể chọn nhiều khung giờ liên tiếp trong cùng một lần đặt.
- Hệ thống tính tổng giá cho tất cả các slot.
- Use case tiếp tục từ bước 10.

### (17a) Người dùng chọn khuyến mãi (chỉ áp dụng cho khách đã đăng nhập)
- Người dùng chọn một khuyến mãi từ danh sách.
- Hệ thống kiểm tra điều kiện áp dụng khuyến mãi (ngày, giờ, số lần sử dụng, điều kiện khác).
- Hệ thống tính lại giá cuối cùng với chiết khấu khuyến mãi.
- Use case tiếp tục từ bước 18.

### (17b) Người dùng bỏ qua khuyến mãi
- Người dùng không chọn khuyến mãi nào.
- Hệ thống tính giá cuối cùng chỉ với chiết khấu loyalty (nếu có).
- Use case tiếp tục từ bước 18.

---

## Luồng ngoại lệ

### (8a) Người dùng chọn ngày trong quá khứ
- Hệ thống hiển thị thông báo "Không thể đặt sân cho ngày trong quá khứ".
- Use case quay lại bước 8.

### (9b) Không có slot khả dụng
- Hệ thống hiển thị thông báo "Không có slot nào khả dụng trong ngày đã chọn".
- Người dùng có thể chọn ngày khác hoặc quay lại chọn sân khác.

### (16a) Thông tin khách vãng lai thiếu hoặc không hợp lệ
- Hệ thống hiển thị thông báo lỗi cho từng trường không hợp lệ:
  - "Vui lòng nhập họ tên"
  - "Số điện thoại không hợp lệ"
  - "Email không hợp lệ"
- Use case quay lại bước 16.

### (17a.1) Khuyến mãi không đáp ứng điều kiện
- Người dùng chọn khuyến mãi nhưng không đáp ứng điều kiện (ví dụ: số booking tối thiểu, giờ áp dụng).
- Hệ thống hiển thị thông báo "Không đáp ứng điều kiện khuyến mãi".
- Use case quay lại bước 17.

### (22a) Slot đã bị đặt bởi người khác (race condition)
- Hệ thống phát hiện slot đã bị đặt trong lúc người dùng đang xác nhận.
- Hệ thống hiển thị thông báo "Slot đã được đặt bởi người khác, vui lòng chọn slot khác".
- Use case quay lại bước 9.

### (22b) Slot đang bị khóa thanh toán
- Hệ thống phát hiện slot đang bị khóa bởi một booking khác đang thanh toán.
- Hệ thống hiển thị thông báo "Slot đang trong quá trình thanh toán, vui lòng chọn slot khác hoặc thử lại sau".
- Use case quay lại bước 9.

### (23a) Sân đã bị tạm ngưng hoặc ngừng hoạt động
- Hệ thống kiểm tra trạng thái sân trước khi tạo booking.
- Nếu sân ở trạng thái SUSPENDED, hệ thống hiển thị "Sân đang tạm ngưng hoạt động".
- Nếu sân ở trạng thái INACTIVE, hệ thống hiển thị "Sân không còn hoạt động".
- Use case quay lại bước 7.

### (27a) Không tạo được URL thanh toán VNPay
- Hệ thống log lỗi và rollback transaction.
- Hệ thống hiển thị thông báo "Không thể tạo URL thanh toán, vui lòng thử lại".
- Use case kết thúc.

### (28a) Lỗi hệ thống hoặc lỗi kết nối cơ sở dữ liệu
- Hệ thống hiển thị thông báo "Đã xảy ra lỗi, vui lòng thử lại sau".
- Hệ thống rollback transaction nếu đang trong quá trình tạo booking.
- Use case kết thúc.

---

## Thông tin bổ sung

### Quy tắc nghiệp vụ

1. **Booking online bắt buộc thanh toán trước**: Tất cả booking online đều yêu cầu thanh toán trước qua VNPay. Không có tùy chọn thanh toán sau hoặc thanh toán tại quầy cho booking online.
2. **Ràng buộc về sân và khung giờ**: Trong một lượt đặt, các slot được chọn phải thuộc cùng một chi nhánh. Nếu đặt nhiều sân trong cùng một booking, các sân phải có cùng khung giờ bắt đầu và kết thúc. Nếu đặt một sân, người dùng có thể chọn nhiều slot liên tiếp theo cấu hình hệ thống.
3. **Thời hạn thanh toán**: Booking online có thời hạn thanh toán 10 phút kể từ khi tạo. Sau 10 phút không thanh toán, booking tự động hết hạn và slot được giải phóng.
4. **Slot lock**: Khi tạo booking PENDING, hệ thống tạo slot lock để khóa slot trong 10 phút. Slot lock ngăn người khác đặt cùng slot trong thời gian chờ thanh toán.
5. **Chiết khấu loyalty chỉ cho khách đã đăng nhập**: Khách hàng đã đăng nhập được hưởng chiết khấu theo hạng thành viên (% trên tổng giá sân). Khách vãng lai không được hưởng chiết khấu loyalty.
6. **Khuyến mãi chỉ cho khách đã đăng nhập**: Khuyến mãi chỉ áp dụng cho khách hàng đã đăng nhập. Khách vãng lai không thể sử dụng khuyến mãi.
7. **Chiết khấu khuyến mãi sau loyalty**: Khuyến mãi áp dụng sau khi trừ chiết khấu loyalty. Công thức: `Giá cuối = Giá gốc - Loyalty - Promotion`.
8. **Điều kiện khuyến mãi**: Khuyến mãi phải đáp ứng các điều kiện (ngày áp dụng, giờ áp dụng, số lần sử dụng tối đa, số booking tối thiểu, v.v.).
9. **Tính giá động**: Giá sân được tính dựa trên cấu hình giá của chi nhánh (giá cơ bản + special price theo ngày/giờ + loại sân đơn/đôi).
10. **Thông tin khách vãng lai bắt buộc**: Khách chưa đăng nhập phải cung cấp họ tên, SĐT và email hợp lệ để nhận thông tin booking.
11. **Không cho phép đặt quá khứ**: Hệ thống không cho phép đặt sân cho ngày trong quá khứ.

### Yêu cầu phi chức năng

1. **Thời gian phản hồi**: Các thao tác tính giá và kiểm tra slot cần phản hồi trong thời gian hợp lý (< 2 giây).
2. **Xử lý race condition**: Hệ thống phải đảm bảo không có hai booking cùng đặt một slot (sử dụng cơ chế kiểm tra ràng buộc ở cơ sở dữ liệu).
3. **Bảo mật thanh toán**: URL thanh toán VNPay phải được tạo an toàn với chữ ký điện tử để tránh giả mạo.
4. **Rate limiting**: Hệ thống giới hạn tần suất tạo booking để tránh spam hoặc lạm dụng.
5. **Ghi nhận hoạt động**: Hệ thống ghi log tất cả các lần tạo booking, thanh toán và hủy booking để hỗ trợ troubleshooting.
6. **Xử lý booking hết hạn**: Scheduled job tự động hủy booking PENDING sau 10 phút và gỡ slot lock.
7. **Thông báo slot trống**: Hệ thống hỗ trợ đăng ký nhận thông báo email khi slot bị đặt/khóa trở nên khả dụng lại.

