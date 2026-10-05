# Báo cáo sửa Sudoku VIP — 05/10/2026

Đã sửa trực tiếp dự án được chỉ định tại `C:\Users\admin\OneDrive\Desktop\sudokuvip\sudokuvip`. Workspace ban đầu của cuộc trò chuyện là thư mục `Sudoku_TQ`; bản repo khác tại `Sudoku_TQ\sudoku-project` không được chỉnh sửa. Một bản làm việc tạm được dùng để chuẩn bị/kiểm thử, sau đó chép danh sách file cụ thể vào dự án đích khi Git vẫn sạch.

## Trạng thái ban đầu và cuối

- Nhánh: `develop`.
- HEAD: `7a8c981c16d3e4d3275c7a447f486b59b06ecba6`.
- Trạng thái ban đầu: `git status --short` không có thay đổi. Không tìm thấy `AGENTS.md` áp dụng trên cây thư mục đích và các thư mục cha được kiểm tra.
- Trạng thái cuối: cùng nhánh/HEAD; 8 file tracked đã sửa và các file mới bên dưới đang chưa commit. Không reset/clean/chuyển nhánh/commit/push. Không kết nối hoặc thay đổi SQL Server thật.
- Giữ nguyên toàn bộ file XAML, bố cục và style gốc. Code-behind chỉ điều chỉnh trạng thái điều khiển, validation và thông báo theo phạm vi sửa lỗi.

## Lỗi đã xác minh và sửa

1. `MakeMove` cộng 10 mỗi lần nhập số đúng; erase không trừ điểm; undo chỉ trừ điểm dương. Thay bằng điểm được tính từ trạng thái ô hiện tại và dấu ô từng dùng hint. Nhập lại cùng số là no-op; xóa/sửa sai mất đóng góp; undo khôi phục giá trị, notes và lỗi. Hint tiêu thụ lượt vĩnh viễn và trần điểm ô đã tiết lộ là 5 cho cả ván, kể cả sau undo/refill.
2. Thắng chỉ dừng timer, không khóa input và có thể gọi lưu lại. Thêm Playing/Paused/Won/Lost; engine và tất cả đường input kiểm tra trạng thái. `TryFinish` chỉ chạy một lần; timer dừng và ván khóa trước khi await lưu. Trạng thái Saving/Saved/Failed độc lập. Thua giữ trạng thái kết thúc cho tới khi chọn ván mới.
3. `RemoveCells` xóa ngẫu nhiên, không kiểm tra nghiệm. Thêm solver dùng bản sao và heuristic chọn ô ít ứng viên; dừng ở nghiệm thứ hai. Chỉ giữ xóa khi đếm được chính xác một nghiệm. Giới hạn lần thử, thời gian và node; fallback là đề tốt nhất đã xác minh duy nhất. Sinh đề chạy worker, generation version bỏ kết quả cũ; UI khóa nút khởi tạo khi đang sinh. Mức độ vẫn theo số ô trống, không đánh giá bằng kỹ thuật giải.
4. Ván mới sau pause không hiện bàn; trạng thái pencil không reset; Space không resume được khi handler return vì pause. Ván mới reset board visibility, model, undo, selection, thời gian, hint, score, lỗi, pencil và nút pencil. Space xử lý pause trước guard input. Timer/event được tháo khi đóng cửa sổ.
5. Thông báo thắng luôn nói SQL đã lưu dù bool thất bại. Lưu trả kết quả thật; UI báo lỗi nếu chưa xác nhận, và không mở lại ván. GameId ổn định cùng unique index, transaction và khóa SQL chống trùng khi API được gọi lại, kể cả phản hồi commit bị mất. UI chưa bổ sung nút retry.
6. Username check trả false khi SQL lỗi; lịch sử catch trả list rỗng. Đổi sang kết quả phân biệt Error/Available/Exists/Invalid và Success/Records. UI có thông báo đang kiểm tra/tải, debounce 350 ms, cancellation và version chống phản hồi cũ. SQL dùng OpenAsync/Execute…Async/transaction async; PBKDF2 chạy worker. Guard busy chặn click/Enter lặp và đổi identity khi auth chưa xong.
7. GuestHistory dùng chung qua các lần logout; migration xóa history trước commit và có thể lấy history ngoài phiên. Mỗi UserAccount có SessionId; guest history thuộc phiên cụ thể. Chỉ migration phiên khách được chụp khi tạo tài khoản mới; chuyển trong transaction, chỉ clear history/stats sau commit được xác nhận. Fail giữ nguyên dữ liệu. Logout kết thúc phiên, khách sau logout bắt đầu phiên khác. Pending result giữ player/userId của ván; không lưu theo CurrentUser lúc SQL hoàn tất. Không chuyển khách sang tài khoản có sẵn.
8. SHA-256 trực tiếp không có salt, password mới chỉ tối thiểu 4, SQL endpoint ghi cứng và bootstrap chạy lúc khởi động. Thay PBKDF2-HMAC-SHA256 với 16-byte salt, 600.000 iterations và lưu tham số; verify SHA-256 cũ rồi nâng cấp sau xác thực thành công. Tham số dựa trên [OWASP Password Storage](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html). Username/display name/avatar kiểm tra giới hạn schema; password mới 8–128 ký tự gồm chữ và ít nhất một ký tự khác. SQL endpoint lấy từ biến môi trường; startup không chạy DDL. Truy vấn có tham số; các thao tác liên quan dùng transaction, account statistics được tính từ lịch sử.

## File thay đổi

File tracked đã sửa:

