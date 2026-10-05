# Luồng chọn chế độ và Sudoku 4×4

Dự án đích: `C:\Users\admin\OneDrive\Desktop\sudokuvip\sudokuvip`.
Trạng thái ban đầu: nhánh `develop`, HEAD `ac30b4819a50786ab81c868574d3811bd6fa6f39`, working tree sạch. Không có `AGENTS.md` áp dụng.
Trong lúc triển khai, `AssemblyInfo.cs` có thay đổi riêng; file đó được giữ nguyên, không chép đè. Hash các file được cập nhật được xác minh lại trước khi áp dụng.

## Hành vi mới

- Sau đăng nhập hoặc chọn Khách, MainWindow mở màn chọn **Bình thường / Biến thể / PvP**. Chưa sinh đề và chưa chạy đồng hồ.
- Chọn chế độ mới mở màn thiết lập. Chọn độ khó chưa bắt đầu ván.
- Với **Biến thể**, người chơi phải chọn **Sudoku 4×4** rồi nhấn **Bắt đầu chơi**.
- Bàn 4×4 có 16 ô, số 1–4, khối 2×2 và bàn phím bốn số; ghi chú cũng có bốn vị trí. Dùng chung logic điểm, gợi ý, undo, pause và khóa kết quả đã sửa trước đây.
- **Bình thường** sử dụng chế độ đang có; không thêm một biến thể 9×9 mới.
- **PvP** chỉ là lựa chọn “Sắp ra mắt”; chưa triển khai đối kháng.
- Quay lại thiết lập/chọn chế độ dừng đồng hồ và bỏ qua kết quả sinh đề đến muộn. Đổi tài khoản quay lại bước chọn chế độ.
- Lịch sử ván 4×4 được phân biệt bằng nhãn `4×4 · <độ khó>` trong trường Difficulty hiện có. Không cần migration mới.

## Sinh đề

Cả hai kích thước dùng thuật toán đếm nghiệm trên bản sao, dừng khi gặp nghiệm thứ hai. Việc xóa ô chỉ được giữ nếu đề còn một nghiệm duy nhất. Bốn mức 4×4 có mục tiêu 6 / 8 / 10 / 12 ô trống; giữ giới hạn thời gian/số lần thử và fallback sang đề duy nhất tốt nhất. Độ khó hiện phân loại theo mục tiêu số ô trống, chưa đánh giá bằng kỹ thuật giải.

## File thay đổi

- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `MainWindow.GameFlow.cs` (mới)
- `Game/GameMode.cs` (mới)
- `Game/SudokuModel.cs`
- `Game/SudokuEngine.cs`
- `Game/SudokuSolver.cs`
- `Tests/Program.cs`
- `WPF_4X4_REPORT.md` (mới)

## Kiểm chứng

SDK `9.0.315`, theo `global.json` hiện có.

```powershell
dotnet build sudokuvip.csproj -p:NuGetAudit=false
dotnet run --project Tests/SudokuVip.Tests.csproj -p:NuGetAudit=false
```

Build: 0 lỗi, 0 cảnh báo. Bộ kiểm thử: 28.931 assertions thành công, gồm 240 đề 4×4 (60 mỗi mức), regression cho bộ sinh đề/chấm điểm hiện có, tài khoản với fake store, và control WPF thật.

Các kiểm thử WPF dùng routed click events trên nút chọn chế độ/độ khó/biến thể/bắt đầu/bàn phím. Đã xác minh đúng 16 ô và bốn phím số, không nhận số ngoài 1–4, ghi chú, undo, pause/ván mới, thắng/thua/lưu một lần, nhãn lịch sử, PvP bị khóa, quay lại màn trước và bỏ qua kết quả sinh đề cũ.

Ảnh `wpf-modes.png`, `wpf-setup.png`, `wpf-game4x4.png` trong workspace thiết kế được render từ cây visual XAML thật bằng `RenderTargetBitmap` để kiểm tra bố cục. Đây không phải ảnh chụp một phiên chơi thủ công trên desktop.

Không truy cập hoặc sửa SQL Server thật, không commit/push. Kiểm thử đăng nhập qua App startup, thao tác chuột/bàn phím thủ công và lưu 4×4 vào SQL Server thật chưa thực hiện. Không có cấu hình hoặc migration mới cần chạy cho thay đổi này.
