---
name: long-task
description: Tạo file kế hoạch/phát hiện/tiến độ gọn nhẹ, bền qua nén context hoặc khởi động lại phiên, cho một việc thật sự dài. Chỉ người dùng gọi.
disable-model-invocation: true
argument-hint: "<slug-ngắn-của-việc>"
---
Với `$ARGUMENTS`, dùng thư mục `.claude/work/$ARGUMENTS/`:

- `plan.md`: mục tiêu, ràng buộc, tiêu chí nghiệm thu, các giai đoạn/checkpoint.
- `findings.md`: chỉ bằng chứng và quyết định có giá trị lâu dài; không dán log hay code thô.
- `progress.md`: đã xong / đang làm / tiếp theo, trạng thái kiểm tra, vướng mắc.

Chỉ cập nhật ở checkpoint thật. Output lệnh dài ghi vào `.claude/work/logs/` và tham chiếu đường dẫn thay vì dán vào context.
Xong việc thì xóa hoặc lưu trữ file khi không còn giá trị. `.claude/work/` không commit.

Nếu việc đã có plan chính thức (`docs/superpowers/plans/<chủ-đề>/`) thì `progress.md` chỉ tham chiếu task trong plan đó,
không chép lại nội dung plan. Viết bằng tiếng Việt.
