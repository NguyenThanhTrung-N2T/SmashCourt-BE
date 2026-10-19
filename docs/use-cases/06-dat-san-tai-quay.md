# Use Case UC-BOOKING-02: Đặt sân tại quầy

## Bảng 3.6. Đặc tả Use case Đặt sân tại quầy

| Thông tin | Nội dung |
|-----------|----------|
| **Tên Use case** | Đặt sân tại quầy |
| **Mã Use case** | UC-BOOKING-02 |
| **Mô tả Use case** | Là nhân viên sân, tôi muốn tạo booking trực tiếp cho khách tại chi nhánh để khách có thể giữ sân theo thời gian mong muốn và thanh toán linh hoạt. |
| **Tác nhân** | Nhân viên sân hoặc Quản lý chi nhánh |
| **Tác nhân phụ** | Không có |
| **Mức độ ưu tiên** | Cao |
| **Điều kiện kích hoạt** | Nhân viên chọn chức năng "Đặt sân tại quầy" hoặc "Walk-in Booking" trên giao diện hệ thống. |
| **Điều kiện tiên quyết** | • Nhân viên đã đăng nhập vào hệ thống.<br>• Chi nhánh có ít nhất một sân đang hoạt động.<br>• Nhân viên có quyền đặt sân tại chi nhánh đó. |
| **Điều kiện cuối** | Booking được tạo thành công ở trạng thái CONFIRMED. Nếu có ghi nhận thanh toán tại thời điểm đặt, hệ thống cập nhật thông tin thanh toán tương ứng; nếu chưa thanh toán, hóa đơn được giữ ở trạng thái chờ thanh toán. |

---

## Luồng chính

1. Nhân viên truy cập màn hình Đặt sân tại quầy.
2. Hệ thống hiển thị form đặt sân với các trường thông tin.
3. Hệ thống tự động xác định chi nhánh của nhân viên hoặc quản lý chi nhánh đang thao tác.
4. Nhân viên chọn ngày đặt sân.
5. Nhân viên chọn loại sân (đơn hoặc đôi).
6. Hệ thống hiển thị danh sách sân khả dụng thuộc chi nhánh đã chọn.
7. Nhân viên chọn sân cụ thể.
8. Nhân viên chọn các khung giờ (slot) muốn đặt.
9. Hệ thống tính toán và hiển thị giá tạm tính cho các slot đã chọn.
10. Nhân viên tìm kiếm tài khoản khách hàng (theo tên, SĐT hoặc email).
11. Nếu tìm thấy tài khoản khách hàng, hệ thống hiển thị thông tin khách hàng (tên, SĐT, email, hạng thành viên, % chiết khấu loyalty).
12. Nếu tìm thấy tài khoản và có khuyến mãi khả dụng, hệ thống hiển thị danh sách khuyến mãi.
13. Nhân viên có thể chọn một khuyến mãi hoặc bỏ qua.
14. Hệ thống tính toán giá cuối cùng (giá gốc - chiết khấu loyalty - chiết khấu khuyến mãi).
15. Nhân viên có thể nhập ghi chú (tùy chọn).
16. Nhân viên chọn phương thức thanh toán: "Thanh toán ngay" hoặc "Thanh toán sau".
17. Nhân viên bấm "Xác nhận đặt sân".
18. Hệ thống kiểm tra slot còn khả dụng (chưa bị đặt hoặc khóa thanh toán).
19. Hệ thống tạo booking với trạng thái CONFIRMED.
20. Hệ thống tạo hóa đơn với thông tin chi tiết.
21. Nếu nhân viên ghi nhận thanh toán ngay, hệ thống cập nhật trạng thái thanh toán của hóa đơn.
22. Nếu chưa thanh toán, hệ thống giữ hóa đơn ở trạng thái chờ thanh toán.
23. Hệ thống hiển thị thông báo "Đặt sân thành công" với mã booking.
24. Hệ thống hiển thị thông tin booking chi tiết để nhân viên xác nhận lại với khách.

---

## Luồng thay thế

