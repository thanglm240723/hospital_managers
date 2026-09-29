---
name: api-contract-change
description: Dùng khi thay đổi backend HMS ảnh hưởng hợp đồng HTTP công khai — route, DTO request/response, validation nhìn thấy được, status code, mã lỗi, quyền yêu cầu, phân trang/lọc/sắp xếp, cookie/CSRF — hoặc ảnh hưởng tới Gateway và frontend benh_vien_fe.
---
1. **Phân loại tương thích**:
   - *Tương thích*: thêm endpoint, thêm trường response tùy chọn, thêm trường request không bắt buộc.
   - *Phá vỡ*: đổi/xóa route, đổi tên/kiểu trường, trường request thành bắt buộc, đổi status code/mã lỗi, siết quyền,
     đổi ngữ nghĩa phân trang. Phá vỡ ⇒ cập nhật mọi client trong cùng thay đổi (repo chỉ có một frontend), hoặc
     route mới `/api/v2/...` (`Endpoints/V2/`) khi cần chạy song song.
2. **Liệt kê hợp đồng bị ảnh hưởng**: route + method (`Presentation/Endpoints/V{n}/<Feature>/`), DTO request
   (`XxxRequest.cs` cạnh endpoint) và response (Application: thư mục use case hoặc `Features/<Feature>/Common/`), status code,
   `code` trong Problem Details (`Error` trong `<Feature>Errors.cs`), quyền và CSRF khai trên route, header
   (`Idempotency-Key`, `If-Match`/ETag).
3. **Quy ước**: `api/v1/<kebab>`, JSON camelCase bỏ null, phân trang `pageNumber`/`pageSize`/`searchTerm` + `PagedResult<T>`
   (`items`, `pageNumber`, `pageSize`, `totalCount` — theo `Application/Common/Models/PagedResult.cs`), lỗi Problem Details
   RFC 9457 với `code`, `traceId`, `errors`. Message tiếng Việt.
4. **Gateway**: route trong `QuanLyBenhVien.Gateway/appsettings.json` khớp; route công khai chỉ khi thật cần.
5. **Frontend**: tìm nơi gọi trong `benh_vien_fe/src/feature/*/api/` và reducer/component dùng trường thay đổi; cập nhật
   xử lý lỗi theo `code`; mã quyền dùng trong `<Can>` khớp `Permissions.cs`.
6. **Swagger**: kiểm tra endpoint hiện đúng ở Swagger UI (Development, `http://localhost:5289/`).
7. **Test**: integration test cho status code, body, quyền (403 + audit), CSRF; Jest cho api/reducer frontend thay đổi.
8. **Báo cáo**: bảng thay đổi hợp đồng (trước → sau), mức tương thích, file frontend/Gateway đã cập nhật, kiểm tra đã chạy.
