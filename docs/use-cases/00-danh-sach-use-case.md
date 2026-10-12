# Danh sách 24 Use Case Đặc Tả Chi Tiết

## Tổng quan

Hệ thống SmashCourt có tổng cộng 24 use case được đặc tả chi tiết, phân bổ đều cho 4 nhóm actor chính:
- **Nhóm 1**: Xác thực và tài khoản (4 UC)
- **Nhóm 2**: Khách hàng đặt sân online (9 UC)
- **Nhóm 3**: Nhân viên sân (5 UC)
- **Nhóm 4**: Quản lý chi nhánh và chủ hệ thống (6 UC)

---

## Nhóm 1: Xác thực và Tài khoản (4 UC)

### UC-01: Đăng ký tài khoản
**Actor**: Guest (Khách truy cập)  
**Mô tả**: Người dùng mới tạo tài khoản bằng email/password hoặc Google OAuth  
**Tài liệu**: `01-dang-ky-tai-khoan.md`

### UC-02: Đăng nhập
**Actor**: Guest, Customer, Staff, Manager, Admin  
**Mô tả**: Người dùng đăng nhập vào hệ thống bằng email/password hoặc Google OAuth  
**Tài liệu**: `02-dang-nhap.md`

### UC-03: Quên mật khẩu
**Actor**: Guest, Customer, Staff, Manager, Admin  
**Mô tả**: Người dùng yêu cầu đặt lại mật khẩu qua email OTP  
**Tài liệu**: `03-quen-mat-khau.md`

### UC-04: Quản lý hồ sơ cá nhân
**Actor**: Customer, Staff, Manager, Admin  
**Mô tả**: Người dùng cập nhật thông tin cá nhân, avatar, đổi mật khẩu  
**Tài liệu**: `04-quan-ly-ho-so-ca-nhan.md`

---

## Nhóm 2: Khách hàng đặt sân online (9 UC)

### UC-05: Xem chi nhánh và lịch sân
**Actor**: Guest, Customer  
**Mô tả**: Xem danh sách chi nhánh, lọc theo khu vực, xem time grid theo ngày  
**Tài liệu**: `05-xem-chi-nhanh-va-lich-san.md`

### UC-06: Đặt sân online
**Actor**: Customer  
**Mô tả**: Chọn sân, khung giờ, dịch vụ đi kèm và tạo booking  
**Tài liệu**: `06-dat-san-online.md`


### UC-07: Áp dụng mã khuyến mãi
**Actor**: Customer  
**Mô tả**: Nhập mã khuyến mãi khi đặt sân để được giảm giá  
**Tài liệu**: `07-ap-dung-ma-khuyen-mai.md`

### UC-08: Thanh toán online qua VNPay
**Actor**: Customer  
**Mô tả**: Thanh toán booking qua cổng VNPay và xử lý callback  
**Tài liệu**: `08-thanh-toan-online-vnpay.md`

### UC-09: Xem lịch sử và chi tiết booking
**Actor**: Customer  
**Mô tả**: Xem danh sách booking đã đặt, chi tiết từng booking, trạng thái, hóa đơn  
**Tài liệu**: `09-xem-lich-su-booking.md`

### UC-10: Hủy booking online và theo dõi hoàn tiền
**Actor**: Customer  
**Mô tả**: Hủy booking, tính toán hoàn tiền theo chính sách, theo dõi trạng thái refund  
**Tài liệu**: `10-huy-booking-online.md`

### UC-11: Xem điểm loyalty và lịch sử giao dịch điểm
**Actor**: Customer  
**Mô tả**: Xem tổng điểm, bậc loyalty, lịch sử cộng/trừ điểm, quyền lợi  
**Tài liệu**: `11-xem-diem-loyalty.md`

### UC-12: Chat với AI chatbot
**Actor**: Guest, Customer  
**Mô tả**: Tương tác với AI chatbot để hỏi về giờ mở cửa, giá sân, chính sách  
**Tài liệu**: `12-chat-voi-ai-chatbot.md`

### UC-13: Xem gợi ý đặt sân từ AI
**Actor**: Customer  
**Mô tả**: Xem gợi ý sân/khung giờ dựa trên lịch sử đặt sân và sở thích  
**Tài liệu**: `13-goi-y-dat-san-ai.md`

---

## Nhóm 3: Nhân viên sân (5 UC)

### UC-14: Tạo booking tại quầy
**Actor**: Branch Staff  
**Mô tả**: Nhân viên tạo booking cho khách walk-in tại quầy  
**Tài liệu**: `14-tao-booking-tai-quay.md`

### UC-15: Check-in booking
**Actor**: Branch Staff  
**Mô tả**: Xác nhận khách hàng đã đến sân và bắt đầu sử dụng  
**Tài liệu**: `15-check-in-booking.md`

### UC-16: Check-out booking
**Actor**: Branch Staff  
**Mô tả**: Xác nhận khách hàng kết thúc sử dụng sân  
**Tài liệu**: `16-check-out-booking.md`

### UC-17: Ghi nhận thanh toán tại quầy
**Actor**: Branch Staff  
**Mô tả**: Ghi nhận thanh toán bằng tiền mặt hoặc thẻ tại quầy  
**Tài liệu**: `17-ghi-nhan-thanh-toan-tai-quay.md`

### UC-18: Hủy booking tại quầy và ghi nhận hoàn tiền
**Actor**: Branch Staff  
**Mô tả**: Nhân viên hủy booking theo yêu cầu khách hàng và xử lý hoàn tiền  
**Tài liệu**: `18-huy-booking-tai-quay.md`

---

