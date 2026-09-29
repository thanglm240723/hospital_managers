# Plan V2 01 — Hoàn tất đổi mật khẩu

> Khi được yêu cầu triển khai, dùng skill superpowers:executing-plans để đi từng task; chỉ dùng subagent-driven-development nếu người dùng chọn cách đó. Các checkbox theo dõi implementation, không đánh dấu chỉ vì đã có tài liệu. Claude viết code khi người dùng gọi skill triển khai (chốt 2026-09-30).

**Mục tiêu:** tài khoản đang đăng nhập đổi được mật khẩu thật và tiếp tục dùng phiên hiện tại; phiên khác bị thu hồi đúng spec.

**Kiến trúc:** giữ Carter → Command → Domain/repository → PostgreSQL; mọi invalidation có bảng chờ, Redis chạy sau commit. Chuyển đúng phần hỗ trợ còn thiếu sang kiến trúc mới, không kéo EF vào Infrastructure.

**Công nghệ:** .NET 10, MediatR 12.5.0, EF Core 10/PostgreSQL 17, Redis, React 16/Redux.

**Spec:** [spec.md](spec.md) §3–5; auth spec cũ §3.1, §3.8–3.10, §4.3. Đọc thêm [quy ước và mốc Git](plan.md).

## Ràng buộc và phạm vi

- Không triển khai refresh/logout trong plan này. Không yêu cầu refresh thành công để chứng minh đổi mật khẩu.
- Giữ `/api/v1/auth/change-password`; trả `AccessTokenDto` hiện có thay vì `AuthTokensResult` chứa refresh token không cần thiết.
- Độ dài 10–128; không trim mật khẩu; lỗi field trả 400; Origin/CSRF sai trả 403 `csrf_failed`; token không hợp lệ trả 401.
- Mọi IO truyền CancellationToken. Thời gian lấy từ TimeProvider; `User.ChangePassword` nhận thời điểm thay vì tự lấy UtcNow cho thao tác này.
- Sai mật khẩu hiện tại được audit Failed nhưng không thay hash/phiên. **Không đếm số lần sai** (spec §2 mục 8): đổi mật khẩu không có 429; limiter email ở login bị gỡ trong task 1.
- DB đã có Users, SessionFamilies, RefreshTokens, AuditRecords, CacheInvalidations. Chỉ thêm migration cho lease bảng chờ ở task 2; không tái tạo schema identity.

## Trọng tâm review

1. Hai yêu cầu đổi mật khẩu đồng thời và login chen giữa → task 1/3 kiểm khóa và kiểm lại credential.
2. Redis lỗi sau DB commit, worker chết sau eviction → task 2/5 kiểm không mất invalidation và giới hạn cache.
3. Token sv cũ nhưng chữ ký còn hạn → task 3/5 kiểm không được đổi mật khẩu tiếp.
4. Đổi xong nhưng `/me` lỗi → task 4 không báo sai rằng mật khẩu chưa đổi và gửi lại thao tác.
5. Test cũ dùng `/refresh` đang 501 hoặc bị Compile Remove → task 5 tách đúng bộ kiểm chứng, không báo PASS giả.

## Task 1 — Chính sách mật khẩu, field errors và khóa User

**File sửa:** `APP/Features/Auth/ChangePassword/ChangePasswordCommand.cs`, `ChangePasswordCommandHandler.cs`, `ChangePasswordCommandValidator.cs`; `APP/Features/Auth/Common/AuthErrors.cs`; `APP/Common/Results/Error.cs`; `PRES/Http/ResultExtensions.cs`; `APP/Common/Identity/ICurrentUser.cs`; `API/Security/CurrentUser.cs`; `DOM/Identity/IUserRepository.cs`, `User.cs`; `PERS/Repositories/Identity/UserRepository.cs`; `APP/Features/Auth/Login/LoginCommandHandler.cs`.

**File tạo:** `APP/Common/Security/PasswordPolicy.cs`.

**Test:** `UT/Application/Security/PasswordPolicyTests.cs` (bật lại), `UT/Application/Auth/ChangePasswordValidatorTests.cs` (mới), `UT/Presentation/Http/ResultFieldErrorsTests.cs` (mới), `IT/Persistence/IdentityWriteConcurrencyTests.cs` (mới).

**Giao diện cung cấp:**

```csharp
// Command trả body access token, không mang refresh token.
ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand<Result<AccessTokenDto>>
PasswordPolicy.ContainsEmailLocalPart(string password, string email) : bool
IUserRepository.GetForUpdateAsync(Guid id, CancellationToken ct = default) : Task<User?>
ICurrentUser.SecurityVersion : int?
User.ChangePassword(string newPasswordHash, DateTimeOffset now) : void
Error.FieldErrors : IDictionary<string, string[]>?
```

