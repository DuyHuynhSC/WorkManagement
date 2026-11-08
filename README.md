# WorkManagement - DevOps Workspace & Automation Hub

**WorkManagement** là ứng dụng C# .NET 9 WPF hiện đại kết hợp chế độ dòng lệnh (Console/CMD Hybrid) và chạy ngầm dưới Khay hệ thống (System Tray), đóng vai trò là trung tâm làm việc (Workspace Hub) dành cho lập trình viên, kỹ sư DevOps và quản trị hệ thống:
1. **Trang Tổng Quan & Nhật Ký Hệ Thống (Dashboard & Audit Log):** Thống kê trực quan số lượng máy ảo, dự án, dung lượng kho Backup/Log cục bộ, danh sách máy ảo sắp hết hạn và công cụ giải phóng ổ đĩa nhanh.
2. **Tìm Kiếm Toàn Cục & Command Palette (`Ctrl + K`):** Bật nhanh từ bất kỳ đâu trên Windows (kể cả khi ứng dụng đang ẩn dưới System Tray) để tìm máy ảo kết nối RDP 1 chạm, mở thư mục dự án hoặc chuyển trang.
3. **Quản lý máy ảo & Remote Desktop (VMs & RDP Hub):** Quản lý tập trung toàn bộ danh sách máy ảo, server cơ sở dữ liệu, tự động đăng nhập Remote Desktop (RDP) chỉ với 1 click, import/export dữ liệu Excel thông minh.
4. **Quản lý Dự Án & Dọn Dẹp Oracle DB (Project & Oracle Cleanup):** Quản lý danh mục dự án, thư mục Source Code, kết nối Oracle SYSDBA để tự động `DROP USER ... CASCADE` và `DROP TABLESPACE` khi dự án kết thúc.
5. **Tự động hóa dọn dẹp & quản lý file FTP (FTP Cleaner & Manager):** Kết nối FTPS (TLS 1.2/1.3), quét đa máy chủ với bộ lọc nâng cao (Whitelist/Extension/Size), sao lưu (backup) trước khi xóa, chỉnh sửa trực tiếp file trên FTP, kéo thả (Drag & Drop) upload và gửi email báo cáo.
6. **Lập lịch chạy tự động ngầm (Windows Task Scheduler) & System Tray:** Hỗ trợ thu nhỏ chạy ngầm dưới góc phải Taskbar và tự động khởi chạy mỗi sáng khi đăng nhập Windows.

---

## 1. Các Tính Năng Nổi Bật

### 1.1. Bảo Mật Mật Khẩu Chuẩn Windows DPAPI (`SecretProtector`)
- **Mã hóa tự động bằng Windows DPAPI (`CurrentUser`):** Toàn bộ mật khẩu nhạy cảm (FTP Password, Proxy Password, SMTP Password, VM RDP Password, Oracle SYSDBA Password) được tự động mã hóa với tiền tố `ENC:DPAPI:...` trước khi lưu xuống ổ cứng (`config.json`, `virtual_machines.json`, `projects.json`).
- **Tự động nâng cấp (Backward-Compatible Auto-Migration):** Nếu phát hiện file cấu hình cũ đang lưu mật khẩu dạng văn bản thuần (plain-text), ứng dụng tự động đọc, giải mã trên RAM và mã hóa lại chuẩn DPAPI ngay trong lần khởi chạy tiếp theo.
- **Nút Ẩn/Hiện Mật Khẩu (Eye Toggle):** Tích hợp nút con mắt (`EyeOutline` / `EyeOffOutline`) tại các màn hình cấu hình FTP, Proxy, Email SMTP và hộp thoại thêm/sửa Máy ảo để kiểm tra nhanh mật khẩu khi nhập.

