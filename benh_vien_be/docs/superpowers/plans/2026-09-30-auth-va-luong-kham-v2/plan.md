# Bộ plan V2 cho xác thực, phân quyền và luồng vào hệ thống

Ngày lập: 2026-09-30. Trạng thái: tài liệu để review và triển khai; chưa triển khai source theo bộ plan này.

**V2 chỉ là phiên bản tài liệu.** API vẫn là `/api/v1/...`; không tạo Login V2 trong code, không chuyển endpoint sang `/api/v2`.

Người dùng chốt làm xong đổi mật khẩu trước, nghiệm thu và đưa mốc đó lên `main`, rồi lần lượt làm logout, refresh, điều hướng theo quyền và quản trị. **Người viết code: Claude** (chốt 2026-09-30, ngoại lệ của mặc định trong AGENTS.md) — chỉ bắt đầu khi người dùng gọi skill triển khai cho từng plan; không gom toàn bộ working tree vào commit.

## Thứ tự thực hiện

| Mốc | Plan riêng | Phụ thuộc | Kết quả nghiệm thu |
|---|---|---|---|
| 01 | [Đổi mật khẩu](plan-01-doi-mat-khau.md) | Login, `/me`, validate-session hiện có | Đổi mật khẩu thật, phiên hiện tại tiếp tục bằng token mới, phiên khác bị thu hồi |
| 02 | [Logout](plan-02-logout.md) | 01: CSRF, invalidation, khóa phiên | Đăng xuất thu hồi phiên ở DB; cookie và state được xóa |
| 03 | [Refresh](plan-03-refresh.md) | 01–02 | F5 giữ đăng nhập, xoay refresh token và xử lý reuse |
| 04 | [Login và vào khu vực theo quyền](plan-04-login-va-khu-vuc.md) | 01–03 | Một login chung; đổi bắt buộc, chọn khu vực, chặn route và kiểm quyền BE |
| 05 | [Vai trò và danh mục quyền](plan-05-vai-tro-va-quyen.md) | 01, 04 | Màn vai trò dùng API/DB thật, quyền thay đổi có invalidation |
| 06 | [Tài khoản và gán quyền](plan-06-tai-khoan.md) | 01–05 | Tạo tài khoản, gán vai trò, quyền bổ sung, khóa/mở khóa |

Đọc [spec.md](spec.md) trước từng plan. Các quyết định khám và hồ sơ đã thống nhất nằm trong [luồng khám và hồ sơ](luong-kham-va-ho-so.md); tài liệu này là đầu vào cho các plan nghiệp vụ sau, không tuyên bố BE nghiệp vụ đã có.

## Giới hạn của từng mốc

- Sau 01: refresh/logout vẫn có thể trả `501`. F5 hoặc hết hạn access token có thể đưa về login. Nút đăng xuất hiện tại mới xóa cục bộ khi BE chưa hỗ trợ. Không coi đây là nền auth đã hoàn chỉnh.
- Sau 02: logout thật hoạt động; vẫn chưa nghiệm thu F5 và gia hạn phiên.
- Sau 03: mới nghiệm thu vòng đời phiên đầy đủ trong phạm vi login/change-password/logout/refresh.
- Sau 04: chỉ định tuyến thật đến khu vực đã triển khai. Màn quản trị chưa có API vẫn hiện trạng thái chưa sẵn sàng cho tới 05/06; màn nghiệp vụ chưa có BE không được quảng bá là đã dùng được.
- Sau 05/06: quyền hiện có chỉ thuộc IdentityAccess. Không seed các mã `patients.read`, `vitals.record`, `encounters.examine` dự kiến chỉ để làm menu xuất hiện.

## Hiện trạng đã đọc

- Nhánh tại lúc khảo sát: `feat/login-khung-moi`.
- Login, `/me`, validate-session đã có implementation; change-password/refresh/logout/logout-all còn stub `501` hoặc handler `NotImplementedException`.
- `ICacheInvalidator` ở Application đang rỗng. Cache invalidator/worker và PermissionService cũ bị loại khỏi build do phụ thuộc DbContext/namespace cũ.
- FE đã có màn Auth, Workspace, Admin, Roles và bản mẫu Reception/Clinic/Vitals/Display. HTTP và token trong RAM đã có; các client nghiệp vụ mới còn hợp đồng dự kiến.
- Test đổi mật khẩu, refresh/logout, quản trị và phân quyền đang bị `Compile Remove`. File test tồn tại không đồng nghĩa được chạy.
- Lần review FE ngày 2026-09-29 đã chạy 68 test/18 suite, ESLint và build production thành công. Đây là bằng chứng của mốc cũ, không thay nghiệm thu implementation mới.

## Quy tắc thực hiện và nghiệm thu chung