- [ ] Viết test validator cho newPassword 9/10/128/129 ký tự, null/rỗng; mật khẩu có khoảng trắng được giữ nguyên. Test local-part email không phân biệt hoa thường; kiểm tra equality mật khẩu thuộc handler vì cần hash hiện tại.
- [ ] Viết test `ValidationResult_PreservesCurrentPasswordField`: `Assert.Equal(400, status); Assert.True(errors.ContainsKey("currentPassword"));` và không rò nội dung password vào response/log.
- [ ] Chạy từ BE: `dotnet test tests/QuanLyBenhVien.UnitTests --filter "FullyQualifiedName~PasswordPolicy|FullyQualifiedName~ChangePasswordValidator|FullyQualifiedName~ResultFieldErrors"`; ghi nhận fail đúng logic/chưa có type, không coi lỗi Docker/SDK là test đỏ nghiệp vụ.
- [ ] Implement contract trên. `ResultExtensions` truyền FieldErrors cho `ProblemResponses.Create`; không đổi trường lỗi cũ. GetForUpdate yêu cầu transaction, SQL tham số hóa, reload user đã tracking sau nhận khóa.
- [ ] Gỡ limiter theo email (spec §2 mục 8): xóa `APP/Features/Auth/Common/ILoginAttemptLimiter.cs`, `INF/Caching/LoginRateLimiter.cs` và đăng ký DI; bỏ nhánh lockout/RegisterFailure/Reset trong `LoginCommandHandler`; xóa test limiter trong `UT/.../Login/LoginCommandHandlerTests.cs`, `IT/Caching/LoginAndSessionCacheTests.cs` (giữ phần SessionCache) và assertion 429 trong `IT/Auth/ChangePasswordTests.cs`. Giữ `AuditActions.RateLimited` nếu Gateway/nơi khác còn dùng, xóa nếu thành mồ côi. Rate limit IP ở Gateway và FE hiển thị 429 của Gateway giữ nguyên. Sửa spec cũ D13/§3.2/§7 cho khớp.
- [ ] Điều chỉnh login tối thiểu: tìm user để định vị, nhận khóa User, kiểm lại hash/IsActive mới nhất rồi tạo family/commit; ghi Redis sau commit. Không thay hợp đồng login (ngoài việc không còn 429 từ BE).
- [ ] Test PostgreSQL `LoginWithOldPassword_AfterPasswordChangeCommit_CannotCreateFamily` và `TwoPasswordChanges_OnlyOneAcceptsOldPassword`; dùng barrier có timeout, không sleep để hy vọng race. Chạy `dotnet test tests/QuanLyBenhVien.IntegrationTests --filter FullyQualifiedName~IdentityWriteConcurrency`.
- [ ] Cập nhật call site/test của chữ ký ChangePassword có `now`; không refactor mọi method User ngoài phạm vi.

## Task 2 — Bảng chờ invalidation và worker hoạt động trên khung mới

**File sửa:** `APP/Common/Caching/ICacheInvalidator.cs`; `PERS/Caching/CacheInvalidation.cs`; `PERS/Configurations/Common/CacheInvalidationConfiguration.cs`; `INF/Caching/GuardedCacheWrite.cs`, `CacheInvalidationWorker.cs`; DI của APP/PERS/INF; `INF/QuanLyBenhVien.Infrastructure.csproj`.

**File tạo:** `APP/Common/Caching/ICacheInvalidationStore.cs`, `ICacheKeyEvictor.cs`, `CacheInvalidationWorkItem.cs`, `CacheInvalidator.cs`, `CacheInvalidationProcessor.cs`; `PERS/Caching/CacheInvalidationStore.cs`; `INF/Caching/RedisCacheKeyEvictor.cs`; migration mới `AddCacheInvalidationClaims` trong `PERS/Migrations/`.

**Test tạo:** `IT/Caching/CacheInvalidationDeliveryTests.cs`, `IT/Caching/CacheGenerationRaceTests.cs`.

**Giao diện:** theo spec §5.2. CacheInvalidator và Processor ở Application dùng hai port; worker gọi Processor scoped. Enqueue sử dụng cùng DbContext với handler, không tự save. `FlushAsync` chỉ claim Id do request đó enqueue; worker claim batch cũ nhất.

