# 04 — Thiết kế và luồng frontend hiện tại

Phạm vi: `benh_vien_fe`, đối chiếu các endpoint/DTO backend mà frontend gọi. Đây là mô tả **code đang có**, không phải xác nhận nghiệm thu hoặc đề xuất tự động triển khai. Rà soát tĩnh ngày 2026-10-03/04; không thay source, không đọc cấu hình bí mật. Các đường dẫn `src/...` trong chương này tương đối với `benh_vien_fe/`, trừ chỗ ghi rõ `benh_vien_be/`.

Nguồn đối chiếu: `AGENTS.md`; auth spec đã duyệt `2026-09-23-auth-permission-redesign/spec.md` §5; spec `2026-09-30-auth-va-luong-kham-v2/spec.md` §2–6. Spec khung `2026-10-03-phan-quyen-hai-lop/spec.md` tự ghi **chờ review**, nên chỉ dùng để chỉ ra hướng cần chốt, không xem là quyết định đã duyệt. Mã NV trích từ spec đã đọc: NV-03/NV-04 phân quyền/phân công; NV-08–NV-13 tiếp nhận/hàng chờ/khám; NV-17–NV-20 kết quả/bệnh án. Không tự gán mã AT khi chưa đối chiếu nguyên văn BRD.

## 1. Kiến trúc, vùng dữ liệu và mức hoàn thiện

Luồng nền: React 16 → `ConnectedRouter` → route guard → container → Redux thunk/API client → `service/http` → Gateway → API. Store thực tế chỉ gắn `routerMiddleware` và `thunk`; `redux-observable` có trong dependency nhưng không tham gia luồng hiện tại (`src/index.js:46`). `redux-form` còn một slice, nhưng các màn đã đọc dùng state cục bộ và input kiểm soát (`src/reducer.js:15`).

| Vùng | Dữ liệu giữ | Vòng đời |
|---|---|---|
| `tokenStore` | Access token, thời điểm hết hạn | Biến module trong RAM; F5 mất; logout xóa |
| `auth` | User DTO, permissions, mustChangePassword, status, thông báo phiên | `/me` cập nhật; logout reset |
| `workspace` | Khu vực đang chọn | RAM; login mới clear; F5 tính lại |
| `admin`, `roles`, `facilities` | Danh sách/chi tiết, catalog, loading/error | Giữ qua điều hướng; logout reset ngay |
| `reception`, `clinic`, `vitals` | Kết quả tìm, lượt/phiếu khám/sinh hiệu giả | Một số màn clear khi unmount; logout reset |
| State component | Mật khẩu form, initialPassword, draft chỉnh sửa, dialog | Mất khi component unmount |
| `localStorage` | Chỉ `auth:refreshGen`, `auth:sessionGen` | Metadata điều phối không bí mật; không token/PHI |

Không thấy lưu token/PHI vào `localStorage`/`sessionStorage`, hoặc dùng `dangerouslySetInnerHTML` trong source đã tìm. Service worker được unregister (`src/index.js:101`). Tuy nhiên reset Redux **không hủy request đang bay**; dữ liệu phản hồi đến sau reset có thể được ghi lại (§9).

| Màn | Kết nối hiện tại | Có được mount bởi router bình thường? |
|---|---|---|
| Login, đổi mật khẩu | API thật | Có |
| `/admin/users` + staff profile | API thật, không mock | Có khi mock tắt và có quyền |
| `/admin/roles` | API thật hoặc rolesMock theo cờ | Có khi có quyền |
| `/admin/facilities` | API thật, không mock | Có khi mock tắt và có quyền |
| Reception, Clinic, Vitals | Client dự kiến + mock | Không: `availability=false` |
| `/display/queue` | Luôn `displayMock`, kể cả production/mock tắt | Có: chỉ `PrivateRoute`, không availability |

Bằng chứng: `src/feature/Workspace/availability.js:6`, `src/index.js:75`, các `api/index.js` chọn implementation khi module được import. Cờ `REACT_APP_USE_MOCK_API` chỉ bật khi chuỗi đúng `'true'` (`src/service/mockMode.js:4`); cờ không tự chứng minh endpoint thật tồn tại. README FE đầu file vẫn gọi Tài khoản là mock và hướng dẫn bật mock để xem nhiều màn; thực tế Users/Facilities sẽ bị tắt và lâm sàng vẫn pending. Đây là tài liệu cũ, không nên dùng làm bản đồ triển khai.

**Lý do thiết kế (suy luận):** duy trì một khung shell/permission chung, phát hành dần từng feature; tách token khỏi Redux để tránh persist/debug state chứa credential. **Ưu điểm:** ít tầng, thunk và client rõ ràng, availability ngăn mount màn chưa có API. **Hạn chế:** mã mock lâm sàng khá đầy đủ về hình thức nên dễ bị hiểu nhầm là nghiệp vụ đã hoạt động; Display là ngoại lệ còn lọt ra route thực tế.

## 2. Khởi động và đăng nhập

`src/index.js:54` thiết lập callback hết phiên/đổi mật khẩu; remote logout reset store; bật BroadcastChannel và scheduler; sau đó dispatch `bootAuth`. Store bắt đầu `status='booting'`. `PrivateRoute` trả null trong thời gian boot nên chưa mount màn bảo vệ và chưa gọi API feature (`src/feature/Auth/PrivateRoute.js:11`). Loading toàn cục không được nối vào boot.

F5/boot:

1. `beginSessionTransition` tăng epoch, cho phép nhận token cùng shared generation.
2. `AUTH_BOOTING` đưa auth về trạng thái đầu.
3. Cookie refresh được gửi tới `POST v1/auth/refresh` qua raw client.
4. Thành công đặt token RAM; `GET v1/auth/me` qua client có Bearer.
5. `/me` chỉ dispatch `AUTH_AUTHENTICATED` nếu epoch vẫn đúng; reducer tách permissions/mustChangePassword và user (`src/feature/Auth/redux/actions.js:17`, `src/feature/Auth/redux/reducer.js:18`).
6. Mọi lỗi trong boot đều clear token và chuyển anonymous, kể cả lỗi mạng/503; không phân biệt 401 và lỗi tạm thời (`actions.js:31`).