### (8a) Nhân viên chọn nhiều slot liên tiếp
- Nhân viên có thể chọn nhiều khung giờ liên tiếp trong cùng một lần đặt.
- Hệ thống tính tổng giá cho tất cả các slot.
- Use case tiếp tục từ bước 9.

### (10a) Không tìm thấy tài khoản khách hàng
- Nhân viên không tìm thấy hoặc không tìm kiếm tài khoản.
- Hệ thống yêu cầu nhập thông tin khách vãng lai (họ tên, SĐT, email).
- Nhân viên nhập thông tin khách vãng lai.
- Use case tiếp tục từ bước 14 (bỏ qua bước 11-13 về loyalty và khuyến mãi).

### (13a) Nhân viên chọn khuyến mãi (chỉ áp dụng khi có tài khoản khách hàng)
- Nhân viên chọn một khuyến mãi từ danh sách.
- Hệ thống kiểm tra điều kiện áp dụng khuyến mãi (ngày, giờ, số lần sử dụng, điều kiện khác).
- Hệ thống tính lại giá cuối cùng với chiết khấu khuyến mãi.
- Use case tiếp tục từ bước 14.

---

## Luồng ngoại lệ

### (4a) Nhân viên chọn ngày trong quá khứ
- Hệ thống hiển thị thông báo "Không thể đặt sân cho ngày trong quá khứ".
- Use case quay lại bước 4.

### (8b) Không có slot khả dụng
- Hệ thống hiển thị thông báo "Không có slot nào khả dụng trong ngày đã chọn".
- Nhân viên có thể chọn ngày khác hoặc quay lại chọn sân khác.

### (10a.1) Thông tin khách vãng lai thiếu hoặc không hợp lệ
- Nhân viên không nhập đủ thông tin bắt buộc (họ tên và SĐT).
- Hệ thống hiển thị thông báo "Vui lòng nhập tên và số điện thoại khách hàng".
- Use case quay lại bước 10a.

### (13a.1) Khuyến mãi không đáp ứng điều kiện
- Nhân viên chọn khuyến mãi nhưng không đáp ứng điều kiện (ví dụ: số booking tối thiểu, giờ áp dụng).
- Hệ thống hiển thị thông báo "Không đáp ứng điều kiện khuyến mãi".
- Use case quay lại bước 13.

### (18a) Slot đã bị đặt bởi booking khác (race condition)
- Hệ thống phát hiện slot đã bị đặt trong lúc nhân viên đang xác nhận.
- Hệ thống hiển thị thông báo "Slot đã được đặt bởi người khác, vui lòng chọn slot khác".
- Use case quay lại bước 8.

### (18b) Slot đang bị khóa thanh toán
- Hệ thống phát hiện slot đang bị khóa bởi một booking online đang thanh toán.
- Hệ thống hiển thị thông báo "Slot đang trong quá trình thanh toán, vui lòng chọn slot khác hoặc thử lại sau X phút".
- Use case quay lại bước 8.

### (19a) Sân đã bị tạm ngưng hoặc ngừng hoạt động
- Hệ thống kiểm tra trạng thái sân trước khi tạo booking.
- Nếu sân ở trạng thái SUSPENDED, hệ thống hiển thị "Sân đang tạm ngưng hoạt động".
- Nếu sân ở trạng thái INACTIVE, hệ thống hiển thị "Sân không còn hoạt động".
- Use case quay lại bước 7.

### (19b) Nhân viên không có quyền đặt sân tại chi nhánh này
- Hệ thống kiểm tra quyền nhân viên với chi nhánh được chọn.
- Nếu nhân viên không thuộc chi nhánh (Staff/Manager), hệ thống hiển thị "Bạn không có quyền đặt sân tại chi nhánh này".
- Use case kết thúc.

### (24a) Lỗi hệ thống hoặc lỗi kết nối cơ sở dữ liệu
- Hệ thống hiển thị thông báo "Đã xảy ra lỗi, vui lòng thử lại sau".
- Hệ thống rollback transaction nếu đang trong quá trình tạo booking.
- Use case kết thúc.