- [ ] Viết test rollback transaction làm cả audit/invalidation biến mất; worker claim cùng lúc không xử lý cùng lease; claim hết hạn được nhận lại; ack claim cũ không xóa dòng của claim mới.
- [ ] Viết test Redis: eviction xóa key và tăng generation nguyên tử; writer đọc generation cũ không thể ghi cache trở lại. Test phải chạy Redis thật.
- [ ] Chạy `dotnet test tests/QuanLyBenhVien.IntegrationTests --filter "FullyQualifiedName~CacheInvalidationDelivery|FullyQualifiedName~CacheGenerationRace"`, ghi nhận fail trước implement.
- [ ] Implement store claim bằng transaction ngắn theo spec; commit trước Redis. Tạo migration bằng EF từ BE, review generated SQL. Chỉ thêm lease columns/index cần thiết; rollback an toàn cho dữ liệu bảng chờ đang có.
- [ ] Implement Redis adapter dùng transaction/Lua cho từng bộ DEL/INCR/EXPIRE; không dùng CreateBatch làm bằng chứng tính nguyên tử. Lease 30 giây; mỗi batch eviction có timeout dưới lease và không ack nếu mất ownership.
- [ ] Implement worker 5 giây/100 dòng, log Error từ lần 10 và vẫn retry. Giữ dòng khi Redis/DB ack lỗi; crash sau eviction được chạy lại an toàn. Fail lưu mã lỗi sạch, không lưu exception chứa cấu hình/credential.
- [ ] Gỡ Compile Remove cho worker đã chuyển; loại phần invalidator/processor cũ trùng trách nhiệm sau review. Không bật PermissionService/health chưa chuyển để “build thử”.
- [ ] Chạy lại test trên; `dotnet build benh_vien_be.sln`. Kiểm namespace/package bảo đảm Infrastructure không cần EF/Persistence.

## Task 3 — Handler, CSRF và gate đổi mật khẩu bắt buộc

**File sửa:** handler/validator/endpoint Auth, DI Presentation/Infrastructure/API, `APP/Features/Auth/ValidateSession/ValidateSessionQueryHandler.cs` chỉ khi test chứng minh cần chỉnh tương thích.

**File tạo:** `APP/Features/Auth/Common/IRequestOriginPolicy.cs`; `INF/Security/RequestOriginPolicy.cs`; `PRES/Auth/CsrfProtectionFilter.cs`; `PRES/Auth/CsrfProtectionMetadata.cs`; `API/Security/PasswordChangeGateMiddleware.cs`; `PRES/Auth/AllowWhilePasswordChangeRequiredMetadata.cs`.

**Test tạo:** `UT/Application/Auth/ChangePasswordHandlerTests.cs`, `IT/Auth/ChangePasswordFlowTests.cs`, `IT/Authorization/PasswordChangeGateTests.cs`.

**Giao diện dùng:** IUserRepository khóa User; ISessionRepository.GetActiveByUserForUpdateAsync; IUnitOfWork; IPasswordHasher; IAccessTokenIssuer; IAuditWriter; ICacheInvalidator; ICurrentUser; TimeProvider. `IRequestOriginPolicy.IsAllowed(string? origin): bool`; filter gắn bằng metadata trên route, không tham chiếu Infrastructure từ Presentation.

- [ ] Viết test sai Origin/CSRF → 403 không đổi DB; sai currentPassword → 400 có field error và audit Failed; newPassword trùng/chứa email → 400; không có/current family đã revoke/hết hạn/sv cũ → 401.
- [ ] Viết test thành công: hash đổi, MustChangePassword=false, sv tăng đúng 1, family hiện tại còn active, family khác revoked, invalidation gồm cả currentFid và permissions, audit và commit cùng nhau.
- [ ] Chạy bộ ChangePasswordFlow/PasswordChangeGate, ghi nhận fail đúng nhánh 501/stub trước implement.
- [ ] Implement handler: kiểm user/family/sv sau khóa; xác minh password; ghi thay đổi + audit + invalidation; SaveChanges và Commit; sau đó FlushAsync và issue access token sv mới. Sai currentPassword audit phải commit trước trả lỗi. Không tự retry request đổi password sau mất response.
- [ ] Endpoint gửi command, map Result; thành công chỉ JSON AccessTokenDto, không ghi lại cookie. CSRF dùng fid từ Bearer, policy Origin so allowlist cấu hình; không đọc secret vào tài liệu/test output.
- [ ] Gate sau authentication: user đang bắt đổi bị 403 `password_change_required` trên route bảo vệ khác; cho qua me/change-password/logout/logout-all theo metadata. Dùng read service Application để đọc cờ; nếu đọc lỗi thì không cho request đi tiếp. Anonymous/internal vẫn theo cơ chế riêng của chúng.
- [ ] Test hồi quy: 6 lần sai currentPassword liên tiếp đều trả 400 (không 429), lần 7 nhập đúng đổi thành công.
- [ ] Chạy lại test; không xem direct API test là bằng chứng Gateway từ chối token cũ (bằng chứng đó ở task 5).

