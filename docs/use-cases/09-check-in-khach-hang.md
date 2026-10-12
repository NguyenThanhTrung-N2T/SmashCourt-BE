# Use Case UC-BOOKING-05: Check-in khách hàng

## Bảng 3.9. Đặc tả Use case Check-in khách hàng

| Thông tin | Nội dung |
|-----------|----------|
| **Tên Use case** | Check-in khách hàng |
| **Mã Use case** | UC-BOOKING-05 |
| **Mô tả Use case** | Là nhân viên sân hoặc quản lý chi nhánh, tôi muốn xác nhận khách hàng đã đến sân để bắt đầu cho phép khách hàng sử dụng dịch vụ đặt sân. |
| **Tác nhân** | Nhân viên sân hoặc Quản lý chi nhánh |
| **Tác nhân phụ** | Không có |
| **Mức độ ưu tiên** | Cao |
| **Điều kiện kích hoạt** | Nhân viên chọn một booking trong danh sách quản lý và bấm nút "Check-in". |
| **Điều kiện tiên quyết** | • Nhân viên đã đăng nhập vào hệ thống.<br>• Booking ở trạng thái "Đã xác nhận" (CONFIRMED) hoặc "Đã thanh toán online" (PAID_ONLINE).<br>• Thời gian hiện tại trong khoảng cho phép check-in (từ 15 phút trước giờ bắt đầu đến 15 phút sau giờ bắt đầu).<br>• Nhân viên có quyền truy cập vào chi nhánh của booking. |
| **Điều kiện cuối** | • Booking được cập nhật trạng thái thành "Đang chơi" (IN_PROGRESS).<br>• Thời điểm check-in được ghi nhận.<br>• Link hủy booking (nếu có) bị vô hiệu hóa để khách không thể hủy khi đang chơi.<br>• Trạng thái các sân trong booking được cập nhật thành "Đang sử dụng" (IN_USE). |

---

## Luồng chính

1. Nhân viên truy cập trang quản lý đặt sân.
2. Hệ thống hiển thị danh sách booking theo bộ lọc.
3. Nhân viên chọn một booking cần check-in.
4. Hệ thống hiển thị chi tiết booking gồm mã booking, khách hàng, sân, thời gian, trạng thái.
5. Nhân viên bấm nút "Check-in".
6. Hệ thống hiển thị thông báo xác nhận với nội dung "Bạn có chắc chắn muốn xác nhận đến cho đơn này?".
7. Nhân viên xác nhận check-in.
8. Hệ thống kiểm tra quyền truy cập chi nhánh của nhân viên.
9. Hệ thống lấy thông tin sân và khung giờ đã đặt.
10. Hệ thống xác định thời gian bắt đầu booking.
11. Hệ thống kiểm tra thời gian hiện tại có nằm trong khoảng cho phép check-in hay không.
12. Hệ thống kiểm tra trạng thái booking.
13. Hệ thống cập nhật trạng thái booking thành "Đang chơi" (IN_PROGRESS).
14. Hệ thống ghi nhận thời điểm check-in.
15. Hệ thống vô hiệu hóa token hủy booking (nếu có) để khách không thể hủy khi đang chơi.
16. Hệ thống cập nhật trạng thái các sân trong booking thành "Đang sử dụng" (IN_USE).
17. Hệ thống hiển thị thông báo "Check-in thành công".
18. Hệ thống cập nhật danh sách booking và thông tin tổng quan quản lý.

---

## Luồng thay thế

### (7a) Nhân viên chọn "Hủy" trong thông báo xác nhận
- Nhân viên không muốn check-in booking.
- Nhân viên bấm nút "Hủy" trong thông báo.
- Hệ thống đóng thông báo.
- Use case kết thúc.

---

## Luồng ngoại lệ

### (8a) Nhân viên không có quyền truy cập chi nhánh
- Hệ thống phát hiện nhân viên không thuộc chi nhánh của booking.
- Hệ thống hiển thị thông báo "Bạn không có quyền check-in đơn này".
- Use case kết thúc.

### (4a) Không tìm thấy booking
- Hệ thống không tìm thấy booking theo bộ lọc hoặc mã booking.
- Hệ thống hiển thị thông báo "Không tìm thấy booking phù hợp".
- Use case kết thúc.

### (9a) Không tìm thấy thông tin sân và khung giờ
- Hệ thống không tìm thấy thông tin sân hoặc khung giờ đã đặt trong booking.
- Hệ thống hiển thị thông báo "Không tìm thấy sân và khung giờ đã đặt".
- Use case kết thúc.

### (11a) Quá sớm để check-in
- Hệ thống phát hiện thời gian hiện tại trước 15 phút so với giờ bắt đầu booking.
- Hệ thống hiển thị thông báo "Quá sớm để check-in. Vui lòng chờ đến [thời gian cho phép]".
- Use case kết thúc.

