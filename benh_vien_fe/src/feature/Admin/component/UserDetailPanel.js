import React from 'react';
import PropTypes from 'prop-types';
import Can from 'feature/Auth/Can';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import { problemTitle } from 'feature/Auth/problem';
import { ConfirmDialog } from 'feature/Shell';
import { groupPermissions } from 'feature/Roles/permissionCatalog';
import { userStatus } from '../userStatus';
import UserStatusBadge from './UserStatusBadge';
import StaffProfileSection from './StaffProfileSection';

const idsOf = roles => (roles || []).map(r => r.id);
const sameSet = (a, b) => JSON.stringify([...a].sort()) === JSON.stringify([...b].sort());

// 412: không tự retry/ghi đè — giữ lựa chọn của người dùng, mời nạp lại bản mới để đối chiếu.
export const describeError = (error) => {
  const data = error && error.response && error.response.data;
  const status = error && error.response && error.response.status;
  if (status === 412 || (data && data.code === 'user_version_conflict')) {
    return {
      conflict: true,
      message: 'Tài khoản đã bị thay đổi (user_version_conflict), có thể do người khác sửa hoặc người dùng vừa đăng nhập. '
        + 'Hãy nạp lại bản mới để đối chiếu; lựa chọn của bạn vẫn được giữ và chưa được lưu.',
    };
  }
  const title = problemTitle(error);
  return { conflict: false, message: data && data.code ? `${title} (${data.code})` : title };
};

class UserDetailPanel extends React.Component {
  constructor(props) {
    super(props);
    this.state = {
      roleIds: idsOf(props.user.roles),
      base: { roleIds: idsOf(props.user.roles), rowVersion: props.user.rowVersion },
      rolesSaving: false,
      rolesError: null,
      reason: '',
      permBusy: false,
      permError: null,
      confirmLock: false,
      busy: false,
      lockError: null,
    };
    this.unmounted = false;
  }

  componentWillUnmount() {
    this.unmounted = true;
  }

  roleDirty = () => {
    const { roleIds, base } = this.state;
    return !sameSet(roleIds, base.roleIds);
  };

  // Server đã đổi so với bản lúc bắt đầu chỉnh sửa (ví dụ sau 412 + nạp lại).
  isStale = () => {
    const { user } = this.props;
    const { base } = this.state;
    return user.rowVersion !== base.rowVersion;
  };

  // Lệnh khác do chính panel gọi thành công làm rowVersion tăng: nếu vai trò server không đổi so với base thì chỉ đồng bộ version, giữ draft.
  syncVersion = (updated) => {
    if (this.unmounted || !updated) return;
    this.setState(state => (sameSet(idsOf(updated.roles), state.base.roleIds)
      ? { base: { ...state.base, rowVersion: updated.rowVersion } }
      : null));
  };

  resetRoles = () => {
    const { user } = this.props;
    this.setState({
      roleIds: idsOf(user.roles), base: { roleIds: idsOf(user.roles), rowVersion: user.rowVersion }, rolesError: null,
    });
  };

  toggleRole = (roleId) => {
    this.setState(state => ({
      roleIds: state.roleIds.indexOf(roleId) === -1 ? [...state.roleIds, roleId] : state.roleIds.filter(id => id !== roleId),
    }));
  };

  saveRoles = async () => {
    const { user, onAssignRoles } = this.props;
    const { roleIds } = this.state;
    this.setState({ rolesSaving: true, rolesError: null });
    try {
      const updated = await onAssignRoles(user.id, roleIds, user.rowVersion);
      if (!this.unmounted) this.setState({ base: { roleIds: idsOf(updated.roles), rowVersion: updated.rowVersion }, roleIds: idsOf(updated.roles) });
    } catch (error) {
      if (!this.unmounted) this.setState({ rolesError: describeError(error) });
    } finally {
      if (!this.unmounted) this.setState({ rolesSaving: false });
    }
  };

  changePermission = async (code, revoke) => {
    const { user, onGrantPermission, onRevokePermission } = this.props;
    const { reason } = this.state;
    this.setState({ permBusy: true, permError: null });
    try {
      const call = revoke ? onRevokePermission : onGrantPermission;
      const updated = await call(user.id, code, reason.trim(), user.rowVersion);
      this.syncVersion(updated);
      if (!this.unmounted) this.setState({ reason: '' });
    } catch (error) {
      // Giữ nguyên lý do đã nhập để người dùng thử lại thủ công sau khi nạp lại.
      if (!this.unmounted) this.setState({ permError: describeError(error) });
    } finally {
      if (!this.unmounted) this.setState({ permBusy: false });
    }
  };