## Task 4 — FE phân biệt đổi thành công và tải lại thông tin lỗi

**File sửa:** `FE/src/feature/Auth/ChangePassword.js`, `redux/actions.js`, `problem.js` nếu cần map field; `FE/src/feature/Auth/session/refreshCoordinator.js` chỉ để đồng bộ access token mới cùng family, không implement refresh ở mốc này.

**Test tạo/sửa:** `FE/src/feature/Auth/__tests__/ChangePassword.test.js`, `FE/src/feature/Auth/redux/__tests__/actions.test.js`.

**Giao diện:** giữ `changePassword(currentPassword,newPassword)`; tách bước POST đã thành công với `loadMe()`. Sau POST thành công, xóa password khỏi form, giữ token mới trong RAM; nếu loadMe thất bại, hiển thị “Mật khẩu đã đổi. Chưa tải lại được thông tin tài khoản.” và nút chỉ gọi lại loadMe. Không POST đổi mật khẩu lần nữa do lỗi loadMe.

- [ ] Viết test hai lần bấm chỉ một POST khi đang gửi; lỗi 400 hiện đúng ô; 403 đưa thông báo phù hợp; thành công chuyển `/start` và form không giữ password.
- [ ] Viết test POST 200 + GET me lỗi: hiển thị trạng thái đã đổi, thử lại chỉ gọi GET me, không gửi mật khẩu cũ lần hai.
- [ ] Chạy từ FE: `$env:CI='true'; npm test -- --runInBand --testPathPattern='ChangePassword|actions'`; ghi nhận fail trước sửa.
- [ ] Implement FE tối thiểu. Chỉ thông báo token mới cho các tab cùng family; không để broadcast cũ khôi phục phiên đã logout. Nếu chưa xây được cơ chế này riêng, ghi giới hạn và đưa test giao thoa sang plan 03, không tự rotate refresh cookie ở đây.
- [ ] Chạy lại Jest và ESLint; kiểm bằng trình duyệt với user bắt đổi và user đổi chủ động. Không chụp/log password hay token làm bằng chứng.

## Task 5 — Nghiệm thu độc lập với refresh và chuẩn bị mốc main

**File sửa:** `UT/QuanLyBenhVien.UnitTests.csproj`, `IT/QuanLyBenhVien.IntegrationTests.csproj`, `IT/Auth/ChangePasswordTests.cs`; thêm `IT/Gateway/PasswordChangeSessionTests.cs`. Cập nhật spec cũ §4.3 sau khi duyệt quyết định invalidation; `VALIDATION.md` chỉ ghi output thật.

- [ ] Bật lại PasswordPolicy và các test đổi mật khẩu đã port. Tách assertion cần refresh sang `IT/Auth/PasswordChangeRefreshTests.cs`, chỉ bật ở plan 03; ở mốc này kiểm DB current-family còn active và token mới gọi `/me` qua Gateway được.
- [ ] Test qua Gateway: sau invalidation thành công, token cũ của current family và family khác bị 401; token mới được 200; Redis miss/lỗi hỏi API đúng sv. API lỗi trả 503, không được coi như đã thu hồi thành công.
- [ ] Test Redis flush fail sau DB commit: password mới vẫn đúng, work item còn trong DB; khi worker xử lý xong token cũ bị từ chối. Ghi rõ khoảng cache chưa đồng bộ, không hứa “ngay lập tức” khi eviction thất bại.
- [ ] Chạy Full + Frontend + build FE theo plan.md. Đếm test thực sự chạy, không để filter không chọn test mà báo PASS.
- [ ] Đi lại: admin seed → đổi → `/start`; login mật khẩu cũ bị từ chối; mật khẩu mới login được; F5 trước plan 03 được ghi giới hạn chứ không báo lỗi đổi password.
- [ ] Review diff/migration và danh sách file mốc Git theo plan.md. Chỉ commit và merge thẳng vào `main` khi có bằng chứng đạt; không stage toàn repo; xác nhận với người dùng ngay trước khi push.

**Trạng thái ban đầu:** mọi task và nghiệm thu NOT_RUN. Rủi ro còn lại sau mốc: refresh/logout chưa làm; các nghiệp vụ ngoài IdentityAccess chưa có BE; cache eventual invalidation theo giới hạn đã mô tả.
