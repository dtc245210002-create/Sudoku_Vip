# Thiết lập Sudoku VIP

Ứng dụng WPF dùng .NET 9 (`net9.0-windows`). `global.json` chọn SDK 9.0.315 và cho phép bản vá mới trong cùng feature band. Cài SDK phù hợp nếu máy chưa có.

## SQL Server (thực hiện thủ công)

Ứng dụng không tạo database, bảng hoặc chạy migration lúc khởi động. Không cần SQL để chơi Khách. Người vận hành phải chọn đúng database; nên xác nhận database test trước khi thử SQL.

1. Tạo một database rỗng dành riêng cho test bằng SSMS hoặc công cụ quản trị hiện có.
2. Chọn database đó trong SSMS. Với database mới, chạy `Database/setup.sql` để tạo bảng. Với database đang có, sao lưu và bỏ qua bootstrap.
3. Đọc và chạy `Database/migrations/001_game_identity.sql` trên database đã chọn. Script bổ sung `GameId` và unique index để lưu chống trùng, khóa username không phân biệt hoa/thường, và tính lại thống kê từ lịch sử. Không xóa lịch sử hoặc tài khoản. Nếu username hiện có trùng khi bỏ phân biệt hoa/thường, migration dừng và rollback; cần xử lý thủ công theo quyết định của chủ dữ liệu.
4. Cấu hình biến môi trường trong tiến trình chạy ứng dụng:

```powershell
$env:SUDOKU_SQL_SERVER = 'localhost\SQLEXPRESS'
$env:SUDOKU_SQL_DATABASE = 'SudokuVIP_Test'
# Chỉ với server test có chứng chỉ tự ký đã xác nhận:
$env:SUDOKU_SQL_TRUST_CERTIFICATE = 'true'
dotnet run --project sudokuvip.csproj
```

Kết nối dùng Windows Integrated Security, mặc định kiểm tra chứng chỉ; không lưu password hay chuỗi kết nối bí mật trong repository. Quyền runtime chỉ cần SELECT/INSERT/UPDATE các bảng liên quan; quyền tạo schema chỉ dùng khi thiết lập thủ công. Nếu thiếu cấu hình, chức năng tài khoản/lịch sử báo lỗi rõ ràng, không tự kết nối server khác.

## Build và kiểm thử không dùng database

```powershell
dotnet build sudokuvip.csproj
dotnet run --project Tests/SudokuVip.Tests.csproj
```

Kiểm thử là console harness trả exit code khác 0 khi thất bại, không phải dự án VSTest; dùng lệnh `dotnet run` ở trên. Harness thực thi các lớp sản phẩm, dùng store giả lập cho SQL, và dựng cửa sổ/control WPF trên STA với Dispatcher. Không dùng tài khoản hay database thật. Không kiểm chứng transaction SQL thực tế hoặc việc hiển thị đầy đủ ứng dụng qua thao tác người dùng.

## Quy tắc ván và phiên

- Điểm hiện tại: ô tự điền đúng 10, ô đã dùng gợi ý 5, ô trống/sai/fixed 0. Xóa và sửa tính lại điểm; undo khôi phục giá trị, notes và lỗi trước thao tác. Undo không hoàn lượt hint, và ô từng được tiết lộ giữ trần 5 điểm cho cả ván.
- Ván thắng/thua khóa thao tác và dừng timer trước khi lưu. Trạng thái lưu độc lập. UI hiện tại không có nút thử lưu lại; API lưu cùng `GameId` chống trùng nếu được gọi lại, kể cả khi commit trước đó đã xảy ra nhưng phản hồi bị mất.
- Logout kết thúc phiên Khách hiện tại. Chọn Khách khi đang là Khách chỉ đổi nickname/avatar và giữ phiên. Chọn Khách sau logout hoặc sau tài khoản tạo phiên mới. Các phiên cũ còn được giữ trong bộ nhớ cho thao tác đã chụp identity, không được chuyển sang tài khoản của phiên khác; tất cả dữ liệu khách mất khi tắt ứng dụng.
- Chuyển lịch sử chỉ có lúc tạo tài khoản mới và chọn checkbox. Lịch sử/thống kê Khách chỉ xóa sau commit được xác nhận; lỗi giữ dữ liệu để thử lại trong phiên hiện tại. Đăng nhập tài khoản có sẵn không chuyển dữ liệu Khách.
- Mật khẩu mới dùng PBKDF2-HMAC-SHA256, salt ngẫu nhiên 16 byte, 600.000 iterations, lưu thuật toán/tham số/salt/hash. Lựa chọn tham số tham khảo [OWASP](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html). Hash SHA-256 cũ được xác minh và nâng cấp sau xác thực thành công. Mật khẩu cũ không chịu quy tắc tối thiểu mới.
- Bốn mức chỉ phân loại bằng mục tiêu số ô trống 32/42/50/56. Sinh đề tối đa 8 lần, khoảng 2 giây cộng một phép kiểm tra bị giới hạn 50.000 node. Nếu không đạt mục tiêu, dùng đề tốt nhất đã xác minh duy nhất với ít ô trống hơn. Chạy trên worker để không giữ luồng UI.

## Cần kiểm thử SQL riêng

Trên database test đã xác nhận: bootstrap/migration, đăng ký đồng thời tên trùng, upgrade hash cũ, mất kết nối giữa transaction/commit, retry cùng GameId từ nhiều tiến trình, chuyển dữ liệu Khách rollback/commit và thống kê lịch sử. Sau đó chạy ứng dụng qua `App.OnStartup`, đăng nhập, chơi và xem lịch sử bằng UI. Script và đường SQL chưa được xác minh trên server thật trong lần sửa này.

Nếu server commit đăng ký nhưng client không nhận được xác nhận, dữ liệu khách vẫn được giữ. Tên tài khoản đã tồn tại có thể khiến lần đăng ký lại thất bại: cần kiểm tra trên database test/đối chiếu dữ liệu trước khi quyết định tiếp theo; ứng dụng không tự chuyển khách vào tài khoản đã có.
