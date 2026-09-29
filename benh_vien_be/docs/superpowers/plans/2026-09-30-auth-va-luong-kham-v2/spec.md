# Spec V2 cho xác thực và vào hệ thống

Ngày: 2026-09-30. Các quyết định nghiệp vụ ghi “đã chốt” đến từ hội thoại; chi tiết kỹ thuật dưới đây là đề xuất triển khai cần review cùng từng plan. Tài liệu không đổi phiên bản API.

## 1. Nguồn và phạm vi

- BRD `Dac_ta_nghiep_vu_v2.0.docx`: NV-03/NV-04 phân quyền/phân công; NV-08–NV-13 tiếp nhận, hàng chờ và khám; NV-17–NV-20 kết quả/bệnh án.
- TSD `Dac_ta_ky_thuat_v3.1.docx`: kiến trúc, xác thực, transaction, PostgreSQL, cache, audit và version hồ sơ.
- [Spec auth đã duyệt](../2026-09-23-auth-permission-redesign/spec.md): §3.1–3.10, §4.1–4.4, §5 và §9.
- [Plan login nền](../2026-09-28-login-khung-moi/plan.md) là bằng chứng mốc nền, không phải bằng chứng các slice còn lại đã hoàn tất. Mã “NV-03 (đăng nhập)” trong plan cũ không đúng tên NV-03 của BRD hiện tại; bộ plan này dùng điều khoản auth spec để nghiệm thu đăng nhập.
- [Quy ước toàn bộ plan](plan.md) và AGENTS.md áp dụng xuyên suốt. .NET 10, PostgreSQL 17, EF Core 10, MediatR 12.5.0; FE React 16/Redux/react-router 4. Không nâng dependency tiện tay.

## 2. Quyết định đã chốt với người dùng

1. Hoàn tất đổi mật khẩu trước, sau đó logout, refresh và luồng vào hệ thống theo quyền; mỗi tính năng có plan riêng.
2. Tài liệu là V2; `POST /api/v1/auth/login` và các route còn lại giữ V1.
3. Nhiều khu vực được vào: chọn mỗi lần đăng nhập. Một khu vực: vào thẳng. Không có khu vực: màn chưa được cấp quyền. Không nhớ khu vực qua một lần đăng nhập mới.
4. Ngoại trú khám trước; bác sĩ yêu cầu đo chỉ số sinh hiệu nào thì mới tạo công việc đo tương ứng. Không ép mọi bệnh nhân đo trước khám.
5. Khi chờ đo, bác sĩ nhận người tiếp theo. Đo xong quay lại cùng bác sĩ; không chen ngang ca đang khám.
6. Sinh hiệu xong và đọc kết quả CLS cùng nhóm Quay lại, FIFO theo lúc thực sự vào nhóm. Quay lại → Ưu tiên → Thường; cấp cứu vẫn theo quy trình riêng.
7. Trong lúc chờ, lượt khám còn mở và bệnh án là nháp đã lưu. Xác nhận bệnh án, hoàn tất sinh hiệu, kết quả CLS, tài chính và cấp thuốc là các vòng đời độc lập.
8. (chốt 2026-09-30) **Bỏ đếm số lần sai theo email** ở cả login và đổi mật khẩu: không còn `ILoginAttemptLimiter`/`rl:email`, không trả 429 `rate_limited` từ BE. Rate limit theo IP ở Gateway (§4.4 spec cũ) giữ nguyên.
9. (chốt 2026-09-30) Bảng `CacheInvalidations` thêm lease `ClaimId`/`ClaimedUntilUtc` như §5.2.
10. (chốt 2026-09-30) Gate bắt đổi mật khẩu làm ngay ở plan 01 (§5.3).
11. (chốt 2026-09-30) Claude viết code theo bộ plan khi người dùng gọi skill triển khai; mỗi mốc đạt nghiệm thu được commit và merge thẳng vào `main`, không qua PR.
12. (chốt 2026-09-30) `activate`/`deactivate` tài khoản không yêu cầu `If-Match`; các mutation quản trị khác vẫn dùng `If-Match`/412.
13. (chốt 2026-09-30) Role `admin` luôn giữ các quyền IdentityAccess lõi; UI/API không bỏ được các quyền này.
14. (chốt 2026-09-30) Tạo tài khoản: server sinh mật khẩu ban đầu ngẫu nhiên, trả plaintext **đúng một lần** trong response tạo; DB chỉ lưu hash; `MustChangePassword=true`.
15. (chốt 2026-09-30) Giữ bộ lọc trạng thái user `active`/`must_change_password`/`locked`.

Chi tiết quyết định 4–7 xem [luồng khám và hồ sơ](luong-kham-va-ho-so.md).

