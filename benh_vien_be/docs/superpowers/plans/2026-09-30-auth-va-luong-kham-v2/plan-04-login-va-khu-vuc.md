# Plan V2 04 — Login chung và vào khu vực theo quyền

> Khi được yêu cầu triển khai, dùng superpowers:executing-plans theo từng task; chỉ dùng subagent nếu được chọn. Claude viết code khi người dùng gọi skill triển khai (chốt 2026-09-30).

**Mục tiêu:** sau login đi đúng màn đổi bắt buộc/chọn khu vực/không có quyền; quyền BE kiểm thật, FE không dùng tên role để quyết định truy cập.

**Kiến trúc:** giữ login V1, bổ sung permission service theo port Application; API áp policy, FE có route registry dùng chung cho guard/sidebar/workspace. Tái dùng invalidation và session của plan 01–03.

**Công nghệ:** .NET 10 authorization/Carter, PostgreSQL/Redis; React 16, react-router 4, Redux.

**Spec:** [spec.md](spec.md) §2, §5.3, §6–7 và auth spec cũ §4.1/§5. Phụ thuộc 01–03; không implement Clinic/Reception BE tại đây.

## Ràng buộc chung

- API vẫn `/api/v1/auth/login`, không tạo login endpoint riêng cho bác sĩ/lễ tân/admin.
- Đổi mật khẩu bắt buộc kiểm trước chọn khu vực; 0 khu vực → no-access, 1 → vào thẳng, nhiều → chọn mỗi login mới.
- Quyền = union các role + grant, không có deny override mới. Admin không mặc định đọc bệnh án.
- Khu vực có quyền nhưng chưa triển khai phải hiện tình trạng chưa sẵn sàng; không gọi mock trong production hay sửa seed để giả quyền.
- Role không thay phân công cơ sở/phòng/buổi hoặc quyền theo người bệnh. Các quyền tài nguyên này thuộc plan nghiệp vụ sau.
- Token/PHI chỉ RAM; không nhớ workspace qua localStorage. Xóa lựa chọn khi login mới/logout. Refresh trong phiên không tự bắt chọn lại nếu lựa chọn trong RAM vẫn hợp lệ.

## Trọng tâm review

1. User chỉ có roles.read → task 3 vào roles, không users.
2. Quyền bị thu hồi khi màn đang mở/cache đang có → task 1/2/3 kiểm BE từ chối và UI cập nhật.
3. Admin/multi-role vừa đăng nhập với return URL → task 3 không vượt bước đổi mật khẩu/chọn khu vực.
4. Chuyển dropdown hoặc bấm Kiểm tra lại không điều hướng → task 3 test render thực tế.
5. Khu vực nghiệp vụ mock và quyền dự kiến → task 3/4 không hiển thị như khu vực vận hành được.

## Task 1 — Permission service tách DB và Redis

**File tạo:** `APP/Common/Identity/UserAccessDto.cs`, `IUserAccessReadService.cs`, `IPermissionService.cs`; `PERS/ReadServices/Identity/UserAccessReadService.cs`.

**File sửa:** `PERS/ReadServices/Identity/EffectivePermissions.cs`, `AuthReadService.cs`; `INF/Identity/PermissionService.cs`, `PermissionCachePayload.cs`; DI các project; `INF/QuanLyBenhVien.Infrastructure.csproj`.

**Test:** port `IT/Authorization/PermissionServiceTests.cs`; tạo `IT/Authorization/PermissionCacheInvalidationTests.cs`.

**Giao diện:** `UserAccessDto(bool IsActive,bool MustChangePassword,IReadOnlySet<string> Permissions)`; `IUserAccessReadService.GetAsync(Guid userId,CancellationToken ct): Task<UserAccessDto?>`; `IPermissionService.GetAsync(Guid userId,CancellationToken ct): Task<UserAccessDto?>`. User không tồn tại trả null; inactive có quyền rỗng. Infrastructure PermissionService đọc DB chỉ qua port, không tham chiếu Persistence.

- [ ] Test union không trùng, revoke grant không gỡ quyền do role, inactive không có quyền; cache miss/Redis lỗi xuống DB; không cache user không tồn tại.
- [ ] Test stale writer: đọc generation trước DB, quyền bị thay và invalidation chạy xen, writer cũ không ghi được; request tiếp theo thấy quyền mới. Không chỉ assert DEL key.
- [ ] Chạy `dotnet test tests/QuanLyBenhVien.IntegrationTests --filter "FullyQualifiedName~PermissionService|FullyQualifiedName~PermissionCacheInvalidation"`, ghi nhận fail trước port.
- [ ] Chuyển DTO chung sang Application; giữ EffectivePermissions là một nơi tính quyền DB. `/me` trả cùng nguồn quyền, roles lấy từ DB, không nhét quyền vào access token.
- [ ] Port PermissionService dùng Redis cache không TTL theo spec, generation guard, cache per-request; schema payload có IsActive và MustChangePassword. Payload hỏng được coi cache miss có log an toàn, không cho phép tất cả quyền.
- [ ] Bật đúng Compile Remove, đăng ký DI, chạy lại tests và build. Khi DB lỗi không cho fallback sang quyền cũ vô thời hạn.

## Task 2 — Policy BE, denial audit và bắt đổi mật khẩu

**File tạo ở API/Security:** `PermissionRequirement.cs`, `PermissionPolicyProvider.cs`, `PermissionAuthorizationHandler.cs`, `ProblemAuthorizationResultHandler.cs`.