## Nhóm 4: Quản lý chi nhánh và Chủ hệ thống (6 UC)

### UC-19: Quản lý khách hàng
**Actor**: Branch Staff, Branch Manager, System Admin  
**Mô tả**: Xem danh sách khách hàng, lịch sử booking, loyalty points  
**Tài liệu**: `19-quan-ly-khach-hang.md`

### UC-20: Quản lý nhân viên chi nhánh
**Actor**: Branch Manager, System Admin  
**Mô tả**: Thêm/xóa/phân quyền nhân viên cho chi nhánh  
**Tài liệu**: `20-quan-ly-nhan-vien-chi-nhanh.md`

### UC-21: Quản lý giá sân và chính sách
**Actor**: Branch Manager, System Admin  
**Mô tả**: Cấu hình giá hệ thống, override giá chi nhánh, chính sách hủy  
**Tài liệu**: `21-quan-ly-gia-san-chinh-sach.md`

### UC-22: Quản lý dịch vụ và khuyến mãi
**Actor**: Branch Manager, System Admin  
**Mô tả**: Quản lý dịch vụ đi kèm và khuyến mãi (thêm/sửa/xóa/kích hoạt)  
**Tài liệu**: `22-quan-ly-dich-vu-khuyen-mai.md`

### UC-23: Xem báo cáo thống kê và phân tích AI
**Actor**: Branch Manager, System Admin  
**Mô tả**: Xem dashboard, báo cáo doanh thu, tỉ lệ lấp đầy, AI insights  
**Tài liệu**: `23-bao-cao-thong-ke-phan-tich-ai.md`

### UC-24: Quản trị hệ thống: chi nhánh, sân, người dùng và cấu hình
**Actor**: System Admin  
**Mô tả**: Quản lý toàn bộ chi nhánh, sân, loại sân, người dùng, phân quyền, cấu hình hệ thống  
**Tài liệu**: `24-quan-tri-he-thong.md`

---

## Phân tích phân bổ Use Case

### Theo Actor:
- **Guest**: 3 UC (UC-01, UC-02, UC-05, UC-12)
- **Customer**: 13 UC (UC-02 đến UC-13)
- **Branch Staff**: 9 UC (UC-02, UC-04, UC-14 đến UC-19)
- **Branch Manager**: 10 UC (UC-02, UC-04, UC-19 đến UC-23)
- **System Admin**: 11 UC (UC-02, UC-04, UC-19 đến UC-24)

### Theo nhóm chức năng:
- **Authentication**: 4 UC (17%)
- **Booking Online**: 9 UC (37%)
- **Staff Operations**: 5 UC (21%)
- **Management**: 6 UC (25%)

---

## Lưu ý khi đặc tả

### Mỗi Use Case sẽ bao gồm:
1. **Bảng đặc tả use case** (Use Case Specification Table)
   - ID, Tên, Actor, Mô tả, Tiền điều kiện, Hậu điều kiện
   - Luồng chính (Main Flow)
   - Luồng thay thế (Alternative Flow)
   - Luồng ngoại lệ (Exception Flow)

2. **Activity Diagram** (Sơ đồ hoạt động)
   - Mô tả quy trình từ góc nhìn người dùng
   - Thể hiện decision points và parallel flows

3. **Sequence Diagram** (Sơ đồ tuần tự)
   - Mô tả tương tác giữa Frontend, Backend, Database, External Services
   - Thể hiện message passing và lifelines

### Ưu tiên Sequence Diagram:
Nếu cần tiết kiệm công sức, nên làm sequence diagram cho **15 use case quan trọng nhất**:
- ✅ UC-01: Đăng ký tài khoản
- ✅ UC-02: Đăng nhập
- ✅ UC-03: Quên mật khẩu
- ✅ UC-06: Đặt sân online
- ✅ UC-07: Áp dụng mã khuyến mãi
- ✅ UC-08: Thanh toán online qua VNPay
- ✅ UC-10: Hủy booking online
- ✅ UC-12: Chat với AI chatbot
- ✅ UC-13: Xem gợi ý đặt sân từ AI
- ✅ UC-14: Tạo booking tại quầy
- ✅ UC-15: Check-in booking
- ✅ UC-17: Ghi nhận thanh toán tại quầy
- ✅ UC-18: Hủy booking tại quầy
- ✅ UC-23: Xem báo cáo thống kê và phân tích AI
- ✅ UC-24: Quản trị hệ thống

---

## Cấu trúc thư mục tài liệu

```
docs/
└── use-cases/
    ├── 00-danh-sach-use-case.md (file này)
    ├── 01-dang-ky-tai-khoan.md
    ├── 02-dang-nhap.md
    ├── ...
    ├── 24-quan-tri-he-thong.md
    └── diagrams/
        ├── activity/
        │   ├── UC-01-activity.png
        │   ├── UC-02-activity.png
        │   └── ...
        └── sequence/
            ├── UC-01-sequence.png
            ├── UC-02-sequence.png
            └── ...
```

---

## Kết luận

Danh sách 24 use case này đã bao phủ đầy đủ các nghiệp vụ core của hệ thống SmashCourt:
- ✅ Xác thực và quản lý tài khoản
- ✅ Booking online với loyalty và AI
- ✅ Vận hành tại quầy cho nhân viên
- ✅ Quản lý và báo cáo cho Manager/Admin

Việc đặc tả chi tiết 24 use case này với bảng đặc tả + activity diagram + sequence diagram (ưu tiên 15 UC) sẽ đủ để thể hiện toàn bộ luồng nghiệp vụ trong báo cáo đồ án.