## 3. Chỗ lệch và mâu thuẫn phải hiển thị rõ

| Nguồn/hành vi cũ | Quyết định cho V2 | Trạng thái |
|---|---|---|
| BRD NV-08/NV-13: tiếp nhận tạo phiếu sinh hiệu trước khám | Tiếp nhận vào hàng khám; sinh hiệu chỉ sau yêu cầu bác sĩ | Người dùng chốt; phải cập nhật BRD/TSD trước nghiệm thu module lâm sàng |
| FE queueLogic: sinh hiệu về được xếp trước đọc CLS | Cùng nhóm quay lại, FIFO | Người dùng chốt; test hiện có cũng phải thay theo quyết định, không lấy test cũ làm chuẩn |
| Auth spec §3.8: invalidation mọi family kể cả hiện tại; bảng §4.3 chỉ ghi family khác | Theo mô tả chi tiết §3.8: mọi family liên quan, kể cả hiện tại, và `perm:{uid}` | Mâu thuẫn nội bộ đã nêu; sửa bảng spec cũ thành điều này khi duyệt plan 01, không âm thầm bỏ phiên hiện tại |
| Auth spec D13/§3.2 bước 3: 5 lần sai theo email/15 phút → 429; test cũ đổi mật khẩu yêu cầu 5 sai, lần 6 bị chặn | Bỏ hoàn toàn limiter theo email ở login và đổi mật khẩu; chỉ còn rate limit IP ở Gateway | Người dùng chốt 2026-09-30; plan 01 gỡ limiter khỏi code/test và sửa spec cũ D13/§3.2/§7 |
| FE role hệ thống bị khóa toàn bộ chỉnh sửa | Spec cũ chỉ cấm đổi Code/xóa; cho quản lý Name/permissions theo quyền | Plan 05 bám spec, ghi rõ khác mock; quyền admin lõi phải được bảo vệ |
| FE reset mật khẩu do admin | Auth spec §8 đặt ngoài phạm vi | Không nối endpoint tự đặt; ẩn/disable với thông báo chưa hỗ trợ |
| Mã quyền nghiệp vụ trong FE là dự kiến, BE chỉ có IdentityAccess | Chỉ dùng quyền BE đã công bố ở môi trường thật | Quyền/phân công module sau phải có spec riêng |

Không có thay đổi nào ở đây cho phép bỏ qua OPEN-xx hoặc xây Appointment.

## 4. Contract và vòng đời auth

| Endpoint | Đầu vào | Thành công | Lỗi chính |
|---|---|---|---|
| POST `/api/v1/auth/change-password` | `{currentPassword,newPassword}`; Bearer, Origin, CSRF | `200 {accessToken,expiresAtUtc,mustChangePassword:false}`; giữ refresh cookie/family | 400 field error, 401 phiên không hợp lệ, 403 csrf_failed |
| POST `/api/v1/auth/logout` | Refresh cookie, Origin, CSRF nếu có family nhận diện được | 204, xóa hai cookie | 403 với Origin/CSRF sai khi xử lý family nhận diện được |
| POST `/api/v1/auth/logout-all` | Bearer, Origin, CSRF | 204, thu hồi mọi family và xóa cookie | 401/403 |
| POST `/api/v1/auth/refresh` | Refresh cookie, Origin, CSRF | Body access token như login, cookie refresh mới, cùng family/hạn tuyệt đối | 401 cookie không hợp lệ/reuse/hết hạn, 403 csrf_failed, 429 rate limit Gateway |
| GET `/api/v1/auth/me` | Bearer | DTO hiện có, roles từ DB và permissions hiệu lực | 401 hoặc 503 lỗi phụ thuộc |

Không đổi lỗi nghiệp vụ dự kiến thành exception. `Error` cần mang field errors để Presentation tạo Problem Details `errors.currentPassword`/`errors.newPassword`. Không trả refresh token trong JSON, không log mật khẩu/token/cookie.

Mật khẩu mới: 10–128 ký tự, khác mật khẩu hiện tại theo kiểm chứng plaintext/hash, không chứa local-part email không phân biệt hoa thường. Không trim mật khẩu. Phần xác nhận nhập lại là kiểm tra FE; server vẫn kiểm tra toàn bộ chính sách mật khẩu mới.

Change-password ghi hash, `SecurityVersion++`, `MustChangePassword=false`, revoke family khác, audit và invalidation trong một transaction. Cấp access token mới sau commit. Không rotate hoặc tự nạp lại cache phiên hiện tại trong handler. Đổi mật khẩu không biến thành logout-all.