**File tạo ở PRES/Auth:** `PermissionEndpointExtensions.cs`. **File sửa:** `API/Security/JwtAuthenticationSetup.cs`, `PasswordChangeGateMiddleware.cs`, DI/API composition.

**Test:** port `IT/Authorization/AuthorizationPipelineTests.cs`, tạo `UT/API/Security/PermissionPolicyTests.cs`.

**Giao diện:** route khai `.RequirePermission(Permissions.Users.Read)` → policy `perm:<code>`. Extension ở Presentation chỉ tạo metadata/policy name; không gọi service Infrastructure. Handler ở API gọi IPermissionService. Denial dùng IAuditWriter + IUnitOfWork trước trả 403, không log toàn request body.

- [ ] Test deny-by-default; thiếu login → 401, không có permission → 403 `forbidden`; cờ bắt đổi → 403 `password_change_required` dù có role admin. Route AllowAnonymous/internal theo cơ chế riêng không bị policy mới chặn nhầm.
- [ ] Test denial audit có actor/permission/path, không có password/token; ghi audit lỗi không cho request được thực thi. Không áp chính sách tài nguyên giả cho module chưa có.
- [ ] Chạy AuthorizationPipeline/PermissionPolicy, ghi nhận fail. Dùng endpoint test-only để chứng minh policy trước khi users/roles production endpoints được tạo ở plan 05/06.
- [ ] Implement policy và chuyển gate plan 01 dùng PermissionService chung, không giữ hai cách tính cờ khác nhau. Giữ thứ tự authentication → gate/policy → endpoint.
- [ ] Chạy lại tests và kiểm endpoint không có permission metadata vẫn yêu cầu đăng nhập theo fallback, không vô tình thành public.

## Task 3 — Route registry, picker và sidebar

**File tạo:** `FE/src/feature/Workspace/routeAccess.js`, `FE/src/feature/Auth/PermissionRoute.js`.

**File sửa:** `FE/src/index.js`; `FE/src/feature/Auth/Login.js`, `PrivateRoute.js`, `redux/actions.js`; `Workspace/workspaces.js`, `Container.js`, `NoAccessContainer.js`, `component/WorkspacePicker.js`, `component/NoAccess.js`, `redux/action.js`, `redux/reducer.js`; `Shell/Container.js`, `component/AppLayout.js`.

**Test tạo/sửa:** `FE/src/feature/Workspace/__tests__/navigation.test.js`, `workspace.test.js`; `FE/src/feature/Auth/__tests__/PermissionRoute.test.js`; `FE/src/__tests__/router.test.js`.

**Giao diện FE đề xuất:** registry entries `{path,workspaceId,permission,availability}`; `getAvailableWorkspaces(permissions,availability)`; `getDefaultRoute(workspaceId,permissions,availability)`; `canAccessRoute(path,permissions,availability)`. Availability là trạng thái phát hành tính năng, không thay quyền. Các hàm này là nguồn chung cho sidebar và route guard.

- [ ] Test render ReactDOM/act với router thật: 0/1/n khu vực; user chỉ roles.read được default `/admin/roles`; chọn card/dropdown thật sự đổi URL/nội dung; no-access recheck thành công thoát khỏi màn cũ.
- [ ] Test mỗi credential login clear selectedId cũ kể cả cùng user; multi-workspace phải chọn mới; refresh giữ lựa chọn hợp lệ trong RAM, F5 mất RAM thì tính lại.
- [ ] Test URL trực tiếp không đủ quyền không mount màn gọi API; return URL chỉ nội bộ và có quyền, không bỏ qua bắt đổi/chọn khu vực; không loop giữa `/start` và `/no-access`.
- [ ] Test loadMe mới thu hồi workspace hiện tại → chọn lại/no-access; sidebar không hiện menu của khu vực khác dù user có quyền ở nhiều khu vực.
- [ ] Chạy Jest `navigation|workspace|PermissionRoute|router`, sửa rồi chạy lại. Không dispatch thay workspace trong render của WorkspacePicker; dùng lifecycle/event phù hợp React 16.
- [ ] Với admin screen chưa có endpoint thật ở mốc 04: registry phân biệt quyền và availability; hiện thông báo khu vực đang hoàn thiện, không báo user thiếu quyền và không gọi mock. Plan 05/06 sẽ bật từng màn sau nghiệm thu API. Route registry vẫn được test với availability đã bật trong fixture.
- [ ] Các khu vực khám/tiếp nhận/sinh hiệu/CLS/dược/nội trú chỉ được bật thật khi spec quyền và endpoint/phân công của module đã có; không cho API identity seed quyền dự kiến để phục vụ demo.

## Task 4 — Nghiệm thu cả FE và BE

- [x] Bật các test authorization đã port trong IT csproj; không bật test admin còn thiếu endpoint để rồi xóa assertions.
- [x] Full + Frontend + build FE (PASS). Kiểm code mới không dựa hard-code tên role để cho quyền (grep `git diff ce19d96..HEAD`: không có).
- [ ] Trình duyệt: đổi bắt buộc, không quyền, nhiều khu vực bằng fixture hợp lệ; thử URL trực tiếp, đổi khu vực, đổi quyền rồi Kiểm tra lại. Fixture/test-only không trở thành seed quyền nghiệp vụ production.
- [ ] Review branch/commit theo plan.md. Sau 04, quản trị vẫn cần 05/06; không báo mọi role đã có đầy đủ màn nghiệp vụ.

**Trạng thái:** Task 4 xong phần tự động (Full 166 unit + 144 IT pass/8 skip, Frontend 130 pass, build FE pass). Kiểm tay trình duyệt: NOT_RUN. Review branch/commit: chưa làm (chờ người dùng).
