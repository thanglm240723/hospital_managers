# Plan V2 03 — Refresh và khôi phục phiên khi F5

> Khi được yêu cầu triển khai, dùng superpowers:executing-plans theo task; subagent chỉ khi được chọn. Claude viết code khi người dùng gọi skill triển khai (chốt 2026-09-30).

**Mục tiêu:** tiếp tục phiên qua F5/hết hạn access token, xoay refresh token an toàn và thu hồi family khi reuse.

**Kiến trúc:** cookie → CSRF → command transaction → rotate/revoke + audit/invalidation → commit → response cookie/access token. FE dùng coordinator hiện có, không viết luồng refresh thứ hai.

**Công nghệ:** PostgreSQL 17 row locks, Redis, .NET 10; axios 0.18, Web Locks/BroadcastChannel, React 16.

**Spec:** [spec.md](spec.md) §4–5; auth spec cũ §3.3, §3.10, §4.4, §5. Phụ thuộc plan 01/02.

## Ràng buộc chung

Strict reuse, không grace window; family có hạn tuyệt đối 7 ngày. Refresh token chỉ cookie HttpOnly Secure, DB chỉ hash. CSRF vẫn cùng family; Max-Age giảm theo hạn tuyệt đối. Không auto retry refresh sau mất response vì token có thể đã consumed. API V1, Result và CancellationToken xuyên suốt.

## Trọng tâm review

1. Hai refresh cùng token → task 1/3, không tạo hai nhánh hợp lệ.
2. Mất response sau commit → task 2/3, retry token cũ theo policy reuse, không tự cứu bằng cấp family mới.
3. Cookie/family bị revoke/hết hạn → task 1/2, 401 và clear cookie.
4. Nhiều tab/cold tab, broadcast trễ, logout khi refresh đang chờ → task 3, không hồi sinh phiên.
5. Refresh kế tiếp sau đổi password/logout và cache cũ → task 4, kiểm đầu-cuối thật.

## Task 1 — Rotate trong transaction và reuse commit trước lỗi

**File sửa:** `APP/Features/Auth/RefreshSession/RefreshSessionCommand.cs`, `RefreshSessionCommandValidator.cs`, `RefreshSessionCommandHandler.cs`; `DOM/Identity/Sessions/SessionFamily.cs` chỉ nếu test chứng minh thứ tự kiểm tra chưa đúng; `PERS/Repositories/Identity/SessionRepository.cs` nếu cần khóa token theo thứ tự.

**Test tạo:** `IT/Auth/RefreshRotationTests.cs`; cập nhật `UT/Domain/Identity/SessionFamilyTests.cs`.

**Giao diện:** giữ `RefreshSessionCommand(string RefreshToken): ICommand<Result<AuthTokensResult>>`; dùng IRefreshSessionLookup plan 02, IUserRepository.GetForUpdateAsync plan 01, ISessionRepository.GetForUpdateAsync(familyId,presentedHash,ct), SessionFamily.Rotate(hash,newHash,now).

- [x] Test valid token: cũ ConsumedAtUtc, ReplacedById trỏ token mới, cùng family, LastRefreshedAtUtc cập nhật, AbsoluteExpiresAtUtc không đổi; `Assert.Single(usableTokens)`.
- [x] Test reuse consumed/revoked: family revoked, audit `auth.refresh.reuse`/Denied và invalidation đã commit trước khi trả 401. Token đã revoke không được repository lọc mất khiến nhánh reuse thành “không tìm thấy”.
- [x] Test family hết hạn/revoked hoặc User.IsActive=false không rotate. Không coi missing cookie là validator 400; endpoint map missing thành unauthorized.
- [x] Chạy `dotnet test tests/QuanLyBenhVien.IntegrationTests --filter FullyQualifiedName~RefreshRotation`; ghi nhận fail.
- [x] Implement định vị → khóa User → family/token → kiểm lại → rotate hoặc revoke → SaveChanges/Commit → flush nếu cần. Không throw Result failure trước commit ở nhánh reuse. Token mới lấy từ generator đã có, không dùng Guid làm secret.
- [x] Giữ `RotationResult` và reason của family nhất quán: family đã revoked không bị đổi reason sang Reuse tùy tiện; nếu đã active và trình token consumed thì phải ghi reuse. Đối chiếu/test rõ với spec §3.3.
- [x] Chạy lại test, thêm race refresh/change-password theo cùng thứ tự khóa và kiểm sv mới khi cấp access token.

## Task 2 — Endpoint, cookie và Gateway

**File sửa:** `PRES/Endpoints/V1/Auth/AuthEndpoints.cs`; `PRES/Auth/CsrfProtectionFilter.cs`; `PRES/Auth/AuthCookieWriter.cs` chỉ nếu kiểm cookie phát hiện sai; cấu hình/route `GW/appsettings.json` chỉ khi thật sự thiếu.

**Test:** tạo `IT/Auth/RefreshHttpTests.cs`; cập nhật `IT/Gateway/GatewayRateLimitAndHealthTests.cs` theo phạm vi refresh.

