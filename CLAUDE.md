# HMS — Chính sách cho Claude Code

@AGENTS.md

Phần dưới chỉ bổ sung những gì riêng cho Claude Code. Quy tắc chung (nguồn sự thật, kiến trúc, PostgreSQL,
API, frontend, bảo mật, test) nằm trong `AGENTS.md` ở trên — không lặp lại ở đây.

## Quy tắc theo đường dẫn

`.claude/rules/*.md` tự nạp khi làm việc với file khớp `paths`:

| Rule | Áp dụng cho |
|---|---|
| `domain.md` | `QuanLyBenhVien.Domain/**` |
| `application.md` | `QuanLyBenhVien.Application/**` |
| `persistence.md` | `QuanLyBenhVien.Persistence/**` |
| `infrastructure.md` | `QuanLyBenhVien.Infrastructure/**` |
| `presentation.md` | `QuanLyBenhVien.Presentation/**` |
| `api.md` | `QuanLyBenhVien.API/**` (host) |
| `gateway.md` | `QuanLyBenhVien.Gateway/**` |
| `workers.md` | worker/`BackgroundService`, Outbox, hàng đợi |
| `hms-business-invariants.md` | code nghiệp vụ ở Domain/Application/Persistence/Infrastructure/Presentation |

Backend đang chuyển sang khung mới — trước khi sửa code backend, đọc `ARCHITECTURE.md` §9 để biết phần nào là mã cũ.
| `tests.md` | `tests/**` |
| `frontend.md` | `benh_vien_fe/src/**` |

## Model và agent

Repo dùng alias model logic, không ghi model ID vật lý.

- Main session: `opus` (đặt trong `.claude/settings.json`). Main nắm yêu cầu, phạm vi, kiến trúc, điều phối
  và tổng hợp cuối; không tự làm việc cơ học nặng khi có thể giao đi.
- `architecture-planner` (`opus`): thiết kế khó, xuyên layer, giao dịch/khóa, bảo mật.
- `reviewer` (`opus`): review diff thật.
- `dotnet-implementer`, `react-implementer`, `debugger`, `database-analyst`, `platform-reviewer` (`sonnet`).
- `mechanical-editor` (`haiku`): sửa lặp lại theo mẫu đã định nghĩa đầy đủ.
- Không đặt `CLAUDE_CODE_SUBAGENT_MODEL`, provider hay biến context-window trong repo.

## Điều phối và tiết kiệm context

- Điều phối phẳng: subagent không có tool Agent, không sinh agent khác.
- Không giao việc nhỏ. Chỉ giao khi agent chuyên môn hoặc điều tra dài dòng giúp giảm context đáng kể.
- Mặc định tối đa một implementer + một reviewer; chạy song song chỉ khi các việc thật sự độc lập.
- Nhớ: mặc định người dùng tự viết code (xem `AGENTS.md` → Cách làm việc). Chỉ gọi implementer khi được yêu cầu.

## Skill của repo

| Skill | Khi dùng |
|---|---|
| `/feature <use case>` | Lập kế hoạch/triển khai một use case theo vertical slice |
| `/complex-plan <vấn đề>` | Thiết kế khó trước khi code (chạy `architecture-planner` ở context riêng) |
| `/review-diff [trọng tâm]` | Review diff hiện tại (chạy `reviewer` ở context riêng) |
| `/ef-migration <mô tả>` | Tạo/kiểm tra migration EF Core code-first |
| `/long-task <slug>` | Việc dài nhiều checkpoint, có nguy cơ bị nén context |
| `api-contract-change` | Thay đổi route/DTO/status/quyền/phân trang ảnh hưởng frontend |
| `transaction-write` | Command có transaction, khóa, idempotency, việc sau commit |
| `postgres-concurrency` | Khóa hàng, isolation, partial index, xmin, retry, SKIP LOCKED |
| `object-storage` | Upload/tải tệp bệnh án, UploadSession, quarantine (module Documents) |
| `react-feature` | Màn hình/feature mới ở `benh_vien_fe` |

## Công cụ

- Kiểm tra: `powershell -NoProfile -ExecutionPolicy Bypass -File tooling/validate.ps1 -Mode Quick` (log đầy đủ ghi vào `.claude/work/logs/`, chỉ trả tóm tắt).
- Đọc DB dev chỉ-đọc qua MCP DBHub: xem `README.md` → "Agent và MCP".
- Hook `.claude/hooks/guard-shell.ps1` chặn cứng lệnh nguy hiểm (git phá lịch sử, xóa đệ quy, DDL/DML qua psql,
  `dotnet ef database drop`, xóa volume Docker, kubectl/helm/terraform ghi). Hook chạy cả khi bỏ qua hỏi quyền.

## Quy trình theo loại việc

- Nhỏ/cục bộ: đọc → sửa → kiểm tra đích → tóm tắt ngắn.
- Feature/sửa lỗi thường: làm rõ phạm vi + nghiệm thu → implementer nếu không tầm thường → test đích →
  `/review-diff` khi thay đổi đáng kể → tổng hợp.
- Rủi ro cao (giao dịch tiền/giường/phiên, quyền, schema): `/complex-plan` trước → implementer → `/review-diff`.
- Debug: thu bằng chứng → cô lập nguyên nhân gốc → sửa tối thiểu → test hồi quy → kiểm tra.

Không tuyên bố "sẵn sàng production" chỉ từ đọc code.
