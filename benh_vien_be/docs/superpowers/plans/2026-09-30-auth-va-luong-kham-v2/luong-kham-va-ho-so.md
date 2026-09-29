# Luồng khám, sinh hiệu và trạng thái hồ sơ đã chốt

Ngày 2026-09-30. Đầu vào thiết kế nghiệp vụ cho các plan sau; chưa phải implementation. V2 là bản tài liệu. “Giấy tờ” trong phạm vi này là phiếu điện tử và bản in từ hệ thống; chưa thiết kế việc giữ/trả CCCD, thẻ BHYT hay giấy gốc.

## 1. Những quyết định người dùng đã đồng ý

1. Lễ tân tiếp nhận trực tiếp, không đặt lịch. Người bệnh vào hàng khám trước.
2. Bác sĩ nhận lượt và khám; chỉ tạo yêu cầu sinh hiệu nếu bác sĩ yêu cầu. Yêu cầu ghi rõ các chỉ số cần đo.
3. Trước khi chuyển sang đo, lưu thành công nội dung khám nháp và yêu cầu đo vào DB. Nếu lưu lỗi, giữ nội dung trên màn và cho thử lại, không báo đã chuyển.
4. Bác sĩ được khám người tiếp theo trong lúc người bệnh chờ đo. Cùng lượt khám vẫn mở, giữ bác sĩ phụ trách; không tạo hồ sơ bệnh nhân/lượt khám mới.
5. Điều dưỡng chỉ xử lý công việc được phân công, xác minh ít nhất hai định danh theo NV-09 trước đo.
6. Hoàn tất đo đưa kết quả về cùng lượt và tạo một phiếu quay lại bác sĩ đã yêu cầu. Kết quả có người thực hiện, thời điểm và đơn vị đo.
7. Sinh hiệu hoàn tất và quay lại đọc CLS ở cùng nhóm Quay lại, FIFO theo thời điểm vào nhóm; nhóm này đứng trước Ưu tiên rồi Thường. Không chen ngang ca đang khám. Cấp cứu vẫn có quy trình riêng.
8. Khi bác sĩ vắng, áp dụng điều phối/bàn giao có dấu vết theo NV-12; không tự gán một bác sĩ bất kỳ.

## 2. Bảng trạng thái hiển thị đề xuất

Tên hiển thị là thiết kế, chưa chốt tên enum/schema. Không gom mọi cột dưới đây thành một enum duy nhất.

| Tình huống | Lượt khám | Phiếu khám/bệnh án | Yêu cầu sinh hiệu | Phiếu hàng chờ |
|---|---|---|---|---|
| Tiếp nhận xong | Chờ khám | Chưa có nội dung lâm sàng | Chưa có | Chờ gọi khám |
| Bác sĩ nhận lượt | Đang khám | Nháp | Chưa có hoặc kết quả lần trước | Đang thực hiện khám |
| Gửi đi đo | Chờ sinh hiệu | Nháp đã lưu | Chờ thực hiện | Phiếu công việc sinh hiệu đang chờ; không giữ ghế khám đang phục vụ |
| Điều dưỡng nhận | Đang đo sinh hiệu | Nháp đã lưu | Đang thực hiện, số đo đang nhập | Đang thực hiện đo |
| Hoàn tất đo | Chờ khám lại | Nháp, tham chiếu kết quả đã hoàn tất | Đã hoàn tất | Một phiếu Quay lại đang hiệu lực |
| Bác sĩ gọi lại | Đang khám | Tiếp tục nháp của lượt này | Giữ kết quả/lịch sử | Đang thực hiện khám |
| Có CLS cần chờ | Chờ kết quả | Nháp, trừ phiên bản đã được xác nhận độc lập | Giữ lịch sử | Công việc CLS và lần quay lại quản lý riêng |
| Bác sĩ kết luận | Đã kết luận | Chỉ thành Đã xác nhận khi bác sĩ chủ động xác nhận | Giữ lịch sử | Kết thúc công việc khám tương ứng |
| Hoàn thành nghĩa vụ còn lại | Hoàn tất | Giữ phiên bản | Giữ lịch sử | Không còn công việc bắt buộc chưa xử lý |

Việc kết thúc một lần phục vụ để chờ công đoạn khác không phải kết thúc toàn lượt. Schema module hàng chờ phải biểu diễn lịch sử chuyển/hoàn tất công việc và liên kết phiếu kế tiếp, không tái dùng một phiếu đang hoạt động ở hai phòng.

## 3. Trạng thái từng giấy tờ

