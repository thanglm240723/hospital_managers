# Spec review thiết kế code hiện tại — HMS

Ngày bắt đầu rà soát: **2026-10-03**; hoàn tất tài liệu: **2026-10-04** (Asia/Saigon). Trạng thái: **tài liệu review hiện trạng, chờ người dùng xem xét các khuyến nghị**.

Repo: `D:/hospital_management`. Branch: `feat/login-khung-moi`. HEAD tại lúc rà soát: `1e4141d87dda76d54e77613320bbfd4306c190fe`.

## 1. Kết luận thiết kế

Thiết kế hiện tại phù hợp một **monolith chia module với Clean Architecture và CQRS**, PostgreSQL giữ dữ liệu nghiệp vụ, Redis tăng tốc kiểm tra phiên/quyền. Các kỹ thuật quan trọng đã có code thực: khóa hàng theo thứ tự, refresh rotation strict reuse, `SecurityVersion`, optimistic concurrency `xmin`, advisory lock bảo vệ admin cuối, invalidation bền vững với lease và generation, frontend điều phối refresh nhiều tab.

Điểm mạnh lớn nhất là kiểm soát tính nguyên tử và cạnh tranh ở DB thay vì chỉ dùng khóa trong RAM. Điểm yếu chính là **bảo đảm phụ thuộc phối hợp nhiều tầng**: handler phải đặt đúng commit/audit/invalidation, Gateway phải là cửa công khai duy nhất, frontend phải loại bỏ response cũ, và worker phải xử lý cache chờ. Test xanh cho các ca hiện có không chứng minh các bảo đảm này đúng ở mọi tình huống lỗi.

Chưa có luồng khám chữa bệnh hoàn chỉnh để review như một sản phẩm đã triển khai. PatientRegistry, ReceptionQueue, Clinical, Inpatient, Billing và Documents chưa có use case/API tương ứng trong backend. Giao diện giả lập hoặc spec cấp số khám không được xem là code nghiệp vụ đã chạy.

## 2. Cách đọc bộ spec

| Tài liệu | Nội dung |
|---|---|
| [01 — Auth, phiên và Gateway](01-auth-session-gateway.md) | Đăng nhập, refresh/reuse, logout, logout-all, đổi mật khẩu, `/me`, internal validation, cookie/CSRF, JWT, rate limit |
| [02 — Quản trị và dữ liệu](02-quan-tri-va-du-lieu.md) | Từng use case Users, Roles, Permissions, StaffProfiles, Facilities; khóa, RowVersion, constraint, audit và cache |
| [03 — Kỹ thuật xuyên suốt](03-ky-thuat-xuyen-suot.md) | Pipeline, CQRS, transaction, cache generation/worker, hai lớp quyền, audit, migration/seeder, log/health |
| [04 — Frontend](04-frontend.md) | State auth và bootstrap, nhiều tab, interceptor, guard/Can, quản trị thật, response cũ và các màn giả lập |

Mỗi chương mô tả đường đi dữ liệu, điều kiện, lỗi, lý do thiết kế, điểm mạnh và điểm yếu. **Lý do suy từ code** là đánh giá kỹ thuật của review; chỉ những quyết định được nguồn ghi rõ mới được xem là ý định đã chốt của tác giả. Bộ tài liệu này không phê duyệt thay đổi nghiệp vụ, không thay thế BRD/TSD hoặc spec đã duyệt, không phải plan triển khai.

Đường dẫn nguồn và dòng dẫn chiếu áp dụng tại mốc trên; tên kỹ thuật được giữ nguyên. Các tham chiếu DOCX bằng §/NV/AT là điều khoản tài liệu, không phải vị trí dòng code.

Quy ước rút gọn đường dẫn: `Application/...`, `Domain/...`, `Persistence/...`, `Infrastructure/...`, `Presentation/...`, `API/...` lần lượt nằm dưới `benh_vien_be/src/QuanLyBenhVien.<layer>/`; `feature/...` nằm dưới `benh_vien_fe/src/`. Khi chỉ ghi tên file tiếp sau một đường dẫn đầy đủ, file thuộc cùng nhóm vừa dẫn. Các bản thân file `.csproj` dùng tên project đầy đủ tương ứng.

## 3. Phạm vi và trạng thái thực tế

### 3.1 Luồng backend đã có

