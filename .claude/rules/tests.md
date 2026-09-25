---
paths:
  - "**/tests/**/*.cs"
---
# Quy tắc test backend

- xUnit. Test hành vi và hợp đồng, không test chi tiết private chỉ để tăng coverage.
- **Unit test** (`tests/CleanArchCqrs.UnitTests/<Layer>/…`, mirror cấu trúc `src`): Domain, validator, behavior, dịch vụ
  thuần — nhanh, tất định, dùng `FakeTimeProvider`. SQLite/InMemory chỉ cho logic không phụ thuộc đặc tính CSDL.
- **Integration test** (`tests/CleanArchCqrs.IntegrationTests/<Feature>/…`): PostgreSQL 17 + Redis 7.4 thật qua
  Testcontainers (`ContainersFixture`), đánh dấu `[Collection(IntegrationCollection.Name)]`. Mỗi class dùng `ApiFactory`
  với database riêng; dùng helper có sẵn (`AuthTestClient`, `AdminSession`, `TestData`, `TestDb`).
- Đặc tính PostgreSQL (partial unique index, `FOR UPDATE`, advisory lock, `xmin`, isolation, rollback) chỉ được chứng minh
  bằng integration test.
- Luồng tranh chấp (gán giường, cấp số, thu tiền, refresh token) cần test đồng thời: N request song song ⇒ đúng một thành công
  hoặc kết quả duy nhất (đặc tả §14.2).
- Test bảo mật: thiếu quyền ⇒ 403 + `AuditRecords`, thiếu CSRF ⇒ 403, token sai issuer/audience ⇒ 401.
- Sửa bug: một test hồi quy fail trước khi sửa, pass sau khi sửa.
- Không sửa assertion để hợp với hành vi sai; không xóa/skip test hợp lệ để cho xanh.
- Không phụ thuộc dữ liệu dùng chung hay thứ tự chạy; tạo dữ liệu riêng trong test.
- Chạy: `tooling/validate.ps1 -Mode Quick` (unit) hoặc `-Mode Full` (có integration, cần Docker).
