---
name: review-diff
description: Review CHỈ ĐỌC diff git hiện tại bằng Opus ở context riêng và chỉ trả về phát hiện đáng kể.
disable-model-invocation: true
context: fork
agent: reviewer
background: false
argument-hint: "[trọng tâm, tùy chọn]"
---
Review diff hiện tại (đã stage, chưa stage và file mới liên quan). Trọng tâm tùy chọn: `$ARGUMENTS`.

Đọc đủ code xung quanh để xác minh hành vi. Trả về theo `Phải sửa`, `Nên sửa`, `Tùy chọn`; bỏ mục rỗng, ghi
`Không có phát hiện đáng kể` khi phù hợp. Mỗi phát hiện có đường dẫn file, dòng/symbol và kịch bản lỗi cụ thể.
Không sửa file. Viết bằng tiếng Việt.