| Nhóm | Use case đã truy vết | Chương |
|---|---|---|
| Auth | `Login`, `RefreshSession`, `Logout`, `LogoutAll`, `ChangePassword`, `GetMe`, `ValidateSession` | 01 |
| Users — đọc/tạo/trạng thái | `ListUsers`, `GetUser`, `CreateUser`, `ActivateUser`, `DeactivateUser` | 02 |
| Users — quyền | `SetUserRoles`, `GrantUserPermission`, `RevokeUserPermission` | 02 |
| Roles | `ListRoles`, `CreateRole`, `RenameRole`, `SetRolePermissions` | 02 |
| Permissions | `ListPermissions` | 02 |
| StaffProfiles | `GetStaffProfile`, `UpsertStaffProfile`, `SetStaffWorkScopes` | 02 |
| Facilities | `GetFacilityTree`, `CreateBranch`, `UpdateBranch`, `CreateDepartment`, `UpdateDepartment`, `CreateRoom`, `UpdateRoom` | 02 |
| Hạ tầng request | Gateway/API authentication, endpoint permission, password gate, validation, Problem Details | 01/03 |
| Hạ tầng dữ liệu | EF mappings/migrations, constraint, audit interceptor, invalidation worker, seeder | 02/03 |
| Nền quyền lớp 2 | AccessContext, ResourceAuthorizer dispatcher, DeniedAccessRecorder, marker architecture tests | 03 |

### 3.2 Luồng frontend

Auth/đổi mật khẩu/đăng xuất, chọn khu vực theo permissions/availability, refresh trong tab và nhiều tab, admin người dùng/vai trò/quyền, hồ sơ nhân sự và cơ sở có nối API. Các màn Reception, Clinic, Vitals, bảng gọi số và tiện ích hàng chờ còn ở mức giả lập hoặc chưa bật availability; chi tiết ranh giới trong chương 04.

### 3.3 Chưa được xem là triển khai

- Bệnh nhân, tiếp nhận/cấp số/gọi lượt qua PostgreSQL, phân công điều trị, bệnh án, CLS, đơn thuốc, giường, tiền và tệp.
- Policy tài nguyên nghiệp vụ lớp 2, CareTeamAssignment, AccessGrant và kiểm soát lịch sử đã xác nhận.
- Outbox nghiệp vụ tổng quát, idempotency cho cấp số/thu tiền/gán giường/cấp phát/xác nhận kết quả.
- `IAuditedRequest`/`AuditBehavior` để audit bền vững trước trả dữ liệu lâm sàng/tệp.
- Readiness PostgreSQL trong `/health` của API.

Không có Appointment. Route cũ hoặc màn giả lập không phải căn cứ để xây chức năng đặt lịch.

## 4. Nguồn sự thật và mâu thuẫn

### 4.1 Nguồn đã đối chiếu

| Nguồn | Điều khoản dùng trong review | Vai trò |
|---|---|---|
| `Dac_ta_nghiep_vu_v2.0.docx` | NV-01..05, AT-09/10/17, OPEN-04, CFG-05 | Chuẩn nghiệp vụ tổ chức, quyền, phân công và audit |
| `Dac_ta_ky_thuat_v3.1.docx` | §1, §3, §4, §11, §12..14 | Chuẩn kiến trúc, phiên, transaction, audit, API và kiểm thử |
| `AGENTS.md` | Quy tắc kiến trúc, dữ liệu, quyền, thời gian, review/validation | Hướng dẫn repo |
| `docs/superpowers/plans/2026-09-23-auth-permission-redesign/spec.md` | §0.1 và các luồng auth/cache | Thiết kế auth đã duyệt, có liệt kê chỗ lệch |
| `docs/superpowers/plans/2026-09-30-auth-va-luong-kham-v2/spec.md` | §2..5, logout/refresh/password/admin | Quyết định V2 ghi đã chốt; chi tiết kỹ thuật theo plan |
| `docs/superpowers/plans/2026-10-03-phan-quyen-hai-lop/spec.md` | §3, §6..10, §12 | Spec khung vẫn ghi **chờ người dùng review**; không tự nâng thành phê duyệt |
| `docs/superpowers/plans/2026-10-03-cap-so-kham/spec.md` | Header/phạm vi/phụ thuộc | Spec đã duyệt có sẵn, chưa phải code cấp số đã triển khai |
| `ARCHITECTURE.md`, `VALIDATION.md`, code, test | DI, middleware, handlers, repository, adapter, UI | Bằng chứng hiện trạng; code/test không sửa được quy tắc cấp cao hơn |