- [x] Test thiếu/rác cookie → 401 và xóa `__Host-rt`/`__Host-csrf`; Origin/CSRF sai với family nhận diện được → 403, không rotate, không revoke chỉ do CSRF.
- [x] Test 200 JSON chỉ chứa accessToken/expiresAtUtc/mustChangePassword; Set-Cookie đủ thuộc tính; refresh không tăng hạn tuyệt đối và không tiết lộ token qua response body.
- [x] Chạy test RefreshHttp, implement thin endpoint gửi command và AuthCookieWriter. 401 nghiệp vụ clear cookie; lỗi phụ thuộc không bị đổi thành thành công/204.
- [x] Kiểm Gateway route anonymous đúng POST refresh, rate-limit 30/phút/IP theo spec; endpoint nội bộ vẫn bảo vệ khóa. Không thêm đường API khác để bypass.
- [x] Chạy lại tests; bảo đảm request refresh không qua axios interceptor gọi chính refresh lặp lại.

## Task 3 — FE F5, single-flight và race logout

**File sửa khi cần:** `FE/src/feature/Auth/session/refreshCoordinator.js`, `refreshScheduler.js`, `tokenStore.js`; `FE/src/feature/Auth/redux/actions.js`; `FE/src/service/http.js`.

**Test sửa:** các file tương ứng trong `session/__tests__/`, `redux/__tests__/actions.test.js`, `service/__tests__/http.test.js`; thêm `FE/src/feature/Auth/__tests__/bootAuth.test.js`.

**Giao diện giữ:** `refreshAccessToken(): Promise<string>`, `bootAuth()`, `startRefreshScheduler(onFailure)`. Tái dùng epoch plan 02: kiểm trước áp dụng response vào tokenStore; request bắt đầu trước logout không được broadcast token mới sau logout.

- [x] Test cold boot refresh → me → authenticated; refresh 401 → anonymous và xóa state; quyền/mustChangePassword lấy lại từ me, không tự đoán từ role cũ.
- [x] Test cùng tab 10 yêu cầu 401 dùng một promise refresh; mỗi request retry tối đa một lần; giữ headers/body/Idempotency-Key và không ghép baseURL hai lần.
- [x] Test Web Locks + broadcast đến trễ + một tab mới không có token; không gửi lại token consumed vì đếm nhầm generation. Trình duyệt thiếu Web Locks có rủi ro strict reuse đã ghi trong spec, không tuyên bố bảo đảm mọi trình duyệt.
- [x] Test response refresh sau logout/đăng nhập tài khoản khác không cập nhật token; thông điệp broadcast phải gắn thế hệ phiên hợp lệ, không chỉ `{type:'token'}` không ngữ cảnh. Cơ chế epoch phải dùng chung với login/change-password, tránh mỗi feature có cờ riêng.
- [x] Chạy Jest `refreshCoordinator|refreshScheduler|http|bootAuth|actions` để thấy đỏ, sửa tối thiểu, chạy lại. Không sửa coordinator chỉ vì muốn viết mới nếu test hiện có đã đáp ứng.
- [x] Test timer theo TimeProvider/fake timers phía tương ứng; không chờ 15 phút thật trong unit test. Refresh thành công lên lịch mới; logout dừng timer.

## Task 4 — Bật test liên tính năng và nghiệm thu

**File sửa:** `IT/QuanLyBenhVien.IntegrationTests.csproj`; `IT/Auth/RefreshAndLogoutTests.cs`; bật `IT/Auth/PasswordChangeRefreshTests.cs`, `LogoutRefreshInteropTests.cs` đã tách ở 01/02; `VALIDATION.md` sau có output.

- [x] Port/bật test cũ còn đúng nghiệp vụ, tách helper không tự gọi admin change-password bất ngờ trong test không liên quan. Không bỏ assertion chỉ để test xanh.
- [x] Test hai HTTP refresh cùng token qua PostgreSQL: request đầu có thể 200, request sau reuse phải thu hồi family; không thể để cả hai token nhánh tiếp tục dùng được.
- [x] Test đổi password → current refresh được, other refresh 401; logout → refresh 401; logout-all → mọi family cũ refresh 401.
- [x] Test mất response refresh: token cũ không được retry tự động; lần trình lại theo strict reuse dẫn đến đăng nhập lại. Ghi UX này trong nghiệm thu.
- [ ] Full + Frontend + build FE, sau đó trình duyệt F5, mở tab, chờ refresh bằng cấu hình test không đổi policy production, logout nhiều tab. Kiểm access token chỉ RAM, refresh cookie HttpOnly, không log secrets.
- [ ] Review diff và chuẩn bị mốc Git theo plan.md.

**Trạng thái:** NOT_RUN. Sau plan này mới được nghiệm thu vòng đời phiên đầy đủ trong phạm vi ba plan đầu; chưa chứng minh phân quyền/luồng nghiệp vụ module sau.
