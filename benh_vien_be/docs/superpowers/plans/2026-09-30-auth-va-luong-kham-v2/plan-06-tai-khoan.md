# Plan V2 06 — Tài khoản, gán vai trò và quyền bổ sung

> Khi được yêu cầu triển khai, dùng superpowers:executing-plans theo task; subagent chỉ khi được chọn. Claude viết code khi người dùng gọi skill triển khai (chốt 2026-09-30).

**Mục tiêu:** màn tài khoản dùng API/DB thật, tạo tài khoản phải đổi mật khẩu, quản lý quyền và khóa/mở khóa an toàn.

**Kiến trúc:** vertical slices Users, read projection qua Persistence, mutation qua User aggregate; tái dùng policy, role catalog và invalidation các plan trước.

**Công nghệ:** .NET 10/Carter/MediatR 12.5.0, PostgreSQL 17/Redis; React 16/Redux.

**Spec:** [spec.md](spec.md) §7 và auth spec cũ §4.2–4.3; phụ thuộc 01–05. Reset mật khẩu admin ngoài phạm vi theo spec cũ §8.

## Ràng buộc chung và contract

| Route V1 | Quyền | Body |
|---|---|---|
| GET `/api/v1/users` | users.read | Query pageNumber/pageSize/searchTerm, roleId/status nếu triển khai theo contract dưới |
| GET `/api/v1/users/{id}` | users.read | Không |
| POST `/api/v1/users` | users.create | `{email,fullName,roleIds[]}` → 201 `{user,initialPassword}` |
| POST `/api/v1/users/{id}/activate` hoặc `/deactivate` | users.activate | Không |
| PUT `/api/v1/users/{id}/roles` | users.roles.manage | `{roleIds[]}` |
| POST `/api/v1/users/{id}/permissions/grant` hoặc `/revoke` | users.permissions.manage | `{permissionCode,reason}` |

Không gửi `permissionCodes[]` cho grant/revoke, không gửi POST cho thay role. Mật khẩu ban đầu do **server sinh ngẫu nhiên** (chốt 2026-09-30), chỉ trả plaintext đúng một lần trong response tạo (`Cache-Control: no-store`); DB chỉ lưu hash; không có trong user DTO, audit hay log; không có API xem lại. Email unique do DB; user không IsDeleted; không tự khóa mình hoặc tự gỡ admin của mình; luôn còn ít nhất một admin active.

Toàn bộ mutation kiểm Origin/CSRF, policy và audit. Phân trang 1-based, pageSize 1..100, lọc trước COUNT/page. FE không tự retry command khi chưa có idempotency contract. Thao tác activate/deactivate đặt trạng thái đích, không toggle dựa trên state FE, và **không yêu cầu If-Match** (chốt 2026-09-30).

## Trọng tâm review

1. Hai admin cùng khóa/gỡ vai trò nhau → task 2, không mất admin cuối.
2. Email trùng đồng thời hoặc roleId giả → task 1/2, 409/400 và rollback.
3. Thay role và sửa permissions role chen nhau → task 2, giao thức khóa như plan 05.
4. Revoke grant nhưng quyền còn từ role → task 3, UI giải thích nguồn, không hiển thị mất quyền giả.
5. Chuyển filter/trang/chọn user khi request cũ còn chạy → task 3, không ghi dữ liệu nhầm người và không mất lựa chọn im lặng.

## Task 1 — DTO/query và phân trang thật

**File tạo APP:** `Features/Users/Common/IUsersReadService.cs`, `UserSummaryDto.cs`, `UserDetailDto.cs`, `UsersErrors.cs`; các use case `ListUsers` và `GetUser` có `XxxQuery.cs`, `XxxQueryHandler.cs`, `XxxQueryValidator.cs` trong thư mục riêng.

**File sửa:** `APP/Common/Models/PagedResult.cs` (hiện rỗng). **File tạo PERS:** `ReadServices/Identity/UsersReadService.cs`. **File tạo PRES:** `Endpoints/V1/Users/UsersEndpoints.cs`.

**Giao diện:** `PagedResult<T>(IReadOnlyList<T> Items,int PageNumber,int PageSize,int TotalCount,int TotalPages)`; `ListUsersQuery(int PageNumber=1,int PageSize=20,string? SearchTerm=null,Guid? RoleId=null,string? Status=null)` trả Result<PagedResult<UserSummaryDto>>. `GetUserQuery(Guid Id)` trả Result<UserDetailDto>.

Summary: Id, Email, FullName, IsActive, MustChangePassword, Roles (Id/Code/Name), RowVersion. Detail thêm PermissionGrants (Code/Reason) và EffectivePermissions; không chứa PasswordHash/refresh token. Status filter nhận active/must_change_password/locked; locked = !IsActive, chờ đổi = IsActive && MustChangePassword, active = IsActive && !MustChangePassword. Đây là contract bổ sung của các filter mock đang có.

`IUsersReadService.ListAsync(ListUsersQuery query,ct): Task<PagedResult<UserSummaryDto>>`; `GetAsync(Guid id,ct): Task<UserDetailDto?>`. Query không tracking, projection và sắp xếp ổn định theo FullName rồi Id trước Skip/Take.