Login:

1. Form giữ email/password trong state cục bộ; HTML kiểm tra email/required.
2. `login` bắt đầu epoch mới, clear workspace rồi raw `POST v1/auth/login {email,password}`.
3. Response đến muộn sau một transition khác trả null; không áp dụng token.
4. Response còn hiệu lực mở shared generation mới, đặt access token, rồi `/me`.
5. Form redirect tới `/change-password` nếu bắt đổi; còn lại `/start`, có thể chuyển return URL cho bước chọn khu vực.

UI có `submitting`, nút disabled, lỗi Problem Details và xóa password khi thất bại. Với 429 đọc `Retry-After` dạng số giây, hẹn timer mở nút và hiển thị thời điểm thử lại (`src/feature/Auth/Login.js:26`). Không có nhánh riêng tiết lộ email tồn tại hay bị khóa. `problemTitle` chọn server title hoặc lỗi kết nối tiếng Việt (`src/feature/Auth/problem.js:2`).

**Lý do (suy luận):** cookie là nguồn khôi phục phiên; `/me` là nguồn quyền, không suy quyền từ JWT/role. **Ưu điểm:** boot/auth response được chặn bằng epoch; không đưa token vào Redux; login mới không kế thừa khu vực trước. **Hạn chế:** login POST thành công nhưng `/me` lỗi để lại token RAM/scheduler trong khi form báo đăng nhập lỗi; form chỉ vô hiệu nút, handler không kiểm tra `submitting` như ChangePassword. Boot có thể để màn trống khi mạng treo vì không có timeout/loading dành cho boot. Các lỗi mạng lúc boot bị biểu diễn thành anonymous.

## 3. HTTP, CSRF, refresh và nhiều tab

`src/service/http.js:18` tạo Axios với `baseURL=process.env.API_URL`, `withCredentials=true`. Các client gọi URL tương đối `v1/...`; để tới `/api/v1/...`, `API_URL` phải có tiền tố `/api/` đúng deployment. `config/env.js:80` đưa biến này vào bundle; review không đọc `.env` nên **chưa xác minh giá trị thực tế**. Dev server có proxy `/api` tới Gateway (`config/webpackDevServer.config.js:85`), phục vụ yêu cầu cùng origin; browser tự gửi Origin cho POST/PUT.

Request interceptor đọc token RAM **tại thời điểm gửi**, thêm `Authorization: Bearer ...`; mọi method trừ GET/HEAD đọc `__Host-csrf` và thêm `X-CSRF-Token`. `csrf.js:4` đọc cookie JS; refresh cookie không được JS đọc. Raw client dành cho login/refresh/logout không có interceptor để refresh lỗi không đệ quy (`src/feature/Auth/api/authClient.js:4`).

Response interceptor:

1. Không có response/config: reject lỗi gốc.
2. 401 lần đầu: chờ coordinator refresh rồi gửi lại **cả request**, giữ body/headers, đặt `retriedAfterRefresh=true`; bỏ baseURL để tránh Axios 0.18 ghép URL hai lần.
3. Refresh thất bại: gọi expire callback và throw lỗi request ban đầu.
4. Request gửi lại vẫn 401: expire callback.
5. 403 `password_change_required`: điều hướng đổi mật khẩu; các 403/409/412 khác trả lỗi cho component.

Không retry timeout/5xx cho command. Riêng mọi command bị 401 đều replay một lần, dựa trên giả thiết 401 được trả trước nghiệp vụ. Giả thiết hợp lý với auth gateway, nhưng không phải bằng chứng cho mọi 401 trong handler tương lai; thêm yêu cầu giữ nguyên phiên trước replay (§9, F02).

Refresh chủ động (`refreshScheduler.js:15`): mỗi thay đổi token hủy timer cũ, tính `expiresAt - 60s - now` cộng jitter 0–4999ms, refresh một lần; lỗi nào cũng gọi expire callback. Token clear hủy lịch tiếp theo. Timer bị browser đình chỉ được bù bằng interceptor 401 khi request tiếp theo chạy.

Coordinator (`refreshCoordinator.js:143`):

1. Promise `inFlight` dùng chung trong tab.
2. Web Lock `auth-refresh` serialize các tab nếu hỗ trợ; không hỗ trợ thì chạy trực tiếp và chấp nhận rủi ro strict reuse theo spec.
3. Bên trong lock kiểm tra epoch + shared generation; phiên đã kết thúc/thay thế không được dùng cookie.
4. Nếu token mới/fresh đã tới từ tab khác, dùng lại token.
5. So sánh bộ đếm refresh shared với thế hệ token của tab; nếu chứng minh tab khác đã refresh nhưng token chưa nhận, gửi `need-token`, poll tối đa 1s. Timeout **không gọi lại refresh**.
6. Gọi raw refresh, kiểm lại phiên, tăng counter trước khi set token rồi broadcast token kèm `sessionGen`.
7. Token broadcast chỉ nhận cho phiên shared hiện tại; response hỏi token có requestId phải khớp. Logout broadcast chỉ xóa đúng thế hệ tab đã tham gia.

`sessionLifecycle.js:50` quản lý epoch local; `claimSharedSession` sinh ID generation ngẫu nhiên và ghi shared storage; `invalidateLocalSession` tăng epoch, từ chối token, clear RAM. Storage lỗi là best effort; fallback generation `'0'` yếu hơn chống lẫn phiên. Access token được truyền qua BroadcastChannel vào RAM cùng origin, không lưu persistent.

