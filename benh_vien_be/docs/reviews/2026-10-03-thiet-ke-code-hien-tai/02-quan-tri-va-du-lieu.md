# Thiết kế code hiện tại — Quản trị tài khoản, quyền và dữ liệu tổ chức

Ngày rà soát: **2026-10-03**. Phạm vi: backend hiện có của `Users`, `Roles`, `Permissions`, `StaffProfiles`, `Facilities`. Đây là bản mô tả triển khai và đánh giá đọc code; không phải đề xuất thay đổi nghiệp vụ đã duyệt.

## 1. Căn cứ, phạm vi và cách đọc

Đã đối chiếu phần liên quan của BRD v2.0 (NV-01, NV-03, NV-04), TSD v3.1 (định danh nhân sự, `IsActive`, `xmin`, kiểm quyền), spec auth `2026-09-23-auth-permission-redesign`, spec V2 `2026-09-30-auth-va-luong-kham-v2` và spec/plan `2026-10-03-phan-quyen-hai-lop`.

- Quyết định V2 §2 mục 14 thay yêu cầu admin nhập mật khẩu tạm của spec auth cũ: **server sinh mật khẩu ban đầu** và trả một lần. Code hiện tại theo quyết định mới.
- Quyền hiệu lực là hợp các quyền của role và grant trực tiếp; không có deny. Khóa tài khoản bằng `IsActive`, không xóa mềm `User`.
- Dữ liệu cơ cấu và nhân sự là dữ liệu quản trị, dùng quyền lớp 1 toàn cục. `Facilities`/`StaffProfiles` chủ ý implement `IUnscopedRequest`; việc đọc bệnh án sau này vẫn cần quyền tài nguyên lớp 2. Có phạm vi khoa không tự cho quyền đọc bệnh án.
- Tên file trong phần chứng cứ là đường dẫn từ `D:/hospital_management/benh_vien_be/`; số sau dấu `:` là dòng của source được đọc trong phiên rà soát. Các liên kết điểm chính dùng đường dẫn tuyệt đối.
- **Suy luận thiết kế** được ghi rõ; không biến suy luận thành quyết định nghiệp vụ. Các trường hợp còn cần chốt ghi `CẦN_XÁC_NHẬN`.
- Không thực thi luồng với DB/dev dùng chung, không đọc secret, không sửa source. Agent phụ kiểm kê test và không tự chạy một lượt riêng; agent điều phối đã chạy bộ Full mới. Kết quả thực thi, các test Skip và file bị loại khỏi build xem **[spec.md §8](spec.md)** trong cùng bộ tài liệu.

## 2. Cơ chế chung của các luồng

### 2.1 Đường đi của request

`Gateway → API authorization/CSRF → Carter endpoint → ISender.Send → LoggingBehavior → ValidationBehavior → handler → repository/read service → AppDbContext → PostgreSQL`.

Endpoint mỏng: bind DTO, đọc `If-Match` nếu cần, gửi command/query với `CancellationToken`, map `Result` sang HTTP. Route mutation đều có `RequireCsrf()`; route đọc chỉ cần quyền tương ứng. Các endpoint không khai `AllowAnonymous` và nằm dưới cơ chế xác thực chung của API.

Điểm cần phân biệt với hướng dẫn kiến trúc: **DI hiện tại chỉ đăng ký `LoggingBehavior` và `ValidationBehavior`**, chưa đăng ký `AuditBehavior`. Audit của command quản trị được thực hiện bằng `IAuditWriter.Record` trong handler và `AuditSaveChangesInterceptor` khi lưu. Chứng cứ: `src/QuanLyBenhVien.Application/DependencyInjection.cs:15`, `src/QuanLyBenhVien.Persistence/DependencyInjection.cs:27`. Không suy từ hướng dẫn rằng mọi query hiện đã tự động audit.

`ResultExtensions` map validation → 400, unauthorized → 401, forbidden → 403, not found → 404, conflict → 409, precondition → 412. `If-Match` hiện là số `uint`, có thể có dấu nháy; thiếu/sai → 400 `invalid_if_match`. Response DTO chứa `RowVersion`; endpoint không phát `ETag` và không nhận danh sách ETag/`*` như một bộ parser HTTP đầy đủ. Chứng cứ: `src/QuanLyBenhVien.Presentation/Http/ResultExtensions.cs:10`, `src/QuanLyBenhVien.Presentation/Http/IfMatch.cs:8`.

### 2.2 Transaction, khóa và phiên bản

- Ghi đơn giản bằng một `SaveChangesAsync`: tạo `User`, `Role`, `Branch`; EF gom entity, bảng con và audit vào cùng lần lưu.
- Ghi cần khóa: handler mở `IUnitOfWork.BeginTransactionAsync`, đọc bằng `FOR UPDATE`, kiểm phiên bản/nghiệp vụ, lưu, commit. Trả lỗi trước commit làm transaction bị dispose và rollback.
- `Users`, `Roles`, `StaffProfiles`, `Branches`, `Departments`, `Rooms` đều có `uint RowVersion` cấu hình `IsRowVersion()`; PostgreSQL quản lý `xmin`.
- Đổi bảng con không tự đổi `xmin` cha: `User.SetRoles/GrantPermission/RevokePermission` cập nhật `UpdatedAt`; `RoleRepository.MarkChanged` buộc cập nhật `Name`; `StaffProfileRepository.MarkChanged` buộc cập nhật `StaffCode`.
- `AppDbContext.SaveChangesAsync` dịch concurrency thành `ConcurrencyConflictException`; chỉ một danh sách unique constraint định trước được dịch thành `UniqueConstraintViolationException`. Handler map từng lỗi nghiệp vụ được biết; không retry mọi unique violation.

Chứng cứ: `src/QuanLyBenhVien.Persistence/AppDbContext.cs:27`, `:43`; `src/QuanLyBenhVien.Persistence/Repositories/Identity/RoleRepository.cs:43`; `src/QuanLyBenhVien.Persistence/Repositories/Identity/StaffProfileRepository.cs:18`.

**Suy luận thiết kế:** kết hợp khóa hàng và `If-Match` phục vụ hai mục tiêu khác nhau: tuần tự hóa phần kiểm tra/ghi ở DB, đồng thời ngăn người dùng ghi đè từ màn hình đã cũ. Giá phải trả là thêm transaction/round trip và thời gian giữ khóa.

### 2.3 Audit và cache

`AuditWriter.Record` đưa `AuditRecord` vào chính `AppDbContext` của nghiệp vụ. Actor, resource, correlation, IP/user-agent, thời điểm được điền từ adapter hiện tại; metadata giới hạn 4096 byte. Interceptor tạo `AuditLog` cho entity implement `IAuditable`, loại `PasswordHash`, `TokenHash`, concurrency token; diff chỉ chứa thuộc tính thực sự thay đổi. Audit record ghi hành động, audit log ghi biến đổi dữ liệu: hai loại bổ sung cho nhau.

Chứng cứ: `src/QuanLyBenhVien.Persistence/Repositories/Common/AuditWriter.cs:26`, `src/QuanLyBenhVien.Persistence/Interceptors/AuditSaveChangesInterceptor.cs:41`.

Đổi quyền/khóa user enqueue `CacheInvalidations` trong lần lưu nghiệp vụ; **không gọi Redis khi đang giữ transaction**. Sau commit flush, lỗi flush được ghi log và worker xử lý lại; mutation đã commit vẫn trả kết quả. `UserCommandCompletion` dùng `CancellationToken.None` cho flush và đọc DTO sau commit, tránh request bị hủy làm che kết quả đã lưu nhưng cũng kéo dài việc chạy khi client đã rời đi.