- `App.xaml.cs`: bỏ bootstrap SQL lúc startup.
- `MainWindow.xaml.cs`: dùng logic tách, async sinh/lưu, state guard, pause/reset/timer, thông báo thật.
- `LoginWindow.xaml.cs`: async auth, debounce username, validation/busy.
- `HistoryWindow.xaml.cs`: tải async và phân biệt lỗi/rỗng.
- `Models/UserModels.cs`: SessionId và GameId.
- `Services/AuthService.cs`: phiên khách, kết quả có trạng thái lỗi, async và chụp identity.
- `Database/DatabaseHelper.cs`: cấu hình môi trường, không bootstrap.
- `sudokuvip.csproj`: loại code test khỏi WPF project.

File mới:

- `Game/SudokuModel.cs`
- `Game/SudokuEngine.cs`
- `Game/SudokuSolver.cs`
- `Services/PasswordHasher.cs`
- `Services/IAccountStore.cs`
- `Services/SqlAccountStore.cs`
- `Database/setup.sql`
- `Database/migrations/001_game_identity.sql`
- `Tests/SudokuVip.Tests.csproj`
- `Tests/Program.cs`
- `global.json`
- `SETUP.md`
- `REPAIR_REPORT.md`

## Kết quả kiểm thử thực tế

Chạy tại dự án đích, SDK .NET **9.0.315**:

```powershell
dotnet build sudokuvip.csproj --nologo -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true
dotnet run --project Tests/SudokuVip.Tests.csproj --no-launch-profile -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true
git diff --check
```

- Build thành công: **0 warning, 0 error**.
- Console harness kết thúc exit code 0: **20.001 assertions PASS**. Không truy cập database thật.
- Sinh **120 đề**, 30 mỗi mức: tất cả đếm đúng một nghiệm, lời giải hợp lệ, clue khớp và phép đếm không thay đổi input. Trong lần chạy cuối, cả bốn mức đạt mục tiêu **32/42/50/56** ô trống; bộ kiểm thử generator mất **152 ms**. Đây là số đo của bộ mẫu, không phải cam kết luôn đạt mục tiêu hay thời gian trên mọi máy.
- Logic sản phẩm được thực thi: nhập lặp, correct/erase/refill, wrong/undo, hint/undo/notes, pause guard, thắng/thua one-shot và terminal lock; solver nhận diện bàn sai/đa nghiệm.
- Auth dùng fake store: migration fail/success, thống kê từ history, guest rename/new session, không tự migrate lúc login cũ, hash mới và legacy upgrade, lỗi username/history/save, retry thường/concurrent/commit-ack loss và save giữ identity khi đổi người chơi.
- WPF thật được khởi tạo trên STA với Dispatcher: MainWindow/LoginWindow/HistoryWindow và controls từ XAML; gọi handler để kiểm tra pause/new game/difficulty, reset pencil/time/model, generation race, finish/failed-save messaging, timer cleanup, username stale response, login busy guard và history error/empty refresh. Thông báo kết thúc được thu qua delegate thay vì mở modal MessageBox trong test.
- `git diff --check` thành công, không báo lỗi whitespace. Git có thông báo chuẩn hóa LF→CRLF, không phải lỗi source.

Lần build chuẩn bị đầu tiên có NU1900 vì không truy cập được feed kiểm tra vulnerability. Các lần cuối dùng packages đã cache, tắt NuGetAudit để xác minh compile/test. **Không coi 0 warning ở build cuối là bằng chứng dependency đã qua audit bảo mật.** Không cài/thay phiên bản SqlClient.

## Chưa kiểm thử và công việc cần thực hiện

- Chưa có database test được xác nhận, nên chưa chạy bootstrap/migration hoặc SQL integration thật. Cần thử transaction rollback/commit, unique-index concurrency, nâng cấp hash, mất kết nối và thống kê trên SQL Server test.
- Chưa chạy toàn bộ entry point `App.OnStartup` hoặc nghiệm thu giao diện qua thao tác người dùng. Kết quả WPF harness chứng minh khởi tạo controls và thực thi handlers, không phải bằng chứng toàn bộ ứng dụng đã được chơi thành công qua UI.
- Cấu hình `SUDOKU_SQL_SERVER`, `SUDOKU_SQL_DATABASE`; chỉ bật `SUDOKU_SQL_TRUST_CERTIFICATE=true` với server/chứng chỉ đã được xác nhận. Xem `SETUP.md`.
- Database mới: chọn đúng DB test đã tạo, chạy `Database/setup.sql` rồi migration. Database cũ: sao lưu, đọc migration và chạy thủ công trên test trước. Migration thêm GameId/unique index, username key và backfill aggregate statistics; không xóa tài khoản/lịch sử.
- Lịch sử cũ có điểm hoặc chiến thắng trùng do bug vẫn được giữ; không tự suy đoán/xóa dữ liệu. Migration chỉ làm thống kê khớp dữ liệu lịch sử đang có.
- Nếu refresh sau save đã commit thất bại, save vẫn được xác nhận là thành công; thống kê in-memory được cập nhật ở lần tải history thành công tiếp theo.
- Nếu đăng ký commit trên server nhưng phản hồi mất, phiên khách được giữ; username có thể đã tồn tại khi retry. Cần đối chiếu trên DB test; ứng dụng không tự migrate vào tài khoản cũ. Không có UI retry-save mới, đúng phạm vi không thêm chức năng.
- Generator có thể trả ít ô trống hơn mục tiêu khi đạt giới hạn; đề trả về vẫn duy nhất. Dữ liệu Khách chỉ sống trong tiến trình và mất khi ứng dụng đóng.