  handleLockToggle = async () => {
    const { user, onActivate, onDeactivate } = this.props;
    this.setState({ busy: true, lockError: null });
    try {
      const updated = user.isActive ? await onDeactivate(user.id) : await onActivate(user.id);
      this.syncVersion(updated);
      if (!this.unmounted) this.setState({ confirmLock: false });
    } catch (error) {
      if (!this.unmounted) this.setState({ lockError: describeError(error).message, confirmLock: false });
    } finally {
      if (!this.unmounted) this.setState({ busy: false });
    }
  };

  renderErrorBlock = (error) => {
    const { onReload } = this.props;
    return (
      <div className="c-form__error" role="alert">
        {error.message}
        {error.conflict && <button type="button" className="c-login__secondary" onClick={onReload}>Nạp lại bản mới</button>}
      </div>
    );
  };

  renderRoles() {
    const { user, roleOptions, roleOptionsError } = this.props;
    const { roleIds, rolesSaving, rolesError } = this.state;
    const stale = this.isStale();
    const dirty = this.roleDirty();
    const nameOf = id => (roleOptions.find(r => r.id === id) || (user.roles || []).find(r => r.id === id) || { name: id }).name;
    const serverIds = idsOf(user.roles);
    const mineNotServer = roleIds.filter(id => serverIds.indexOf(id) === -1);
    const serverNotMine = serverIds.filter(id => roleIds.indexOf(id) === -1);

    return (
      <section className="c-detail-panel__section">
        <h3>Vai trò</h3>
        <p>{(user.roles || []).map(r => r.name).join(', ') || 'Chưa có vai trò.'}</p>
        <Can permission={PERMISSIONS.USERS_ROLES_MANAGE}>
          <div>
            {roleOptionsError && (
              <p className="c-form__error" role="alert">{`Không tải được danh sách vai trò: ${roleOptionsError}. Chưa thể thay đổi vai trò.`}</p>
            )}
            {roleOptions.map(role => (
              <label key={role.id} className="c-checkbox" htmlFor={`udp-role-${role.id}`}>
                <input
                  id={`udp-role-${role.id}`}
                  type="checkbox"
                  checked={roleIds.indexOf(role.id) !== -1}
                  disabled={rolesSaving}
                  onChange={() => this.toggleRole(role.id)}
                />
                {role.name}
              </label>
            ))}
            {stale && dirty && (
              <div className="c-form__error" role="alert">
                <p>Dữ liệu trên máy chủ đã thay đổi so với bản bạn đang sửa.</p>
                {mineNotServer.length > 0 && <p>{`Bản của bạn có thêm: ${mineNotServer.map(nameOf).join(', ')}`}</p>}
                {serverNotMine.length > 0 && <p>{`Máy chủ có thêm: ${serverNotMine.map(nameOf).join(', ')}`}</p>}
                <button type="button" className="c-login__secondary" onClick={this.resetRoles}>Bỏ chỉnh sửa của tôi</button>
              </div>
            )}
            {rolesError && this.renderErrorBlock(rolesError)}
            {roleOptions.length > 0 && (
              <button type="button" className="c-login__submit" onClick={this.saveRoles} disabled={rolesSaving || !dirty || stale}>
                {rolesSaving ? 'Đang lưu…' : 'Lưu vai trò'}
              </button>
            )}
          </div>
        </Can>
      </section>
    );
  }