**Lý do (suy luận):** strict reuse thu hồi cả family khi token cũ được dùng lại, nên tránh refresh đồng thời; epoch/generation ngăn response/notification cũ hồi sinh phiên. **Ưu điểm:** single-flight, giới hạn replay, khóa đa tab, counter chống lệch thứ tự broadcast và lock, fail-safe khi không nhận token. **Hạn chế:** hỗ trợ browser/storage ảnh hưởng bảo đảm; timeout/mạng/429 refresh đều gây logout local và broadcast tới tab khác dù server chưa xác nhận hết phiên. `/me` không được tự tải lại sau refresh/broadcast nên UI có thể giữ permissions cũ; backend vẫn phải quyết định quyền.

## 4. Logout và đổi mật khẩu

Logout local (`actions.js:51`): lấy generation cũ → kết thúc shared generation nếu tab còn hiện hành → hủy pending need-token → invalidate epoch/token → dispatch `AUTH_LOGGED_OUT` → broadcast generation cũ → raw `POST v1/auth/logout`. Root reducer reset mọi feature/form/loading khi thấy `AUTH_LOGGED_OUT` (`src/reducer.js:29`). Server lỗi không retry, chỉ thêm thông báo “chưa xác nhận thu hồi phiên” nếu epoch chưa đổi. Expire session làm phần local/broadcast tương tự nhưng không gọi logout server.

Đổi mật khẩu (`ChangePassword.js:27`): chặn hai submit, kiểm xác nhận khớp, POST `{currentPassword,newPassword}`; HTML giới hạn 10–128 ký tự, server vẫn kiểm policy. Thunk giữ token mới chỉ nếu epoch còn đúng (`actions.js:83`). Không gộp `/me` vào mutation: POST thành công xóa mọi password trong form và đánh dấu `changed`; sau đó GET `/me`, điều hướng `/start`. Nếu `/me` lỗi, UI nói rõ mật khẩu đã đổi và chỉ retry GET. Backend giữ refresh cookie/family, trả `AccessTokenDto`; FE khớp contract này (`benh_vien_be/.../Auth/AuthEndpoints.cs:110`).

**Ưu điểm:** logout xóa local tức thời, không báo server thành công giả; change-password tách commit thành công và reload lỗi, tránh gửi POST lần hai do mất mạng ở GET. Cờ MustChangePassword áp ở route và 403 interceptor. **Hạn chế:** logout raw request dùng cookie shared hiện tại mà không gắn generation/lock (F03). `loadMeAfterChange` không kiểm kết quả null trước redirect, một response sau logout có thể điều hướng `/start` rồi guard đưa về login; auth Redux vẫn được epoch bảo vệ. Không có client/UI logout-all dù BE đã có endpoint; chưa có quản lý session trong FE.

## 5. Guard, workspace, menu và quyền hiển thị

`PrivateRoute` xử lý boot → anonymous/login → mustChange/change-password → render. `PermissionRoute` bọc PrivateRoute rồi `RouteGate`. Registry `routeAccess.js:8` là nguồn chung cho guard, menu, default và return URL. Quyết định: thiếu quyền → `/start`; chưa chọn khu vực/hết quyền khu vực → picker; mở URL khu vực khác có quyền → lifecycle đổi selectedId; availability false → `FeaturePending` thay vì mount component/API. Registry chưa có route được gọi trong guard mặc định render; mọi route bảo vệ mới phải đăng ký, nếu không sẽ chỉ cần đăng nhập (`PermissionRoute.js:17`).

`/start`: 0 khu vực → `/no-access`; 1 → chọn sẵn; nhiều → picker; nếu khu vực đang chọn bị thu hồi thì clear. Return URL chỉ nhận đường dẫn nội bộ trong registry, đúng khu vực, có permission; không cho `//...` (`routeAccess.js:63`). Shell tính menu từ khu vực đang chọn, switch qua router push, không suy quyền từ role name.

`getAvailableWorkspaces` tính một khu vực tồn tại nếu có bất kỳ route được phép, kèm ready; default ưu tiên route đã phát hành, nếu chưa có thì vào pending. Vì vậy khu vực chưa phát hành vẫn có thể hiện trong picker khi người dùng có quyền dự kiến. BE hiện chỉ công bố IdentityAccess/facilities/staff nên môi trường thật chưa có các quyền lâm sàng dự kiến. `/display/queue` nằm ngoài registry, chỉ PrivateRoute.

`hasPermission`/`Can` chỉ đối chiếu chuỗi permissions từ `/me`; không có admin bypass, không cấp quyền theo role name. No-access có nút GET `/me` “Kiểm tra lại”, redirect nếu nhận quyền; lỗi GET bị nuốt, chưa có thông báo thất bại (`Workspace/component/NoAccess.js:17`). Staff work scope không ở MeDto và FE không tự chứng minh quyền tài nguyên.

**Lý do (suy luận):** khu vực là nhóm nhiệm vụ, không thay thế permission; một registry giảm guard/sidebar lệch nhau. **Ưu điểm:** không mount feature thiếu quyền/chưa phát hành; không loop mặc định users khi chỉ có roles.read; lựa chọn chỉ RAM. **Hạn chế:** registry và `index.js` vẫn là hai danh sách route cần đồng bộ; route thiếu registry được render; permissions đổi không tự đồng bộ tới tab đang mở ngoài các lần `/me` tường minh. Không được coi Can hoặc work scope picker là hàng rào bảo mật.

## 6. Các luồng quản trị chạy API thật

### 6.1 Danh sách, chi tiết và tạo tài khoản

Mount Users tải danh sách trang 10, roles nếu `roles.read`, permission catalog nếu `permissions.read` (`Admin/Container.js:25`). Filter search/role/status đổi đưa về trang 1; đổi trang giữ filter. Client bỏ query rỗng; mỗi phím search gọi GET ngay, không debounce. List/detail requestId riêng chỉ nhận phản hồi mới nhất trong nhóm; detail reducer còn kiểm id đang chọn. Chọn user không dựng panel từ summary, phải GET detail đầy đủ.

List/detail loading/error ở Redux; list lỗi giữ dữ liệu cũ và có “Thử lại”; detail lỗi có retry. `ADMIN_USER_UPDATED` chỉ cập nhật hàng/detail cùng id; không tự re-filter/re-page sau khóa hoặc đổi vai trò, nên hàng có thể tiếp tục xuất hiện trong bộ lọc không còn phù hợp (`Admin/redux/reducer.js:71`).

