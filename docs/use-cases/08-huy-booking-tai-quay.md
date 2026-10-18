# Use Case UC-BOOKING-04: Hủy booking tại quầy

> **Thay đổi ở Phase 6 (Đồ án 2, thiết kế chốt 18/10/2026).** Đặc tả dưới đây mô tả hành vi Đồ án 1. Nhân viên hủy được đơn `PENDING_APPROVAL`; tiền hoàn tính trên số đã thanh toán; không giảm lượt dùng khuyến mãi khi đơn chưa thu tiền; mọi lần hủy ghi lý do hủy. Xem `docs/api/DEPOSIT_APPROVAL_API.md` Mục 3.4. Use case UC-DEPOSIT-xx nằm ở `TADSS-Runtime/docs/report/USE_CASES.md`.

## Bảng 3.8. Đặc tả Use case Hủy booking tại quầy

| Thông tin | Nội dung |
|-----------|----------|
| **Tên Use case** | Hủy booking tại quầy |
| **Mã Use case** | UC-BOOKING-04 |
| **Mô tả Use case** | Là nhân viên, tôi muốn hủy booking cho khách hàng tại quầy để xử lý các trường hợp khách không thể đến hoặc có yêu cầu hủy đặt sân. |
| **Tác nhân** | Nhân viên sân hoặc Quản lý chi nhánh |
| **Tác nhân phụ** | Email Service |
| **Mức độ ưu tiên** | Cao |
| **Điều kiện kích hoạt** | Nhân viên chọn một booking trong danh sách quản lý và bấm nút "Hủy đơn đặt". |
| **Điều kiện tiên quyết** | • Nhân viên đã đăng nhập vào hệ thống.<br>• Booking ở trạng thái cho phép hủy theo quy định hệ thống.<br>• Booking chưa bị hủy trước đó.<br>• Booking chưa bắt đầu (thời gian bắt đầu chơi > hiện tại).<br>• Nhân viên có quyền truy cập vào chi nhánh của booking. |
| **Điều kiện cuối** | • Booking được đánh dấu là đã hủy (trạng thái "Đã hủy" hoặc "Đã hủy - Chờ hoàn tiền").<br>• Slot đã đặt được giải phóng để người khác có thể đặt lại.<br>• Yêu cầu hoàn tiền được tạo để nhân viên xử lý thủ công tại quầy (nếu đủ điều kiện).<br>• Hệ thống gửi email thông báo hủy đến khách hàng (nếu gửi thành công).<br>• Thông báo được gửi đến các khách hàng quan tâm slot này (nếu có). |

---

## Luồng chính

1. Nhân viên truy cập trang quản lý đặt sân.
2. Hệ thống hiển thị danh sách booking theo bộ lọc.
3. Nhân viên chọn một booking cần hủy.
4. Hệ thống hiển thị chi tiết booking gồm mã booking, khách hàng, sân, thời gian, trạng thái, tổng tiền.
5. Nhân viên bấm nút "Hủy đơn đặt".
6. Hệ thống hiển thị thông báo xác nhận với nội dung "Bạn có chắc chắn muốn hủy đơn này? Hành động này không thể hoàn tác."
7. Nhân viên xác nhận hủy booking.
8. Hệ thống kiểm tra quyền truy cập chi nhánh của nhân viên.
9. Hệ thống kiểm tra trạng thái booking.
10. Hệ thống tính toán thời gian còn lại trước giờ chơi.
11. Hệ thống tra cứu chính sách hoàn tiền áp dụng:
    - Hủy trước từ 24 giờ trở lên: hoàn 100%
    - Hủy trước từ 2 giờ đến dưới 24 giờ: hoàn 50%
    - Hủy dưới 2 giờ: không hoàn tiền
12. Hệ thống cập nhật trạng thái booking:
    - Nếu có hoàn tiền: "Đã hủy - Chờ hoàn tiền"
    - Nếu không hoàn tiền: "Đã hủy"
13. Hệ thống ghi nhận thời điểm hủy, người hủy (nhân viên), và nguồn hủy (tại quầy).
14. Hệ thống giải phóng các slot đã đặt trong booking.
15. Nếu có hoàn tiền, hệ thống tạo yêu cầu hoàn tiền để nhân viên xử lý thủ công tại quầy.
16. Hệ thống gửi email thông báo hủy booking thành công đến khách hàng với thông tin hoàn tiền (nếu có).
17. Hệ thống thông báo cho các khách hàng đã đăng ký quan tâm slot này (nếu có).
18. Hệ thống hiển thị thông báo "Hủy đơn đặt thành công".
19. Hệ thống tự động làm mới danh sách booking và thông tin tổng quan quản lý.

---

## Luồng thay thế

### (7a) Nhân viên chọn "Hủy" trong thông báo xác nhận
- Nhân viên không muốn hủy booking.
- Nhân viên bấm nút "Hủy" trong thông báo.
- Hệ thống đóng thông báo.
- Use case kết thúc.

### (15a) Nhân viên hoàn tiền ngay tại quầy
- Nhân viên có thể hoàn tiền trực tiếp cho khách hàng tại quầy ngay sau khi hủy.
- Nhân viên xác nhận đã hoàn tiền cho khách.
- Hệ thống cập nhật trạng thái yêu cầu hoàn tiền thành "Đã hoàn tiền".
- Use case tiếp tục từ bước 16.

---

## Luồng ngoại lệ

### (8a) Nhân viên không có quyền truy cập chi nhánh
- Hệ thống phát hiện nhân viên không thuộc chi nhánh của booking.
- Hệ thống hiển thị thông báo "Bạn không có quyền hủy đơn này".
- Use case kết thúc.

