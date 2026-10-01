import React from 'react';

// Người dùng CÓ quyền nhưng màn chưa phát hành (chưa có endpoint thật đã nghiệm thu). Không phải lỗi thiếu quyền.
const FeaturePending = () => (
  <div className="c-page c-feature-pending">
    <h1 className="c-page__title c-feature-pending__title">Khu vực đang hoàn thiện</h1>
    <p className="c-feature-pending__desc">Màn hình này chưa sẵn sàng sử dụng. Tài khoản của bạn đã có quyền; chức năng sẽ mở khi hoàn tất.</p>
  </div>
);

export default FeaturePending;