Mọi đường dẫn code trong bộ tài liệu tính từ `D:/hospital_management`. Ký hiệu: `BE = benh_vien_be`, `FE = benh_vien_fe`, `APP = BE/src/QuanLyBenhVien.Application`, `DOM = BE/src/QuanLyBenhVien.Domain`, `PERS = BE/src/QuanLyBenhVien.Persistence`, `INF = BE/src/QuanLyBenhVien.Infrastructure`, `PRES = BE/src/QuanLyBenhVien.Presentation`, `API = BE/src/QuanLyBenhVien.API`, `GW = BE/src/QuanLyBenhVien.Gateway`, `UT = BE/tests/QuanLyBenhVien.UnitTests`, `IT = BE/tests/QuanLyBenhVien.IntegrationTests`. Mẫu `<UseCase>` trong bảng file phải khai triển theo đúng danh sách use case ngay tại task, một file/type.

- Trước mỗi task: kiểm working tree, viết test hồi quy, chạy để thấy lỗi đúng nguyên nhân, triển khai tối thiểu, chạy lại, đọc diff. Không sửa assertion để khớp hành vi sai.
- Các contract/schema mới trong plan là thiết kế đề xuất để review, không tự nhận là spec đã được duyệt trước đây.
- Cuối mỗi plan có BE/auth chạy `powershell -NoProfile -ExecutionPolicy Bypass -File tooling/validate.ps1 -Mode Full`; có FE chạy thêm `-Mode Frontend` và `npm run build` từ FE.
- Cần output mới, số test thực sự được phát hiện, exit code và log. Docker không chạy: `BLOCKED`, không dùng EF InMemory/SQLite thay bằng chứng PostgreSQL.
- Không gọi endpoint còn `501` để làm tiêu chí của plan trước. Tách các test dùng refresh sang plan 03, thay bằng kiểm DB và request qua Gateway khi nghiệm thu plan 01/02.
- Có thay đổi schema: migration mới, review SQL; không sửa migration đã commit, không chạy DDL/DML tay vào DB dùng chung.

## Mốc Git và đưa lên main

Người dùng chốt 2026-09-30: mỗi mốc đạt nghiệm thu được **merge thẳng vào `main`, không qua PR**. Không commit/push trong lượt lập kế hoạch. Khi hoàn tất một mốc:

1. Kiểm tra lại repo root, nhánh, remote và chênh lệch với `main` trước khi đưa ra lệnh cụ thể. Không coi nhánh đang mở là `main`.
2. Kiểm kê thay đổi chuyển thư mục cũ sang `benh_vien_be`/`benh_vien_fe`, file chưa tracked và hướng dẫn agent đang sửa. Không `git add .`; không bỏ thay đổi có sẵn.
3. Review phạm vi mốc login nền + đổi mật khẩu, kèm dependency/schema/test thực sự cần. Nếu thiếu các file chuyển thư mục để checkout sạch build được, đưa chúng thành phần nền rõ ràng, không giấu dưới commit đổi mật khẩu.
4. Chỉ chuẩn bị commit sau khi các kiểm tra bắt buộc có output PASS; tách lỗi có sẵn. Review diff staged không chứa secret.
5. Commit trên nhánh làm việc, tách commit nền (chuyển thư mục + cấu hình agent) khỏi commit tính năng; kiểm checkout sạch build được; merge vào `main` (fast-forward hoặc merge commit, không PR). Push `origin main` là thao tác ra ngoài: xác nhận với người dùng ngay trước khi push. Không force push, không reset/clean hoặc ghi đè main.

## Những phần còn phải chốt trước plan nghiệp vụ

- Danh mục chỉ số sinh hiệu, chỉ số không đo được, ngưỡng cảnh báo chờ lâu; không tự chọn giá trị y khoa.
- Phạm vi phòng/buổi, phân công, bàn giao bác sĩ và tiếp nhận nội trú.
- Role → quyền cụ thể cho từng module, do spec module quyết định; không suy quyền xem hồ sơ chỉ từ tên role.
- Quyền in bản nháp và mẫu giấy chính thức; nhận/giữ/trả giấy tờ bản gốc chưa được yêu cầu rõ.
- Các OPEN-xx của BRD vẫn chưa chốt, đặc biệt OPEN-03 tài chính và OPEN-04 phạm vi/phân công.

Thứ tự đề xuất cho các plan nghiệp vụ sau: danh mục/cơ sở/khoa/phòng/buổi và phân công → hồ sơ bệnh nhân → tiếp nhận/cấp số → hàng chờ/gọi lượt/TV → khám và yêu cầu sinh hiệu → đo và quay lại → CLS/kết quả → thu tiền/cấp phát → nội trú. Mỗi mốc phải có dữ liệu, API, quyền, FE và kiểm thử đầu-cuối; không mở rộng tiếp chỉ bằng mock.
