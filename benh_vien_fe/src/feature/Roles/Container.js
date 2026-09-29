import React from 'react';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { AppLayout } from 'feature/Shell';
import { hasPermission } from 'feature/Auth/permissions';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import { loadRoles, selectRole, createRole, updateRolePermissions } from './redux/action';
import RoleList from './component/RoleList';
import RoleDetail from './component/RoleDetail';

class RolesContainer extends React.Component {
  componentDidMount() {
    const { loadRoles } = this.props;
    loadRoles();
  }

  render() {
    const {
      items, selectedId, selectRole: onSelect, createRole: onCreateRole, updateRolePermissions: onSave, canManage,
    } = this.props;
    const selected = items.find(r => r.id === selectedId) || items[0];

    return (
      <AppLayout>
        <div className="c-page">
          <h1 className="c-page__title">Vai trò &amp; quyền</h1>
          {canManage && (
            <div className="c-toolbar">
              <button
                type="button"
                className="c-toolbar__action"
                onClick={() => {
                  const name = window.prompt('Tên vai trò mới:'); // eslint-disable-line no-alert
                  if (name && name.trim()) onCreateRole({ name: name.trim(), permissions: [] });
                }}
              >
                + Tạo vai trò
              </button>
            </div>
          )}
          <div className="c-admin-users__layout">
            <RoleList items={items} selectedId={selected && selected.id} onSelect={onSelect} />
            {selected && (
              <RoleDetail
                role={selected}
                onSave={onSave}
                onClone={(name, permissions) => onCreateRole({ name, permissions })}
              />
            )}
          </div>
        </div>
      </AppLayout>
    );
  }
}

RolesContainer.propTypes = {
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  selectedId: PropTypes.string,
  canManage: PropTypes.bool.isRequired,
  loadRoles: PropTypes.func.isRequired,
  selectRole: PropTypes.func.isRequired,
  createRole: PropTypes.func.isRequired,
  updateRolePermissions: PropTypes.func.isRequired,
};

RolesContainer.defaultProps = { selectedId: null };

const mapStateToProps = state => ({
  items: state.roles.items,
  selectedId: state.roles.selectedId,
  canManage: hasPermission(state.auth, PERMISSIONS.ROLES_MANAGE),
});

export default connect(mapStateToProps, {
  loadRoles, selectRole, createRole, updateRolePermissions,
})(RolesContainer);