Đường dẫn `docs/...` ở bảng trên tính từ `benh_vien_be/`. Chỉ trích nội dung DOCX liên quan phạm vi; không đọc secret hoặc sửa tài liệu gốc.

### 4.2 Mâu thuẫn cần giữ rõ

| Điểm | Nguồn yêu cầu / mô tả | Code hoặc spec mới | Cách hiểu trong review |
|---|---|---|---|
| Ai kiểm phiên | TSD §4: kiểm family/SecurityVersion DB | Auth spec §0.1: Gateway đọc Redis, miss/lỗi hỏi API; API validate JWT lại | Lệch đã được auth spec liệt kê; cần deployment chặn API công khai |
| Quyền không TTL | TSD §3.2: không cache quyền TTL dài MVP | Auth spec: Redis không TTL + invalidation/worker | Lệch có chủ đích; độ trễ thu hồi là đánh đổi, cần xử lý tình huống sự cố |
| Rate limit theo tài khoản | TSD §4: theo IP và tài khoản chuẩn hóa | V2 §2.8 bỏ bộ đếm email; code giữ Gateway IP | V2 ghi đã chốt và §3 công khai mâu thuẫn; cần cập nhật DOCX, không coi khóa User thay thế chống brute force |
| Audit pipeline | TSD §3.3 và ARCHITECTURE.md nhắc contract/behavior | DI chỉ Logging + Validation; audit bằng handler/interceptor | Khoảng trống triển khai cho dữ liệu nhạy cảm tương lai |
| Trạng thái kiến trúc | ARCHITECTURE.md §9 ghi stub/migration rỗng/API cũ | Code có handler, Persistence/migrations và Carter | Tài liệu trạng thái đã lạc hậu; review lấy code làm bằng chứng triển khai, không đổi quyết định đích |
| Trạng thái kiểm thử | VALIDATION.md cuối file ghi chưa có kiểm tra khung mới | Phiên này chạy mới Full + Frontend, kết quả ở §8 | Không dùng bảng cũ để kết luận code hiện tại không build |
| PostgreSQL health | ARCHITECTURE.md §7 nói API health gồm DB | DI chỉ Redis; DB health file bị loại khỏi build | Khoảng trống vận hành xác nhận |
| Quyền tài nguyên | Spec khung mô tả ReBAC/ABAC đầy đủ | Code hiện chỉ nền, chưa policy lâm sàng | Không tuyên bố đã đáp ứng AT-09/10/17 cho bệnh án |
| OPEN-04/CFG-05 | BRD để quy tắc truy cập khẩn cấp/liên cơ sở cần duyệt | TSD nêu thời hạn mặc định, spec khung chưa bật emergency | **CẦN_XÁC_NHẬN**; review không tự chốt thời hạn/ngoại lệ/vai trò |

Spec cấp số khám đang có trong working tree được giữ nguyên. Các mã `queues.*`/`reception.register` của spec này khác danh mục `queue.*`/`encounters.register` trong spec khung hai lớp: đây là **mâu thuẫn tài liệu tương lai cần hợp nhất trước implement**, không phải lỗi route đã tồn tại ở backend hiện tại.

## 5. Vì sao các kỹ thuật chính được chọn