Chứng cứ: `src/QuanLyBenhVien.Application/Common/Caching/CacheInvalidator.cs:23`, `:44`; `src/QuanLyBenhVien.Application/Features/Users/Common/UserCommandCompletion.cs:13`. Đây là cơ chế đồng bộ cache có độ trễ khi Redis/worker lỗi, **không phải cam kết thu hồi quyền tức thì trong mọi điều kiện**.

`Facilities`/`StaffProfiles` không enqueue permission/session invalidation: dữ liệu scope được `AccessContext` đọc DB theo request và chỉ nhớ trong cùng request. `AccessContext` chỉ lấy profile active, scope có Department active và Branch active. Chứng cứ: [AccessContext.cs:26](D:/hospital_management/benh_vien_be/src/QuanLyBenhVien.Persistence/Authorization/AccessContext.cs:26), [AccessContext.cs:35](D:/hospital_management/benh_vien_be/src/QuanLyBenhVien.Persistence/Authorization/AccessContext.cs:35).

## 3. Users

### 3.1 Danh sách tài khoản — `ListUsersQueryHandler`

**Hợp đồng:** `GET /api/v1/users/`, quyền `users.read`; query mặc định `pageNumber=1`, `pageSize=20`, tùy chọn `searchTerm`, `roleId`, `status`. Validator yêu cầu page ≥1, size 1..100, search tối đa 200, status trong `active`, `must_change_password`, `locked`. Trả 200 `PagedResult<UserSummaryDto>`: `Id`, `Email`, `FullName`, `IsActive`, `MustChangePassword`, roles (`Id/Code/Name`), `RowVersion`.

**Trình tự:** endpoint tạo query → handler gọi `IUsersReadService.ListAsync` → `UsersReadService` dùng `AsNoTracking`; tìm email/họ tên bằng `ILIKE` đã escape `\\`, `%`, `_`; lọc role qua `Any`; lọc trạng thái trước `COUNT` và phân trang. `active` nghĩa là active và đã đổi mật khẩu; `must_change_password` là active nhưng chưa đổi; `locked` là inactive. Sort `FullName`, rồi `Id` để ổn định khi tên trùng. Offset tính `long`, trang vượt tổng trả rỗng trước khi cast sang `int`.

**An toàn:** không transaction ghi, không khóa, audit mutation hay cache invalidation. Không trả hash/token/security version. Quyền `users.read` cho phép xem danh sách toàn hệ thống; không lọc theo khoa vì đây là quản trị tài khoản.

**Vì sao — suy luận:** query projection tránh tải aggregate cùng credential và giữ API đọc độc lập với logic ghi. Sort thứ hai làm phân trang tất định với dữ liệu không đổi.

**Điểm mạnh:** lọc trước count/page; wildcard người dùng không vô tình mở rộng tìm kiếm; không overflow với page rất lớn. **Hạn chế:** `COUNT` và đọc page là hai câu lệnh, có thể lệch dưới ghi đồng thời; tìm `%term%` trên nhiều dữ liệu cần đo index/hiệu năng; `locked` không phải khóa do thử sai mật khẩu. Validator không cấm `roleId=Guid.Empty`, kết quả hiện là không khớp thay vì lỗi.

**Chứng cứ:** `src/QuanLyBenhVien.Presentation/Endpoints/V1/Users/UsersEndpoints.cs:25`, `:64`; `src/QuanLyBenhVien.Application/Features/Users/ListUsers/ListUsersQueryValidator.cs:10`; [UsersReadService.cs:15](D:/hospital_management/benh_vien_be/src/QuanLyBenhVien.Persistence/ReadServices/Identity/UsersReadService.cs:15).

### 3.2 Chi tiết tài khoản — `GetUserQueryHandler`

**Hợp đồng:** `GET /api/v1/users/{id:guid}`, `users.read`; `Id` không rỗng. Trả 200 `UserDetailDto` hoặc 404 `user_not_found`. DTO thêm `PermissionGrants(Code, Reason)` và `EffectivePermissions`; vẫn không có password/security version/token.

**Trình tự:** query → read service lấy profile user, roles/grants theo thứ tự mã → `EffectivePermissions.LoadAsync` đọc `IsActive/MustChangePassword`; inactive thì quyền hiệu lực rỗng, active thì `RolePermissions ∪ UserPermissions`, unique bằng tập ordinal → trả DTO và `RowVersion`.

**Transaction/audit/cache:** các truy vấn đọc DB `AsNoTracking`, không dùng Redis, không khóa và không audit truy cập riêng ở handler. **Vì sao — suy luận:** màn hình quản trị thấy nguồn grant và quyền hợp nhất để tránh nhầm thu hồi grant với deny role. **Điểm mạnh:** tái dùng cùng bộ tính quyền DB với auth; không lộ credential. **Hạn chế:** nhiều câu truy vấn không có snapshot chung; DTO có thể ghép `RowVersion`/roles từ trước và quyền hiệu lực từ sau một thay đổi đồng thời. `MustChangePassword` vẫn là cờ riêng, không khiến danh sách quyền trong DTO tự rỗng.

**Chứng cứ:** `src/QuanLyBenhVien.Application/Features/Users/GetUser/GetUserQueryHandler.cs:12`; `src/QuanLyBenhVien.Persistence/ReadServices/Identity/UsersReadService.cs:54`; `src/QuanLyBenhVien.Persistence/ReadServices/Identity/EffectivePermissions.cs:17`.

### 3.3 Tạo tài khoản — `CreateUserCommandHandler`

**Hợp đồng:** `POST /api/v1/users/`, `users.create`, CSRF. `CreateUserRequest(Email, FullName, RoleIds?)`; không nhận password/`IsActive`/`SecurityVersion`. Null roleIds được endpoint đổi thành tập rỗng. Email bắt buộc, trim ≤256, một `@`, local/domain không rỗng, không whitespace; tên bắt buộc, trim ≤200; roleIds không trùng/không Guid.Empty. Trả 201 `CreateUserResultDto(User, InitialPassword)`, `Location` tới user, `Cache-Control: no-store`, `Pragma: no-cache`.

**Trình tự:** chuẩn hóa email lowercase+trim → kiểm email tồn tại → kiểm toàn bộ roleId tồn tại → sinh password (tối đa 20 lần), kiểm chiều dài theo `PasswordPolicy` và không chứa local-part → hash → `User.Create` với thời gian từ `TimeProvider` → `SetRoles` → add user → audit `users.create` → `SaveChangesAsync` → đọc DTO DB → trả password plaintext chỉ ở response tạo. User mới mặc định active, `MustChangePassword=true`, `SecurityVersion=1`, Guid v7.

**DB/lỗi:** `IX_Users_Email` unique là trọng tài cuối cho hai request đồng thời; catch đúng constraint → 409 `email_taken`. Role không tồn tại → 400 `role_not_found` kèm `roleIds`. FK `UserRoles.RoleId → Roles` restrict, PK `(UserId,RoleId)`, child ownership từ user cascade. Một lần SaveChanges bảo đảm user+assignments+audit nguyên tử; user mới chưa có cache nên không invalidation. Chứng cứ: `src/QuanLyBenhVien.Persistence/Configurations/Identity/UserConfiguration.cs:11`, `UserRoleConfiguration.cs:12`.

**Vì sao — suy luận:** server sở hữu trạng thái bảo mật và credential, admin không chọn mật khẩu yếu; precheck cho phản hồi nhanh nhưng unique DB mới bảo đảm race. **Điểm mạnh:** credential không lưu plaintext; không cho mass assignment security fields; policy mật khẩu được kiểm cả sau sinh. **Hạn chế:** quy tắc email là kiểm tối giản; không giới hạn số role trong payload. Tài khoản có thể được tạo thành công nhưng client mất response/password một lần do mạng lỗi hoặc lỗi đọc DTO sau commit; chưa có idempotency hay admin reset password trong phạm vi hiện tại. Không suy diễn rằng tạo lại cùng email sẽ lấy lại mật khẩu.

