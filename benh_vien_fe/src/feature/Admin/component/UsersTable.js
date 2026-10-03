import React from 'react';
import PropTypes from 'prop-types';
import { userStatus } from '../userStatus';
import UserStatusBadge from './UserStatusBadge';

const UsersTable = ({
  items, loading, selectedId, onSelect,
}) => (
  <table className="c-table">
    <thead>
      <tr>
        <th>Họ tên</th>
        <th>Email</th>
        <th>Vai trò</th>
        <th>Trạng thái</th>
      </tr>
    </thead>
    <tbody>
      {!loading && items.length === 0 && (
        <tr><td colSpan={4} className="c-table__empty">Không có tài khoản phù hợp.</td></tr>
      )}
      {items.map(user => (
        <tr
          key={user.id}
          className={`c-table__row${user.id === selectedId ? ' is-selected' : ''}`}
          onClick={() => onSelect(user.id)}
        >
          <td>{user.fullName}</td>
          <td>{user.email}</td>
          <td>{(user.roles || []).map(r => r.name).join(', ') || '—'}</td>
          <td><UserStatusBadge status={userStatus(user)} /></td>
        </tr>
      ))}
    </tbody>
  </table>
);

UsersTable.propTypes = {
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  loading: PropTypes.bool.isRequired,
  selectedId: PropTypes.string,
  onSelect: PropTypes.func.isRequired,
};

UsersTable.defaultProps = { selectedId: null };

export default UsersTable;
