# Kiểm tra trước và sau sửa PvP

Ngày: 06/10/2026. Dự án thực tế: `C:\Users\admin\OneDrive\Desktop\test_pvp\Sudoku_Vip`, nhánh `develop`, HEAD `dd3a6a0`.

Báo cáo gốc `PVP_MERGE_REVIEW.md` dùng snapshot remote. Lần này kiểm tra lại checkout người dùng thực sự chạy; ba thay đổi giao diện từ lần trước được giữ nguyên. Bản sửa được build/test trong snapshot riêng trước khi áp dụng; đối chiếu SHA-256 trước khi ghi vào checkout thật.

## Trước sửa

- Main app build được; Tests lỗi CS1513/CS8300 vì dòng conflict `=======` và danh sách test bị lặp.
- Harness trên arena WPF thật tái hiện cùng một ô: đúng `(1,10)` → xóa `(0,10)` → đúng lại `(1,20)` → sai `(1,20)` → đúng `(2,30)`.
- Harness manager tái hiện người thắng nhận Elo 1184 thay vì 1216; cùng kết quả nhận hai lần làm PvP games từ 1 lên 2.
- Đường PvP ở danh mục thứ ba và việc dừng solo khi mở lobby đã được sửa ở lần trước; lần này kiểm thử lại và giữ lại.
- Đọc lại mạng/lifecycle xác nhận server nhận tổng điểm từ client, xóa/undo không gửi tin, HelloAck không trả ID, đóng lobby/arena thiếu cleanup, stats chưa có schema/store và connect lỗi tự tạo local server.

## Sau sửa

| Mục báo cáo gốc | Thay đổi | Kiểm chứng |
|---|---|---|
| 1. Đường vào PvP | Giữ PvP ở mục thứ ba, không có nút header; vào lobby và dừng solo | WPF flow mở lobby thật qua test seam, timer solo dừng; 4×4 vẫn nằm trong Biến thể |
| 2. ID không khớp | HelloAck trả profile có ID canonical; manager chờ handshake | Hai client TCP và manager; dialog WPF hiển thị đúng P1/P2 thắng/thua và Elo |
| 3. Điểm/tiến độ | Dùng PvpBoardState chung, tái sử dụng SudokuEngine; server nhận thao tác, tự tính điểm và điều kiện kết thúc | Đúng/xóa/điền lại/sai/undo; giả tổng 999 không thắng; chỉ bàn hoàn thành mới thắng; ba lỗi KO |
| 4. Xóa/undo không sync | Move/Erase/Undo/Note có MatchId và sequence; server phát local ack và trạng thái mini-board đầy đủ | Cả hai TCP client nhận cùng điểm/tiến độ; xóa thành ô trống, undo khôi phục ô; từ chối replay và MatchId cũ |
| 5. Conflict Tests | Hợp nhất danh sách suite, bỏ dấu conflict; thêm integration tests | Toàn bộ suite PASS: **29.036 assertions** |
| 6. Lifecycle/identity | Captured owner, kết quả idempotent; đóng arena đầu hàng, đóng lobby disconnect; kết thúc trận dọn match/room/ready; socket loop cũ không đóng connection mới | Đóng control WPF thật, queue/room/bot, surrender/disconnect/reconnect, đổi guest, kết quả nhận lặp |
| 7. Lịch sử/Elo | Lưu vào GameHistory cùng metadata PvP, GameId riêng cho mỗi người; transaction/dedupe; migration 002 và fallback schema cũ | FakeStore: lưu/lỗi/retry/load lại, không hồi quy Elo khi retry trận cũ; chuyển guest khi đăng ký thành công, giữ dữ liệu khi thất bại |
| 8. Endpoint | Chọn host/cổng, Kết nối, Tạo máy chủ LAN, Chơi AI tại máy; connect lỗi không tự tạo server | TCP loopback cổng tạm; render UI kiểm tra bố cục; chưa test hai máy vật lý |

Phát hiện thêm và đã sửa: callback dùng Dispatcher.Invoke bao gồm ShowDialog có thể chặn luồng nhận tin. Các callback PvP nay dùng BeginInvoke; gửi server theo thứ tự, tránh Task.Run làm đảo StartMatch/progress/result. Dừng server hủy bot và listener; lỗi bind không báo giả IsRunning.

## Chính sách dữ liệu và kết nối