**Chứng cứ:** [CreateUserCommandHandler.cs:30](D:/hospital_management/benh_vien_be/src/QuanLyBenhVien.Application/Features/Users/CreateUser/CreateUserCommandHandler.cs:30), `:49`, `:59`; `src/QuanLyBenhVien.Domain/Identity/User.cs:38`; `src/QuanLyBenhVien.Presentation/Endpoints/V1/Users/UsersEndpoints.cs:78`.

### 3.4 Mở khóa — `ActivateUserCommandHandler`

**Hợp đồng:** `POST /api/v1/users/{id}/activate`, `users.activate`, CSRF; Id không rỗng; không yêu cầu `If-Match`; trả 200 chi tiết hoặc 404.

**Trình tự:** mở transaction → `Users FOR UPDATE` → nếu inactive gọi `Activate(now)`, enqueue permission invalidation → audit `users.activate` → SaveChanges → commit → flush/read DTO. Nếu đã active thì vẫn ghi audit thành công, không đổi user/không invalidation. `Activate` chỉ bật active, touch thời gian, raise event; không phục hồi family đã thu hồi, không hạ `SecurityVersion` và không xóa `MustChangePassword`.

**Vì sao — suy luận:** command đặt trạng thái đích nên gọi lặp an toàn, không cần phiên bản màn hình cho toggle. **Điểm mạnh:** không hồi sinh phiên cũ; khóa hàng nối cùng chuỗi tuần tự hóa với login/deactivate. **Hạn chế:** admin có thể kích hoạt từ màn hình cũ vì không dùng `If-Match`; đây là contract hiện tại cần UI hiểu đúng. Response là dữ liệu đọc sau commit, có thể phản ánh thao tác mới hơn của admin khác.

**Chứng cứ:** `src/QuanLyBenhVien.Application/Features/Users/ActivateUser/ActivateUserCommandHandler.cs:28`; `src/QuanLyBenhVien.Domain/Identity/User.cs:96`.

### 3.5 Khóa — `DeactivateUserCommandHandler`

**Hợp đồng:** `POST /api/v1/users/{id}/deactivate`, `users.activate`, CSRF; Id không rỗng; không `If-Match`. 404 nếu không có; 409 `self_action_forbidden` nếu tự khóa; 409 `last_admin` nếu mất admin active cuối.

**Trình tự:** đọc role code `admin` → transaction → `pg_advisory_xact_lock` cố định bảo vệ admin → khóa target `User` và nạp assignments/grants → cấm tự khóa (kể cả target đã inactive) → nếu đang active/admin, đếm admin active khác → `Deactivate(now)` tăng `SecurityVersion`, đổi active=false → khóa/thu hồi các family active bằng `AccountDeactivated`, enqueue session key từng family và permission key user → audit `users.deactivate` → save/commit → flush/read. Target đã khóa thì không tăng `SecurityVersion` lần nữa nhưng vẫn audit.

**Vì sao — suy luận:** chỉ khóa một user không ngăn hai admin cùng khóa nhau và mỗi người đều thấy còn admin khác; advisory lock tuần tự hóa toàn bộ phép đếm/ghi bảo vệ bất biến. `SecurityVersion` và revoke family cùng DB transaction ngăn login xen giữa tạo phiên sống sót bằng trạng thái cũ.

**Điểm mạnh:** bảo vệ last-admin và self-action, không xóa tài khoản/lịch sử, mutation+revoke+audit+invalidation nguyên tử. **Hạn chế:** một advisory lock toàn cục làm các thao tác admin serialize; khả năng thu hồi ở Gateway vẫn chịu độ trễ invalidation; nhánh không tìm thấy role admin dựa vào seed đúng chứ không coi đó là lỗi cấu hình. Khóa user không tự đổi `StaffProfile.IsActive` vì hai trạng thái độc lập.

**Chứng cứ:** [DeactivateUserCommandHandler.cs:34](D:/hospital_management/benh_vien_be/src/QuanLyBenhVien.Application/Features/Users/DeactivateUser/DeactivateUserCommandHandler.cs:34), `:57`; `src/QuanLyBenhVien.Application/Features/Users/Common/AdminSafetyGuard.cs:13`; `src/QuanLyBenhVien.Persistence/Repositories/Identity/UserRepository.cs:85`.

### 3.6 Thay tập vai trò — `SetUserRolesCommandHandler`

**Hợp đồng:** `PUT /api/v1/users/{id}/roles`, `users.roles.manage`, CSRF; `SetUserRolesRequest(RoleIds)`, `If-Match` bắt buộc. Cho phép tập rỗng; cấm null/trùng/Guid.Empty. 400 `role_not_found` nếu mã Id lạ; 404 user; 412 `user_version_conflict`; 409 self-action/last-admin khi bỏ admin.

**Trình tự:** tìm role admin → transaction → admin-safety lock → đọc roleId cũ → khóa hợp `cũ ∪ mới` theo Id tăng dần → kiểm role yêu cầu tồn tại → khóa User và nạp access mới nhất → kiểm role cũ nằm trong tập đã khóa (nếu không rollback và thử tối đa 3 lần) → so `RowVersion` → bảo vệ bỏ role admin → aggregate diff assignments, touch user khi có thay đổi → enqueue permission key nếu tập thực sự đổi → audit `users.set_roles` → save/commit → flush/read DTO.

**Khóa/FK:** cùng admin-safety lock với `SetRolePermissions` tránh member mới lọt ra ngoài danh sách invalidation của role; thứ tự `admin-safety → Roles → User` tránh lấy thêm role lock sau khi đã giữ User. PK/FK UserRoles bảo đảm dữ liệu tham chiếu. Đây là vòng retry đặc biệt cho đọc membership đã cũ, không phải retry SQL deadlock/unique mù; code hiện không tạo DbContext mới cho vòng này, nhưng repository detach User/children trước khi nạp lại.

**Vì sao — suy luận:** thay nguyên tập phù hợp màn hình checkbox, aggregate tính diff giữ attribution cũ của assignment còn tồn tại. **Điểm mạnh:** version check thật, cache invalidation chỉ khi đổi, last-admin an toàn dưới đồng thời. **Hạn chế:** global lock giữ cả khi đổi role không liên quan admin; payload không cap size; no-op vẫn audit, nhưng request dùng version cũ vẫn bị 412. Command không tăng `SecurityVersion`; quyền đổi qua invalidation, JWT giữ nguyên định danh.

**Chứng cứ:** [SetUserRolesCommandHandler.cs:55](D:/hospital_management/benh_vien_be/src/QuanLyBenhVien.Application/Features/Users/SetUserRoles/SetUserRolesCommandHandler.cs:55), `:79`; `src/QuanLyBenhVien.Persistence/Repositories/Identity/RoleRepository.cs:32`; `src/QuanLyBenhVien.Persistence/Repositories/Identity/UserRepository.cs:67`; `src/QuanLyBenhVien.Domain/Identity/User.cs:113`.

### 3.7 Cấp quyền trực tiếp — `GrantUserPermissionCommandHandler`

**Hợp đồng:** `POST /api/v1/users/{id}/permissions/grant`, `users.permissions.manage`, CSRF; `PermissionGrantRequest(PermissionCode, Reason)`, `If-Match`. Mã phải thuộc `Permissions.IsDefined`, lý do không whitespace/tối đa 500; Id không rỗng. Trả chi tiết, 400 input/If-Match, 404 user, 412 version.

**Trình tự:** transaction → User FOR UPDATE kèm assignments/grants → so version → `GrantPermission` kiểm mã/lý do tại Domain, thêm grant nếu chưa có, trim reason/touch parent/raise event → nếu count đổi enqueue permission key → audit `users.permissions.grant` với reason và code → save/commit → flush/read.