Refresh: khóa và kiểm tra token đã trình; token consumed/revoked không được bỏ qua chỉ vì có token mới. Reuse phải commit revoke + audit + invalidation rồi mới trả 401. Family hết hạn sau 7 ngày kể từ login, refresh không kéo dài hạn đó. Refresh thành công không ghi AuditRecord riêng theo spec cũ. Không thêm grace window cho reuse.

Logout với cookie thiếu/không nhận diện được vẫn clear cookies/204 và không mutate family khác. Cookie nhận diện được phải qua Origin/CSRF, kể cả token cũ trong cùng family; logout không bị biến thành một lần rotate. CSRF sai không được revoke family.

## 5. Thiết kế chung do plan 01 cung cấp

### 5.1 Khóa và truy cập DB

`IUserRepository.GetForUpdateAsync(Guid id, CancellationToken ct)` được thêm, yêu cầu transaction, khóa hàng User và trả dữ liệu mới nhất có tracking. Nếu user đã tracking do bước tra email trước đó, phải reload sau khi nhận khóa; không dùng password/SecurityVersion cũ từ change tracker.

Thứ tự khóa của auth: User → SessionFamilies theo Id tăng → RefreshTokens theo Id tăng. Login cũng tham gia khóa User và kiểm lại mật khẩu/IsActive sau khóa để không tạo family bằng credential cũ sau đổi mật khẩu. Tra token/family trước khóa chỉ phục vụ định vị; phải kiểm lại sau khóa. Không gọi Redis trong transaction. Khi quản trị thêm khóa bảo vệ admin, thứ tự là admin-safety → User → families/tokens; không đảo chiều giữa các command.

`ICurrentUser` bổ sung `int? SecurityVersion`, adapter ở API đọc claim `sv`. Handler đổi mật khẩu kiểm tra user active, family thuộc user, còn hạn/active và SecurityVersion khớp dữ liệu mới nhất. Điều này không phụ thuộc việc Gateway đang giữ cache cũ.

### 5.2 Invalidation bền vững

Tách code cũ đúng kiến trúc: Application điều phối qua port; Persistence chỉ ghi/đọc bảng chờ; Infrastructure chỉ Redis và worker, không EF/DbContext.

- `ICacheInvalidator.InvalidateSession(Guid)` và `.InvalidatePermissions(Guid)` thêm dòng vào unit of work hiện tại. `FlushAsync(ct)` chỉ chạy sau commit.
- `ICacheInvalidationStore` tại Application: `Enqueue(string key, DateTimeOffset now)` trả Id; `ClaimAsync(IReadOnlyCollection<Guid>? ids, int take, Guid claimId, DateTimeOffset now, TimeSpan lease, CancellationToken ct)` trả các `CacheInvalidationWorkItem(Id,Key,Attempts)`; `CompleteAsync(ids,claimId,ct)`; `FailAsync(ids,claimId,string safeError,ct)`. `ids=null` dành cho worker, ids cụ thể dành cho flush của request.
- Claim dùng transaction ngắn, `FOR UPDATE SKIP LOCKED` + `UPDATE ... RETURNING`, commit trước khi gọi Redis. Đề xuất thêm `ClaimId`, `ClaimedUntilUtc` nullable vào bảng hiện có bằng migration mới; lease kỹ thuật 30 giây, Redis mỗi batch timeout ngắn hơn lease. Ack/fail chỉ áp dụng claim còn sở hữu; có thể xử lý lặp vì eviction idempotent.
- `ICacheKeyEvictor.EvictAsync(IReadOnlyCollection<string> keys, CancellationToken ct)` do Infrastructure implement. Mỗi key xóa + tăng `:gen` + TTL generation 1 ngày phải nguyên tử bằng Redis transaction/Lua; `CreateBatch` hiện có không bảo đảm nguyên tử.
- Worker chạy lúc khởi động và mỗi 5 giây, tối đa 100 dòng, báo Error khi Attempts ≥ 10 nhưng không xóa bỏ công việc. Retry không giữ DB transaction khi gọi Redis. Lưu lỗi đã làm sạch, không ghi endpoint/credential từ exception thô vào bảng.
- `CacheInvalidationProcessor.ProcessPendingAsync(int batchSize, CancellationToken ct): Task<int>` điều phối claim → eviction → ack/fail, trả số dòng đã ack thành công. Request flush dùng cùng quy trình nhưng giới hạn các Id đã enqueue trong request; không chạy lại transaction nghiệp vụ khi eviction thất bại.
- Flush lỗi sau commit: không rollback/báo như mật khẩu chưa đổi; giữ công việc chờ. Không tuyên bố thu hồi token tức thì tuyệt đối khi Redis đang lỗi/mất đồng bộ. Test phải đo cả lúc Redis không đọc được và lúc phục hồi trước/sau drain; đây là giới hạn cache trong spec hiện tại.

### 5.3 CSRF và bắt đổi mật khẩu

