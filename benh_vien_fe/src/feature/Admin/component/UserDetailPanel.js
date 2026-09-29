import React from 'react';
import PropTypes from 'prop-types';
import Can from 'feature/Auth/Can';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import { ConfirmDialog } from 'feature/Shell';
import { SYSTEM_ROLES, PERMISSION_CATALOG } from 'feature/Roles/permissionCatalog';
import { newIdempotencyKey } from 'service/idempotency';
import UserStatusBadge from './UserStatusBadge';

class UserDetailPanel extends React.Component {
  state = { confirmLock: false, busy: false, lockError: null, resetResult: null };

  handleToggleRole = (roleId) => {
    const { user, onAssignRoles } = this.props;
    const roleIds = user.roleIds.indexOf(roleId) === -1
      ? [...user.roleIds, roleId]
      : user.roleIds.filter(id => id !== roleId);
    onAssignRoles(user.id, roleIds);
  };

  handleTogglePermission = (code) => {
    const { user, onGrantPermission, onRevokePermission } = this.props;
    const granted = (user.extraPermissions || []).indexOf(code) !== -1;
    if (granted) onRevokePermission(user.id, [code]);
    else onGrantPermission(user.id, [code]);
  };

  handleResetPassword = async () => {
    const { user, onResetPassword } = this.props;
    this.setState({ busy: true });
    const result = await onResetPassword(user.id);
    this.setState({ busy: false, resetResult: result.temporaryPassword });
  };

  handleLockToggle = async () => {
    const { user, onActivate, onDeactivate } = this.props;
    this.setState({ busy: true, lockError: null });
    try {
      const key = newIdempotencyKey();
      if (user.status === 'locked') await onActivate(user.id, key);
      else await onDeactivate(user.id, key);
      this.setState({ busy: false, confirmLock: false });
    } catch (error) {
      const title = (error.response && error.response.data && error.response.data.title) || 'Không thực hiện được thao tác.';
      this.setState({ busy: false, lockError: title });
    }
  };

  render() {
    const { user } = this.props;
    const { confirmLock, busy, lockError, resetResult } = this.state;
    const willLock = user.status !== 'locked';

    return (
      <div className="c-detail-panel">
        <h2 className="c-detail-panel__title">{user.fullName}</h2>
        <p className="c-detail-panel__subtitle">{user.email}</p>
        <UserStatusBadge status={user.status} />

        <section className="c-detail-panel__section">
          <h3>Vai trò</h3>
          <Can permission={PERMISSIONS.USERS_ROLES_MANAGE}>
            <div>
              {SYSTEM_ROLES.map(role => (
                <label key={role.id} className="c-checkbox" htmlFor={`udp-role-${role.id}`}>
                  <input
                    id={`udp-role-${role.id}`}
                    type="checkbox"
                    checked={user.roleIds.indexOf(role.id) !== -1}
                    onChange={() => this.handleToggleRole(role.id)}
                  />
                  {role.name}
                </label>
              ))}
            </div>
          </Can>
        </section>

        <section className="c-detail-panel__section">
          <h3>Quyền cấp thêm / thu hồi</h3>
          <Can permission={PERMISSIONS.USERS_PERMISSIONS_MANAGE}>
            <div>
              {PERMISSION_CATALOG.flatMap(group => group.items).map(item => (
                <label key={item.code} className="c-checkbox" htmlFor={`udp-perm-${item.code}`}>
                  <input
                    id={`udp-perm-${item.code}`}
                    type="checkbox"
                    checked={(user.extraPermissions || []).indexOf(item.code) !== -1}
                    onChange={() => this.handleTogglePermission(item.code)}
                  />
                  {item.label}
                </label>
              ))}
            </div>
          </Can>
        </section>

        <section className="c-detail-panel__section">
          <h3>Mật khẩu</h3>
          <button type="button" className="c-login__secondary" onClick={this.handleResetPassword} disabled={busy}>
            Cấp lại mật khẩu tạm
          </button>
          {resetResult && (
            <p className="c-confirm-dialog__secret">{resetResult}</p>
          )}
        </section>

        <Can permission={PERMISSIONS.USERS_ACTIVATE}>
          <section className="c-detail-panel__section">
            <h3>Trạng thái tài khoản</h3>
            {lockError && <p className="c-confirm-dialog__warning">{lockError}</p>}
            <button
              type="button"
              className={willLock ? 'c-login__submit c-login__submit--danger' : 'c-login__submit'}
              onClick={() => this.setState({ confirmLock: true, lockError: null })}
            >
              {willLock ? 'Khóa tài khoản' : 'Mở khóa tài khoản'}
            </button>
          </section>
        </Can>

        {confirmLock && (
          <ConfirmDialog
            title={willLock ? 'Khóa tài khoản?' : 'Mở khóa tài khoản?'}
            description={willLock ? `Tài khoản ${user.fullName} sẽ không đăng nhập được sau khi khóa.` : `Tài khoản ${user.fullName} sẽ đăng nhập lại được.`}
            warning={willLock ? 'Hệ thống sẽ từ chối nếu đây là quản trị viên đang hoạt động cuối cùng.' : null}
            danger={willLock}
            busy={busy}
            confirmLabel={willLock ? 'Khóa' : 'Mở khóa'}
            onConfirm={this.handleLockToggle}
            onCancel={() => this.setState({ confirmLock: false })}
          />
        )}
      </div>
    );
  }
}

UserDetailPanel.propTypes = {
  user: PropTypes.shape({
    id: PropTypes.string.isRequired,
    fullName: PropTypes.string.isRequired,
    email: PropTypes.string.isRequired,
    status: PropTypes.string.isRequired,
    roleIds: PropTypes.arrayOf(PropTypes.string).isRequired,
    extraPermissions: PropTypes.arrayOf(PropTypes.string),
  }).isRequired,
  onAssignRoles: PropTypes.func.isRequired,
  onGrantPermission: PropTypes.func.isRequired,
  onRevokePermission: PropTypes.func.isRequired,
  onResetPassword: PropTypes.func.isRequired,
  onActivate: PropTypes.func.isRequired,
  onDeactivate: PropTypes.func.isRequired,
};

export default UserDetailPanel;