  renderPermissions() {
    const { user, permissions, permissionsError, roleOptions } = this.props;
    const { reason, permBusy, permError } = this.state;
    const grants = user.permissionGrants || [];
    const effective = user.effectivePermissions || [];
    const grantOf = code => grants.find(g => g.code === code);
    const roleSources = code => (user.roles || [])
      .filter(r => (roleOptions.find(o => o.id === r.id) || { permissionCodes: [] }).permissionCodes.indexOf(code) !== -1)
      .map(r => r.name);

    const known = permissions.map(p => p.code);
    const extraCodes = Array.from(new Set([...grants.map(g => g.code), ...effective])).filter(c => known.indexOf(c) === -1);
    const groups = groupPermissions(permissions);
    if (extraCodes.length > 0) groups.push({ module: 'Quyền khác', items: extraCodes.map(code => ({ code, description: code })) });

    const rowFor = (item) => {
      const grant = grantOf(item.code);
      const isEffective = effective.indexOf(item.code) !== -1;
      const viaRoles = roleSources(item.code);
      const unknownSource = isEffective && !grant && roleOptions.length === 0;
      const fromRole = isEffective && (viaRoles.length > 0 || (!grant && roleOptions.length > 0));
      const canGrant = !grant && known.indexOf(item.code) !== -1;
      const reasonOk = reason.trim().length > 0;
      return (
        <div key={item.code} className="c-permission-row" data-code={item.code}>
          <span>{item.description}</span>
          {isEffective ? <span className="c-badge c-badge--success">Có quyền</span> : <span className="c-badge c-badge--default">Không có quyền</span>}
          {grant && <span className="c-badge c-badge--warning">{`Cấp thêm: ${grant.reason}`}</span>}
          {fromRole && <span className="c-badge c-badge--default">{viaRoles.length > 0 ? `Từ vai trò: ${viaRoles.join(', ')}` : 'Từ vai trò'}</span>}
          {unknownSource && <span className="c-badge c-badge--default">Không xác định nguồn vai trò (thiếu roles.read)</span>}
          <Can permission={PERMISSIONS.USERS_PERMISSIONS_MANAGE}>
            <span>
              {grant && (
                <button type="button" className="c-login__secondary" disabled={permBusy || !reasonOk} onClick={() => this.changePermission(item.code, true)}>
                  Thu hồi quyền cấp thêm
                </button>
              )}
              {canGrant && (
                <button type="button" className="c-login__secondary" disabled={permBusy || !reasonOk} onClick={() => this.changePermission(item.code, false)}>
                  Cấp thêm
                </button>
              )}
            </span>
          </Can>
        </div>
      );
    };

    return (
      <section className="c-detail-panel__section">
        <h3>Quyền hiệu lực và nguồn</h3>
        {permissionsError && (
          <p className="c-form__error" role="alert">
            {`Không tải được danh mục quyền: ${permissionsError}. Chỉ hiển thị mã quyền hiện có; không thể cấp thêm quyền mới.`}
          </p>
        )}
        <Can permission={PERMISSIONS.USERS_PERMISSIONS_MANAGE}>
          <div className="c-login__field">
            {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
            <label className="c-login__label" htmlFor="udp-reason">Lý do cấp/thu hồi (bắt buộc)</label>
            <input
              id="udp-reason"
              className="c-login__input"
              value={reason}
              maxLength={500}
              onChange={e => this.setState({ reason: e.target.value })}
            />
            <p className="c-detail-panel__subtitle">Thu hồi quyền cấp thêm không loại bỏ quyền mà vai trò của tài khoản đang cung cấp.</p>
          </div>
        </Can>
        {permError && this.renderErrorBlock(permError)}
        {groups.map(group => (
          <div key={group.module}>
            <h4>{group.module}</h4>
            {group.items.map(rowFor)}
          </div>
        ))}
      </section>
    );
  }

  render() {
    const { user } = this.props;
    const { confirmLock, busy, lockError } = this.state;
    const willLock = user.isActive;

    return (
      <div className="c-detail-panel">
        <h2 className="c-detail-panel__title">{user.fullName}</h2>
        <p className="c-detail-panel__subtitle">{user.email}</p>
        <UserStatusBadge status={userStatus(user)} />

        {this.renderRoles()}
        {this.renderPermissions()}
        <Can permission={PERMISSIONS.STAFF_PROFILES_READ}>
          <StaffProfileSection key={user.id} userId={user.id} />
        </Can>

        <section className="c-detail-panel__section">
          <h3>Mật khẩu</h3>
          <button type="button" className="c-login__secondary" disabled>Cấp lại mật khẩu tạm</button>
          <p className="c-detail-panel__subtitle">Chưa hỗ trợ: hệ thống chưa có chức năng cấp lại mật khẩu cho tài khoản.</p>
        </section>

        <Can permission={PERMISSIONS.USERS_ACTIVATE}>
          <section className="c-detail-panel__section">
            <h3>Trạng thái tài khoản</h3>
            {lockError && <p className="c-confirm-dialog__warning" role="alert">{lockError}</p>}
            <button
              type="button"
              className={willLock ? 'c-login__submit c-login__submit--danger' : 'c-login__submit'}
              disabled={busy}
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
            warning={willLock ? 'Hệ thống sẽ từ chối nếu đây là tài khoản của chính bạn hoặc quản trị viên đang hoạt động cuối cùng.' : null}
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
    isActive: PropTypes.bool.isRequired,
    mustChangePassword: PropTypes.bool,
    roles: PropTypes.arrayOf(PropTypes.object).isRequired,
    permissionGrants: PropTypes.arrayOf(PropTypes.shape({ code: PropTypes.string, reason: PropTypes.string })),
    effectivePermissions: PropTypes.arrayOf(PropTypes.string),
    rowVersion: PropTypes.number,
  }).isRequired,
  roleOptions: PropTypes.arrayOf(PropTypes.object),
  roleOptionsError: PropTypes.string,
  permissions: PropTypes.arrayOf(PropTypes.object),
  permissionsError: PropTypes.string,
  onAssignRoles: PropTypes.func.isRequired,
  onGrantPermission: PropTypes.func.isRequired,
  onRevokePermission: PropTypes.func.isRequired,
  onActivate: PropTypes.func.isRequired,
  onDeactivate: PropTypes.func.isRequired,
  onReload: PropTypes.func,
};

UserDetailPanel.defaultProps = {
  roleOptions: [], roleOptionsError: null, permissions: [], permissionsError: null, onReload: () => {},
};

export default UserDetailPanel;
