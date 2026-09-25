---
name: postgres-concurrency
description: Áp dụng quy tắc PostgreSQL 17 của HMS cho khóa hàng (FOR UPDATE), advisory lock, isolation (READ COMMITTED/REPEATABLE READ/SERIALIZABLE), concurrency token xmin, partial unique index, hàng đợi SKIP LOCKED, retry theo SQLSTATE, pool kết nối và migration an toàn.
---
- **Isolation**: mặc định READ COMMITTED (MVCC, mỗi câu lệnh một snapshot). Cần tổng + trang nhất quán ⇒ hai SELECT trong
  transaction `REPEATABLE READ READ ONLY`. `SERIALIZABLE` chỉ khi chứng minh cần và có retry `40001`.
- **Khóa bi quan**: `SELECT 1 … FOR UPDATE` qua `ExecuteSqlInterpolatedAsync` rồi mới nạp entity (không ghép được với `Include`).
  `FOR NO KEY UPDATE` khi không đổi khóa. Nhiều hàng ⇒ `ORDER BY "Id"`. Khóa logic ⇒ `pg_advisory_xact_lock(hằng)` trong transaction.
- **Khóa lạc quan**: `xmin` (`uint` + `IsRowVersion()`); `DbUpdateConcurrencyException` ⇒ 412, không tự merge.
- **Một bản ghi đang hiệu lực**: partial unique index `HasFilter("\"EndedAtUtc\" IS NULL")` — code kiểm tra trước chỉ để
  báo lỗi đẹp, index mới là bảo đảm.
- **Hàng đợi/Outbox**: CTE `SELECT … ORDER BY … LIMIT n FOR UPDATE SKIP LOCKED` + `UPDATE … RETURNING` đặt claim/lease,
  commit trước khi xử lý. `SKIP LOCKED` chỉ cho hàng đợi, không cho truy vấn cần kết quả đầy đủ.
- **Bộ đếm số thứ tự**: `UPDATE "Queues" SET … = … + 1 WHERE … RETURNING …` — không `MAX()+1`.
- **SQLSTATE**: `40P01` deadlock, `40001` serialization ⇒ retry cả transaction (tối đa 3 lần, backoff jitter) nếu use case cho phép;
  `55P03` lock_timeout ⇒ 409 `operation_in_progress`; `23505` unique, `23503` FK, `23514` CHECK ⇒ 409/400 có mã, không retry.
- **Timeout**: cân nhắc `lock_timeout`, `statement_timeout`, `idle_in_transaction_session_timeout` ở connection string/role.
- **Pool**: Npgsql pool mặc định 100/connection string; integration test nhiều `ApiFactory` cần giới hạn `Maximum Pool Size`
  (xem `ContainersFixture`). Không giữ kết nối qua lời gọi ngoài.
- **Migration an toàn**: index lớn trên bảng có dữ liệu ⇒ cân nhắc `CREATE INDEX CONCURRENTLY` (migration SQL riêng,
  `suppressTransaction: true`); thêm cột NOT NULL ⇒ có default hoặc expand → backfill → contract.
- **Bằng chứng**: mọi khẳng định về hành vi khóa/isolation/index phải có integration test trên PostgreSQL thật.
- HA/replica: chưa có trong MVP; nếu thêm thì ghi và migration đi Primary, đọc replica chỉ cho truy vấn chấp nhận trễ.