Tạo user: local dialog bắt email/họ tên và ít nhất một role, trim email/tên, POST; response `{user,initialPassword}` chỉ ở local dialog, không Redux/storage/log. Hiển thị một lần, có copy clipboard; đóng dialog tải lại list. Dialog dùng unmounted guard (`CreateUserDialog.js:35`). Nếu click backdrop lúc đang POST, dialog vẫn có thể đóng dù nút Hủy disabled (`CreateUserDialog.js:102`): account tạo thành công sau đó không còn màn hiển thị initialPassword, mà reset mật khẩu chưa hỗ trợ. Đây là rủi ro UX thật (F05).

**Lý do (suy luận):** list nhẹ và detail đầy đủ riêng; giữ mật khẩu ban đầu khỏi state global; server là nơi sinh credential. **Ưu điểm:** hợp đồng summary/detail rõ, requestId chống search cũ, lỗi thiếu catalog hiện tường minh, không retry tạo account. **Hạn chế:** requestId không gắn phiên/unmount; callback role/catalog/mutation không có requestId; thiếu debounce và retry catalog; clipboard tồn tại ngoài vòng đời dialog nếu người dùng chủ động copy.

### 6.2 Vai trò/quyền lẻ và khóa tài khoản

Panel giữ roleIds draft + snapshot version. Lưu PUT roles với If-Match; 412 giữ draft và mời GET bản mới, so sánh tập quyền/role; stale vô hiệu nút save cho tới bỏ draft. Các command khác thành công có thể sync version nếu server role set không đổi. Permission panel hiển thị grant reason, effectivePermissions, role sources từ roleOptions; thiếu catalog giữ quyền hiện có, không tự thêm mã. Grant/revoke cần reason không rỗng và If-Match; revoke chỉ xóa grant lẻ, quyền từ role vẫn có thể còn.

Khóa/mở khóa có ConfirmDialog, `users.activate`, POST activate/deactivate **không If-Match**, khớp quyết định spec V2 §2.12. Server giữ quy tắc self/last-admin; FE chỉ cảnh báo. Reset password tạm disabled, có lý do chưa hỗ trợ (`UserDetailPanel.js:289`).

**Ưu điểm:** không ghi đè tự động khi 412, update đúng id, khác biệt grant/role diễn đạt rõ. **Hạn chế:** vai trò, permission và lock có busy riêng, nên có thể gửi các command trên cùng User đồng thời và một command chịu 412; sau update không reload `/me` nếu sửa chính actor, menu/quyền hiển thị chưa đổi ngay. Handler mutation vẫn dispatch khi panel đã unmount/logout.

### 6.3 Vai trò và danh mục quyền

Roles mount GET roles và tùy quyền GET permissions. Chọn role lấy từ list; tạo role mới hoặc clone giữ permissionCodes của role đã lưu và yêu cầu code/tên mới. Server quyết định isSystem. Redux upsert role mới tự chọn; update role cũ không thay lựa chọn.

RoleDetail giữ name/permissionCodes/snapshot. Một nút Lưu có thể thực hiện hai transaction HTTP tuần tự: PUT rename → nhận version mới → PUT permissions dùng version mới (`RoleDetail.js:77`). Nếu rename thành công, permissions lỗi, UI báo “Tên đã được lưu, quyền chưa được lưu”, giữ phần draft chưa lưu. 412 mời reload, stale hiển thị diff và không tự gửi lại. Khi đổi role còn dirty, container hỏi bỏ draft hay ở lại; chưa bảo vệ chuyển route/đóng tab.

**Mâu thuẫn đã xác minh:** `isEditable()` bắt `canManage && !role.isSystem` (`RoleDetail.js:59`), khóa mọi system role. Spec V2 §3 dòng 41 cho quản lý Name/permissions, chỉ Code/xóa là bất biến. Domain Role.Rename/SetPermissions không cấm IsSystem; BE chỉ bảo vệ admin core permission (`benh_vien_be/.../SetRolePermissionsCommandHandler.cs:43`). Do đó UI thiếu capability mà API đã cho phép; test FE hiện còn xác nhận hành vi cũ (§9 F04). Không được lấy test này làm chuẩn nghiệp vụ.

**Lý do (suy luận):** giữ snapshot để đối chiếu concurrency; hai endpoint cho tên/quyền độc lập. **Ưu điểm:** partial success được báo chính xác; draft không bị reload âm thầm. **Hạn chế:** list/catalog không requestId/epoch; input name/checkbox vẫn cho sửa lúc saving, trong khi payload đã chụp từ lúc bấm lưu, có thể tạo draft lệch kết quả; dialog create chỉ disable nút, không guard saving trong handler. System role read-only là quy tắc legacy cần sửa khi có yêu cầu implement.

### 6.4 Hồ sơ nhân sự và phạm vi làm việc

Panel mount khi `staff-profiles.read`; component tự GET profile và tree nếu `facilities.read` (`StaffProfileSection.js:57`). GET 404 **đúng code** `staff_profile_not_found` mới hiểu là chưa có hồ sơ, cho tạo nếu manage. PUT không If-Match tạo; PUT có If-Match sửa code/isActive; PUT work-scopes gửi departmentIds và If-Match.

State profile/tree/selection/form ở local; unmounted + userId guard bỏ response user trước. Picker nhóm theo branch, lọc khoa active hoặc đã chọn để vẫn hiển thị scope cũ. 412/staff_profile_exists tự GET profile mới và giữ lựa chọn chưa lưu, không tự gửi mutation lại. Lỗi invalid_departments diễn giải tên khoa từ tree. Staff inactive hoặc chưa có scope có cảnh báo; không tự nhận rằng permission hành động đủ quyền đọc bệnh án.