**DB:** PK `(UserId,PermissionCode)`, FK code tới `Permissions` restrict, reason required/≤500. Quyền hiện thuộc catalog tĩnh đã kích hoạt; mã chưa seed DB sẽ bị FK dù Domain biết mã, vì DB vẫn là trọng tài dữ liệu.

**Vì sao — suy luận:** quyền thêm là ngoại lệ có lý do, không phải một tập quyền ghi đè role. **Điểm mạnh:** giữ nguồn cấp và phiên bản user, lặp grant không tăng version/cache. **Hạn chế:** grant trùng giữ **lý do cũ**, audit có thể ghi lý do mới của lần gọi không đổi; cần UI diễn đạt rõ. Không có hạn hiệu lực/deny/resource scope trong grant này. Không có quy tắc giới hạn người cấp chỉ được cấp các quyền bản thân có: quyền `users.permissions.manage` hiện là thẩm quyền rộng theo contract quản trị.

**Chứng cứ:** `src/QuanLyBenhVien.Application/Features/Users/GrantUserPermission/GrantUserPermissionCommandHandler.cs:28`; `src/QuanLyBenhVien.Domain/Identity/User.cs:129`; `src/QuanLyBenhVien.Persistence/Configurations/Identity/UserPermissionConfiguration.cs:12`.

### 3.8 Thu hồi quyền trực tiếp — `RevokeUserPermissionCommandHandler`

**Hợp đồng:** `POST /api/v1/users/{id}/permissions/revoke`, cùng quyền/DTO/CSRF/version và lỗi như grant. Validator vẫn yêu cầu code có trong catalog dù không có grant.

**Trình tự:** transaction → khóa/nạp User → so version → `RevokePermission` chỉ xóa grant trực tiếp → nếu count giảm touch parent/enqueue permission key → audit `users.permissions.revoke` với lý do → save/commit → flush/read. Không có grant thì no-op, giữ version và quyền; vẫn ghi audit thành công.

**Vì sao — suy luận:** quyền hiệu lực hợp từ role/grant không có deny nên việc bỏ ngoại lệ không được tác động role. **Điểm mạnh:** không âm thầm xóa quyền qua role; idempotent ở trạng thái đích. **Hạn chế:** người dùng có thể vẫn giữ quyền sau revoke vì role; UI phải cho thấy nguồn. Reason là audit của thao tác, không tồn tại như một bản grant đã xóa. No-op không có diff nhưng có AuditRecord; đây là khác biệt cần hiểu khi đối soát.

**Chứng cứ:** `src/QuanLyBenhVien.Application/Features/Users/RevokeUserPermission/RevokeUserPermissionCommandHandler.cs:28`; `src/QuanLyBenhVien.Domain/Identity/User.cs:142`; `src/QuanLyBenhVien.Persistence/ReadServices/Identity/EffectivePermissions.cs:19`.

## 4. Roles và Permissions

### 4.1 Danh sách vai trò — `ListRolesQueryHandler`

**Hợp đồng:** `GET /api/v1/roles/`, `roles.read`; không input/validator; trả 200 `RoleDto[]` gồm Guid thật, Code, Name, IsSystem, PermissionCodes, RowVersion.

**Trình tự:** query → `IRolesReadService` → DB `AsNoTracking`, sort role Code theo DB, project bảng con, sort PermissionCodes ordinal trong bộ nhớ → DTO. Không cache, khóa, transaction ghi hay audit query riêng. **Vì sao — suy luận:** catalog quản trị nhỏ trả toàn bộ để cấu hình role/checkbox. **Điểm mạnh:** FE nhận Guid thật và phiên bản, không hard-code role code thành Id; vẫn thấy system flag. **Hạn chế:** không phân trang/lọc; thứ tự role dựa collation DB trong khi permission dùng ordinal; khi tăng nhiều role cần đo payload. `IsSystem` không có nghĩa bất biến Name/permissions.

**Chứng cứ:** `src/QuanLyBenhVien.Presentation/Endpoints/V1/Roles/RolesEndpoints.cs:21`; `src/QuanLyBenhVien.Persistence/ReadServices/Identity/RolesReadService.cs:10`.

### 4.2 Tạo vai trò — `CreateRoleCommandHandler`

**Hợp đồng:** `POST /api/v1/roles/`, `roles.manage`, CSRF; `CreateRoleRequest(Code,Name,PermissionCodes?)`, null codes → rỗng. Code theo regex `^[a-z][a-z0-9-]{1,49}$`, name bắt buộc/≤100, mỗi permission thuộc catalog. Duplicated codes được Domain deduplicate. Trả 201 DTO và Location; 409 `role_code_taken` khi trùng.

**Trình tự:** precheck CodeExists → `Role.Create` (`IsSystem=false`, Guid v7, trim name) → `SetPermissions` tính tập → Add → audit `roles.create` → một SaveChanges → map DTO từ aggregate đã được EF cập nhật version. `IX_Roles_Code` unique quyết định race cuối; child PK `(RoleId,PermissionCode)` và FK tới catalog.

**Vì sao — suy luận:** danh tính Code ổn định, Name phục vụ hiển thị. **Điểm mạnh:** tên không unique nên có thể đổi theo ngôn ngữ; code unique có map 409 cụ thể; không cho client đặt IsSystem. **Hạn chế:** regex gọi kebab-case nhưng cho phép dấu `-` cuối/liên tiếp; chưa cap số permission hay yêu cầu ít nhất một quyền. Không kiểm quyền được cấp có nằm trong quyền của người tạo: `roles.manage` hiện cho cấu hình catalog rộng. Location chỉ định route chi tiết role nhưng hiện không có GET role-by-id.

**Chứng cứ:** `src/QuanLyBenhVien.Application/Features/Roles/CreateRole/CreateRoleCommandHandler.cs:22`; `src/QuanLyBenhVien.Domain/Identity/Role.cs:9`; `src/QuanLyBenhVien.Persistence/Configurations/Identity/RoleConfiguration.cs:14`.

### 4.3 Đổi tên vai trò — `RenameRoleCommandHandler`

**Hợp đồng:** `PUT /api/v1/roles/{id}`, `roles.manage`, CSRF, `RenameRoleRequest(Name)` và `If-Match`; name bắt buộc ≤100. Không cho đổi Code/IsSystem; role hệ thống vẫn được đổi Name. 404 `role_not_found`, 412 `role_version_conflict`.

**Trình tự:** transaction → Roles FOR UPDATE kèm permissions → so version → Domain trim/Rename → audit `roles.rename` → save/commit → DTO từ aggregate. No-op name đã giống không đổi xmin; vẫn có audit. Không permission invalidation vì cache quyền chứa code chứ không chứa display name.

**Vì sao — suy luận:** đổi nhãn không thay định danh security. **Điểm mạnh:** không phá role reference/permissions; sửa màn hình cũ bị 412. **Hạn chế:** validator kiểm chiều dài thô còn Domain trim, khác các facility validator kiểm sau trim; client gửi tên đã giống có thể hiểu nhầm cần version mới. Không GET role chi tiết; client lấy phiên bản từ list.

**Chứng cứ:** `src/QuanLyBenhVien.Application/Features/Roles/RenameRole/RenameRoleCommandHandler.cs:21`; `src/QuanLyBenhVien.Domain/Identity/Role.cs:33`; `src/QuanLyBenhVien.Presentation/Endpoints/V1/Roles/RolesEndpoints.cs:53`.

### 4.4 Thay quyền vai trò — `SetRolePermissionsCommandHandler`

**Hợp đồng:** `PUT /api/v1/roles/{id}/permissions`, `roles.manage`, CSRF, `SetRolePermissionsRequest(PermissionCodes)` + `If-Match`; null bị validator từ chối, mọi code phải trong catalog. Cho phép tập rỗng với non-admin. Admin phải giữ đủ 8 quyền `IdentityAccess`; vi phạm → 409 `admin_core_permissions_required`.

