# Use Case UC-BOOKING-06: Check-out khách hàng

> **Thay đổi ở Phase 6 (Đồ án 2, thiết kế chốt 18/10/2026).** Đặc tả dưới đây mô tả hành vi Đồ án 1. Số thu tại quầy = tổng tiền hiện tại − tổng đã thanh toán (gồm phần tiền sân chưa cọc, không chỉ dịch vụ phát sinh). Xem UC-DEPOSIT-08 và `docs/api/DEPOSIT_APPROVAL_API.md` Mục 3.5. Use case UC-DEPOSIT-xx nằm ở `TADSS-Runtime/docs/report/USE_CASES.md`.

## Bảng 3.10. Đặc tả Use case Check-out khách hàng

| Thông tin | Nội dung |
|-----------|----------|
| **Tên Use case** | Check-out khách hàng |
| **Mã Use case** | UC-BOOKING-06 |
| **Mô tả Use case** | Là nhân viên sân hoặc quản lý chi nhánh, tôi muốn xác nhận khách hàng đã rời sân, hoàn tất thanh toán (nếu còn phí dịch vụ hoặc thanh toán sau), giải phóng sân và tích điểm thành viên để kết thúc quy trình đặt sân. |
| **Tác nhân** | Nhân viên sân hoặc Quản lý chi nhánh |
| **Tác nhân phụ** | Không có |
| **Mức độ ưu tiên** | Cao |
| **Điều kiện kích hoạt** | Nhân viên chọn một booking trong danh sách quản lý và bấm nút "Checkout". |
| **Điều kiện tiên quyết** | • Nhân viên đã đăng nhập vào hệ thống.<br>• Booking ở trạng thái "Đang chơi" (IN_PROGRESS) hoặc "Chờ thanh toán" (PENDING_PAYMENT).<br>• Nhân viên có quyền truy cập vào chi nhánh của booking. |
| **Điều kiện cuối** | • Booking được cập nhật trạng thái thành "Đã hoàn thành" (COMPLETED).<br>• Hóa đơn được cập nhật trạng thái thanh toán thành "Đã thanh toán" (PAID).<br>• Phiếu thanh toán tiền mặt được tạo cho số tiền còn thiếu (nếu có).<br>• Thời điểm kết thúc thực tế được ghi nhận (nếu check-out sớm).<br>• Trạng thái các sân trong booking được cập nhật thành "Sẵn sàng" (AVAILABLE) nếu không còn booking nào đang sử dụng sân tại thời điểm đó.<br>• Điểm thành viên được cộng cho khách hàng (nếu có tài khoản). |

---

## Luồng chính

1. Nhân viên truy cập trang quản lý đặt sân.
2. Hệ thống hiển thị danh sách booking theo bộ lọc.
3. Nhân viên chọn một booking cần check-out.
4. Hệ thống hiển thị chi tiết booking gồm mã booking, khách hàng, sân, thời gian, trạng thái, tổng tiền, phí dịch vụ.
5. Nhân viên bấm nút "Checkout".
6. Hệ thống hiển thị thông báo xác nhận với nội dung "Bạn có chắc chắn muốn checkout đơn này?".
7. Nhân viên xác nhận check-out.
8. Hệ thống kiểm tra quyền truy cập chi nhánh của nhân viên.
9. Hệ thống kiểm tra trạng thái booking.
10. Hệ thống bắt đầu giao dịch dữ liệu.
11. Hệ thống truy vấn lại hóa đơn từ cơ sở dữ liệu để lấy tổng tiền mới nhất.
12. Hệ thống xử lý thanh toán còn thiếu theo trạng thái hóa đơn.
13. Hệ thống cập nhật trạng thái thanh toán hóa đơn thành "Đã thanh toán" (PAID).
14. Hệ thống kiểm tra thời điểm check-out:
    - Nếu check-out sớm (trước giờ kết thúc): ghi nhận thời điểm kết thúc thực tế
    - Nếu check-out đúng giờ hoặc muộn: không cần ghi nhận