**Lý do (suy luận):** tách tài khoản đăng nhập khỏi nhân sự/phạm vi tổ chức; chọn khoa từ cây dữ liệu chuẩn. **Ưu điểm:** lỗi chưa có hồ sơ phân biệt với 404 khác; If-Match khớp backend; stale response được chặn tại component. **Hạn chế:** thiếu facilities.read thì user có manage profile cũng không chọn scope được; tree/load profile lỗi chưa có nút retry; load cùng user không requestId nên nhiều lần reload có thể đảo thứ tự; selected scope được giữ sau 412 nhưng chưa có diff rõ với snapshot như role panel.

### 6.5 Cơ cấu tổ chức

Facilities GET cây cơ sở → khoa → phòng. Quyền read vào route; manage mở nút thêm/sửa. Tạo branch code/name, department thêm branchId/kind, room thêm departmentId; edit chỉ name/isActive (code/parent/kind giữ nguyên). Dialog trim/upcase code; badge inactive và nhãn kind tiếng Việt. Sau mutation thành công thunk reload cả cây; không giả định insert/update local.

412/404 reload cây, giữ dialog/draft để kiểm lại, không tự retry mutation (`Facilities/Container.js:55`). If-Match của lần save lấy từ item trong Redux **ở thời điểm save**, không snapshot lúc mở dialog. Reload thành công sau mutation lỗi thì người dùng bấm lại có thể dùng version mới với draft cũ. UI báo kiểm tra lại nhưng chưa cho diff đầy đủ. Mutation thành công mà reload lỗi: action ghi tree error nhưng trả mutation success; dialog đóng, lỗi list còn hiện (`Facilities/redux/action.js:19`).

**Ưu điểm:** cây khớp ownership hierarchy, server giữ ràng buộc parent/active children; tách lỗi mutation và tải lại. **Hạn chế:** request reload không requestId/epoch; vẫn cho Add dưới parent inactive, server phải từ chối; dialog handler không guard saving và fields vẫn chỉnh được khi request chạy; chưa hiển thị loading cây rõ ngoài giữ dữ liệu cũ.

## 7. Hợp đồng HTTP thực tế

Tiền tố bảng là `/api/v1` trên backend; FE gọi `v1/...` tương đối với `API_URL`. Các mutation quản trị thật gửi Bearer + cookie/CSRF qua `http`; phía Presentation RequirePermission/RequireCsrf. JSON casing mặc định lower camel được các client sử dụng; rowVersion là uint phía BE và Number/string header phía FE (uint vẫn trong giới hạn biểu diễn chính xác Number).

| Method + route | Payload/query/header FE | Response thành công đang được FE dùng |
|---|---|---|
| POST `/auth/login` | `{email,password}`, raw cookie client | 200 `{accessToken,expiresAtUtc,mustChangePassword}` + cookies |
| POST `/auth/refresh` | null body, raw cookie, X-CSRF-Token | 200 token DTO + rotate refresh cookie |
| GET `/auth/me` | Bearer | 200 `{id,email,fullName,avatarUrl,roles,permissions,mustChangePassword}` |
| POST `/auth/logout` | null, cookie + CSRF | 204, clear cookies |
| POST `/auth/change-password` | `{currentPassword,newPassword}` | 200 token DTO; giữ refresh cookie |
| GET `/users` | pageNumber/pageSize/searchTerm/roleId/status; bỏ giá trị rỗng | PagedResult: items/pageNumber/pageSize/totalCount/totalPages |
| GET `/users/{id}` | Guid | UserDetailDto: isActive/mustChangePassword/roles/permissionGrants/effectivePermissions/rowVersion |
| POST `/users` | `{email,fullName,roleIds}` | 201 `{user,initialPassword}` |
| PUT `/users/{id}/roles` | `{roleIds}` + If-Match | 200 UserDetailDto |
| POST `/users/{id}/permissions/grant` hoặc `/revoke` | `{permissionCode,reason}` + If-Match | 200 UserDetailDto |
| POST `/users/{id}/activate` hoặc `/deactivate` | Không body/version | 200 UserDetailDto |
| GET `/roles` | — | Array RoleDto: id/code/name/isSystem/permissionCodes/rowVersion |
| GET `/permissions` | — | Array `{code,group,description}` |
| POST `/roles` | `{code,name,permissionCodes}` | 201 RoleDto |
| PUT `/roles/{id}` | `{name}` + If-Match | 200 RoleDto |
| PUT `/roles/{id}/permissions` | `{permissionCodes}` + If-Match | 200 RoleDto |
| GET `/facilities` | — | Array BranchDto chứa departments chứa rooms |
| POST `/facilities/branches` | `{code,name}` | 201 BranchDto |
| POST `/facilities/departments` | `{branchId,code,name,kind}` | 201 DepartmentDto |
| POST `/facilities/rooms` | `{departmentId,code,name}` | 201 RoomDto |
| PUT `/facilities/{branches|departments|rooms}/{id}` | `{name,isActive}` + If-Match | 200 DTO tương ứng |
| GET `/users/{userId}/staff-profile` | — | 200 `{id,userId,staffCode,isActive,rowVersion,workScopes}` |
| PUT `/users/{userId}/staff-profile` | `{staffCode,isActive}`; không version=tạo, có If-Match=sửa | 201/200 StaffProfileDto |
| PUT `/users/{userId}/staff-profile/work-scopes` | `{departmentIds}` + If-Match | 200 StaffProfileDto |

Bằng chứng client: `Auth/api/authClient.js:13`, `Auth/redux/actions.js:18`, `Admin/api/usersClient.js:12`, `Admin/api/staffProfileClient.js:4`, `Roles/api/rolesClient.js:7`, `Facilities/api/facilitiesClient.js:5`. Đối chiếu BE: `Presentation/Endpoints/V1/{Auth,Users,Roles,Permissions,Facilities,StaffProfiles}/*Endpoints.cs` và DTO ở `Application/Features/*/Common`, `Auth/GetMe`, `Users/CreateUser`. Không thấy lệch method/body/If-Match giữa các client quản trị thật và endpoint đã đọc. POST logout-all tồn tại BE nhưng chưa có client/UI; sessions/revoke trong spec không có route tương ứng trong AuthEndpoints hiện tại.

