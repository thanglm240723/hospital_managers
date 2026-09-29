# Plan V2 02 — Logout và logout-all

> Khi được yêu cầu triển khai, dùng superpowers:executing-plans theo từng task; subagent chỉ khi người dùng chọn. Claude viết code khi người dùng gọi skill triển khai (chốt 2026-09-30). Checkbox là trạng thái thực hiện, không phải trạng thái viết tài liệu.

**Mục tiêu:** đăng xuất thu hồi phiên thật, xóa cookie/state và đồng bộ các tab.

**Kiến trúc:** tái dùng transaction, invalidation và CSRF plan 01; bổ sung lookup cookie qua port Application. Không rotate token trong logout.

**Công nghệ:** .NET 10/PostgreSQL 17/Redis, React 16/Redux/BroadcastChannel.

**Spec:** [spec.md](spec.md) §4–5; auth spec cũ §3.1, §3.4–3.5, §5. Phụ thuộc [plan 01](plan-01-doi-mat-khau.md).

## Ràng buộc chung

Giữ V1, CancellationToken, TimeProvider, Result/Problem Details, audit không secret. Logout cookie là public ở Gateway nhưng không bỏ kiểm Origin/CSRF. Logout-all cần access token và CSRF. Không thêm màn quản lý từng thiết bị/revoke từng session ở mốc này. Không gọi Redis khi giữ transaction; không có migration mới dự kiến ngoài schema plan 01.

## Trọng tâm review

1. Cookie thiếu/rác và cookie thuộc family đã revoke → task 1/2, 204 không mutate người khác.
2. Origin/CSRF sai với cookie nhận diện được → task 1, 403 và không revoke.
3. Logout-all cạnh tranh login/change-password → task 2, cùng thứ tự khóa.
4. Logout API lỗi nhưng FE đã xóa state → task 3, không mô tả là server chắc chắn đã thu hồi.
5. Response auth cũ trở về sau logout → task 3/4, không khôi phục state đăng nhập.

## Task 1 — CSRF cookie và định vị phiên

**File tạo:** `APP/Features/Auth/Common/IRefreshSessionLookup.cs`, `RefreshSessionRef.cs`; `PERS/ReadServices/Identity/RefreshSessionLookup.cs`; `IT/Auth/CookieCsrfTests.cs`.

**File sửa:** `PRES/Auth/CsrfProtectionFilter.cs`, `CsrfProtectionMetadata.cs`; `PERS/DependencyInjection.cs`; `PRES/Endpoints/V1/Auth/AuthEndpoints.cs`.

**Giao diện:** `FindAsync(string tokenHash, CancellationToken ct): Task<RefreshSessionRef?>`; ref gồm FamilyId, UserId, SessionStatus. Dùng `IRefreshTokenGenerator.Hash(string)`; lookup không có tracking, tra cả token đã consumed/revoked. Filter cookie đọc Origin + X-CSRF-Token, không tin so header bằng cookie là đủ: xác minh HMAC theo family.

- [ ] Viết test Origin không thuộc allowlist, CSRF thiếu/sai, cookie thuộc family khác đều không được revoke; `Assert.Equal(403, status)` với cookie nhận diện được.
- [ ] Viết test cookie thiếu/không nhận diện → handler logout trả 204 và clear cookie; khác refresh sẽ trả 401. Không dùng validator NotEmpty để biến logout thiếu cookie thành 400.
- [ ] Chạy `dotnet test tests/QuanLyBenhVien.IntegrationTests --filter FullyQualifiedName~CookieCsrf` từ BE; xác nhận fail trước sửa.
- [ ] Implement lookup/filter đúng contract; phân nhánh missing cookie phải được kiểm rõ, không nuốt lỗi DB thành “cookie không tìm thấy”. Dependency lỗi là lỗi phụ thuộc, không 204 giả.
- [ ] Chạy lại tests; bảo đảm Presentation chỉ dùng port Application, không gọi Persistence/Infrastructure.

## Task 2 — Thu hồi và commit

**File sửa:** `APP/Features/Auth/Logout/LogoutCommand.cs`, `LogoutCommandHandler.cs`; `APP/Features/Auth/LogoutAll/LogoutAllCommand.cs`, `LogoutAllCommandHandler.cs`; `PRES/Endpoints/V1/Auth/AuthEndpoints.cs`.

**Test tạo:** `IT/Auth/LogoutTests.cs`, `IT/Gateway/LogoutSessionTests.cs`; dùng helper AuthTestClient hiện có.

**Giao diện:** giữ `LogoutCommand(string? RefreshToken): ICommand<Result>` và `LogoutAllCommand(): ICommand<Result>`. Endpoint gọi `AuthCookieWriter.Clear` sau kết quả thành công; clear hai cookie với Path/Secure/SameSite đúng như lúc tạo.

