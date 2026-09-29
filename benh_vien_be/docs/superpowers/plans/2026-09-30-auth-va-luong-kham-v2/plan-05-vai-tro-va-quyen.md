# Plan V2 05 — Vai trò và danh mục quyền

> Khi được yêu cầu triển khai, dùng superpowers:executing-plans theo task; subagent chỉ khi được chọn. Claude viết code khi người dùng gọi skill triển khai (chốt 2026-09-30).

**Mục tiêu:** màn Vai trò & quyền đọc/ghi PostgreSQL qua API thật, dùng danh mục quyền server và cập nhật quyền hiệu lực của thành viên.

**Kiến trúc:** CQRS Roles/Permissions, read service cho query, Role aggregate/repository cho command; policy và invalidation từ 01/04.

**Công nghệ:** .NET 10/Carter, EF Core 10/PostgreSQL 17/Redis, React 16/Redux.

**Spec:** [spec.md](spec.md) §3, §7; auth spec cũ §4.2–4.3. Phụ thuộc 01–04.

## Ràng buộc chung

| Route V1 | Permission | Request |
|---|---|---|
| GET `/api/v1/roles` | roles.read | Không |
| POST `/api/v1/roles` | roles.manage | `{code,name,permissionCodes[]}` |
| PUT `/api/v1/roles/{id}` | roles.manage | `{name}` |
| PUT `/api/v1/roles/{id}/permissions` | roles.manage | `{permissionCodes[]}` |
| GET `/api/v1/permissions` | permissions.read | Không |

Code lower-kebab-case 2–50 ký tự theo Domain; tên tối đa 100 theo configuration hiện có. Không xóa role, không đổi Code role hệ thống. Role `admin` luôn giữ các quyền IdentityAccess lõi (đã chốt, spec §2 mục 13): `SetRolePermissions` trên admin thiếu quyền lõi → 409 `admin_core_permissions_required`, không mutate. Không reset password hoặc thêm quyền lâm sàng dự kiến.

Danh mục quyền và role Id dùng dữ liệu thật; không đưa `role-admin` hay permission không tồn tại vào DB. Ghi AuditRecord và diff qua interceptor; mọi mutation có Origin/CSRF cho auth cookie hiện tại.

## Trọng tâm review

1. Mã role trùng do hai request đồng thời → task 1/2, unique DB → 409.
2. Bỏ quyền khi nhiều thành viên đã có cache → task 2, invalidate đủ membership và không stale writer.
3. Cạnh tranh gán role/sửa quyền role → task 2 và plan 06, khóa membership cùng giao thức.
4. FE role hệ thống chỉ đọc nhưng spec cho sửa Name/quyền → task 3, hành vi nhất quán với giới hạn admin lõi.
5. Hai tab sửa permissions và request save lỗi → task 2/3, version conflict và UI không treo saving.

## Task 1 — Query và contract chung

**File tạo APP:** `Features/Roles/Common/{IRolesReadService,RoleDto,RolesErrors}.cs`; `Features/Roles/ListRoles/{ListRolesQuery,ListRolesQueryHandler}.cs`; `Features/Permissions/ListPermissions/{ListPermissionsQuery,ListPermissionsQueryHandler,PermissionDto}.cs`; `Features/Permissions/Common/IPermissionsReadService.cs`.

**File tạo PERS:** `ReadServices/Identity/RolesReadService.cs`, `PermissionsReadService.cs`. **File tạo PRES:** `Endpoints/V1/Roles/RolesEndpoints.cs`, `Endpoints/V1/Permissions/PermissionsEndpoints.cs`. Sửa DI và kiểm GW route đã có.

**Giao diện:** `IRolesReadService.ListAsync(CancellationToken ct): Task<IReadOnlyList<RoleDto>>`; RoleDto gồm `Guid Id,string Code,string Name,bool IsSystem,IReadOnlyList<string> PermissionCodes,uint RowVersion`. `IPermissionsReadService.ListAsync(ct): Task<IReadOnlyList<PermissionDto>>`; PermissionDto gồm Code, Group, Description. Không thêm GET role/id chỉ vì mock client có nếu list đã đủ dữ liệu.

**Test:** port `IT/Admin/RolesAdminTests.cs`; thêm `IT/Admin/PermissionCatalogTests.cs`.

- [ ] Test 9 system roles từ DB, Guid thật, permissions không có mã ngoài catalog, query không trả entity tracking/password; 401/403 theo permission.
- [ ] Chạy RolesAdmin/PermissionCatalog để thấy fail; triển khai query/endpoint mỏng, WithName có V1, API đăng ký Carter hiện có.
- [ ] Role.RowVersion (`uint`) map `xmin` bằng IsRowVersion. Query trả version để FE gửi If-Match cho PUT. Kiểm migration/schema diff bằng EF, không tạo cột vật lý xmin thủ công; nếu tooling sinh thay đổi thì review migration mới.
- [ ] Chạy lại tests. Giữ shape từ spec, FE sẽ map ở task 3.

## Task 2 — Command và invalidation toàn bộ thành viên

**File tạo APP/Roles:** mỗi use case `CreateRole`, `RenameRole`, `SetRolePermissions` có `XxxCommand.cs`, `XxxCommandHandler.cs`, `XxxCommandValidator.cs` trong `Features/Roles/<UseCase>/`.

**File tạo PRES/Roles:** `CreateRoleRequest.cs`, `RenameRoleRequest.cs`, `SetRolePermissionsRequest.cs`. **File sửa:** `DOM/Identity/IRoleRepository.cs`, `Role.cs`; `PERS/Repositories/Identity/RoleRepository.cs`, `Configurations/Identity/RoleConfiguration.cs`; RolesEndpoints; seeder chỉ nếu test phát hiện phá thay đổi đã được cho phép.