Presentation có filter dùng port `ICsrfTokenService` và `IRequestOriginPolicy.IsAllowed(string? origin)`, không tham chiếu Infrastructure options. Implement Origin policy ở Infrastructure từ `AuthOptions.AllowedOrigins`, giữ nguyên HTTPS/Secure cookies.

Plan 01 chỉ cần filter dùng family từ access-token claim. Plan 02 mở rộng filter cookie qua lookup Application/Persistence; không inject repository Domain trực tiếp vào Presentation. Lookup: `IRefreshSessionLookup.FindAsync(string tokenHash, CancellationToken ct)` trả `RefreshSessionRef(Guid FamilyId, Guid UserId, SessionStatus Status)` hoặc null.

API có gate `MustChangePassword` sau authentication, trước thực thi endpoint. Ban đầu đọc qua `IAuthReadService.GetMeAsync` để biết cờ; plan 04 chuyển sang permission service dùng chung. Miễn gate cho me, change-password, logout/logout-all; login/refresh công khai tiếp tục đúng contract. Không coi role admin là ngoại lệ. Test bằng endpoint bảo vệ chỉ đăng ký trong test, không thêm route giả vào production.

## 6. Login và khu vực

Một login chung. Thứ tự: xác thực → bắt đổi mật khẩu nếu cần → tính khu vực có quyền và đã sẵn sàng → 0/1/n khu vực. Quyền lấy từ `/me`, không hard-code `if role === doctor` để cho phép dữ liệu.

Mỗi login mới clear workspace; refresh/F5 là tiếp tục phiên, không phải lần nhập credential mới. Không dùng localStorage để nhớ lựa chọn. Nếu RAM mất, tính lại an toàn; với nhiều khu vực hiện lại picker. Return URL chỉ dùng khi hợp lệ, có quyền và không vượt bước chọn khu vực của lần login mới.

Plan 04 tạo route metadata dùng chung cho guard/menu/default route. Menu lọc theo khu vực đang chọn. Chọn khu vực phải thật sự điều hướng. Chỉ có `roles.read` phải vào roles, không luôn vào users. Khi quyền đổi hoặc khu vực bị thu hồi, loại selectedId cũ, không loop redirect. Nút Kiểm tra lại ở no-access tải `/me` rồi đi đúng picker/màn đích.

Các màn đích mong muốn theo nhiệm vụ: quản trị → users/roles theo quyền; lễ tân → tìm hồ sơ; điều dưỡng ngoại trú → hàng sinh hiệu; bác sĩ → hàng của phòng/buổi được phân công; CLS → công việc được phép thực hiện; thu ngân → khoản cần thu; dược → đơn đủ điều kiện cấp; điều dưỡng nội trú → người bệnh được phân công; quản lý chuyên môn → công việc cần duyệt. Ngoài quản trị hiện chưa có contract/quyền BE được duyệt; đây là định hướng UI, không là quyền tài nguyên đã triển khai.

## 7. Quản trị

Plan 05/06 bám endpoint/method của auth spec §4.2. Sửa FE cho khớp, không biến contract mock thành nguồn sự thật. RoleIds là Guid thật; code role của `SystemRoles` không đồng nghĩa Id. Danh mục quyền đọc từ BE; không hard-code danh sách mock vào request.

Không reset mật khẩu admin, quên mật khẩu, MFA, quản lý phiên từng thiết bị hoặc quyền tài nguyên lâm sàng trong bộ plan này. Grant/revoke là quyền cấp thêm; revoke grant không phải deny quyền đến từ role. UI phải thể hiện nguồn quyền.

Role hệ thống không đổi Code/không xóa. Khi sửa permissions phải bảo vệ khả năng quản trị: `admin` giữ các quyền IdentityAccess lõi; seeder hiện tự bổ sung các quyền này nên không cho UI tạo thay đổi sẽ bị âm thầm đảo khi restart. Quy tắc bảo vệ role admin đã được người dùng chốt (§2 mục 13), không áp dụng đọc bệnh án cho admin.

## 8. Kiểm chứng và giới hạn

Mỗi plan có test riêng; mọi ô nghiệm thu ban đầu là NOT_RUN. Gỡ `Compile Remove` theo tính năng thực sự được chuyển. Các bài test phụ thuộc endpoint tương lai phải được tách tên/file và ghi rõ kế hoạch chạy lại, không xóa assertion nghiệp vụ.

Đặc biệt cần test DB rollback, hai request cùng lúc, cache stale generation, cookie bị xóa/đổi, sửa quyền khi tab đang mở và auth response về sau logout. Source không được sửa trong lượt tạo tài liệu này. Không triển khai hoặc commit/push khi chỉ mới viết plan.