**Trình tự:** transaction → admin-safety lock → Role FOR UPDATE trước khi đọc members → so version → target hashset ordinal → kiểm lõi admin → Domain thay tập permission → nếu thực sự đổi `roles.MarkChanged(role)` để bump xmin, đọc mọi user trong role và enqueue key từng user → audit `roles.set_permissions` → SaveChanges → DTO với version sau lưu → commit → flush ngoài transaction, lỗi flush không đảo kết quả.

**DB:** PK/FK RolePermissions ngăn trùng/mã mồ côi; Role MarkChanged quan trọng vì chỉ đổi child không tự cập nhật xmin cha. Role Id lock trước member list và advisory lock dùng chung với SetUserRoles xử lý race gán thành viên. Admin được giữ quyền quản trị truy cập lõi, không đồng nghĩa tự có quyền lâm sàng. Các quyền organization mới không thuộc `IdentityAccess` nên guard này không bảo vệ chúng như 8 quyền lõi.

**Vì sao — suy luận:** thay role permission ảnh hưởng cả nhóm; fan-out invalidation bền vững tránh chỉ cập nhật quyền của admin đang sửa. **Điểm mạnh:** có version thật cho child change; no-op không enqueue; bảo vệ admin; giao dịch không gọi Redis. **Hạn chế:** số dòng invalidation tăng theo số member và nằm trong transaction giữ global lock; cần đo với role lớn. Audit record không ghi tập before/after trong metadata nhưng diff bảng con có. Nhánh conflict/no-op giữ version cũ; admin core là quy tắc Application, không constraint DB.

**Chứng cứ:** [SetRolePermissionsCommandHandler.cs:27](D:/hospital_management/benh_vien_be/src/QuanLyBenhVien.Application/Features/Roles/SetRolePermissions/SetRolePermissionsCommandHandler.cs:27), `:49`; `src/QuanLyBenhVien.Domain/Identity/Permissions.cs:41`.

### 4.5 Danh mục quyền — `ListPermissionsQueryHandler`

**Hợp đồng:** `GET /api/v1/permissions/`, `permissions.read`; trả 200 `PermissionDto(Code,Group,Description)[]`. Hiện catalog Domain kích hoạt 8 quyền IdentityAccess + 4 quyền organization. Catalog được đọc từ DB, không từ một danh sách mock trong endpoint.

**Trình tự:** query → `IPermissionsReadService` → `Permissions AsNoTracking`, sort Group rồi Code → projection DTO. Không validator/input, mutation, transaction ghi, khóa, audit query riêng hay Redis. Code là PK của bảng Permissions; RolePermissions/UserPermissions tham chiếu bằng FK restrict. Không có API tạo/đổi/xóa Permission, việc thêm mã thuộc source+seed+endpoint module.

**Vì sao — suy luận:** tách catalog code do ứng dụng sở hữu khỏi tập quyền admin gán; UI chỉ gán mã đang hỗ trợ. **Điểm mạnh:** cùng DB catalog với FK, description tiếng Việt. **Hạn chế:** cần duy trì đồng bộ Domain.IsDefined/seed DB; thứ tự theo collation DB; không quyền lâm sàng có endpoint hiện tại không phải bằng chứng rằng matrix tương lai đã triển khai.

**Chứng cứ:** `src/QuanLyBenhVien.Presentation/Endpoints/V1/Permissions/PermissionsEndpoints.cs:18`; `src/QuanLyBenhVien.Persistence/ReadServices/Identity/PermissionsReadService.cs:9`; `src/QuanLyBenhVien.Persistence/Configurations/Identity/PermissionConfiguration.cs:12`; `src/QuanLyBenhVien.Domain/Identity/Permissions.cs:62`.

## 5. StaffProfiles

### 5.1 Đọc hồ sơ nhân sự — `GetStaffProfileQueryHandler`

**Hợp đồng:** `GET /api/v1/users/{userId:guid}/staff-profile`, `staff-profiles.read`; trả 200 `StaffProfileDto(Id,UserId,StaffCode,IsActive,RowVersion,WorkScopes)` hoặc 404 `staff_profile_not_found`. Không validator UserId riêng; Guid route rỗng được xử lý như không tìm thấy.

**Trình tự:** query `IUnscopedRequest` → read service đọc profile `AsNoTracking.Include(WorkScopes)` → đọc Departments theo ids và Branches theo scope → map tên cơ sở/khoa, kind string → sort theo BranchName/DepartmentName ordinal. DTO quản trị giữ cả scope tới mục ngừng dùng; không đồng nghĩa scope đó có hiệu lực trong `AccessContext`.

**Vì sao — suy luận:** StaffProfile là định danh nhân sự độc lập UserId, làm nền cho phân công lâm sàng/thiết bị. **Điểm mạnh:** không ép DoctorId=UserId; tham chiếu có FK; quản trị thấy scope lịch sử hiện còn cấu hình. **Hạn chế:** không phân biệt user không có với user chưa có profile ở GET; nhiều truy vấn không snapshot chung; `StaffWorkScopeDto` không chứa cờ active của cơ sở/khoa nên UI cần cây tổ chức để giải thích scope không hiệu lực. Không audit đọc nhân sự riêng trong handler.

**Chứng cứ:** `src/QuanLyBenhVien.Application/Features/StaffProfiles/GetStaffProfile/GetStaffProfileQueryHandler.cs:12`; `src/QuanLyBenhVien.Persistence/ReadServices/Identity/StaffProfilesReadService.cs:11`; `src/QuanLyBenhVien.Application/Features/StaffProfiles/Common/StaffProfileDto.cs:3`.

### 5.2 Tạo/cập nhật hồ sơ — `UpsertStaffProfileCommandHandler`

**Hợp đồng:** `PUT /api/v1/users/{userId}/staff-profile`, `staff-profiles.manage`, CSRF; `UpsertStaffProfileRequest(StaffCode,IsActive)`. **Không If-Match = yêu cầu tạo** → 201; có If-Match = yêu cầu cập nhật → 200. Không phải upsert tự động ghi đè. Code bắt buộc theo `^[A-Z0-9][A-Z0-9-]{1,29}\\z` (2..30, không trim/normalize).

**Trình tự:** transaction → khóa User làm điểm tuần tự hóa chung → nếu user thiếu 404 → nạp tracked profile+scopes → create: profile đã có → 409 `staff_profile_exists`; chưa có tạo Guid v7 và set active theo request → update: chưa có 404, version khác 412, đổi StaffCode/IsActive → audit `staff_profiles.create/update` → save → catch code unique 409 `staff_code_taken`, UserId unique 409 exists, concurrency 412 → commit → đọc DTO.

**DB:** `StaffCode` unique toàn tổ chức; `UserId` unique đảm bảo 1–1; FK User restrict; WorkScopes cascade theo profile; xmin profile. User inactive vẫn có thể có profile active, vì không có điều kiện cấm trong handler; hai vòng đời là riêng.

**Vì sao — suy luận:** khóa User cho cả tạo và cập nhật tránh race lúc chưa có profile để khóa; If-Match nullable cho client lựa chọn create/update rõ ràng. **Điểm mạnh:** không có SELECT-rồi-insert thiếu unique, nhận 412 cả dưới concurrency DB, không chạm credential user. **Hạn chế:** khóa User chung với login/auth làm các thao tác profile có thể chờ nhau; đọc DTO sau commit vẫn dùng cancellation của request, khác UserCommandCompletion, nên mutation có thể đã thành công nhưng client nhận hủy/lỗi. No-op vẫn ghi audit, version chỉ đổi nếu scalar thực sự thay đổi. Không cache invalidation là chủ ý vì scope DB mỗi request.

