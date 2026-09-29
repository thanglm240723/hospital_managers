import React from 'react';
import PropTypes from 'prop-types';

const LABELS = {
  active: { text: 'Hoạt động', modifier: 'success' },
  must_change_password: { text: 'Chờ đổi mật khẩu', modifier: 'warning' },
  locked: { text: 'Đã khóa', modifier: 'danger' },
};

const UserStatusBadge = ({ status }) => {
  const info = LABELS[status] || { text: status, modifier: 'default' };
  return <span className={`c-badge c-badge--${info.modifier}`}>{info.text}</span>;
};

UserStatusBadge.propTypes = { status: PropTypes.string.isRequired };

export default UserStatusBadge;
