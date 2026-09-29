import React from 'react';
import PropTypes from 'prop-types';

const RoleList = ({ items, selectedId, onSelect }) => (
  <ul className="c-role-list">
    {items.map(role => (
      <li key={role.id}>
        <button
          type="button"
          className={`c-role-list__item${role.id === selectedId ? ' is-selected' : ''}`}
          onClick={() => onSelect(role.id)}
        >
          <span>{role.name}</span>
          {role.system ? <span className="c-badge c-badge--default">Hệ thống</span> : <span className="c-badge c-badge--success">Tùy chỉnh</span>}
        </button>
      </li>
    ))}
  </ul>
);

RoleList.propTypes = {
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  selectedId: PropTypes.string,
  onSelect: PropTypes.func.isRequired,
};

RoleList.defaultProps = { selectedId: null };

export default RoleList;