- Guest giữ lịch sử/Elo trong session; đăng xuất bắt đầu guest mới. Chỉ chuyển dữ liệu khi người dùng chọn chuyển lịch sử lúc đăng ký. Đăng nhập tài khoản có sẵn không tự chuyển guest. Kết quả đang lưu luôn thuộc người chơi đã chụp lúc bắt đầu trận.
- PvP xuất hiện trong Lịch sử với nhãn `PvP · độ khó`, đối thủ và Elo sau trận. Tổng số ván/điểm gồm solo và PvP; PvpGames/PvpWins tính riêng từ records PvP.
- MatchId chung nhưng GameId được dẫn xuất từ MatchId + ID canonical, nên hai người có thể lưu trên cùng DB có unique GameId. Retry không thêm record hoặc thay thời điểm kết thúc trận.
- Kết quả lưu thất bại được giữ để dùng **Thử lưu lại** trong sảnh, trong phiên ứng dụng hiện tại. Đóng ứng dụng trước khi lưu thành công sẽ mất hàng đợi RAM này.
- Server local mặc định bind loopback. Chỉ nút **Tạo máy chủ LAN** bind các interface. Hai máy phải dùng cùng IP máy host và cùng cổng; PIN thuộc server đó. Host phải giữ ứng dụng đang chạy. Chưa mở firewall hoặc thay cấu hình mạng máy người dùng.
- Đây là thay đổi protocol: cả client và host cần chạy cùng bản mới. Client cũ gửi PlayerProgress không được server dùng để chấm điểm.

## SQL và giới hạn kiểm chứng

- Đã chuẩn bị `Database/migrations/002_pvp_history.sql`, chạy thủ công sau 001 trên đúng database đã chọn. Không chạy SQL thật hoặc migration trong lần sửa này.
- Nếu chưa có 002: code chọn query schema cũ cho đăng nhập, đăng ký không chuyển PvP, history và lưu solo; lưu PvP vào account báo chưa lưu. Nếu guest có PvP cần chuyển mà schema chưa có, transaction đăng ký rollback và giữ guest data. Nhánh tương thích này được soát code, chưa kiểm chứng trên SQL Server thật.
- SQL persistence kiểm thử bằng FakeStore; syntax/migration và hoạt động transaction trên SQL Server thật chưa chạy. Cần áp dụng migration vào DB kiểm thử trước khi dùng lưu PvP lâu dài.
- TCP end-to-end chạy trên 127.0.0.1 với cổng tạm; không thay thế kiểm thử hai máy LAN/Internet. Mất socket kết thúc trận ở server và cho đối thủ thắng; client mất kết nối chưa có cơ chế lấy lại kết quả server sau reconnect.
- Hình preview được render từ control WPF bằng RenderTargetBitmap, không phải screenshot desktop đang chạy.

## Lệnh kiểm chứng

```powershell
dotnet build sudokuvip.csproj -p:NuGetAudit=false -v:minimal
dotnet run --project Tests\SudokuVip.Tests.csproj -p:NuGetAudit=false
# Chạy riêng các kiểm thử PvP sau này:
dotnet run --project Tests\SudokuVip.Tests.csproj -p:NuGetAudit=false -- --pvp-only
```

Build snapshot: 0 warning, 0 error. Suite gồm engine, 240 đề 4×4/120 đề thường, auth/history, flow WPF và kiểm thử PvP end-to-end. Kiểm thử không gọi SQL thật. Build và suite trên checkout thật được ghi thêm sau khi áp dụng.

## Kiểm chứng sau áp dụng vào checkout thật

- 18 file được áp dụng, SHA-256 của source gốc được kiểm tra trước khi ghi. Bản gốc nằm tại `C:\Users\admin\OneDrive\Desktop\Sudoku_TQ\pvp-repair-originals`. Hai file giao diện đã sửa trước đó (`MainWindow.GameFlow.cs`, `MainWindow.xaml`) được giữ nguyên.
- Build checkout thật với BaseOutputPath riêng: **0 warning, 0 error**. Không thay exe đang chạy của phiên debug cũ.
- Toàn bộ suite trên checkout thật: **PASS 29.036 assertions**, không truy cập SQL thật.
- Đã tái tạo design-time XAML bằng MSBuild Compile cho Visual Studio; đã kiểm tra render WPF lobby và các màn chế độ/4×4.
- `git diff --check` được chạy sau chuẩn hóa newline. Không commit/push, không thực thi migration, không thay cấu hình database.
- Dừng phiên debug cũ rồi build/chạy lại trong Visual Studio để sử dụng mã mới.