- **Phiếu khám/bệnh án:** Nháp → Đã xác nhận → Bản sửa bổ sung. Đã lưu chỉ có nghĩa dữ liệu bền vững; chưa đồng nghĩa chính thức. Bản Confirmed bất biến; sửa tạo phiên bản mới, có tác giả/thời điểm/lý do/PreviousVersionId theo NV-19/NV-20.
- **Sinh hiệu:** công việc Chờ thực hiện → Đang thực hiện → Đã hoàn tất; thao tác hoàn tất của điều dưỡng không xác nhận bệnh án thay bác sĩ. Chỉ số không đo được phải ghi rõ lý do, không điền 0 hoặc số giả. Danh sách trường bắt buộc và quy tắc hoàn tất một phần cần chốt trước implementation.
- **Chỉ định CLS:** tiến độ thực hiện, thanh toán và trạng thái kết quả độc lập. Thu đủ phí ngoại trú trước thực hiện trừ ngoại lệ cấp cứu được xác nhận theo NV-15; không tin cờ Paid/Emergency từ client.
- **Kết quả CLS:** Nháp → Đã xác nhận → Có bản sửa bổ sung. Chỉ công bố kết quả chính thức sau xác nhận; tệp bắt buộc phải Available. Một lượt chỉ có một phiếu đọc kết quả đang hiệu lực trong cùng vòng; kết quả đến thêm không sinh phiếu trùng (NV-18).
- **Đơn thuốc:** nội dung có trạng thái Nháp/Đã xác nhận và quy tắc phiên bản riêng. Tiến độ cấp phát Chưa cấp/Cấp một phần/Cấp đủ là trường độc lập; xác nhận đơn không đồng nghĩa đã cấp thuốc hoặc người bệnh đã dùng thuốc.
- **Hóa đơn/chứng từ tiền:** theo vòng đời tài chính, độc lập bệnh án. Lịch sử tiền không sửa/xóa; điều chỉnh bằng sự kiện tham chiếu.
- **Phiếu số/hướng dẫn:** có thể in để đi đúng bộ phận. In lại giữ số cũ; chuyển sang hàng khác theo quy tắc cấp số hàng đích và giữ liên kết nguồn NV-08.
- **Tệp/PDF:** upload và trạng thái Available độc lập với xác nhận nội dung. PDF gắn SourceVersionId; bản nháp nếu được cho in phải ghi rõ Nháp. Quyền/mẫu in nháp chưa chốt, không mặc định mở cho mọi role.

## 4. Thời gian chờ và công việc chưa xong

Hiển thị thời gian chờ sinh hiệu từ lúc tạo phiếu vào hàng; thời gian thực hiện từ lúc điều dưỡng nhận; thời gian quay lại từ lúc vào nhóm Quay lại. Không cộng các khoảng thành một thời gian chờ không rõ nghĩa. Lưu UTC và hiển thị theo Asia/Ho_Chi_Minh.

Không tự đóng lượt, xác nhận bệnh án hoặc hủy công việc chỉ vì chờ lâu. Ngưỡng cảnh báo số phút là CẦN_XÁC_NHẬN; chưa có SLA sinh hiệu được phê duyệt. Hết buổi cần điều phối/bàn giao theo NV-12; phiếu trễ và ca đang thực hiện không áp chung một tác vụ xóa.

Người bệnh bỏ về: kết thúc sớm bởi người có quyền, có lý do, giữ hồ sơ và chi phí/công việc đã thực hiện. Hủy yêu cầu sinh hiệu chưa làm, dừng khi đã bắt đầu, yêu cầu đo lại và sửa số đo đã hoàn tất cần được mô tả trong spec Clinical trước khi triển khai; không xóa lịch sử hoặc âm thầm ghi đè.

## 5. Điểm lệch tài liệu và điểm chưa chốt

BRD NV-08/NV-13 và bảng chuyển trạng thái cũ đặt sinh hiệu trước khám. Quyết định hội thoại thay thế phần thứ tự này; cần cập nhật BRD/TSD và tiêu chí AT liên quan khi triển khai, giữ nguyên các yêu cầu xác minh, quyền tài nguyên, audit và bất biến phiên bản.

CẦN_XÁC_NHẬN trước plan nghiệp vụ: danh mục chỉ số/đơn vị, lý do không đo được và đo một phần; bác sĩ có được ra nhiều yêu cầu đo đồng thời; quy tắc sửa số đo đã hoàn tất; xử lý CLS và sinh hiệu cùng chờ; quyền kết thúc sớm/bàn giao; ngưỡng chờ lâu; quyền in bản nháp. Không dùng những mục này để chặn các plan auth 01–06.

## 6. Ca nghiệm thu nghiệp vụ bắt buộc cho plan tương lai

- Không yêu cầu sinh hiệu: tiếp nhận → khám → kết luận được, không tự sinh phiếu đo.
- Yêu cầu đo: lưu nháp thành công rồi mới chuyển; quay lại mở đúng nội dung và đúng người bệnh.
- Hai nhân viên nhận cùng phiếu: chỉ một người nhận thành công; người còn lại tải lại trạng thái.
- Hoàn tất đo gửi lại cùng Idempotency-Key: không tạo hai kết quả/phiếu quay lại.
- Hai người quay lại từ sinh hiệu/CLS: FIFO theo thời điểm vào nhóm, không ưu tiên ngầm loại công việc.
- Bác sĩ đang khám người khác: không tự chuyển màn/chen ca; báo công việc quay lại đã sẵn sàng.
- Kết quả hoàn tất không tự xác nhận bệnh án; bệnh án xác nhận không tự đánh dấu đã thanh toán/cấp thuốc.
- Hai tab sửa nháp cùng version: lần lưu sau nhận 412 và đối chiếu; không tự merge nội dung lâm sàng.
- Bác sĩ bị thay phân công: không tiếp tục đọc/ghi chỉ vì FE còn state cũ; bàn giao và audit đúng quyền.

Trạng thái kiểm chứng: NOT_RUN. Đây là tiêu chí cần chuyển thành test ở các plan nghiệp vụ tương ứng.
