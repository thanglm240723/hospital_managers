# Thêm adapter / công nghệ mới

API là composition root: chỉ tham chiếu và đăng ký những adapter mà **tiến trình này thật sự dùng**.

Khi thêm một công nghệ mới (ví dụ object storage cho module Documents, Outbox, SMS/email):

1. Khai port (interface) ở Application — `Common/<Nhóm>/` nếu dùng chung, `Features/<Feature>/Common/` nếu riêng feature.
2. Implement ở Infrastructure (hoặc Persistence nếu chỉ là truy cập PostgreSQL), kèm một extension DI công khai và
   options có validate khi khởi động.
3. Gọi extension đó trong `Composition/`; secret lấy từ user-secrets/secret store, không ghi vào `appsettings*.json`.
4. Thêm health check nếu phụ thuộc có thể lỗi (`Health/`); phụ thuộc tùy chọn lỗi ⇒ `Degraded`, không làm chết tiến trình.

Tác vụ nặng hoặc chạy lâu (quét tệp, xử lý ảnh/PDF, import lớn) nên chạy ở host worker riêng thay vì trong API.
Nhà cung cấp object storage cuối cùng chưa chốt (`OPEN-01`) — không hard-code.