15. Hệ thống kết thúc các lượt sân trong booking.
16. Hệ thống cập nhật trạng thái các sân trong booking thành "Sẵn sàng" (AVAILABLE) nếu không còn booking nào đang sử dụng.
17. Hệ thống tích điểm thành viên cho khách hàng nếu booking gắn với tài khoản hợp lệ.
18. Hệ thống cập nhật trạng thái booking thành "Đã hoàn thành" (COMPLETED).
19. Hệ thống hoàn tất giao dịch dữ liệu.
20. Hệ thống hiển thị thông báo "Checkout thành công".
21. Hệ thống cập nhật danh sách booking và thông tin tổng quan quản lý.

---

## Luồng thay thế

### (7a) Nhân viên chọn "Hủy" trong thông báo xác nhận
- Nhân viên không muốn check-out booking.
- Nhân viên bấm nút "Hủy" trong thông báo.
- Hệ thống đóng thông báo.
- Use case kết thúc.

### (12a) Hóa đơn chưa thanh toán
- Hệ thống phát hiện hóa đơn ở trạng thái "Chưa thanh toán" (UNPAID).
- Hệ thống tạo phiếu thanh toán tiền mặt cho toàn bộ số tiền còn thiếu.
- Use case tiếp tục từ bước 13.

### (12b) Hóa đơn đã thanh toán một phần
- Hệ thống phát hiện hóa đơn ở trạng thái "Đã thanh toán một phần" (PARTIALLY_PAID).
- Hệ thống tạo phiếu thanh toán tiền mặt cho phần tiền còn thiếu.
- Use case tiếp tục từ bước 13.

### (12c) Hóa đơn đã thanh toán đầy đủ
- Hệ thống phát hiện hóa đơn ở trạng thái "Đã thanh toán" (PAID).
- Hệ thống không tạo phiếu thanh toán mới.
- Use case tiếp tục từ bước 13.

---

## Luồng ngoại lệ

### (8a) Nhân viên không có quyền truy cập chi nhánh
- Hệ thống phát hiện nhân viên không thuộc chi nhánh của booking.
- Hệ thống hiển thị thông báo "Bạn không có quyền checkout đơn này".
- Use case kết thúc.

### (4a) Không tìm thấy booking
- Hệ thống không tìm thấy booking theo bộ lọc hoặc mã booking.
- Hệ thống hiển thị thông báo "Không tìm thấy booking phù hợp".
- Use case kết thúc.

### (9a) Booking không ở trạng thái cho phép check-out
- Hệ thống phát hiện booking không ở trạng thái IN_PROGRESS hoặc PENDING_PAYMENT.
- Hệ thống hiển thị thông báo "Chỉ có thể checkout đơn đang tiến hành hoặc chờ thanh toán".
- Use case kết thúc.

### (18a) Booking đã được check-out bởi nhân viên khác
- Hệ thống phát hiện booking đã được cập nhật trạng thái bởi nhân viên khác (kiểm tra đồng thời).
- Hệ thống rollback giao dịch.
- Hệ thống hiển thị thông báo "Đơn đã được checkout bởi người khác".
- Use case kết thúc.

### (11a) Không tìm thấy hóa đơn
- Hệ thống không tìm thấy hóa đơn liên kết với booking.
- Hệ thống rollback giao dịch.
- Hệ thống hiển thị thông báo "Không tìm thấy hóa đơn".
- Use case kết thúc.

### (19a) Lỗi hệ thống hoặc lỗi kết nối cơ sở dữ liệu
- Hệ thống gặp lỗi không mong muốn trong quá trình xử lý.
- Hệ thống rollback giao dịch nếu đang trong quá trình cập nhật.
- Hệ thống hiển thị thông báo "Checkout thất bại. Vui lòng thử lại sau".
- Use case kết thúc.

---

## Thông tin bổ sung

### Quy tắc nghiệp vụ

1. **Kiểm tra quyền truy cập**: Nhân viên chỉ được check-out booking thuộc chi nhánh mà họ được phân công quản lý. Hệ thống kiểm tra quyền này trước khi cho phép check-out.