### 1.2. Trang Tổng Quan (Dashboard), Command Palette (`Ctrl + K`) & System Tray
- **Trang Tổng Quan (System Dashboard):**
  - Hiển thị 4 thẻ chỉ số KPI thời gian thực: *Tổng số Máy ảo & RDP*, *Số Máy ảo sắp hết hạn ($\le 14$ ngày)*, *Tổng số Dự án & Oracle DB*, và *Dung lượng lưu trữ cục bộ (Backups + Logs)*.
  - **Bảng cảnh báo sớm Máy ảo sắp/đã hết hạn:** Theo dõi trực quan số ngày còn lại để chủ động gia hạn server.
  - **Công cụ Dọn dẹp ổ đĩa cục bộ (Local Storage Cleaner):** Xóa nhanh các bản sao lưu FTP (`Backups`) và nhật ký (`logs`) cũ hơn $N$ ngày ngay trên giao diện.
  - **Nhật ký hoạt động (Audit Log Viewer):** Xem trực tiếp 150 dòng nhật ký mới nhất kèm bộ lọc chỉ xem lỗi (`[ERR]`).
- **Command Palette Toàn Cục (`Ctrl + K` / `Ctrl + Alt + K`):**
  - Nhấn `Ctrl + K` ở bất kỳ màn hình nào (hoặc nhấn từ bất kỳ đâu trên Windows khi ứng dụng đang thu nhỏ dưới **System Tray**) để mở thanh tìm kiếm trung tâm.
  - Chỉ hiển thị các **Máy ảo Yêu thích (được gắn ngôi sao ★)** trong danh sách Tìm Nhanh giúp bảng tìm kiếm luôn gọn gàng; gõ tên máy ảo hoặc IP $\rightarrow$ nhấn `Enter` để mở thẳng **Remote Desktop (RDP)**.
  - Gõ tên dự án $\rightarrow$ nhấn `Enter` để mở ngay thư mục Source Code trong Windows Explorer.
- **Chạy Ngầm Dưới Khay Hệ Thống (System Tray / Notification Area):**
  - Nút **"Ẩn xuống Tray"** trên thanh tiêu đề hoặc tự động thu nhỏ xuống System Tray khi bấm nút Đóng (`X`).
  - **Menu chuột phải tại biểu tượng System Tray:**
    - Mở lại cửa sổ chính hoặc bật nhanh **Command Palette (`Ctrl + K`)**.
    - **Submenu "Kết nối nhanh RDP (Máy ảo)":** Liệt kê danh sách máy ảo giúp kết nối 1 chạm trực tiếp từ góc phải thanh Taskbar.
    - Kích hoạt nhanh **Quét Xem Trước FTP (Preview)**.
    - Tùy chọn bật/tắt *"Thu nhỏ xuống Tray khi bấm Đóng (X)"* và nút *"Thoát hoàn toàn (Exit)"*.

### 1.3. Quản Lý Máy Ảo & Kết Nối Remote Desktop (VMs & RDP Hub)
- **Danh mục máy ảo chi tiết:** Quản lý đầy đủ các trường thông tin: *STT, Địa chỉ IP, Tên máy, Domain/Workgroup, Hệ điều hành (OS), User, Password, TNS, Database Name, Oracle Version, Người kiểm tra, Ghi chú, Ngày hết hạn*.
- **Đánh dấu Máy ảo Yêu thích (Ngôi sao ★ - Favorite VM):** Nằm ngay giữa cột CheckBox và nút Play; khi tích chọn ngôi sao vàng (`IsFavorite`), máy ảo đó sẽ xuất hiện trong **Tìm Nhanh (`Ctrl + K`)** và menu kết nối nhanh dưới **System Tray** (các máy ảo không gắn sao sẽ được ẩn khỏi Tìm Nhanh để tránh làm rối danh sách).
- **Kết nối RDP 1-click (Nút Play):**
  - Tự động nạp thông tin đăng nhập vào Windows Credential (`cmdkey`) theo chuẩn định dạng `[Tên máy]\[User]` (ví dụ: `D2-SVR15-6016\Administrator`) và khởi chạy `mstsc.exe` kết nối tới IP đích mà không cần nhập lại mật khẩu.