**Contract:** `CreateRoleCommand(string Code,string Name,IReadOnlyList<string> PermissionCodes): ICommand<Result<RoleDto>>`; `RenameRoleCommand(Guid Id,string Name,uint ExpectedVersion)` và `SetRolePermissionsCommand(Guid Id,IReadOnlyList<string> PermissionCodes,uint ExpectedVersion)` cùng trả Result<RoleDto>. PUT yêu cầu If-Match chứa RowVersion; thiếu/sai định dạng → 400, version cũ → 412. Header/version là bổ sung concurrency kỹ thuật cần review cùng DTO.

`IRoleRepository.GetForUpdateAsync(Guid id,ct): Task<Role?>` khóa role/reload mới nhất. Sửa quyền khóa role trước khi đọc danh sách member. Plan 06 gán/gỡ role khóa tất cả role liên quan theo Id trước User; không có thao tác nào được giữ User rồi mới lấy role lock. Nếu cần admin safety thì thứ tự toàn cục: admin-safety → Roles tăng Id → Users tăng Id → families/tokens.

Thay đổi bảng con RolePermissions không tự tăng `xmin` của Role. Bổ sung `IRoleRepository.MarkChanged(Role role): void`; Persistence đánh dấu thuộc tính Name của bản ghi Role đang tracking là modified khi tập quyền thực sự thay đổi, để luôn phát sinh UPDATE bản ghi cha và version mới trong cùng transaction. Không tạo cột vật lý xmin hoặc coi version bảng con là version aggregate.

- [ ] Test code trùng/không hợp lệ/permission lạ → 409/400; tên trắng/quá dài → 400; role không tồn tại → 404; sửa version cũ → 412 không thay dữ liệu.
- [ ] Test chỉ sửa permissions cũng đổi RowVersion của Role; request tiếp theo dùng If-Match trước lần sửa đó nhận 412. Đọc DTO trả về sau SaveChanges để lấy version mới.
- [ ] Test không bỏ được quyền admin lõi; không đổi Code/xóa system role; role thường sửa Name/permissions được; role system không phải admin được chỉnh theo spec, không khóa hết chỉ vì IsSystem.
- [ ] Test quyền của mọi member được invalidation trong cùng transaction; rollback role phải rollback cả invalidation/audit. Test gán role chen giữa thao tác đổi quyền không để member mới giữ cache sai.
- [ ] Chạy `dotnet test tests/QuanLyBenhVien.IntegrationTests --filter FullyQualifiedName~RolesAdmin`; implement command/repository, audit và commit/flush theo spec. Tái dùng cơ chế bảng chờ plan 01.
- [ ] Map 23505 đúng constraint Code → 409 ổn định `role_code_taken`; không catch mọi DbUpdateException thành “trùng role”. Set permissions là replace toàn bộ sau validate catalog.
- [ ] Create trả 201 + DTO/Location phù hợp route hiện có; PUT trả 200 DTO version mới. Chạy lại integration và kiểm querycache sau đổi.

## Task 3 — Nối FE, bỏ dữ liệu role/quyền hard-code

**File sửa:** `FE/src/feature/Roles/api/rolesClient.js`, `api/index.js`, `Container.js`, `component/RoleDetail.js`, `component/RoleList.js`, `permissionCatalog.js`, `redux/action.js`, `redux/reducer.js`; registry availability plan 04.

**File tạo:** `FE/src/feature/Roles/component/CreateRoleDialog.js`, `FE/src/feature/Roles/__tests__/rolesClient.test.js`, `RoleDetail.test.js`.

**Giao diện FE:** `listRoles()`, `listPermissions()`, `createRole({code,name,permissionCodes})`, `renameRole(id,name,version)`, `updateRolePermissions(id,codes,version)`. Client gửi PUT đúng route, If-Match, map `isSystem`/`permissionCodes` sang UI rõ ràng; không đổi server thành `system`/`permissions` để chiều mock.

- [ ] Test client URL/method/body/version; create/clone phải nhập Code mới, không chỉ window.prompt tên; clone từ role đã lưu, không gán IsSystem từ client.
- [ ] Test read-only do thiếu roles.manage; thiếu permissions.read thì hiện lý do không tải được catalog, không tự thêm quyền. Không suy manage tự động bao hàm read nếu DB chưa cấp.
- [ ] Test save lỗi/412: finally bỏ saving, giữ chỉnh sửa để đối chiếu, không tự ghi đè nội dung từ server. Đổi role có unsaved change hỏi lưu/bỏ rõ ràng trước chuyển.
- [ ] Chạy Jest `rolesClient|RoleDetail`, implement và chạy lại. Khi API thật đạt, bật availability màn roles, tắt mock theo cấu hình thật.
- [ ] Hiển thị lỗi loadRoles thay vì bảng trống như không có dữ liệu; không giữ promise rejection không xử lý.

## Task 4 — Nghiệm thu và mốc Git

- [ ] Bật RolesAdminTests/PermissionCatalogTests trong IT csproj; test PermissionService kế thừa 04 vẫn PASS.
- [ ] Full + Frontend + build FE; kiểm schema diff, không sửa migration đã commit.
- [ ] Trình duyệt role chỉ đọc, quản lý, tạo/clone, sửa quyền, 412; tài khoản member tải `/me` sau invalidation thấy đúng quyền. Người không quyền gọi API trực tiếp vẫn bị chặn.
- [ ] Review diff, điều khoản bảo vệ admin lõi và source spec lệch mock; chuẩn bị commit theo plan.md. Chưa nghiệm thu quản lý tài khoản trước plan 06.

**Trạng thái:** NOT_RUN.
