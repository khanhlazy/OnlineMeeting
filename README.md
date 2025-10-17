OnlineMeeting (NET 9) – Họp trực tuyến, chat, camera/mic, UI rực rỡ

### 1) Yêu cầu môi trường
- **HĐH**: Windows 10+
- **.NET**: SDK 9.0 (hoặc Runtime 9.0 nếu chỉ chạy)
- **CSDL**: SQL Server Express/LocalDB (tùy chọn cho tính năng đăng ký/đăng nhập)

### 2) Cấu trúc giải pháp
- **`OnlineMeeting/MeetingServer`**: Server TCP xử lý lobby/phòng, chat, video, audio
- **`OnlineMeeting/MeetingClient`**: Ứng dụng WinForms cho người dùng cuối (đã áp theme mới)
- **`OnlineMeeting/MeetingShared`**: Giao thức, model, hằng số dùng chung

### 3) Cài đặt & Build
```bash
dotnet --version          # cần 9.x
dotnet restore
dotnet build -c Release
```
Hoặc mở `OnlineMeeting_fixed.sln` bằng Visual Studio 2022 (17.12+) và Build.

### 4) Cấu hình & Database (tùy chọn)
- Mở `OnlineMeeting/MeetingServer/appsettings.json` để chỉnh chuỗi kết nối.
- Chạy script `OnlineMeeting/MeetingServer/Sql/Create.sql` để tạo DB `OnlineMeetingDb` và bảng `Users` (hash + salt).

### 5) Chạy thử nhanh (Local)
1. Chạy server:
   ```bash
   dotnet run --project OnlineMeeting/MeetingServer
   ```
   Mặc định lắng nghe TCP port `5555`.
2. Chạy client:
   ```bash
   dotnet run --project OnlineMeeting/MeetingClient
   ```
3. Ở màn hình đăng nhập: Host `127.0.0.1`, Port `5555`, đăng ký tài khoản mới hoặc đăng nhập.
4. Vào Lobby, **Tạo phòng** để lấy mã, rồi **Tham gia** ở các client khác bằng mã đó.

### 6) Tính năng chính
- **Đăng ký/Đăng nhập**: xác thực đơn giản qua server + SQL (hash + salt)
- **Phòng họp**: tạo/join phòng bằng mã, hiển thị danh sách người và trạng thái (Host/Cam/Mic)
- **Chat**: văn bản thời gian thực trong phòng
- **Camera/Mic**: bật/tắt, chọn nguồn; có chế độ DEMO cho cả video và mic
- **Kick**: Host có quyền đưa người dùng ra khỏi phòng
- **Khả năng nhiều phiên bản**: mở nhiều client trên cùng máy để thử nghiệm

### 7) UI & Trải nghiệm
- Giao diện WinForms đã **áp dụng theme đậm** với màu **tím/teal nổi bật**, trạng thái rõ ràng
- Nút hành động chính dùng màu nổi bật; thanh công cụ và status bar đồng bộ tông màu
- TextBox/ListBox/RichTextBox dùng nền tối, chữ sáng; hỗ trợ tooltip, placeholder

### 8) Mẹo & Khắc phục sự cố
- Nếu không thấy camera/mic, bật danh sách chọn để refresh thiết bị
- Client gửi video ~10 fps (JPEG chất lượng ~60) để cân bằng hiệu năng
- Âm thanh rè/echo khi chạy nhiều client trên cùng máy: dùng headphone
- Không đăng nhập được: kiểm tra DB và firewall (TCP `5555`, SQL Server)



