import React from 'react';
import PropTypes from 'prop-types';

// Hộp thoại xác nhận dùng chung — không dùng window.confirm để giữ giao diện nhất quán và test được.
const ConfirmDialog = ({
  title, description, warning, confirmLabel, cancelLabel, danger, busy, onConfirm, onCancel,
}) => (
  <div className="c-confirm-dialog" role="dialog" aria-modal="true" aria-label={title}>
    <div className="c-confirm-dialog__backdrop" onClick={onCancel} role="presentation" />
    <div className="c-confirm-dialog__box">
      <h2 className="c-confirm-dialog__title">{title}</h2>
      {description && <p className="c-confirm-dialog__desc">{description}</p>}
      {warning && <p className="c-confirm-dialog__warning">{warning}</p>}
      <div className="c-confirm-dialog__actions">
        <button type="button" className="c-login__secondary" onClick={onCancel} disabled={busy}>
          {cancelLabel}
        </button>
        <button
          type="button"
          className={`c-login__submit${danger ? ' c-login__submit--danger' : ''}`}
          onClick={onConfirm}
          disabled={busy}
        >
          {busy ? 'Đang xử lý…' : confirmLabel}
        </button>
      </div>
    </div>
  </div>
);

ConfirmDialog.propTypes = {
  title: PropTypes.string.isRequired,
  description: PropTypes.string,
  warning: PropTypes.string,
  confirmLabel: PropTypes.string,
  cancelLabel: PropTypes.string,
  danger: PropTypes.bool,
  busy: PropTypes.bool,
  onConfirm: PropTypes.func.isRequired,
  onCancel: PropTypes.func.isRequired,
};

ConfirmDialog.defaultProps = {
  description: null, warning: null, confirmLabel: 'Xác nhận', cancelLabel: 'Hủy', danger: false, busy: false,
};

export default ConfirmDialog;