**Chứng cứ:** [UpsertStaffProfileCommandHandler.cs:25](D:/hospital_management/benh_vien_be/src/QuanLyBenhVien.Application/Features/StaffProfiles/UpsertStaffProfile/UpsertStaffProfileCommandHandler.cs:25), `:53`; `src/QuanLyBenhVien.Presentation/Endpoints/V1/StaffProfiles/StaffProfilesEndpoints.cs:37`; `src/QuanLyBenhVien.Persistence/Configurations/Identity/StaffProfileConfiguration.cs:15`.

### 5.3 Thay phạm vi làm việc — `SetStaffWorkScopesCommandHandler`

**Hợp đồng:** `PUT /api/v1/users/{userId}/staff-profile/work-scopes`, `staff-profiles.manage`, CSRF, `If-Match` bắt buộc; request `DepartmentIds`, tối đa 100. Endpoint biến null thành `[]`; tập rỗng nghĩa là xóa hết scope. Id trùng được deduplicate, Guid.Empty đi vào danh sách không hợp lệ. Trả 200 DTO; 404 user/profile, 412 version, 400 `invalid_departments` với id không có hoặc inactive.

**Trình tự:** transaction → khóa User → nạp profile+scopes → so xmin → distinct DepartmentIds → đọc Departments → chỉ nhận active → lấy BranchId **từ DB**, không client → Domain thay tập scope → nếu đổi buộc UPDATE parent bằng MarkChanged, audit `staff_profiles.set_work_scopes` với danh sách Id, save/catch concurrency → commit → đọc DTO. Nếu không đổi thì không SaveChanges, không audit riêng, không bump version. Đây khác no-op của nhiều command Users/Roles vẫn ghi AuditRecord.

**DB:** PK `(StaffProfileId,DepartmentId)`; FK riêng tới Department và Branch restrict. BranchId được lưu để lọc nhưng FK hiện không ép cặp `(DepartmentId,BranchId)` khớp nhau; Application bảo đảm khi ghi bằng route hiện tại. Không cho chuyển khoa sang cơ sở khác qua Facilities update, nên parent Id hiện ổn định.

**Vì sao — suy luận:** client chỉ cần chọn khoa; cơ sở suy ra giảm dữ liệu không nhất quán. Profile xmin được bump khi thay bảng con giúp hai PUT cùng version có một bên thua 412. **Điểm mạnh:** không tin BranchId client, reject cả request nếu có khoa lạ, giới hạn payload, không Redis scope cache. **Hạn chế/cần chốt:** Department được đọc **không FOR UPDATE**, chỉ kiểm `Department.IsActive`, không kiểm Branch.IsActive. Khoa có thể bị ngừng giữa lần đọc và commit, làm scope được lưu nhưng ngay request sau không hiệu lực. Đây là race cấu hình/UX, **chưa chứng minh mở quyền trái phép**: AccessContext lọc cả khoa/cơ sở active. Nếu yêu cầu “chỉ gán phạm vi đang hiệu lực tại thời điểm commit”, cần chốt bất biến và test cạnh tranh tương ứng. Null payload thành rỗng có thể gây xóa scope khi client gửi sai shape; khác validation null của SetUserRoles.

**Chứng cứ:** [SetStaffWorkScopesCommandHandler.cs:26](D:/hospital_management/benh_vien_be/src/QuanLyBenhVien.Application/Features/StaffProfiles/SetStaffWorkScopes/SetStaffWorkScopesCommandHandler.cs:26), `:36`, `:43`; `src/QuanLyBenhVien.Persistence/Repositories/Catalog/FacilityRepository.cs:21`; `src/QuanLyBenhVien.Persistence/Configurations/Identity/StaffWorkScopeConfiguration.cs:13`; `src/QuanLyBenhVien.Presentation/Endpoints/V1/StaffProfiles/StaffProfilesEndpoints.cs:57`.

## 6. Facilities — cơ sở, khoa, phòng

### 6.1 Đọc cây — `GetFacilityTreeQueryHandler`

**Hợp đồng:** `GET /api/v1/facilities/`, `facilities.read`; trả 200 `BranchDto[] → DepartmentDto[] → RoomDto[]`, mỗi mục có Id,Code,Name,IsActive,RowVersion; khoa có Kind. Không filter chỉ-active, không phân trang, không validator. `IUnscopedRequest` cho phép xem cây tổ chức toàn hệ thống theo quyền quản trị.

**Trình tự:** query → read service → ba truy vấn toàn bảng `AsNoTracking` → group rooms theo DepartmentId, departments theo BranchId → sort Code ordinal trong bộ nhớ ở cả ba tầng → cây DTO. Không write transaction/khóa/audit query/cache.

**Vì sao — suy luận:** cơ cấu thường nhỏ, ba truy vấn tránh N+1; cả mục ngừng dùng cần cho màn hình quản trị. **Điểm mạnh:** sort không phụ thuộc collation, cấu trúc trực quan, không trả entity tracking. **Hạn chế:** toàn bộ tổ chức tải vào RAM; ba truy vấn không snapshot chung nên cạnh tranh thêm con/đổi trạng thái có thể cho một cây tạm thời không đầy đủ. Chưa có GET từng entity, filter/paging; Location create/update không có route GET đơn lẻ để follow.

**Chứng cứ:** `src/QuanLyBenhVien.Application/Features/Facilities/GetFacilityTree/GetFacilityTreeQueryHandler.cs:10`; [FacilitiesReadService.cs:10](D:/hospital_management/benh_vien_be/src/QuanLyBenhVien.Persistence/ReadServices/Catalog/FacilitiesReadService.cs:10).

### 6.2 Quy ước DTO/validation/DB chung

Code facility: `^[A-Z0-9][A-Z0-9-]{0,29}\\z` (1..30); không normalize/trim code. Name không whitespace, sau trim ≤200; Domain cũng trim và bảo vệ. `DepartmentKind`: `clinical`, `laboratory`, `pharmacy`, `billing`, `administrative`; parser chỉ chấp nhận chữ thường, enum đã định nghĩa, không số.

`Branch.Code` unique toàn hệ thống; `Department(BranchId,Code)` unique trong cơ sở; `Room(DepartmentId,Code)` unique trong khoa. FK `Department→Branch`, `Room→Department` đều restrict. Không có xóa facility; IsActive là ngừng dùng. Code/parent/Kind không có method sửa từ API; mutation hiện chỉ đổi Name/IsActive. Các unique là **không partial**: mục ngừng dùng vẫn giữ mã.

Create/Update trả DTO từ entity; Branch/Department DTO **không kèm con** (`[]`), chỉ GET tree trả cây đầy đủ. Client cần nạp cây lại hoặc merge đúng, không thay subtree bằng `[]` từ response mutation.

Chứng cứ: `src/QuanLyBenhVien.Application/Features/Facilities/Common/FacilitiesValidationRules.cs:7`; `FacilitiesErrors.cs:28`; `src/QuanLyBenhVien.Persistence/Configurations/Catalog/BranchConfiguration.cs:15`, `DepartmentConfiguration.cs:16`, `RoomConfiguration.cs:15`.

### 6.3 Tạo cơ sở — `CreateBranchCommandHandler`

**Hợp đồng:** `POST /api/v1/facilities/branches`, `facilities.manage`, CSRF, `CreateBranchRequest(Code,Name)` → 201 BranchDto+Location. **Trình tự:** validate → `Branch.Create` Guid v7/active=true → add → audit `facilities.create` resource Branch → một SaveChanges → DTO. Unique `IX_Branches_Code` → 409 `facility_code_taken`.

**Vì sao — suy luận:** root không có parent cần khóa; DB unique bảo vệ thay cho SELECT precheck. **Điểm mạnh:** đơn giản, audit nguyên tử, mã không bị tái sử dụng khi inactive. **Hạn chế:** message lỗi nói “trong phạm vi cha” nhưng Branch không có cha tường minh; không cấu hình địa chỉ/thông tin pháp lý cơ sở trong entity hiện tại. Không idempotency, gửi lại create cùng code nhận 409 chứ không trả tài nguyên cũ.

