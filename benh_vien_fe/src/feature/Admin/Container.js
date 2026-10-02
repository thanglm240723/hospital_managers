import React from 'react';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { AppLayout } from 'feature/Shell';
import { hasPermission } from 'feature/Auth/permissions';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import {
  loadUsers, loadUserDetail, selectUser, createUser, assignRoles, grantPermission, revokePermission,
  activateUser, deactivateUser, loadRoleOptions, loadPermissionCatalog,
} from './redux/action';
import UserFilters from './component/UserFilters';
import UsersTable from './component/UsersTable';
import UsersPagination from './component/UsersPagination';
import UserDetailPanel from './component/UserDetailPanel';
import CreateUserDialog from './component/CreateUserDialog';

const PAGE_SIZE = 10;

class AdminUsersContainer extends React.Component {
  state = {
    filters: { searchTerm: '', roleId: '', status: '', pageNumber: 1 },
    creating: false,
  };

  componentDidMount() {
    const {
      loadRoleOptions: onLoadRoles, loadPermissionCatalog: onLoadPermissions, canReadRoles, canReadPermissions,
    } = this.props;
    const { filters } = this.state;
    this.fetch(filters);
    if (canReadRoles) onLoadRoles();
    if (canReadPermissions) onLoadPermissions();
  }

  // Đổi bộ lọc luôn về trang 1; đổi trang giữ bộ lọc.
  handleFiltersChange = (filters) => {
    this.setState({ filters });
    this.fetch(filters);
  };

  handlePageChange = (pageNumber) => {
    const { filters } = this.state;
    this.handleFiltersChange({ ...filters, pageNumber });
  };

  handleReloadDetail = () => {
    const { selectedId, loadUserDetail: onLoadDetail } = this.props;
    if (selectedId) onLoadDetail(selectedId);
  };

  fetch(filters) {
    const { loadUsers: onLoadUsers } = this.props;
    onLoadUsers({ ...filters, pageSize: PAGE_SIZE });
  }

  render() {
    const {
      items, loading, error, pageNumber, totalPages, totalCount, selectedId, detail, detailLoading, detailError,
      canCreate, canReadRoles, canReadPermissions, roleOptions, roleOptionsError, permissions, permissionsError,
      selectUser: onSelect, assignRoles: onAssignRoles, grantPermission: onGrant, revokePermission: onRevoke,
      activateUser: onActivate, deactivateUser: onDeactivate, createUser: onCreate,
    } = this.props;
    const { filters, creating } = this.state;
    const rolesReason = canReadRoles ? roleOptionsError : 'tài khoản không có quyền roles.read';
    const permissionsReason = canReadPermissions ? permissionsError : 'tài khoản không có quyền permissions.read';

    return (
      <AppLayout>
        <div className="c-page">
          <h1 className="c-page__title">Quản lý tài khoản</h1>
          <UserFilters
            value={filters}
            onChange={this.handleFiltersChange}
            onCreate={() => this.setState({ creating: true })}
            canCreate={canCreate}
            roleOptions={roleOptions}
            roleOptionsReason={rolesReason}
          />
          {error && (
            <p className="c-form__error" role="alert">
              {`Không tải được danh sách tài khoản: ${error}`}
              <button type="button" className="c-login__secondary" onClick={() => this.fetch(filters)}>Thử lại</button>
            </p>
          )}
          <div className="c-admin-users__layout">
            <div>
              <UsersTable items={items} loading={loading} selectedId={selectedId} onSelect={onSelect} />
              <UsersPagination
                pageNumber={pageNumber}
                totalPages={totalPages}
                totalCount={totalCount}
                disabled={loading}
                onPageChange={this.handlePageChange}
              />
            </div>
            {selectedId && detailLoading && !detail && <p>Đang tải chi tiết tài khoản…</p>}
            {selectedId && detailError && (
              <p className="c-form__error" role="alert">
                {`Không tải được chi tiết tài khoản: ${detailError}`}
                <button type="button" className="c-login__secondary" onClick={this.handleReloadDetail}>Thử lại</button>
              </p>
            )}
            {selectedId && detail && detail.id === selectedId && (
              <UserDetailPanel
                key={detail.id}
                user={detail}
                roleOptions={roleOptions}
                roleOptionsError={rolesReason}
                permissions={permissions}
                permissionsError={permissionsReason}
                onAssignRoles={onAssignRoles}
                onGrantPermission={onGrant}
                onRevokePermission={onRevoke}
                onActivate={onActivate}
                onDeactivate={onDeactivate}
                onReload={this.handleReloadDetail}
              />
            )}
          </div>
          {creating && (
            <CreateUserDialog
              roleOptions={roleOptions}
              roleOptionsReason={rolesReason}
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
  error: PropTypes.string,
  pageNumber: PropTypes.number.isRequired,
  totalPages: PropTypes.number.isRequired,
  totalCount: PropTypes.number.isRequired,
  selectedId: PropTypes.string,
  detail: PropTypes.shape({ id: PropTypes.string }),
  detailLoading: PropTypes.bool.isRequired,
  detailError: PropTypes.string,
  canCreate: PropTypes.bool.isRequired,
  canReadRoles: PropTypes.bool.isRequired,
  canReadPermissions: PropTypes.bool.isRequired,
  roleOptions: PropTypes.arrayOf(PropTypes.object).isRequired,
  roleOptionsError: PropTypes.string,
  permissions: PropTypes.arrayOf(PropTypes.object).isRequired,
  permissionsError: PropTypes.string,
  loadUsers: PropTypes.func.isRequired,
  loadUserDetail: PropTypes.func.isRequired,
  loadRoleOptions: PropTypes.func.isRequired,
  loadPermissionCatalog: PropTypes.func.isRequired,
  selectUser: PropTypes.func.isRequired,
  createUser: PropTypes.func.isRequired,
  assignRoles: PropTypes.func.isRequired,
  grantPermission: PropTypes.func.isRequired,
  revokePermission: PropTypes.func.isRequired,
  activateUser: PropTypes.func.isRequired,
  deactivateUser: PropTypes.func.isRequired,
};

AdminUsersContainer.defaultProps = {
  selectedId: null, detail: null, error: null, detailError: null, roleOptionsError: null, permissionsError: null,
};

const mapStateToProps = state => ({
  items: state.admin.items,
  loading: state.admin.loading,
  error: state.admin.error,
  pageNumber: state.admin.pageNumber,
  totalPages: state.admin.totalPages,
  totalCount: state.admin.totalCount,
  selectedId: state.admin.selectedId,
  detail: state.admin.detail,
  detailLoading: state.admin.detailLoading,
  detailError: state.admin.detailError,
  roleOptions: state.admin.roleOptions,
  roleOptionsError: state.admin.roleOptionsError,
  permissions: state.admin.permissions,
  permissionsError: state.admin.permissionsError,
  canCreate: hasPermission(state.auth, PERMISSIONS.USERS_CREATE),
  canReadRoles: hasPermission(state.auth, PERMISSIONS.ROLES_READ),
  canReadPermissions: hasPermission(state.auth, PERMISSIONS.PERMISSIONS_READ),
});

export default connect(mapStateToProps, {
  loadUsers,
  loadUserDetail,
  selectUser,
  createUser,
  assignRoles,
  grantPermission,
  revokePermission,
  activateUser,
  deactivateUser,
  loadRoleOptions,
  loadPermissionCatalog,
})(AdminUsersContainer);