---

## Thông tin bổ sung

### Quy tắc nghiệp vụ

1. **Booking tại quầy luôn CONFIRMED**: Tất cả booking tại quầy đều được tạo ở trạng thái CONFIRMED (đã xác nhận) ngay lập tức, không cần chờ thanh toán như booking online.
2. **Ràng buộc về sân và khung giờ**: Trong một lượt đặt, các slot được chọn phải thuộc cùng một chi nhánh. Nếu đặt nhiều sân trong cùng một booking, các sân phải có cùng khung giờ bắt đầu và kết thúc. Nếu đặt một sân, nhân viên có thể chọn nhiều slot liên tiếp theo cấu hình hệ thống.
3. **Linh hoạt thanh toán trước hoặc sau**: Nhân viên có thể chọn "Thanh toán ngay" (thu tiền ngay tại quầy) hoặc "Thanh toán sau" (khách trả tiền khi đến chơi hoặc sau khi chơi xong).
4. **Phân quyền chi nhánh**: Staff và Branch Manager chỉ được đặt sân tại chi nhánh mình đang quản lý. Owner có thể đặt sân tại bất kỳ chi nhánh nào.
5. **Chiết khấu loyalty chỉ cho khách có tài khoản**: Khách hàng có tài khoản được hưởng chiết khấu theo hạng thành viên (% trên tổng giá sân). Khách vãng lai không được hưởng chiết khấu loyalty.
6. **Khuyến mãi chỉ cho khách có tài khoản**: Khuyến mãi chỉ áp dụng cho khách hàng có tài khoản trong hệ thống. Khách vãng lai không thể sử dụng khuyến mãi.
7. **Chiết khấu khuyến mãi sau loyalty**: Khuyến mãi áp dụng sau khi trừ chiết khấu loyalty. Công thức: `Giá cuối = Giá gốc - Loyalty - Promotion`.
8. **Điều kiện khuyến mãi**: Khuyến mãi phải đáp ứng các điều kiện (ngày áp dụng, giờ áp dụng, số lần sử dụng tối đa, số booking tối thiểu, v.v.).
9. **Tính giá động**: Giá sân được tính dựa trên cấu hình giá của chi nhánh (giá cơ bản + special price theo ngày/giờ + loại sân đơn/đôi).
10. **Thông tin khách vãng lai bắt buộc**: Nhân viên phải nhập ít nhất tên và số điện thoại khách hàng nếu không tìm thấy tài khoản trong hệ thống.
11. **Không cho phép đặt quá khứ**: Hệ thống không cho phép đặt sân cho ngày trong quá khứ.

### Yêu cầu phi chức năng

1. **Thời gian phản hồi**: Các thao tác tính giá và kiểm tra slot cần phản hồi trong thời gian hợp lý (< 2 giây).
2. **Xử lý race condition**: Hệ thống phải đảm bảo không có hai booking cùng đặt một slot (sử dụng cơ chế kiểm tra ràng buộc ở cơ sở dữ liệu).
3. **Phân quyền chặt chẽ**: Hệ thống phải kiểm tra quyền nhân viên với chi nhánh trước khi cho phép đặt sân.
4. **Ghi nhận hoạt động**: Hệ thống ghi log tất cả các lần đặt sân tại quầy với thông tin nhân viên thực hiện để hỗ trợ kiểm tra và đối soát.
5. **Xử lý transaction an toàn**: Toàn bộ quá trình đặt sân phải nằm trong một transaction để đảm bảo tính nhất quán dữ liệu.
6. **Hiển thị thông tin rõ ràng**: Sau khi đặt sân thành công, hệ thống phải hiển thị đầy đủ thông tin để nhân viên xác nhận lại với khách (mã booking, sân, giờ, giá tiền).
7. **Tìm kiếm khách hàng linh hoạt**: Hệ thống hỗ trợ tìm kiếm khách hàng theo tên, SĐT hoặc email để giúp nhân viên tra cứu nhanh chóng.
