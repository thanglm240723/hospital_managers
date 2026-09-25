---
name: complex-plan
description: Chạy một lượt thiết kế/lập kế hoạch Opus ở context riêng cho thay đổi khó, xuyên layer, nhạy cảm bảo mật, nặng về giao dịch/khóa, hoặc ảnh hưởng rộng — trước khi code.
disable-model-invocation: true
context: fork
agent: architecture-planner
background: false
argument-hint: "<vấn đề/thay đổi>"
---
Lập kế hoạch cho `$ARGUMENTS` dựa trên repo thực tế và đặc tả (mã NV/AT, đặc tả kỹ thuật v3.1, spec theo chủ đề).

Kết quả phải triển khai được ngay, gồm: phạm vi và những gì không làm; mục `OPEN-xx` liên quan; layer/luồng bị ảnh hưởng;
phương án và đánh đổi; ranh giới transaction, thứ tự khóa, idempotency, việc sau commit; ràng buộc CSDL và migration;
quyền và audit; tác động Gateway/frontend; test và tiêu chí nghiệm thu; rủi ro; các bước theo thứ tự, mỗi bước có cách kiểm tra.

Không sửa file. Viết bằng tiếng Việt.