- **Import Đa Sheet Thông Minh & Xuất Danh Sách Ra Excel (`Export Excel`):**
  - Hỗ trợ import cả file `.xlsx` và `.xls`, tự động nhận diện dòng tiêu đề và chấm điểm đề xuất Sheet.
  - Hỗ trợ 3 chế độ xử lý khi trùng IP/Tên máy: *Ghi đè (Overwrite)*, *Bỏ qua (Skip)*, *Thêm tất cả (Append)*.
  - **Nút "Xuất Excel":** Kết xuất danh sách máy ảo đang lọc hoặc các dòng đang tích chọn ra file `.xlsx` chuyên nghiệp để chia sẻ hoặc lưu trữ.
- **Tiện ích bảng biểu:** Nút *"Auto-Fit Cột"*, *"Xóa Tất Cả"*, *"Tải File Mẫu"* và sao chép nhanh IP/Password vào Clipboard.

### 1.4. Quản Lý Dự Án & Tự Động Dọn Dẹp Oracle DB (Project Manager)
- Quản lý danh sách dự án gồm: *No, Tên dự án, Source Folder, Dung lượng (GB), Host Server, Port, Service Name, SYSDBA User/Password, DB Users, Tablespaces, Người phụ trách, Trạng thái*.
- **Tính năng Dọn dẹp Dự án (Clean Up):** Tự động xóa thư mục Source Folder cục bộ và kết nối vào Oracle Database với quyền `SYSDBA` để ngắt kết nối (`KILL SESSION`), xóa `DB Users` (`DROP USER ... CASCADE`) và xóa `Tablespaces` (`INCLUDING CONTENTS AND DATAFILES`).
- Hỗ trợ **Import Excel**, **Tải File Mẫu** và **Xuất Excel (`Export Excel`)** danh sách dự án hiện tại.

### 1.5. Phân Hệ FTP Cleaner & Quản Lý File Nâng Cao (FTP Automation)
- **Tự động quét & dọn dẹp file theo hạn lưu trữ & Bộ lọc nâng cao:**
  ```text
  SystemDate.Date > UploadDate.Date + RetentionDays (Mặc định RetentionDays = 1)
  ```
  - **Danh sách loại trừ (Exclude Whitelist Patterns):** Bảo vệ các file/thư mục quan trọng (ví dụ: `keep*, *.bak, / Important /`) không bao giờ bị quét xóa.
  - **Chỉ quét đuôi file chỉ định (Include Extensions):** Giới hạn chỉ dọn dẹp các định dạng cụ thể (ví dụ: `.zip, .log, .tmp`).
  - **Kích thước file tối thiểu (Min File Size MB):** Chỉ quét các file có dung lượng lớn hơn ngưỡng cấu hình.
- **Quét đồng thời nhiều Profile (`Multi-Profile Scan`):** Quét trên profile hiện tại hoặc quét hàng loạt trên tất cả các máy chủ FTP đã cấu hình.
- **Cơ chế Fail-safe Backup:** Tải file về máy tính cục bộ bảo toàn cấu trúc thư mục con trước khi thực hiện lệnh xóa thật trên FTP Server.
- **Trình duyệt File FTP Trực Quan & Chỉnh Sửa Trực Tiếp:**
  - **Kéo & Thả (Drag & Drop Upload):** Kéo file hoặc thư mục từ Windows Explorer thả trực tiếp vào bảng File Manager để tải lên FTP Server.
  - **Sửa nhanh & Lưu trực tiếp lên FTP (Quick Edit & Save):** Mở xem trước các file văn bản/cấu hình (`.txt, .json, .xml, .config, .sql, .sh, .log...`), bật **"Chế độ chỉnh sửa"** để sửa nội dung và bấm **"Lưu trực tiếp lên FTP"** mà không cần tải về thủ công.
  - **Menu chuột phải & Thao tác nhanh:** Đổi tên file/thư mục trực tiếp (`Rename` / phím `F2`), Sao chép đường dẫn FTP (`Copy Remote Path`), Upload file/thư mục, Download, Tạo thư mục mới và Xóa trực tiếp.