| Kỹ thuật | Vấn đề nó giải quyết | Điểm mạnh | Điểm yếu / giới hạn |
|---|---|---|---|
| Clean Architecture + vertical slice | Đổi adapter và điều hướng use case | Ranh giới rõ, test handler qua port | Nhiều file, cần kỷ luật chiều phụ thuộc |
| CQRS trong cùng DB | Ghi cần aggregate, đọc cần projection | Không bắt query load cả aggregate | Nhiều contract DTO/read service; không tự tạo eventual-consistency architecture |
| JWT ngắn + family + SecurityVersion | Vừa kiểm nhanh vừa có thu hồi phiên | Không gói permission thay đổi vào JWT | HS256 chung khóa; trạng thái sống phụ thuộc Gateway/cache |
| Refresh rotation strict reuse | Phát hiện token cũ bị dùng lại | Một chuỗi token, revoke nguyên tử | Mất response hoặc refresh race hợp lệ cũng buộc đăng nhập lại |
| Cookie HttpOnly + signed CSRF + Origin | Refresh cookie tự gửi theo browser | Secret refresh không đi qua JS, CSRF gắn phiên | Cookie flag/origin/proxy phải cấu hình đúng |
| Khóa User → family → token | Không dùng credential/session cũ khi cạnh tranh | Bảo vệ nhiều instance | Tranh chấp/timeout; mọi luồng phải giữ cùng thứ tự |
| Advisory lock admin-safety | Hai admin đồng thời khóa/bỏ admin của nhau | Giữ bất biến admin cuối ở DB | Khóa logic toàn nhóm, không thay validation thường |
| `xmin` + If-Match/RowVersion | Tránh ghi đè quyết định dựa bản cũ | PostgreSQL tự quản version | FE phải reload, action trạng thái không dùng version có ngữ nghĩa riêng |
| Unique/FK tại DB | Chặn duplicate/dangling dữ liệu khi race | Hàng rào cuối đáng tin | Phải map đúng constraint; precheck không đủ |
| Permission cache + generation | Giảm join, ngăn refill dữ liệu cũ sau eviction | Cache writer không ghi qua invalidation đã xảy ra | Không bảo đảm thu hồi ngay sau commit; không TTL phụ thuộc worker |
| Claim/lease/SKIP LOCKED | Worker chết/đa instance | Công việc bền vững, phát lại được | At-least-once; backlog/retry/monitor chưa đầy đủ |
| Audit riêng cho denied | Không mất nhật ký khi business rollback | Context độc lập giữ bằng chứng từ chối | Thêm kết nối/ghi DB, cần timeout riêng |
| Web Locks/BroadcastChannel + epoch | Refresh đa tab và response auth cũ | Giảm strict reuse do browser cạnh tranh | Không phải mọi browser hỗ trợ; feature state phải dùng cùng nguyên tắc |
| Availability tách permission | Permission không chứng minh màn/API đã sẵn sàng | Chặn mở nhầm chức năng chưa triển khai | Danh mục FE/BE cần cập nhật đồng bộ |

Phân tích cụ thể theo từng luồng ở chương 01..04; bảng này không thay phần chi tiết.

## 6. Phát hiện review và ưu tiên

Phân loại: **xác nhận** = thấy trực tiếp trong code/DI; **rủi ro suy luận** = có chuỗi sự kiện hợp lý nhưng chưa có bài test tái hiện mới; **khoảng trống** = phần được yêu cầu cho module tương lai chưa triển khai. P1 ưu tiên cao, P2 ưu tiên tiếp theo, P3 bảo trì.