**Test:** port `IT/Admin/UsersAdminTests.cs`, tạo `IT/Admin/UsersReadTests.cs`.

- [ ] Test pageNumber 0/pageSize 0/101 → 400; tổng/totalPages đúng với filter; trang quá cuối trả items rỗng; không lộ trường nhạy cảm.
- [ ] Test search/filter RoleId là Guid thật, sort ổn định với tên trùng, detail user không tồn tại 404.
- [ ] Chạy UsersRead để thấy fail; implement query/DTO/read service và Carter route RequirePermission/WithName V1.
- [ ] User.RowVersion uint map xmin. Không thêm cột vật lý xmin bằng SQL tay; review schema diff/migration nếu có. Chạy lại test PostgreSQL.

## Task 2 — Commands và bảo vệ admin cuối

**File tạo APP/Features/Users:** use case `CreateUser`, `ActivateUser`, `DeactivateUser`, `SetUserRoles`, `GrantUserPermission`, `RevokeUserPermission`; mỗi use case có `XxxCommand.cs`, `XxxCommandHandler.cs`, `XxxCommandValidator.cs`.

**File tạo APP:** `Common/Security/IInitialPasswordGenerator.cs` (`string Generate()`), `Features/Users/CreateUser/CreateUserResultDto.cs` (`UserDetailDto User, string InitialPassword`). **File tạo INF:** `Security/InitialPasswordGenerator.cs` dùng `RandomNumberGenerator`.

**File tạo PRES/Users:** `CreateUserRequest.cs`, `SetUserRolesRequest.cs`, `PermissionGrantRequest.cs` (dùng cho grant/revoke). **File sửa:** IUserRepository/UserRepository, User, UserConfiguration và UsersEndpoints. Tái dùng IRoleRepository và danh mục Permissions.

**Giao diện command:** CreateUserCommand(Email,FullName,IReadOnlyList<Guid> RoleIds) trả Result<CreateUserResultDto>; ActivateUserCommand/DeactivateUserCommand(Guid Id); SetUserRolesCommand(Guid Id,IReadOnlyList<Guid> RoleIds,uint ExpectedVersion); GrantUserPermissionCommand/RevokeUserPermissionCommand(Guid Id,string PermissionCode,string Reason,uint ExpectedVersion), cùng trả Result<UserDetailDto>.

Mutation vào user hiện có (trừ activate/deactivate) nhận If-Match như plan 05: thiếu/sai định dạng 400, stale 412; trả DTO version mới. Create 201, email trùng 409 `email_taken`; self_action_forbidden/last_admin 409. Thêm `TimeProvider`/now vào đường thay đổi mới nếu method hiện dùng UtcNow trực tiếp; không refactor phương thức không liên quan.

**Test:** port `IT/Admin/UserAccessManagementTests.cs`, `UsersAdminTests.cs`; thêm `IT/Admin/LastAdminConcurrencyTests.cs`.

- [ ] Test create: email chuẩn hóa, unique DB, roleIds hợp lệ/không trùng, hash password, MustChangePassword=true; không cho client đặt IsActive/SecurityVersion/IsSystem tùy ý.
- [ ] Mật khẩu ban đầu do generator sinh (đủ entropy, ví dụ 16 ký tự từ bảng chữ-số dễ đọc), luôn thỏa `PasswordPolicy` của plan 01 (10–128, không chứa local-part email — sinh lại nếu vi phạm). Test: response 201 có `initialPassword` login được, DB chỉ có hash, GET user không trả mật khẩu, audit/log không chứa plaintext; generator được thay bằng fake trong unit test.
- [ ] Test self-lock/self-remove-admin/last-admin trả đúng lỗi và không mutate; hai admin thao tác đồng thời vẫn còn ≥1 active admin sau commit.
- [ ] Test grant reason trắng/mã lạ → 400; grant đã có/revoke không có có hành vi idempotent rõ ràng; revoke grant không xóa quyền đến từ role.
- [ ] Chạy UsersAdmin/UserAccess/LastAdminConcurrency, ghi nhận test đỏ; implement khóa admin-safety trước role locks/User theo thứ tự spec. Với SetUserRoles, danh sách role cũ đọc trước để định vị phải được kiểm lại sau khóa; nếu membership đổi khiến thiếu role lock, rollback và tải lại, không lấy khóa role mới trong lúc đã giữ User.
- [ ] Set roles thay nguyên tập; role không tồn tại trả 400 trước mutate. Deactivate tăng sv, revoke mọi family active, invalidate session + perm cùng transaction; Activate invalidate perm nhưng không hồi sinh family đã revoked. Grant/revoke/set roles chỉ invalidate permission theo spec.
- [ ] Cache quyền `perm:{userId}` không có TTL và là nguồn quyết định quyền: MỌI command đổi `UserRole`, `UserPermission` (grant/revoke) hoặc `IsActive` phải gọi `ICacheInvalidator.InvalidatePermissions` cho mọi user bị ảnh hưởng, ghi trong CÙNG transaction với thay đổi (rollback thì mất cùng), `FlushAsync` sau commit. Integration test "đổi quyền/khóa xong thì request kế tiếp có hiệu lực": làm ấm cache bằng một request, chạy command, request kế tiếp của chính user đó phải thấy quyền mới / bị 401 khi đã khóa (không chỉ kiểm tra dòng `CacheInvalidations` hay key bị DEL).
- [ ] Create/activate/deactivate/roles/grants ghi đúng AuditActions. Login đồng thời với deactivate phải tham gia User lock, kiểm lại IsActive để không tạo session sau khóa bằng dữ liệu cũ.
- [ ] Flush sau commit, không gọi Redis/HTTP trong transaction. Map unique đúng constraint; DTO response đọc từ state/projection hợp lệ, không trả entity.
- [ ] Chạy lại integration, test race với plan 05 sửa role permissions và cache generation.