- [ ] Test logout một family giữ các family khác active; logout-all revoke mọi family active của user; không động vào user khác; token cũ bị Gateway từ chối sau invalidation.
- [ ] Test lặp logout 204, không tạo thêm thay đổi nghiệp vụ/audit thành công trùng khi family đã revoke; DB lỗi rollback không trả 204.
- [ ] Chạy `dotnet test tests/QuanLyBenhVien.IntegrationTests --filter "FullyQualifiedName~LogoutTests|FullyQualifiedName~LogoutSessionTests"`; ghi nhận test đỏ.
- [ ] Implement lookup để tìm UserId → transaction khóa User → khóa family/tokens → kiểm lại ownership/state → revoke với Logout/LogoutAll → audit/invalidation → commit → flush. Logout-all kiểm current-user/current-family/sv, không dùng UserId từ client.
- [ ] Test race login/logout-all bằng barrier: kết quả theo thứ tự khóa, family tạo trước điểm logout-all được thu hồi; login hợp lệ sau logout-all có thể tạo phiên mới. Không đặt yêu cầu “cấm login mãi”.
- [ ] Chạy lại tests trên PostgreSQL/Redis thật. Trước plan 03 kiểm family và request Gateway, không gọi RefreshAsync để chứng minh logout.

## Task 3 — FE logout rõ ràng và chống phản hồi đến muộn

**File sửa:** `FE/src/feature/Auth/redux/actions.js`, `session/refreshCoordinator.js`, `session/tokenStore.js`, `FE/src/reducer.js`, `FE/src/index.js`; vị trí hiển thị sessionMessage trên Login.

**Test:** `FE/src/feature/Auth/redux/__tests__/actions.test.js`, `session/__tests__/refreshCoordinator.test.js`, `FE/src/__tests__/logoutState.test.js` (mới).

**File tạo:** `FE/src/feature/Auth/session/sessionLifecycle.js`.

**Giao diện đề xuất:** module này có `getSessionEpoch(): number`, `beginSessionTransition(): number`, `invalidateLocalSession(): void`, `isCurrentSessionEpoch(epoch): boolean`. Login mới bắt đầu một epoch; login/loadMe/refresh giữ epoch và không áp dụng kết quả nếu epoch đã đổi. Broadcast logout hủy hiệu lực token/response đang chờ. Không lưu token vào storage; epoch chỉ là điều phối, không cấp quyền.

Epoch mỗi tab không đủ để nhận diện token broadcast cũ. Chỉ nhận cập nhật token cho family đang hoạt động của tab; phản hồi `need-token` khi khởi tạo phải khớp requestId đang chờ. Logout xóa requestId và family đang nhận, bỏ qua token tự phát cho đến lần khởi tạo/login tường minh tiếp theo. Metadata family chỉ phục vụ điều phối; server vẫn xác thực token/quyền. Plan 03 kiểm lại giao thức này với refresh thật.

- [ ] Test logout clear token, auth, workspace và dữ liệu các feature; remote logout cũng clear toàn root state và ngừng refresh timer.
- [ ] Test pending login/loadMe hoặc token broadcast cũ về sau logout không set authenticated. Plan 03 mở rộng epoch cho network refresh thật.
- [ ] Test logout server lỗi: vẫn xóa dữ liệu cục bộ, hiển thị “Đã thoát trên thiết bị này; chưa xác nhận thu hồi phiên trên máy chủ.” Không log cookie/token; không thông báo thu hồi thành công giả.
- [ ] Chạy Jest lọc `actions|refreshCoordinator|logoutState`, sửa tối thiểu, chạy lại. Không tự retry vô hạn request logout khi lỗi mạng.
- [ ] Logout-all có BE/API test; nếu bổ sung nút FE thì đặt tường minh “Đăng xuất mọi phiên” cạnh thao tác tài khoản và xác nhận phạm vi, không đổi nút logout thường thành logout-all. UI logout-all chưa được yêu cầu thì giữ ngoài mốc giao diện.

## Task 4 — Bật đúng test và nghiệm thu

**File sửa:** `IT/QuanLyBenhVien.IntegrationTests.csproj`, `IT/Auth/RefreshAndLogoutTests.cs`; tạo `IT/Auth/LogoutRefreshInteropTests.cs` cho phần chỉ chạy khi plan 03 xong.

- [ ] Tách test logout khỏi file cũ còn phụ thuộc refresh. Không bật cả file rồi sửa assertions chấp nhận 501.
- [ ] Full + Frontend + build FE; ghi số lượng test, kiểm lỗi từ thay đổi và lỗi có sẵn riêng.
- [ ] Trình duyệt hai tab: logout một tab, tab kia mất dữ liệu và về login; request token cũ không dùng được sau invalidation; tài khoản khác không bị ảnh hưởng.
- [ ] Kiểm cookie thực sự bị xóa qua Gateway; không hạ Secure để vượt qua môi trường test sai.
- [ ] Review và mốc Git theo plan.md. Giới hạn ghi nhận: refresh/F5 vẫn chưa được nghiệm thu trước plan 03.

**Trạng thái:** NOT_RUN.