- **Email Báo Cáo HTML:** Gửi báo cáo chi tiết danh sách file cần xóa qua SMTP server tới danh sách email cấu hình sẵn.

### 1.6. Giao Diện Người Dùng & 8 Phong Cách Theme
- **Menu Dock Bên Trái (Sidebar):** Điều hướng linh hoạt giữa các chức năng, có nút thu gọn/mở rộng menu (Collapse/Expand) để mở rộng tối đa không gian làm việc.
- **8 Chủ đề giao diện (Themes):**
  - *Material Light* (Sáng hiện đại, độ tương phản cao)
  - *Material Dark* (Tối dịu mắt)
  - *Skeuomorphism* (Mô phỏng chất liệu nổi)
  - *Neumorphism* (Giao diện dập nổi mượt mà)
  - *Frutiger Aero* (Kính trong suốt, xanh lục bảo)
  - *Claymorphism* (Đất sét 3D bo tròn)
  - *Glassmorphism* (Kính mờ acrylic)
  - *Y2K Digital* (Công nghệ số hoài cổ)

---

## 2. Chế Độ Dòng Lệnh & Lập Lịch Tự Động (Windows Task Scheduler)

Ứng dụng hỗ trợ chạy ở chế độ Console/CMD không cần mở giao diện đồ họa, hoặc khởi động thu nhỏ trực tiếp dưới System Tray.

### 2.1. Các Tham Số Dòng Lệnh (CLI Arguments)

| Tham số | Ý nghĩa |
| :--- | :--- |
| `--preview` hoặc `/preview` | **(Mặc định - Khuyên dùng):** Chỉ quét và liệt kê danh sách file cần xóa ra màn hình console và file log, **không xóa thật** trên FTP. |
| `--auto` hoặc `--console` | **Tự Động Thực Thi:** Tự động sao lưu file về máy và thực hiện lệnh xóa thật trên FTP. |
| `--all` hoặc `--all-profiles` | Áp dụng quét trên **TẤT CẢ các Site Profile** đã cấu hình trong `config.json`. |
| `--send-email` hoặc `--email` | **Tự động gửi email thông báo** nếu có ít nhất 1 file thỏa điều kiện xóa. |
| `--tray` hoặc `--minimized` | Khởi động ứng dụng ở chế độ **ẩn dưới Khay hệ thống (System Tray)**, sẵn sàng gọi nhanh bằng `Ctrl + K`. |

### 2.2. Lệnh CLI Thường Dùng

1. **Quét kiểm tra tất cả Profile và gửi mail báo cáo (Chế độ khuyến nghị mỗi sáng):**
   ```cmd
   WorkManagement.exe --preview --all --send-email
   ```
2. **Khởi động ngầm dưới Khay hệ thống (System Tray) cùng Windows:**
   ```cmd
   WorkManagement.exe --tray
   ```
3. **Xem trước danh sách file trên console (không gửi mail, không xóa):**
   ```cmd
   WorkManagement.exe --preview --all
   ```
4. **Thực thi Backup & Xóa tự động trên tất cả Profile:**
   ```cmd
   WorkManagement.exe --auto --all
   ```

---

## 3. Cấu Hình Windows Task Scheduler

Bạn có thể thiết lập lịch chạy tự động theo 1 trong 2 cách sau:

### Cách 1: Thiết lập trực tiếp từ Giao diện Ứng dụng (Nhanh nhất - 1 Click)