**Chứng cứ:** `src/QuanLyBenhVien.Application/Features/Facilities/CreateBranch/CreateBranchCommandHandler.cs:22`; `src/QuanLyBenhVien.Domain/Catalog/Facilities/Branch.cs:15`.

### 6.4 Tạo khoa — `CreateDepartmentCommandHandler`

**Hợp đồng:** `POST /api/v1/facilities/departments`, `facilities.manage`, CSRF, `CreateDepartmentRequest(BranchId,Code,Name,Kind)`; BranchId không rỗng → 201 DepartmentDto+Location. 404 `facility_not_found` nếu cha thiếu; 409 `parent_facility_inactive` nếu cha ngừng; 409 duplicate mã trong cơ sở.

**Trình tự:** transaction → Branch FOR UPDATE → kiểm tồn tại/active → parse Kind đã validator bảo đảm → Department.Create với BranchId DB → add → audit resource Department → save/catch `IX_Departments_BranchId_Code` → commit → DTO. Khóa Branch cùng với UpdateBranch bảo đảm tạo khoa không xen qua việc ngừng cơ sở sau kiểm tra active.

**Vì sao — suy luận:** dùng parent làm điểm đồng bộ cho bất biến lúc tạo con. **Điểm mạnh:** FK và unique đúng phạm vi; không kiểm ở code rồi bỏ mặc race deactivate parent; Kind hữu hạn. **Hạn chế:** các create cùng Branch serialize dù mã khác; API chưa sửa Kind/đổi Branch, nên việc cấu hình sai cần quy trình riêng, không suy diễn có thao tác move.

**Chứng cứ:** [CreateDepartmentCommandHandler.cs:22](D:/hospital_management/benh_vien_be/src/QuanLyBenhVien.Application/Features/Facilities/CreateDepartment/CreateDepartmentCommandHandler.cs:22); `src/QuanLyBenhVien.Persistence/Repositories/Catalog/FacilityRepository.cs:24`.

### 6.5 Tạo phòng — `CreateRoomCommandHandler`

**Hợp đồng:** `POST /api/v1/facilities/rooms`, `facilities.manage`, CSRF, `CreateRoomRequest(DepartmentId,Code,Name)`; DepartmentId không rỗng → 201 RoomDto+Location. Cùng nhóm lỗi parent-not-found/inactive/code-taken.

**Trình tự:** transaction → Department FOR UPDATE → kiểm tồn tại/active → Room.Create với DepartmentId DB, active=true → add → audit resource Room → save/catch `IX_Rooms_DepartmentId_Code` → commit → DTO. Không khóa/kiểm Branch trực tiếp.

**Vì sao — suy luận:** đồng bộ trên cha trực tiếp bảo vệ tạo phòng với ngừng khoa; không tải toàn aggregate cơ sở. **Điểm mạnh:** nhỏ, unique trong khoa, nguồn parent rõ. **Hạn chế:** cùng khoa serialize; nếu Department active nhưng Branch inactive (có thể qua activation hiện tại), tạo phòng vẫn được phép vì chỉ kiểm cha trực tiếp. Đây là semantics trạng thái cần thống nhất với UI, không tự kết luận scope truy cập lọt.

**Chứng cứ:** `src/QuanLyBenhVien.Application/Features/Facilities/CreateRoom/CreateRoomCommandHandler.cs:22`; `src/QuanLyBenhVien.Domain/Catalog/Facilities/Room.cs:16`.

### 6.6 Cập nhật cơ sở — `UpdateBranchCommandHandler`

**Hợp đồng:** `PUT /api/v1/facilities/branches/{id}`, `facilities.manage`, CSRF, `UpdateFacilityRequest(Name,IsActive)`, `If-Match`; 200 BranchDto, 404 missing, 412 `facility_version_conflict`, 409 `facility_has_active_children`.

**Trình tự:** transaction → Branch FOR UPDATE → so version → nếu active→inactive và còn Department active thì từ chối → Rename/SetActive → audit `facilities.update` resource Branch → save/commit → DTO. CreateDepartment khóa cùng Branch nên không chèn khoa giữa kiểm “không con active” và commit. No-op vẫn AuditRecord, parent version chỉ tăng khi có scalar đổi.

**Vì sao — suy luận:** ngừng dùng qua thao tác riêng từng tầng, tránh tự cascade trạng thái không được người dùng nhìn nhận. **Điểm mạnh:** không xóa quan hệ/lịch sử, chống stale UI. **Hạn chế/cần chốt:** khóa Branch không đồng bộ với **kích hoạt lại Department** vì update department chỉ khóa Department; ngừng cơ sở và bật khoa đồng thời có thể kết thúc với cha inactive/con active. Plan hiện chỉ quy định chặn tạo con dưới cha inactive và chặn ngừng cha đang có con active; chưa có bất biến tổng quát mọi con active phải có cha active. Ghi nhận khoảng trống kiểm chứng, không gán thành bug đã được đặc tả kết luận.

**Chứng cứ:** [UpdateBranchCommandHandler.cs:24](D:/hospital_management/benh_vien_be/src/QuanLyBenhVien.Application/Features/Facilities/UpdateBranch/UpdateBranchCommandHandler.cs:24); `src/QuanLyBenhVien.Persistence/Repositories/Catalog/FacilityRepository.cs:42`; plan `2026-10-03-phan-quyen-hai-lop/plan-01-nen-lop-2.md:522`.

### 6.7 Cập nhật khoa — `UpdateDepartmentCommandHandler`

**Hợp đồng:** `PUT /api/v1/facilities/departments/{id}`, cùng quyền/CSRF/DTO/If-Match/lỗi UpdateBranch; trả DepartmentDto không con.

**Trình tự:** transaction → Department FOR UPDATE → so version → active→inactive mà còn Room active thì 409 → Rename/SetActive → audit resource Department → save/commit → DTO. CreateRoom khóa cùng Department nên bảo vệ chèn mới khi ngừng khoa. Không đổi BranchId/Code/Kind, không tự xóa StaffWorkScopes và không cache invalidation.

**Vì sao — suy luận:** giữ tham chiếu phân công ổn định, trạng thái “ngừng dùng” khiến AccessContext tự loại scope request sau. **Điểm mạnh:** không mất cấu hình khi ngừng; khóa+version; module Catalog không phải cập nhật trực tiếp bảng nhân sự để thu hồi scope. **Hạn chế/cần chốt:** kích hoạt khoa không kiểm cơ sở active; cập nhật phòng có thể kích hoạt con xen giữa check/commit vì không khóa Department. Nếu muốn cây active nhất quán liên tục cần thống nhất giao thức khóa cha khi activation và viết test đồng thời. Chưa có bằng chứng yêu cầu ngừng khoa phải xóa scope hoặc vô hiệu hóa user/profile; hiện không làm vậy.

**Chứng cứ:** [UpdateDepartmentCommandHandler.cs:24](D:/hospital_management/benh_vien_be/src/QuanLyBenhVien.Application/Features/Facilities/UpdateDepartment/UpdateDepartmentCommandHandler.cs:24); `src/QuanLyBenhVien.Persistence/Authorization/AccessContext.cs:39`.

### 6.8 Cập nhật phòng — `UpdateRoomCommandHandler`

**Hợp đồng:** `PUT /api/v1/facilities/rooms/{id}`, cùng quyền/CSRF/DTO/If-Match; trả 200 RoomDto; 404/412, không có kiểm con vì leaf.

**Trình tự:** transaction → Room FOR UPDATE → so version → Rename/SetActive → audit resource Room → save/commit → DTO. Không đọc/khóa Department/Branch. Không đổi Code/DepartmentId, không xóa, không cache invalidation.