## Task 3 — FE tài khoản, chi tiết và nguồn quyền

**File sửa:** `FE/src/feature/Admin/api/usersClient.js`, `api/index.js`, `Container.js`, `redux/action.js`, `redux/reducer.js`; `component/CreateUserDialog.js`, `UserDetailPanel.js`, `UserFilters.js`, `UsersTable.js`, `UserStatusBadge.js`; route availability plan 04.

**File tạo:** `FE/src/feature/Admin/component/UsersPagination.js`; test `FE/src/feature/Admin/__tests__/usersClient.test.js`, `UserDetailPanel.test.js`, `UsersNavigation.test.js`.

**Giao diện FE:** listUsers theo PagedResult, getUser(id) khi mở panel; createUser đúng body contract; assignRoles dùng PUT + roleIds thật; grant/revoke nhận một code và reason mỗi command; mutation gửi version và cập nhật DTO server trả về. Danh sách chọn role/permission dùng API plan 05 và đúng quyền đọc, không fake role IDs.

- [ ] Test từ danh sách qua trang 2 hoạt động; filter đổi reset page 1, response tìm kiếm cũ bị bỏ bằng requestId; không mở detail từ summary thiếu trường.
- [ ] Test đổi selectedUser khi save đang chờ không cập nhật panel của user mới nhầm; response cũ chỉ cập nhật entity đúng Id. Handle error/finally để busy không treo.
- [ ] Test tạo user yêu cầu email/fullName/roleIds (không có ô mật khẩu), lỗi field rõ; sau 201 dialog hiển thị `initialPassword` một lần kèm nút sao chép và cảnh báo không xem lại được; mật khẩu chỉ nằm trong state cục bộ của dialog, không vào Redux/storage/log, xóa khi đóng dialog.
- [ ] Test phân biệt quyền từ role và quyền cấp thêm; revoke grant còn quyền do role phải hiển thị vẫn có quyền và nguồn. UI nhập reason khi grant/revoke.
- [ ] Test 412 giữ dữ liệu người dùng để đối chiếu và nạp phiên bản mới; không tự retry ghi đè. Thiếu permission thì ẩn/disable đúng nút, BE vẫn enforce.
- [ ] Chạy Jest `usersClient|UserDetailPanel|UsersNavigation`, implement rồi chạy lại. Loại/disable nút Reset mật khẩu với thông báo chưa hỗ trợ, không tự xây API ngoài spec.
- [ ] Bật availability users khi API thật đạt; tắt mock trong đường thật. Nếu user có quyền create/manage nhưng thiếu quyền đọc catalog, hiện lý do và không tự cấp quyền ngầm.

## Task 4 — Nghiệm thu chuỗi quản trị đến đăng nhập

- [ ] Bật lại UsersAdminTests/UserAccessManagementTests trong IT csproj sau port; giữ các test của plan trước chạy được.
- [ ] Full + Frontend + build FE, kiểm migration và audit không secret.
- [ ] Trình duyệt: admin tạo user → user login V1 → đổi mật khẩu → màn theo quyền; multi-workspace chọn mỗi login mới; không quyền hiện no-access.
- [ ] Admin đổi quyền/role → user Kiểm tra lại hoặc request mới cập nhật quyền; khóa user → session cũ không dùng được sau invalidation; mở khóa → phải login mới.
- [ ] Test qua Gateway cho quyền bị thu hồi/token cũ, không chỉ direct API. Không dùng tài khoản admin để che thiếu policy của role thường.
- [ ] Review diff và checklist main theo plan.md. Không tự bật Reception/Clinic/Vitals chỉ vì đã tạo các role có tên tương ứng.

**Trạng thái:** ĐÃ TRIỂN KHAI 2026-10-03 (f13c5b6..ebd5d89). validate Full PASS (unit 170, IT 195/8 skip), Frontend PASS (196 test), `npm run build` PASS, không pending model change. Trình duyệt: NOT_RUN. CẦN_XÁC_NHẬN: `User.RowVersion` = xmin nên đăng nhập của user đích làm If-Match của admin cũ → 412 giả; hiện chấp nhận (FE nạp lại khi 412). Kết thúc bộ auth/quản trị, chuyển sang plan nghiệp vụ dựa trên [luồng khám và hồ sơ](luong-kham-va-ho-so.md), không coi BE nghiệp vụ đã hoàn thành.