1. Mở ứng dụng **WorkManagement**, ở menu bên trái chọn **Lịch Task Scheduler**.
2. Cửa sổ cấu hình hiển thị:
   - **Thời điểm kích hoạt Task:**
     - **Tùy chọn 1 (Mặc định - Khuyên dùng cho mỗi sáng):** *"Khi khởi động / đăng nhập Windows (Tự động delay 1 phút chờ mạng/VPN sẵn sàng)"*.
     - **Tùy chọn 2:** *"Chạy định kỳ vào giờ cố định hàng ngày (HH:mm)"*.
   - **Chế độ thực thi:** Mặc định chọn **`Chỉ Preview & Gửi Mail Báo Cáo (--preview)`** để đảm bảo an toàn tuyệt đối.
   - Tích chọn sẵn **Áp dụng cho tất cả Profiles FTP (--all)** và **Gửi email thông báo kết quả (--send-email)**.
3. Bấm nút **"Tạo Task Vào Windows"**. Ứng dụng sẽ tự động đăng ký Task vào Windows Task Scheduler.
   *(Nếu cần chạy thủ công, bạn có thể bấm **Copy Lệnh** để dán vào PowerShell Administrator).*

---

### Cách 2: Thiết lập bằng PowerShell Administrator

Mở PowerShell với quyền Quản trị (`Run as Administrator`) và chạy đoạn mã sau:

```powershell
$exePath = "C:\SourceCode\WorkManagement\src\WorkManagement\bin\Release\net9.0-windows\WorkManagement.exe"
$workingDir = [System.IO.Path]::GetDirectoryName($exePath)

$action = New-ScheduledTaskAction -Execute $exePath -Argument "--preview --all --send-email" -WorkingDirectory $workingDir
$trigger = New-ScheduledTaskTrigger -AtLogOn
# Delay 1 phút sau khi đăng nhập để đảm bảo card mạng/VPN/Proxy đã kết nối ổn định
$trigger.Delay = "PT1M"
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable

Register-ScheduledTask -TaskName "WorkManagement_MorningPreview" `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Description "WorkManagement tu dong quet va kiem tra FTP moi sang khi mo may." `
    -RunLevel Highest -Force

Write-Host "Da tao Task thanh cong! Chay thu nghiem ngay:" -ForegroundColor Green
Start-ScheduledTask -TaskName "WorkManagement_MorningPreview"
```

---

## 4. Cấu Trúc Lưu Trữ Dữ Liệu & Bảo Mật

1. **Cấu hình FTP, Bộ lọc, Theme & Email (`config.json`):**
   - Lưu trữ cùng thư mục với file thực thi `WorkManagement.exe` (Mật khẩu FTP, Proxy, SMTP được mã hóa chuẩn Windows DPAPI).
2. **Danh sách máy ảo (`virtual_machines.json`):**
   - Lưu trữ tại thư mục AppData của người dùng:
     `%APPDATA%\WorkManagement\virtual_machines.json` (Mật khẩu RDP được mã hóa chuẩn Windows DPAPI).
3. **Danh sách dự án (`projects.json`):**
   - Lưu trữ tại thư mục AppData của người dùng:
     `%APPDATA%\WorkManagement\projects.json` (Mật khẩu SYSDBA được mã hóa chuẩn Windows DPAPI).
4. **Nhật ký hoạt động (`logs`) & Sao lưu (`Backups`):**
   - Toàn bộ log quét, kết nối RDP, thao tác backup/xóa file được lưu tại `logs/WorkManagement_yyyyMMdd.log` và có thể xem hoặc dọn dẹp trực tiếp từ trang **Tổng Quan (Dashboard)**.

---

## 5. Hướng Dẫn Biên Dịch & Kiểm Thử (Developer Guide)

- **Môi trường yêu cầu:** .NET 9.0 SDK trở lên trên Windows.
- **Biên dịch bản Release:**
  ```powershell
  dotnet build -c Release WorkManagement.sln
  ```
- **Chạy kiểm thử toàn bộ Unit Tests (xUnit):**
  ```powershell
  dotnet test WorkManagement.sln
  ```
- File thực thi sau khi biên dịch:
  `src\WorkManagement\bin\Release\net9.0-windows\WorkManagement.exe`