2. **Trạng thái cho phép check-out**: Chỉ booking ở trạng thái "Đang chơi" (IN_PROGRESS) hoặc "Chờ thanh toán" (PENDING_PAYMENT) mới được check-out. Booking ở trạng thái PENDING_PAYMENT được phép checkout để hoàn tất thanh toán còn thiếu và kết thúc booking (thường xảy ra khi hệ thống tự động chuyển booking sang PENDING_PAYMENT sau giờ chơi nhưng chưa thu tiền).

3. **Trạng thái booking sau check-out**: Sau khi check-out thành công, trạng thái booking chuyển thành "Đã hoàn thành" (COMPLETED), đánh dấu kết thúc quy trình đặt sân.

4. **Thanh toán theo trạng thái hóa đơn**: 
   - Hóa đơn "Chưa thanh toán" (UNPAID): Thu toàn bộ số tiền còn thiếu bằng tiền mặt tại quầy
   - Hóa đơn "Đã thanh toán một phần" (PARTIALLY_PAID): Thu phần tiền còn thiếu bằng tiền mặt, thường là phí dịch vụ phát sinh hoặc các khoản chưa thanh toán khác
   - Hóa đơn "Đã thanh toán" (PAID): Không cần thu thêm tiền

5. **Ghi nhận check-out sớm**: Nếu khách hàng rời sân trước giờ kết thúc đã đặt, hệ thống ghi nhận thời điểm kết thúc thực tế để phục vụ báo cáo và đối soát.

6. **Giải phóng sân có điều kiện**: Sau khi check-out, hệ thống kết thúc các lượt sân trong booking, sau đó cập nhật trạng thái sân thành "Sẵn sàng" (AVAILABLE) nếu không còn booking nào khác đang sử dụng sân tại thời điểm đó.

7. **Tích điểm thành viên**: Khách hàng có tài khoản sẽ được tích điểm dựa trên tiền sân theo quy định (1 điểm cho mỗi 1,000 VNĐ tiền sân). Hệ thống tự động kiểm tra và nâng hạng thành viên nếu đủ điểm.

8. **Xử lý giao dịch nguyên tử**: Toàn bộ quá trình check-out được thực hiện trong một giao dịch dữ liệu. Nếu bất kỳ bước nào thất bại, toàn bộ giao dịch sẽ được rollback để đảm bảo tính nhất quán dữ liệu.

9. **Kiểm tra đồng thời**: Hệ thống sử dụng cơ chế kiểm tra đồng thời khi cập nhật trạng thái booking thành COMPLETED để đảm bảo chỉ một nhân viên có thể check-out booking, tránh xung đột khi nhiều nhân viên thao tác cùng lúc.

10. **Truy vấn hóa đơn mới nhất**: Hệ thống truy vấn lại hóa đơn từ cơ sở dữ liệu trong giao dịch để đảm bảo có tổng tiền mới nhất, tránh xung đột dữ liệu khi nhiều nhân viên thêm dịch vụ hoặc thay đổi hóa đơn cùng lúc.

### Yêu cầu phi chức năng

1. **Thời gian phản hồi**: Thao tác check-out cần hoàn thành trong thời gian hợp lý để đảm bảo trải nghiệm nhân viên và khách hàng.

2. **Xử lý giao dịch an toàn**: Toàn bộ quá trình check-out phải đảm bảo tính nhất quán dữ liệu, đặc biệt là cập nhật booking, hóa đơn, thanh toán, sân, và điểm thành viên.

3. **Kiểm tra quyền truy cập nghiêm ngặt**: Hệ thống phải đảm bảo nhân viên chỉ có thể check-out booking thuộc chi nhánh của họ để tránh thao tác trái phép.

4. **Ghi nhận hoạt động**: Hệ thống ghi log tất cả các lần check-out với thông tin nhân viên, thời gian, số tiền thanh toán, điểm tích lũy để hỗ trợ kiểm tra và đối soát.

5. **Xử lý đồng thời**: Hệ thống phải xử lý đúng trường hợp nhiều nhân viên cùng cố gắng check-out một booking để tránh xung đột dữ liệu.

6. **Cập nhật realtime**: Sau khi check-out, hệ thống cần cập nhật trạng thái booking và sân realtime để nhân viên khác có thể thấy thông tin mới nhất.