## 8. Luồng lâm sàng/tiếp nhận còn dự kiến

Các mục dưới đây mô tả component/mock chưa được phát hành; không chứng minh nghiệp vụ đã chạy với PostgreSQL hoặc an toàn nhiều nhân viên. Availability hiện false nên không cần sửa để nghiệm thu phần quản trị, nhưng phải xử lý trước khi bật module.

### 8.1 Tìm và tạo hồ sơ; tiếp nhận/cấp số

Search form gửi documentNumber/fullName/birthDate/phone; thunk lấy array và ghi results, không pagination/loading/error/requestId. Create dialog check nghi trùng chỉ bằng **fullName**; nếu trùng chọn hồ sơ cũ hoặc yêu cầu reason rồi vẫn tạo. `submitCreate` không gửi `forceCreateReason` cho API (`CreatePatientDialog.js:45`), nên reason chỉ ở UI, chưa audit được. Năm sinh dựng `YYYY-01-01` kèm birthYearOnly; unknown gửi rỗng; hợp đồng phải được chốt với PatientRegistry.

Intake mount Promise.all patient/openVisits/departmentSessions; loading false chỉ khi cả ba thành công, không catch/unmount guard. Rời màn clear lastVisit. Form chọn khoa/buổi/bác sĩ, lý do khám, paymentMethod, nhóm ưu tiên và kiểm chứng UI; có lượt mở cần openVisitReason. Cấp số gửi **department/sessionLabel/doctor/patientName dạng tên, không patientId/sessionId** (`IntakeForm.js:49`), vì đang phục vụ mock. Đây chưa phải hợp đồng định danh đủ để backend ghi chính xác.

Một Idempotency-Key được sinh trong constructor và giữ khi lỗi (`IntakeForm.js:32`); request real dùng header. Nếu lỗi rồi thay nội dung form, key vẫn giữ cho payload mới, dễ gây conflict khi backend có payload hash; không có state phân biệt lần submit mới và retry cùng nội dung. Transfer có reason nhưng không busy/error/idempotency, chỉ gửi department tên. Mock cấp số bằng counter RAM, không chứng minh unique/transaction.

**Ưu điểm định hướng:** cảnh báo nghi trùng, ngày sinh không đầy đủ, lý do mở lượt mới, key giữ khi retry, phiếu in riêng. **Hạn chế trước phát hành:** quyền create/register chưa Can riêng, error bị bỏ, các khóa định danh/chống trùng/idempotency chưa đúng contract, không lọc scope từ backend. Không có Appointment.

Các route dự kiến: GET/POST `/patients`, POST `/patients/check-duplicates`, GET `/patients/{id}`, GET `/patients/{id}/open-visits`, GET `/reception/department-sessions`, POST `/reception/visits` + Idempotency-Key, POST `/reception/visits/{id}/transfer` (`Reception/api/receptionClient.js:1`). Không có endpoint tương ứng trong Presentation đã liệt kê; **CẦN_XÁC_NHẬN**, không dùng client này làm source of truth.

### 8.2 Hàng chờ khám

QueueContainer tải queue/late từ client; gọi tiếp theo, gọi lại, countdown 60s tối đa 3 lần, chuyển trễ, quay lại đều chỉ setState. Confirm match điều hướng encounter theo calling.id. Timer 1s được clear khi unmount. Không command server cho call/recall/late/return; `saveQueue` real cố ý reject vì chưa có hợp đồng (`Clinic/api/clinicClient.js:8`). Hai máy không thể dùng UI này để đồng bộ hàng.

`queueLogic.js:13` xếp toàn bộ backReason=vitals trước backReason khác, sau đó FIFO riêng; **lệch quyết định V2 §2.6/§3**: sinh hiệu/CLS cùng nhóm Quay lại, FIFO thực sự, không ưu tiên sinh hiệu. Test `Clinic/__tests__/queueLogic.test.js:17` đang khẳng định hành vi cũ. Client chỉ GET `/clinic/queue`; cần command server và idempotency theo NV-10/11 trước phát hành, không phục hồi bằng API save whole queue.

**Ưu điểm định hướng:** hàm thuần test được, không chọn tùy ý waiting candidate, timer cleanup. **Hạn chế:** local time/order/status là demo; không khóa server, không error/loading, back FIFO sai nguồn đã chốt.

### 8.3 Phiếu khám, chỉ định, đơn thuốc

Mount tải encounter vào Redux và hai catalog vào local. Input mỗi lần thay đổi gửi PUT form ngay, không debounce/local draft/await/error/concurrency version (`EncounterContainer.js:59`). Add order/prescription/endEarly/confirm gọi thunk nhưng không await rồi xóa input/đóng dialog; fail có thể làm mất draft và promise rejection không được xử lý. Clear encounter khi unmount nhưng response cũ vẫn có thể tái ghi.

UI tách clinicalStatus/recordStatus và các perform/payment/result status của chỉ định; confirmed khiến readOnly (`EncounterContainer.js:392`), hợp định hướng vòng đời độc lập/bản xác nhận bất biến. Nhưng đây chỉ chặn UI/mock; chưa có API/domain version thật, chưa có IAuditedRequest làm bằng chứng trả PHI an toàn. Yêu cầu sinh hiệu hiện chỉ `requestVitals(id)` không chọn chỉ số bác sĩ yêu cầu, còn thiếu yêu cầu V2 §2.4.

Route dự kiến: GET `/encounters/{id}`, PUT `.../form`, POST `.../request-vitals`, `.../orders {orderCode}`, `.../prescriptions item`, `.../end-early {reason}`, `.../wait-for-results`, `.../admit`, `.../confirm`, GET `/catalog/services`, `/catalog/medicines` (`Clinic/api/clinicClient.js:12`). Confirm/admit chưa có Idempotency-Key. **CẦN_XÁC_NHẬN** toàn bộ contract khi triển khai Clinical.

