import React from 'react';
import PropTypes from 'prop-types';

const UsersPagination = ({
  pageNumber, totalPages, totalCount, disabled, onPageChange,
}) => (
  <nav className="c-pagination" aria-label="Phân trang tài khoản">
    <button type="button" className="c-login__secondary" disabled={disabled || pageNumber <= 1} onClick={() => onPageChange(pageNumber - 1)}>
      Trang trước
    </button>
    <span className="c-pagination__info">{`Trang ${pageNumber} / ${totalPages} (${totalCount} tài khoản)`}</span>
    <button type="button" className="c-login__secondary" disabled={disabled || pageNumber >= totalPages} onClick={() => onPageChange(pageNumber + 1)}>
      Trang sau
    </button>
  </nav>
);

UsersPagination.propTypes = {
  pageNumber: PropTypes.number.isRequired,
  totalPages: PropTypes.number.isRequired,
  totalCount: PropTypes.number.isRequired,
  disabled: PropTypes.bool,
  onPageChange: PropTypes.func.isRequired,
};

UsersPagination.defaultProps = { disabled: false };

export default UsersPagination;
