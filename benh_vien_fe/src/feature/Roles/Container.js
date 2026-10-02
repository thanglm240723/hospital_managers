import React from 'react';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { AppLayout } from 'feature/Shell';
import { hasPermission } from 'feature/Auth/permissions';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import {
  loadRoles, loadPermissions, selectRole, createRole, renameRole, updateRolePermissions,
} from './redux/action';
import RoleList from './component/RoleList';
import RoleDetail from './component/RoleDetail';
import CreateRoleDialog from './component/CreateRoleDialog';

class RolesContainer extends React.Component {
  constructor(props) {
    super(props);
    // creating: null | { sourceName, permissionCodes }; pendingSelectId: vai trò muốn chuyển tới khi còn thay đổi chưa lưu.
    this.state = { creating: null, dirty: false, pendingSelectId: null };
  }

  componentDidMount() {
    const { loadRoles: onLoadRoles, loadPermissions: onLoadPermissions, canReadPermissions } = this.props;
    onLoadRoles();
    if (canReadPermissions) onLoadPermissions();
  }

  handleSelect = (id) => {
    const { selectRole: onSelect } = this.props;
    const { dirty } = this.state;
    if (dirty) {
      this.setState({ pendingSelectId: id });
      return;
    }
    this.setState({ creating: null });
    onSelect(id);
  };

  confirmDiscard = () => {
    const { selectRole: onSelect } = this.props;
    const { pendingSelectId } = this.state;
    this.setState({ dirty: false, pendingSelectId: null, creating: null });
    onSelect(pendingSelectId);
  };

  handleCreate = async (payload) => {
    const { createRole: onCreateRole } = this.props;
    await onCreateRole(payload);
    this.setState({ creating: null });
  };

  render() {
    const {
      items, selectedId, canManage, canReadPermissions, loading, error, permissions, permissionsError,
      renameRole: onRename, updateRolePermissions: onSavePermissions, loadRoles: onReload,
    } = this.props;
    const { creating, pendingSelectId } = this.state;
    const selected = items.find(r => r.id === selectedId) || items[0];
    const catalogError = canReadPermissions ? permissionsError : 'tài khoản không có quyền permissions.read';

    return (
      <AppLayout>
        <div className="c-page">
          <h1 className="c-page__title">Vai trò &amp; quyền</h1>
          {error && (
            <p className="c-form__error" role="alert">
              {`Không tải được danh sách vai trò: ${error}`}
              <button type="button" className="c-login__secondary" onClick={onReload}>Thử lại</button>
            </p>
          )}
          {canManage && (
            <div className="c-toolbar">
              <button
                type="button"
                className="c-toolbar__action"
                disabled={this.state.dirty} // eslint-disable-line react/destructuring-assignment
                onClick={() => this.setState({ creating: { sourceName: null, permissionCodes: [] } })}
              >
                + Tạo vai trò
              </button>
            </div>
          )}
          {pendingSelectId && (
            <div className="c-form__error" role="alert">
              Vai trò đang chọn có thay đổi chưa lưu.
              <button type="button" className="c-login__secondary" onClick={this.confirmDiscard}>Bỏ thay đổi và chuyển</button>
              <button type="button" className="c-login__secondary" onClick={() => this.setState({ pendingSelectId: null })}>Ở lại để lưu</button>
            </div>
          )}
          <div className="c-admin-users__layout">
            <RoleList items={items} selectedId={selected && selected.id} onSelect={this.handleSelect} />
            {creating ? (
              <CreateRoleDialog
                sourceName={creating.sourceName}
                initialPermissionCodes={creating.permissionCodes}
                onCreate={this.handleCreate}
                onCancel={() => this.setState({ creating: null })}
              />
            ) : selected && (
              <RoleDetail
                key={selected.id}
                role={selected}
                canManage={canManage}
                permissions={permissions}
                permissionsError={catalogError}
                onRename={onRename}
                onSavePermissions={onSavePermissions}
                onReload={onReload}
                onDirtyChange={dirty => this.setState({ dirty })}
                onClone={role => this.setState({ creating: { sourceName: role.name, permissionCodes: role.permissionCodes } })}
              />
            )}
            {!selected && !loading && !error && <p>Chưa có vai trò nào.</p>}
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
  canReadPermissions: PropTypes.bool.isRequired,
  loading: PropTypes.bool.isRequired,
  error: PropTypes.string,
  permissions: PropTypes.arrayOf(PropTypes.object).isRequired,
  permissionsError: PropTypes.string,
  loadRoles: PropTypes.func.isRequired,
  loadPermissions: PropTypes.func.isRequired,
  selectRole: PropTypes.func.isRequired,
  createRole: PropTypes.func.isRequired,
  renameRole: PropTypes.func.isRequired,
  updateRolePermissions: PropTypes.func.isRequired,
};

RolesContainer.defaultProps = { selectedId: null, error: null, permissionsError: null };

const mapStateToProps = state => ({
  items: state.roles.items,
  selectedId: state.roles.selectedId,
  loading: state.roles.loading,
  error: state.roles.error,
  permissions: state.roles.permissions,
  permissionsError: state.roles.permissionsError,
  canManage: hasPermission(state.auth, PERMISSIONS.ROLES_MANAGE),
  canReadPermissions: hasPermission(state.auth, PERMISSIONS.PERMISSIONS_READ),
});

export default connect(mapStateToProps, {
  loadRoles, loadPermissions, selectRole, createRole, renameRole, updateRolePermissions,
})(RolesContainer);