### 8.4 Sinh hiệu và bảng gọi số

Vitals tải queue → reducer lấy người đầu làm current bằng action đồng bộ → nhập 8 chỉ số hoặc notMeasurable/reason → POST complete → clear current → GET lại queue. Form dùng chuỗi số, chưa validator/range; chưa busy guard, hai submit có thể trùng. GET lỗi Redux có lưu nhưng component chỉ connect queue/current, nên UI có thể hiển thị “Hàng chờ trống” khi thực tế tải thất bại (`Vitals/Container.js:162`). Nếu complete thành công nhưng GET reload lỗi, handler biểu diễn lỗi như hoàn thành thất bại dù current đã clear; cần tách hai kết quả như ChangePassword.

Sinh hiệu/người bệnh chỉ RAM, unmount clear, nhưng response late không bị chặn. Thời điểm/đo chỉ số/ngưỡng/reason đang CẦN_XÁC_NHẬN trong UI; không tự chọn giá trị OPEN. Client dự kiến GET `/vitals/queue`, POST `/vitals/{ticketId}/complete`; không command server gọi tiếp theo, không idempotency. Mock nhận id nhưng không chứng minh lưu measurements.

Display luôn tạo snapshot số/phòng từ seed RAM và đổi mỗi 4s; không PHI/tên, timer cleanup (`Display/Container.js:5`). **Ưu điểm:** hiển thị public board chỉ số/phòng giữ riêng khỏi màn khám. **Hạn chế:** bất kỳ người đăng nhập có thể mở board và thấy số giả mà không có nhãn demo; chưa realtime/server, chưa permission/availability.

## 9. Phát hiện ưu tiên, giới hạn an toàn bất đồng bộ

Mức P1: rủi ro dữ liệu/định danh phiên cần kiểm tra trước sử dụng thật; P2: capability hoặc UX sai; P3: cải thiện bảo trì. Các tình huống dưới đây suy ra từ code tĩnh, chưa có tái hiện browser/test mới. Không biến suy luận thành kết luận khai thác đã xảy ra.

### F01 — P1: Response feature có thể tái ghi state sau logout

**Bằng chứng:** reset root chỉ xảy ra trên action `AUTH_LOGGED_OUT` (`src/reducer.js:29`); `Admin/redux/action.js:15` chỉ so listRequestId với biến module, không epoch; role/catalog tại `Admin/redux/action.js:51`, `Roles/redux/action.js:11`, facilities tại `Facilities/redux/action.js:6` dispatch sau await không kiểm phiên. Clinic/Vitals cũng tương tự.

**Tình huống:** A đang GET list → logout reset store → GET của A trả thành công → requestId vẫn mới nhất → ghi dữ liệu A vào store anonymous hoặc phiên B. Với module PHI sau này đây là dữ liệu của phiên trước trên thiết bị dùng chung. Unmount guard ở component không chặn thunk dispatch. Test logoutState chỉ assert reset tức thời, không resolve deferred feature request sau reset.

**Hướng xử lý khi implement:** mọi API/thunk nhận epoch/session ownership; bỏ response khi epoch đổi; hủy request nếu abstraction cho phép. Cần test deferred GET và mutation rồi logout/login B, không chỉ test reducer reset.

### F02 — P1: HTTP replay 401 chưa gắn với phiên gửi ban đầu

**Bằng chứng:** `service/http.js:20` lấy token hiện tại mỗi request; `http.js:36` gọi refresh/replay mà không lưu/so epoch. Auth loadMe/login có epoch, nhưng interceptor áp cho mọi command.

**Tình huống:** request của A chưa nhận response → logout/login B → 401 cũ về → interceptor refresh phiên B, replay body lệnh A bằng Bearer/CSRF B. Backend vẫn kiểm quyền của B, nhưng nếu B có quyền thì mutation có thể chạy dưới actor B dù thao tác thuộc A. Cũng có thể 401/refresh lỗi cũ gọi expire và đăng xuất B.

**Hướng xử lý:** capture epoch khi gửi, reject/discard lỗi cũ trước refresh, sau await và trước replay/expire callback. Test xác nhận không gửi mutation lần hai, không expire B. Chính refreshCoordinator kiểm epoch của **lần refresh** chưa đủ để biết epoch request gốc.

### F03 — P1/P2: Logout tab cũ có thể nhắm cookie phiên mới

**Bằng chứng:** `sessionLifecycle.js:69` không đổi shared generation nếu tab cũ; nhưng `actions.js:67` vẫn raw logout không điều kiện; raw CSRF đọc cookie mới khi gọi (`authClient.js:21`). BE logout tìm family từ cookie (`AuthEndpoints.cs:93`).

**Tình huống:** tab A giữ Redux phiên cũ, tab B login tài khoản khác tạo shared generation/cookie mới. A bấm logout; local generation guard đúng nhưng POST gửi cookie/CSRF shared của B, có thể thu hồi family B. Epoch sau await chỉ bảo vệ thông báo local, không ngăn side effect server. Logout response clear cookies cũng có thể đến sau login mới và xóa cookie mới.

**Hướng xử lý:** định nghĩa thao tác auth serialize hoặc chặn logout server khi shared generation đã thay; test nhiều tab/login/logout và response Set-Cookie trễ. Không tự đổi contract server khi chưa chốt semantics shared browser.

### F04 — P2: System role bị khóa toàn bộ chỉnh sửa trái spec/API

**Bằng chứng:** `Roles/component/RoleDetail.js:59` và dòng 137; spec V2 dòng 41; Domain Role.Rename dòng 33, SetPermissions dòng 41; BE SetRolePermissions handler dòng 43 chỉ chặn bỏ admin core. Test RoleDetail dòng 50 giữ kỳ vọng legacy.

**Ảnh hưởng:** có roles.manage vẫn không đổi tên/quyền system role qua UI; không thể dùng màn quản trị để áp policy được backend hỗ trợ. Cần chỉnh UI/test theo nguồn đã chốt, giữ Code/isSystem và admin core safety.