| ID | Mức / loại | Phát hiện | Tác động | Bằng chứng và hướng xử lý |
|---|---|---|---|---|
| R-01 | P1, xác nhận | `/health` API không kiểm PostgreSQL | Redis khỏe nhưng DB lỗi vẫn có thể báo khỏe | `Infrastructure/DependencyInjection.cs:59`; `Infrastructure.csproj:24`. Thêm DB readiness đúng layer, test DB down |
| R-02 | P1, rủi ro suy luận và giới hạn đã ghi trong spec V2 | Cache thu hồi chỉ xóa sau commit; cache hit không kiểm pending invalidation | Cửa sổ quyền/phiên cũ khi crash, Redis phục hồi hoặc backlog | `PermissionService.cs:20`; `CacheInvalidator.cs:27`; chương 03 §5. Chốt bảo đảm thu hồi rồi test fault injection |
| R-03 | P1 trước module PHI, khoảng trống | Chưa `IAuditedRequest`/`AuditBehavior` | Không có bảo đảm audit bền vững trước trả dữ liệu nhạy cảm | `Application/DependencyInjection.cs:18`; chương 03 §7. Hoàn tất trước bệnh án/tệp |
| R-04 | P2, xác nhận phạm vi clock | AuditLog/domain event/User fallback dùng thời gian trực tiếp | Timestamp không cùng TimeProvider; test thời gian không tất định toàn bộ | `Domain/Common/Auditing/AuditLog.cs:25`; `Domain/Common/IDomainEvent.cs:10`; `Domain/Identity/User.cs:51,149` |
| R-05 | P2, rủi ro suy luận | Khóa seeder chỉ ở applier, chưa bao bootstrap catalog/roles/admin | Hai instance bootstrap DB mới có thể tranh insert và lỗi startup | `Persistence/Seed/IdentitySeeder.cs:33`; `RoleDefaultsApplier.cs:17`. Test toàn SeedAsync đồng thời |
| R-06 | P2, xác nhận giới hạn | Architecture marker chỉ quét danh sách namespace, không kiểm authorizer/filter runtime | Module mới có thể thiếu kiểm tài nguyên dù test marker xanh | `ScopedRequestRules.cs:9,36`; chương 03 §6. Thêm kiểm theo module và integration list/detail/export |
| R-07 | P2, xác nhận giới hạn | Worker chưa backoff theo dòng/metric tuổi backlog | Lỗi dài làm chậm thu hồi phía sau; ping Redis không đo delivery | `CacheInvalidationWorker.cs:14`; `CacheInvalidationStore.cs:29`. Theo dõi oldest age/attempts và test backlog |
| R-08 | P3, xác nhận | ARCHITECTURE/VALIDATION còn trạng thái cũ | Người bảo trì chọn sai điểm bắt đầu hoặc hiểu sai bảo đảm | Chương 03, mục trạng thái tài liệu. Cập nhật ở tác vụ riêng, giữ lịch sử kết quả |
| R-09 | P1, rủi ro suy luận từ thiếu hàng rào phiên | Các thunk feature không kiểm epoch khi response về sau logout | Reset Redux xong vẫn có thể nhận lại danh sách/quyền từ phiên cũ, kể cả khi tài khoản khác đăng nhập | `benh_vien_fe/src/reducer.js:28`; `feature/Admin/redux/action.js:15`; chương 04. Thêm test response resolve sau chuyển phiên |
| R-10 | P1, rủi ro suy luận từ cookie dùng chung | Tab cũ logout vẫn gửi raw request dù shared generation đã thuộc login mới | Có thể thu hồi và clear cookie của phiên mới do tab khác vừa đăng nhập | `feature/Auth/redux/actions.js:61`; `feature/Auth/session/sessionLifecycle.js:68`; `feature/Auth/api/authClient.js:24`; chương 04. Kiểm generation trước gửi và điều phối thao tác cookie |
| R-11 | P1, rủi ro suy luận | Interceptor retry 401 không gắn request với epoch lúc gửi đầu tiên | Response 401 của tài khoản cũ có thể làm replay request/command dưới token tài khoản mới | `benh_vien_fe/src/service/http.js:34`; chương 04. Bổ sung test đổi tài khoản trước 401 và cấm replay xuyên phiên |
| R-12 | P2, xác nhận mâu thuẫn contract | UI Roles cấm sửa mọi system role bằng `!role.isSystem`, trong khi V2/API cho đổi Name/permissions với bảo vệ Admin core | Người có `roles.manage` không thực hiện được chức năng backend hỗ trợ | `feature/Roles/component/RoleDetail.js:59`; V2 §3; chương 04 F04. Sửa UI/test theo spec, vẫn giữ bất biến mã vai trò/quyền lõi |
| R-13 | P2, rủi ro suy luận | Backdrop dialog tạo user vẫn đóng được khi POST đang chạy | User có thể tạo thành công nhưng mất response mật khẩu ban đầu chỉ trả một lần; reset password chưa hỗ trợ | `feature/Admin/component/CreateUserDialog.js:102`; chương 04 F05. Test deferred POST và vòng đời nhận kết quả |
| R-14 | P2, xác nhận thiếu xử lý lỗi tại adapter | Gateway SessionValidator deserialize JSON cache/nội bộ nhưng không bắt lỗi JSON | Payload hỏng không đi nhánh fallback/Unavailable đã định nghĩa; HTTP cuối cần kiểm chứng riêng | `Gateway/Auth/SessionValidator.cs:41,64`; chương 01 §11. Test malformed cache và malformed internal body |

Các phát hiện riêng về auth/Gateway, quản trị và frontend được ghi trong chương tương ứng. Không gọi mọi tradeoff là bug: strict reuse, quyền lẻ chỉ cấp thêm, lớp 2 chưa có module sử dụng và trạng thái active cha/con cần được đánh giá theo quyết định nghiệp vụ.

## 7. Tiêu chí review/kiểm thử tiếp theo