### (4a) Không tìm thấy booking
- Hệ thống không tìm thấy booking theo bộ lọc hoặc mã booking.
- Hệ thống hiển thị thông báo "Không tìm thấy booking phù hợp".
- Use case kết thúc.

### (9a) Booking không ở trạng thái cho phép hủy
- Hệ thống phát hiện booking không ở trạng thái được phép hủy theo quy định hệ thống.
- Hệ thống hiển thị thông báo "Không thể hủy đơn ở trạng thái này".
- Use case kết thúc.

### (9b) Booking đã bắt đầu (khách đang chơi)
- Hệ thống phát hiện thời gian bắt đầu chơi đã qua và booking đang ở trạng thái IN_PROGRESS.
- Hệ thống hiển thị thông báo "Không thể hủy đơn khi khách đang chơi. Sử dụng chức năng checkout sớm thay vì hủy".
- Use case kết thúc.

### (11a) Không tra cứu được chính sách hoàn tiền
- Hệ thống gặp lỗi khi tải chính sách hoàn tiền từ server.
- Hệ thống sử dụng chính sách mặc định: ≥24h = 100%, ≥2h = 50%, <2h = 0%.
- Use case tiếp tục từ bước 12.

### (16a) Không gửi được email thông báo hủy
- Hệ thống gặp lỗi khi gửi email.
- Hệ thống ghi log lỗi nhưng không rollback giao dịch.
- Booking vẫn được hủy thành công.
- Use case tiếp tục từ bước 17.

### (19a) Lỗi hệ thống hoặc lỗi kết nối cơ sở dữ liệu
- Hệ thống gặp lỗi không mong muốn trong quá trình xử lý.
- Hệ thống hiển thị thông báo "Hủy đơn đặt thất bại. Vui lòng thử lại sau".
- Use case kết thúc.

---

## Thông tin bổ sung

### Quy tắc nghiệp vụ

1. **Chính sách hoàn tiền**: Số tiền hoàn phụ thuộc vào thời gian hủy trước giờ chơi theo chính sách: Hủy trước từ 24 giờ trở lên hoàn 100%, hủy trước từ 2 giờ đến dưới 24 giờ hoàn 50%, hủy dưới 2 giờ không hoàn tiền.

2. **Kiểm tra quyền truy cập**: Nhân viên chỉ được hủy booking thuộc chi nhánh mà họ được phân công quản lý. Hệ thống kiểm tra quyền này trước khi cho phép hủy.

3. **Trạng thái có thể hủy**: Booking ở các trạng thái nhất định mới được phép hủy theo quy định hệ thống, ví dụ như "Đã xác nhận" (CONFIRMED) hoặc "Đã thanh toán online" (PAID_ONLINE). Booking đang chơi (IN_PROGRESS) không được hủy mà phải dùng checkout sớm.

4. **Trạng thái booking sau khi hủy**: Nếu có hoàn tiền, trạng thái booking là "Đã hủy - Chờ hoàn tiền"; nếu không hoàn tiền, trạng thái là "Đã hủy".

5. **Giải phóng slot để đặt lại**: Khi hủy booking, các slot đã đặt được giải phóng trong lịch đặt sân và người khác có thể đặt lại ngay lập tức.

6. **Hoàn tiền qua nhân viên tại quầy**: Tiền hoàn không tự động qua VNPay, mà hệ thống tạo yêu cầu hoàn tiền để nhân viên xử lý thủ công tại quầy. Nhân viên có thể hoàn tiền ngay cho khách hàng hoặc khách hàng đến nhận sau theo quy định của chi nhánh.

7. **Cập nhật lượt sử dụng khuyến mãi**: Khi hủy booking có sử dụng khuyến mãi, hệ thống tự động cập nhật lại lượt sử dụng khuyến mãi theo quy định để giải phóng slot cho khách hàng khác.

8. **Thông báo khách hàng quan tâm**: Khi slot được giải phóng, hệ thống tự động thông báo cho các khách hàng đã đăng ký quan tâm đến slot đó (nếu có).

9. **Ghi nhận thông tin hủy**: Hệ thống ghi nhận thời điểm hủy, nhân viên thực hiện hủy, và nguồn hủy (tại quầy) để phục vụ kiểm tra và đối soát.

10. **Làm mới giao diện tự động**: Sau khi hủy thành công, hệ thống tự động làm mới danh sách booking và thông tin tổng quan quản lý để nhân viên thấy cập nhật mới nhất.

### Yêu cầu phi chức năng

1. **Thời gian phản hồi**: Thao tác hủy booking cần hoàn thành trong thời gian hợp lý để đảm bảo trải nghiệm nhân viên.

2. **Xử lý giao dịch an toàn**: Toàn bộ quá trình hủy booking phải đảm bảo tính nhất quán dữ liệu.

3. **Ghi nhận hoạt động**: Hệ thống ghi log tất cả các lần hủy booking với thông tin nhân viên, thời gian, nguồn hủy để hỗ trợ kiểm tra và đối soát.

4. **Email không ảnh hưởng nghiệp vụ**: Lỗi gửi email không được ảnh hưởng đến việc hủy booking. Hệ thống phải hoàn thành hủy booking trước khi gửi email.

5. **Kiểm tra quyền truy cập nghiêm ngặt**: Hệ thống phải đảm bảo nhân viên chỉ có thể hủy booking thuộc chi nhánh của họ để tránh thao tác trái phép.

6. **Cập nhật realtime**: Sau khi hủy booking, hệ thống cần cập nhật trạng thái sân và danh sách booking realtime để nhân viên và khách hàng khác có thể thấy slot vừa được giải phóng.
