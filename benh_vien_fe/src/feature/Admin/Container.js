import React from 'react';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { AppLayout } from 'feature/Shell';
import { hasPermission } from 'feature/Auth/permissions';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import {
  loadUsers, selectUser, createUser, assignRoles, grantPermissions, revokePermissions, resetTemporaryPassword,
  activateUser, deactivateUser,
} from './redux/action';
import UserFilters from './component/UserFilters';
import UsersTable from './component/UsersTable';
import UserDetailPanel from './component/UserDetailPanel';
import CreateUserDialog from './component/CreateUserDialog';

class AdminUsersContainer extends React.Component {
  state = {
    filters: { searchTerm: '', roleId: '', status: '', pageNumber: 1 },
    creating: false,
  };

  componentDidMount() {
    const { filters } = this.state;
    this.fetch(filters);
  }

  handleFiltersChange = (filters) => {
    this.setState({ filters });
    this.fetch(filters);
  };

  fetch(filters) {
    const { loadUsers } = this.props;
    loadUsers({ ...filters, pageSize: 10 });
  }

  render() {
    const {
      items, loading, selectedId, canCreate, selectUser: onSelect,
      assignRoles: onAssignRoles, grantPermissions: onGrant, revokePermissions: onRevoke,
      resetTemporaryPassword: onReset, activateUser: onActivate, deactivateUser: onDeactivate, createUser: onCreate,
    } = this.props;
    const { filters, creating } = this.state;
    const selectedUser = items.find(u => u.id === selectedId);

    return (
      <AppLayout>
        <div className="c-page">
          <h1 className="c-page__title">Quản lý tài khoản</h1>
          <UserFilters value={filters} onChange={this.handleFiltersChange} onCreate={() => this.setState({ creating: true })} canCreate={canCreate} />
          <div className="c-admin-users__layout">
            <UsersTable items={items} loading={loading} selectedId={selectedId} onSelect={onSelect} />
            {selectedUser && (
              <UserDetailPanel
                user={selectedUser}
                onAssignRoles={onAssignRoles}
                onGrantPermission={onGrant}
                onRevokePermission={onRevoke}
                onResetPassword={onReset}
                onActivate={onActivate}
                onDeactivate={onDeactivate}
              />
            )}
          </div>
          {creating && (
            <CreateUserDialog
              onCreate={payload => onCreate(payload)}
              onClose={() => { this.setState({ creating: false }); this.fetch(filters); }}
            />
          )}
        </div>
      </AppLayout>
    );
  }
}

AdminUsersContainer.propTypes = {
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  loading: PropTypes.bool.isRequired,
  selectedId: PropTypes.string,
  canCreate: PropTypes.bool.isRequired,
  loadUsers: PropTypes.func.isRequired,
  selectUser: PropTypes.func.isRequired,
  createUser: PropTypes.func.isRequired,
  assignRoles: PropTypes.func.isRequired,
  grantPermissions: PropTypes.func.isRequired,
  revokePermissions: PropTypes.func.isRequired,
  resetTemporaryPassword: PropTypes.func.isRequired,
  activateUser: PropTypes.func.isRequired,
  deactivateUser: PropTypes.func.isRequired,
};

AdminUsersContainer.defaultProps = { selectedId: null };

const mapStateToProps = state => ({
  items: state.admin.items,
  loading: state.admin.loading,
  selectedId: state.admin.selectedId,
  canCreate: hasPermission(state.auth, PERMISSIONS.USERS_CREATE),
});

export default connect(mapStateToProps, {
  loadUsers,
  selectUser,
  createUser,
  assignRoles,
  grantPermissions,
  revokePermissions,
  resetTemporaryPassword,
  activateUser,
  deactivateUser,
})(AdminUsersContainer);