### (11b) Đã quá thời gian check-in
- Hệ thống phát hiện thời gian hiện tại sau 15 phút so với giờ bắt đầu booking.
- Hệ thống hiển thị thông báo "Đã quá thời gian check-in. Vui lòng liên hệ quản lý".
- Use case kết thúc.

### (12a) Booking không ở trạng thái cho phép check-in
- Hệ thống phát hiện booking không ở trạng thái CONFIRMED hoặc PAID_ONLINE.
- Hệ thống hiển thị thông báo "Chỉ có thể check-in đơn đang xác nhận hoặc đã thanh toán trực tuyến. Booking đã bị hủy, đã hoàn thành hoặc đang chơi không được check-in".
- Use case kết thúc.

### (18a) Lỗi hệ thống hoặc lỗi kết nối cơ sở dữ liệu
- Hệ thống gặp lỗi không mong muốn trong quá trình xử lý.
- Hệ thống rollback giao dịch nếu đang trong quá trình cập nhật trạng thái.
- Hệ thống hiển thị thông báo "Check-in thất bại. Vui lòng thử lại sau".
- Use case kết thúc.

---

## Thông tin bổ sung

### Quy tắc nghiệp vụ

1. **Kiểm tra quyền truy cập**: Nhân viên chỉ được check-in booking thuộc chi nhánh mà họ được phân công quản lý. Hệ thống kiểm tra quyền này trước khi cho phép check-in.

2. **Thời gian cho phép check-in**: Khách hàng chỉ được check-in trong khoảng thời gian từ 15 phút trước giờ bắt đầu đến 15 phút sau giờ bắt đầu. Ngoài khoảng thời gian này, hệ thống từ chối check-in.

3. **Trạng thái cho phép check-in**: Chỉ booking ở trạng thái "Đã xác nhận" (CONFIRMED) hoặc "Đã thanh toán online" (PAID_ONLINE) mới được check-in. Booking ở các trạng thái khác không thể check-in.

4. **Trạng thái booking sau check-in**: Sau khi check-in thành công, trạng thái booking chuyển thành "Đang chơi" (IN_PROGRESS), cho phép khách hàng bắt đầu sử dụng sân.

5. **Vô hiệu hóa link hủy**: Khi booking chuyển sang trạng thái "Đang chơi", hệ thống tự động vô hiệu hóa token hủy booking (nếu có) để đảm bảo khách hàng không thể hủy booking khi đang sử dụng dịch vụ.

6. **Cập nhật trạng thái sân**: Sau khi check-in, tất cả các sân trong booking được cập nhật trạng thái thành "Đang sử dụng" (IN_USE) để phản ánh đúng tình trạng sân đang được sử dụng.

7. **Ghi nhận thời điểm check-in**: Hệ thống ghi nhận chính xác thời điểm check-in để phục vụ cho các nghiệp vụ khác như tính thời gian sử dụng, báo cáo, và đối soát.

8. **Không thể check-in nhiều lần**: Một booking chỉ có thể check-in một lần duy nhất. Sau khi đã check-in, booking không thể check-in lại.

9. **Cập nhật danh sách và thông tin quản lý**: Sau khi check-in thành công, hệ thống cập nhật danh sách booking và thông tin tổng quan quản lý để nhân viên thấy cập nhật mới nhất.

### Yêu cầu phi chức năng

1. **Thời gian phản hồi**: Thao tác check-in cần hoàn thành trong thời gian hợp lý để đảm bảo trải nghiệm nhân viên và khách hàng.

2. **Xử lý giao dịch an toàn**: Toàn bộ quá trình check-in phải đảm bảo tính nhất quán dữ liệu, đặc biệt là cập nhật trạng thái booking và sân.

3. **Kiểm tra quyền truy cập nghiêm ngặt**: Hệ thống phải đảm bảo nhân viên chỉ có thể check-in booking thuộc chi nhánh của họ để tránh thao tác trái phép.

4. **Xử lý múi giờ chính xác**: Hệ thống phải xử lý múi giờ Việt Nam chính xác để đảm bảo kiểm tra thời gian check-in đúng.

5. **Ghi nhận hoạt động**: Hệ thống ghi log tất cả các lần check-in với thông tin nhân viên, thời gian để hỗ trợ kiểm tra và đối soát.

6. **Cập nhật realtime**: Sau khi check-in, hệ thống cần cập nhật trạng thái booking và sân realtime để nhân viên khác có thể thấy thông tin mới nhất.

7. **Xử lý đồng thời**: Hệ thống phải xử lý đúng trường hợp nhiều nhân viên cùng cố gắng check-in một booking để tránh xung đột dữ liệu.