**Vì sao — suy luận:** leaf update chỉ cần khóa row chính, giữ nhỏ phần truy cập DB. **Điểm mạnh:** sửa tên/trạng thái an toàn với stale version, không ảnh hưởng định danh. **Hạn chế/cần chốt:** bật phòng dưới khoa inactive được handler cho phép; `Room.IsActive` hiện không tham gia AccessContext (scope ở mức khoa/cơ sở), chưa có module buổi khám sử dụng phòng trong phạm vi đang xét. Không thể suy rằng inactive room sẽ tự ngừng một buổi khám tương lai; phải thiết kế ở module sở hữu buổi khám.

**Chứng cứ:** [UpdateRoomCommandHandler.cs:24](D:/hospital_management/benh_vien_be/src/QuanLyBenhVien.Application/Features/Facilities/UpdateRoom/UpdateRoomCommandHandler.cs:24); `src/QuanLyBenhVien.Domain/Catalog/Facilities/Room.cs:26`.

## 7. Test hiện có và khoảng trống cần theo dõi

**Các mục sau là kiểm kê coverage từ source.** Bộ Full chạy tập trung trong phiên này đạt 230 unit test, 224 integration test và 8 integration test Skip; chi tiết kết quả và file bị loại khỏi build nằm ở [spec.md §8](spec.md). Không lấy bảng PASS cũ trong plan làm bằng chứng cho phiên này; các ca đề xuất bổ sung chưa được tái hiện mới.

| Nhóm | Test hiện có, nội dung đã nhận diện | Khoảng trống đọc code cần bổ sung nếu thay đổi |
|---|---|---|
| Users đọc | `IntegrationTests/Admin/UsersReadTests.cs`: paging, huge page, role/status filter, tên trùng, 401/403, DTO không sensitive | Count/page snapshot dưới ghi đồng thời; roleId empty; chính sách audit đọc thông tin nhân sự |
| Users tạo/trạng thái | `UsersAdminTests.cs`: password một lần/no-store/hash, mass assignment, duplicate email đồng thời, CSRF, deactivate/login race, reactivate không hồi phiên | Mất response mật khẩu một lần; cancel/lỗi read sau commit; kích hoạt từ stale màn hình |
| User access | `UserAccessManagementTests.cs`: grant/revoke request sau, no-op, revoke không deny role, version/self/last-admin, unknown/duplicate role | Grant trùng lý do mới; kích thước roleIds; fail audit/flush ở từng nhánh |
| Last-admin đồng thời | `LastAdminConcurrencyTests.cs`: hai admin khóa nhau/bỏ role nhau/khóa và bỏ role đồng thời | Tương tác với mọi đường ghi membership tương lai, đảm bảo đều dùng cùng admin-safety lock |
| Roles | `RolesAdminTests.cs`, `RolesCommandTests.cs`: role thật, create unique, rename version, child change bump xmin, member invalidation, role assignment race, no-op, system non-admin edit, audit diff | Fan-out lớn; code regex edge; cancel sau commit; khả năng đọc bằng Location |
| Permission catalog | `PermissionCatalogTests.cs`, Domain `PermissionsTests.cs`: catalog code/DB tương ứng, 401/403 | Seed thiếu mã trong multi-instance/deploy, mã tương lai chưa kích hoạt |
| Facilities | `FacilitiesAdminTests.cs`: tạo cây, duplicate/validation, If-Match, ngừng khoa còn phòng, create dưới cha inactive, quyền/audit; `FacilitiesPersistenceTests.cs`: reload, unique phạm vi, xmin | Activation dưới cha inactive; ngừng cha đồng thời kích hoạt con; cây multi-query khi có ghi đồng thời |
| StaffProfiles | `StaffProfilesAdminTests.cs`: create lại 409, version/unique/validation, BranchId suy DB, khoa lạ/inactive, hai PUT cùng version, quyền/audit; `StaffProfilePersistenceTests.cs`: unique StaffCode/UserId, scopes reload, child-only xmin | Assign scopes đồng thời ngừng khoa/cơ sở; null→[] xóa scope; profile inactive/user inactive độc lập; cancel/read sau commit |
| Cache/audit/seed | `Caching/CacheInvalidationDeliveryTests.cs`, `Authorization/PermissionCacheInvalidationTests.cs`, unit interceptor/writer; `RoleDefaultsSeederTests.cs`: default một lần, không tự gán lại khi admin bỏ, concurrent apply | Kiểm chứng tại thời điểm runtime hiện tại; giới hạn độ trễ khi Redis/worker lỗi; không mặc định tuyên bố mọi mutation fail đều có audit thất bại |

Các prefix test trong bảng thuộc `benh_vien_be/tests/QuanLyBenhVien.IntegrationTests/` hoặc `QuanLyBenhVien.UnitTests/` như tên nhóm. Bằng chứng cụ thể: `Admin/StaffProfilesAdminTests.cs:151` kiểm PUT cùng version; `Admin/RolesCommandTests.cs:228` kiểm đổi quyền và gán role đồng thời; `Admin/FacilitiesAdminTests.cs:103` kiểm ngừng khoa còn phòng và tạo phòng dưới khoa inactive.

## 8. Nhận xét tổng thể và những điều không nên suy diễn

**Các quyết định tốt đã có bằng chứng code:** CQRS đọc projection/ghi aggregate; unique/FK làm trọng tài; version của bảng cha được bump khi child-only mutation; audit bền vững cùng nghiệp vụ; thu hồi phiên và đổi security version trong cùng transaction; cache invalidation ghi chờ rồi flush sau commit; khóa admin toàn cục xử lý race last-admin và fan-out membership.

**Hạn chế đã xác nhận ở code, không đồng nghĩa tất cả là bug:** đọc nhiều query không snapshot; hợp đồng `If-Match` khác nhau theo luồng; response sau commit không đồng nhất cách xử lý cancellation; DTO facility mutation không có con; thiếu GET individual cho Location; không audit query quản trị riêng; null work-scopes thành xóa tất cả; payload role/permission chưa có giới hạn count; không có idempotency cho create quản trị. Cần cân nhắc theo tác động thực tế trước khi sửa.

**`CẦN_XÁC_NHẬN`:** trạng thái active cha/con và đảm bảo scope active tại thời điểm commit. Code có bất đối xứng tạo/ngừng/activation, nhưng spec/plan chưa khẳng định bất biến “mọi con active luôn có cha active”. AccessContext lọc cha/khoa active nên không có bằng chứng quyền lớp 2 vượt phạm vi chỉ vì trạng thái cây không đồng nhất. Nếu quy tắc nghiệp vụ muốn giữ trạng thái riêng để khôi phục sau này, UI cần biểu thị “active riêng nhưng không hiệu lực do cha ngừng”; nếu muốn cấm, phải chốt rồi thiết kế thứ tự khóa thống nhất.

**Khoảng cách kiến trúc:** hướng dẫn AGENTS mô tả `AuditBehavior` nhưng DI/code hiện tại chưa đưa vào pipeline. Đối với phần quản trị đã rà, audit ghi tay+interceptor hoạt động theo thiết kế hiện tại; không dùng khoảng cách này để khẳng định command đã không audit. Khi thêm tài nguyên lâm sàng trả dữ liệu nhạy cảm, phải chứng minh audit đọc bền vững riêng trước trả dữ liệu.

**Ngoài phạm vi hiện tại:** reset password bởi admin, quên mật khẩu, MFA, xóa role, deny permission, grant trực tiếp có thời hạn/phạm vi tài nguyên, API xem audit, hồ sơ lâm sàng, buổi khám/hàng chờ/giường. `StaffProfile`, `StaffWorkScope`, catalog quyền và khung lớp 2 hiện có không tự chứng minh các workflow tương lai đã được triển khai. Gateway có `users-route`, `roles-route`, `permissions-route`, `facilities-route`; staff-profile đi qua wildcard users, không cần route riêng. Chứng cứ: `src/QuanLyBenhVien.Gateway/appsettings.json:39`.