| Nhóm | Ca cần chứng minh | Mục tiêu |
|---|---|---|
| Thu hồi cache | Crash sau DB commit; Redis phục hồi còn key cũ; worker backlog; request mới trước/sau drain | Đo đúng cửa sổ thu hồi và hành vi đã chốt |
| Worker lease | Chủ cũ pause quá lease, chủ mới reclaim/ack; timeout Redis; lỗi kéo dài | Không ack nhầm claim; công việc không mất; có cảnh báo hữu ích |
| Seeder | Hai host chạy toàn `SeedAsync` trên DB mới | Không trùng quyền/role/admin, không lỗi startup do precheck race |
| Auth nhiều tab | Mất response refresh, login mới khi logout cũ chưa gửi, tab wake-up, thiếu Web Locks | Không revoke nhầm phiên mới; UX đăng nhập lại theo strict reuse |
| Frontend feature | Request list/detail/scopes bắt đầu trước logout/login khác, resolve sau reset | Không đưa dữ liệu phiên cũ trở lại Redux/UI |
| Lớp 2 | Cùng khoa không phân công; khác cơ sở; thu hồi phân công; grant hết hạn; danh sách COUNT | AT-09/10/17 theo từng module, kiểm quyền trước pagination |
| Audit đọc | Audit write fail trước trả DTO/stream nhạy cảm | Không phát dữ liệu khi chưa có audit bền vững |
| Health | DB down Redis up; Redis down DB up; worker backlog | Liveness/readiness phản ánh phụ thuộc bắt buộc |
| Admin concurrency | Admin cuối, role-member race, request If-Match cũ | Bất biến admin, tập quyền và 412 nhất quán |

Các ca trên là tiêu chí bổ sung/kiểm tay, **không tuyên bố đã tái hiện trong phiên này** nếu không có test output cụ thể.

## 8. Kiểm chứng đã chạy trong phiên review

Chạy từ gốc repo qua script chuẩn, không sửa source/test/assertion và không migrate DB dùng chung.

| Lệnh / bước | Kết quả mới | Bằng chứng |
|---|---|---|
| `tooling/validate.ps1 -Mode Full` — build | **PASS**, 0 error, 6 warning có sẵn trong test | `.claude/work/logs/validate-20261003-233844-build.log` |
| Full — unit | **PASS**, 230/230, 0 skipped | `.claude/work/logs/validate-20261003-233844-unit-tests.log` |
| Full — integration (Testcontainers) | **PASS**, 224 passed, **8 skipped**, 232 total | `.claude/work/logs/validate-20261003-233844-integration-tests.log` |
| `tooling/validate.ps1 -Mode Frontend` — Jest | **PASS**, 225/225, 36 suite | `.claude/work/logs/validate-20261003-233844-frontend-tests.log` |
| Frontend — ESLint | **PASS**, exit 0 | `.claude/work/logs/validate-20261003-233844-frontend-lint.log` |
| Trình duyệt nhiều tab, response mất, fault injection bổ sung | **NOT_RUN** | Không có bằng chứng kiểm tay trong phiên này |
| Production/deployment/secret/network policy | **NOT_RUN** | Ngoài phạm vi review code local |

Hai script trả exit code 0. Build/test là bằng chứng code được kiểm hiện tại, **không có nghĩa tất cả phát hiện review đã được sửa**.

Kiểm tra tài liệu mới: 5 file, 113 liên kết có file đích/số dòng hợp lệ, code fence cân bằng. Working tree cuối chỉ thêm thư mục review bên cạnh thư mục spec cấp số khám có sẵn; không có source/config/test/migration bị sửa.

8 integration test còn skip: 4 ca quản lý sessions ở `Auth/MeAndSessionsTests.cs`; 2 ca ở `Gateway/GatewayAuthenticationTests.cs`; 2 ca DB health ở `Gateway/GatewayRateLimitAndHealthTests.cs`. Lý do skip trong code còn nhắc slice cũ; cần rà lại trước nghiệm thu tương ứng, không suy 8 ca đã pass.

Ngoài skip, project test còn loại một số file khỏi compile: integration `Caching/RedisServicesTests.cs`, `Health/HealthEndpointTests.cs`; unit `Application/Behaviors/AuditBehaviorTests.cs` và DI tests cũ. Kết quả PASS chỉ tính các test đang được build/chạy, không gồm các file bị loại (chi tiết chương 01 §12).

## 9. Thay đổi của tác vụ này

Chỉ tạo bộ tài liệu mới trong `benh_vien_be/docs/reviews/2026-10-03-thiet-ke-code-hien-tai/`. Không sửa source, cấu hình chạy, test, migration hoặc spec có sẵn. Không commit/push. Thư mục `docs/superpowers/plans/2026-10-03-cap-so-kham/` là thay đổi có sẵn của người dùng và được giữ nguyên.

Bước hữu ích tiếp theo: chọn các phát hiện cần xử lý, chốt tiêu chí thu hồi/health/state phiên, rồi lập plan nhỏ theo từng nhóm. Không dùng tài liệu review này để tự triển khai phần nghiệp vụ còn OPEN-xx.