### F05 — P2: Đóng backdrop khi tạo user đang chạy có thể mất mật khẩu một lần

**Bằng chứng:** `Admin/component/CreateUserDialog.js:102` luôn onClick=onClose, `:140` chỉ disable Hủy; response bị unmounted guard bỏ ở `:48`. Reset password disabled `UserDetailPanel.js:291`.

**Ảnh hưởng:** account đã tạo nhưng initialPassword response không được hiển thị lại. Chặn đóng toàn bộ dialog lúc submitting hoặc thiết kế nơi nhận kết quả mutation có vòng đời đủ dài; test click backdrop với deferred POST.

### F06 — P2 trước phát hành: Hàng Quay lại vẫn ưu tiên sinh hiệu trên CLS

**Bằng chứng:** `Clinic/queueLogic.js:13`, test `:17`; spec V2 §2.6 và §3 dòng 38. Hiện feature bị tắt nên chưa ảnh hưởng luồng thật; cần thay thuật toán/test theo FIFO và quyền server trước bật.

### F07 — P2: Board số giả vẫn được phục vụ ngoài cờ mock

**Bằng chứng:** `/display/queue` là PrivateRoute `src/index.js:85`; `Display/Container.js:2` import displayMock trực tiếp. Tắt mock không đổi board. Cần availability/nhãn demo hoặc API thật trước dùng TV bệnh viện; không kết luận board này đang được triển khai production.

### F08 — P2: Trạng thái thành công/lỗi sau mutation chưa đồng nhất

Vitals complete + reload gộp một promise nên reload lỗi bị coi như complete lỗi (`Vitals/redux/action.js:23`); trong khi Facilities mutation success/reload fail được tách ở store và ChangePassword có màn retry GET riêng. Clinical chưa có error/busy. Trước phát hành cần thống nhất trạng thái “đã ghi, chưa tải lại”, tránh người dùng gửi lại command đã commit.

### F09 — P3: Loading toàn cục hiện không tham gia các flow

`Loading` luôn mount nhưng `loadingAction/showLoading/hideLoading` chỉ được định nghĩa/export, không có caller trong source feature ngoài test. Bộ đếm xử lý overlap đúng về thiết kế; thực tế boot và nhiều màn chỉ trả null/giữ dữ liệu cũ. Nên mô tả loading theo từng use case, không mặc định spinner toàn cục đang chạy.

## 10. Test hiện có và bằng chứng kiểm tra

Đã chạy mới: tìm file/source và đọc line-number, liệt kê endpoint Presentation, đối chiếu DTO/method/version, kiểm working tree trước khi viết tài liệu. Inventory mới tìm thấy **36 file `*.test.js`** trong `benh_vien_fe/src` (không tương đương số test case đã chạy).

| Nhóm test có sẵn | Phạm vi đọc được | Kiểm chứng trong phiên này |
|---|---|---|
| Auth boot/actions/reducer/Login/ChangePassword | epoch, bắt đổi, lỗi/password form, partial success `/me` | Đã chạy trong suite Jest tập trung |
| tokenStore/csrf/refreshScheduler/refreshCoordinator | RAM, cookie đọc, timer/jitter, single-flight/lock/generation/logout/broadcast | Đã chạy trong suite Jest tập trung |
| service/http | Bearer/CSRF, 401 một replay, second 401, 403, 10 request chung refresh và giữ payload/key | Đã chạy trong suite Jest tập trung |
| router/workspace/navigation/PermissionRoute | registry, 0/1/n khu vực, return URL, quyền, pending, tránh loop | Đã chạy trong suite Jest tập trung |
| logoutState | reset toàn bộ root tức thời, clear token và timer | Đã chạy; chưa có deferred feature-response coverage trong file này |
| Admin client/reducer/Container/navigation/detail/staff | route/body/header, list/detail loading/error/stale request, 412, profile/scopes | Đã chạy trong suite Jest tập trung |
| Roles client/reducer/Container/RoleDetail | If-Match, draft/412, clone, partial rename/permissions; còn test system read-only legacy | Đã chạy trong suite Jest tập trung |
| Facilities client/reducer/Container | tree, quyền manage, create, 409, 412 giữ draft/reload | Đã chạy trong suite Jest tập trung |
| Reception/Vitals reducer, Clinic queueLogic, Loading | state/hàm thuần; không chứng minh server/PHI/locking | Đã chạy trong suite Jest tập trung |

Kiểm tra tập trung trong phiên này: Jest **225/225 PASS, 36 suite**, ESLint **PASS**; xem [spec.md §8](spec.md) và log mới được dẫn ở đó. Agent phụ không tự chạy thêm một lượt. Không có bằng chứng E2E mới cho cookie thực, Web Locks/BroadcastChannel thực, sleep/background tab, đổi tài khoản nhiều tab hoặc stale feature data sau logout: các ca này **NOT_RUN**. Lệnh phù hợp khi implement: `tooling/validate.ps1 -Mode Frontend`; khi đổi hợp đồng backend+FE dùng `-Mode All`. Các test cần thêm phải nhắm tình huống F01–F05/partial commit, không sao chép implementation.

## 11. Kết luận phạm vi

Frontend hiện là khung đăng nhập/phiên/workspace cùng quản trị IdentityAccess, facilities và staff profile đã có client thật. Phần tiếp nhận/lâm sàng/sinh hiệu là code dựng UI với contract dự kiến và đang bị khóa phát hành; board Display là mock có route riêng. Điểm mạnh là token RAM, guard theo permission, CSRF/refresh điều phối, draft/version conflict và phân biệt đổi mật khẩu thành công với reload thất bại. Điểm cần ưu tiên review/implement là ownership của request qua logout/đổi tài khoản, logout cookie shared, system role editability và kết quả tạo user một lần. Suite hiện có đã chạy; chưa thêm test tái hiện riêng cho F01–F05 nên các rủi ro runtime tương ứng còn **chưa xác minh**.
