import React from 'react';
import PropTypes from 'prop-types';
import { problemTitle } from 'feature/Auth/problem';
import { groupPermissions } from '../permissionCatalog';

const snapshot = role => ({ name: role.name, permissionCodes: role.permissionCodes, rowVersion: role.rowVersion });
const sameSet = (a, b) => JSON.stringify([...a].sort()) === JSON.stringify([...b].sort());

export const describeError = (error) => {
  const data = error && error.response && error.response.data;
  const status = error && error.response && error.response.status;
  if (status === 412 || (data && data.code === 'role_version_conflict')) {
    return { conflict: true, message: 'Vai trò đã bị người khác thay đổi (role_version_conflict). Hãy nạp lại để đối chiếu; chỉnh sửa của bạn vẫn được giữ.' };
  }
  const title = problemTitle(error);
  return { conflict: false, message: data && data.code ? `${title} (${data.code})` : title };
};

class RoleDetail extends React.Component {
  constructor(props) {
    super(props);
    this.state = {
      name: props.role.name, permissionCodes: props.role.permissionCodes, saving: false, error: null, base: snapshot(props.role),
    };
    this.lastDirty = false;
    this.unmounted = false;
  }

  componentDidUpdate(prevProps, prevState) {
    const { role, onDirtyChange } = this.props;
    if (prevProps.role.id !== role.id) {
      this.resetFrom(role); // eslint-disable-line react/no-did-update-set-state
    }
    if (onDirtyChange && prevState !== this.state && this.isDirty() !== this.lastDirty) {
      this.lastDirty = this.isDirty();
      onDirtyChange(this.lastDirty);
    }
  }

  componentWillUnmount() {
    const { onDirtyChange } = this.props;
    this.unmounted = true;
    if (onDirtyChange && this.lastDirty) onDirtyChange(false);
  }

  resetFrom = (role) => {
    this.setState({
      name: role.name, permissionCodes: role.permissionCodes, saving: false, error: null, base: snapshot(role),
    });
  };

  // Server đã đổi so với bản lúc bắt đầu chỉnh sửa (ví dụ sau 412 + nạp lại).
  isStale = () => {
    const { role } = this.props;
    const { base } = this.state;
    return role.rowVersion !== base.rowVersion;
  };

  isEditable = () => {
    const { canManage, role } = this.props;
    return canManage && !role.isSystem;
  };

  isDirty = () => {
    const { name, permissionCodes, base } = this.state;
    return this.isEditable() && (name.trim() !== base.name || !sameSet(permissionCodes, base.permissionCodes));
  };

  togglePermission = (code) => {
    this.setState(state => ({
      permissionCodes: state.permissionCodes.indexOf(code) === -1
        ? [...state.permissionCodes, code]
        : state.permissionCodes.filter(c => c !== code),
    }));
  };

  handleSave = async () => {
    const { role, onRename, onSavePermissions } = this.props;
    const { name, permissionCodes, base } = this.state;
    const newName = name.trim();
    this.setState({ saving: true, error: null });
    let renamedOk = false;
    try {
      let version = role.rowVersion;
      let last = null;
      if (newName !== base.name) {
        last = await onRename(role.id, newName, version);
        if (last && last.rowVersion !== undefined) version = last.rowVersion;
        renamedOk = true;
        if (!this.unmounted) this.setState(s => ({ base: { ...s.base, name: newName, rowVersion: version } }));
      }
      if (!sameSet(permissionCodes, base.permissionCodes)) {
        last = await onSavePermissions(role.id, permissionCodes, version);
        if (last && last.rowVersion !== undefined) version = last.rowVersion;
      }
      if (!this.unmounted) this.setState({ base: { name: newName, permissionCodes, rowVersion: version } });
    } catch (error) {
      // Giữ nguyên chỉnh sửa để người dùng đối chiếu; không ghi đè bằng dữ liệu server.
      const described = describeError(error);
      if (renamedOk) described.message = `Tên đã được lưu, quyền chưa được lưu. ${described.message}`;
      if (!this.unmounted) this.setState({ error: described });
    } finally {
      if (!this.unmounted) this.setState({ saving: false });
    }
  };

  render() {
    const {
      role, canManage, permissions, permissionsError, onClone, onReload,
    } = this.props;
    const {
      name, permissionCodes, saving, error,
    } = this.state;
    const stale = this.isStale();
    const serverNotMine = permissionCodes.filter(c => role.permissionCodes.indexOf(c) === -1);
    const mineNotServer = role.permissionCodes.filter(c => permissionCodes.indexOf(c) === -1);
    const editable = this.isEditable();
    const dirty = this.isDirty();
    const groups = groupPermissions(permissions);
    // Quyền role đang có nhưng catalog không tải được/không chứa vẫn hiển thị để không tự thêm/bớt quyền.
    const known = permissions.map(p => p.code);
    const unknown = permissionCodes.filter(c => known.indexOf(c) === -1);

    return (
      <div className="c-detail-panel">
        {editable ? (
          <div className="c-login__field">
            {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
            <label className="c-login__label" htmlFor="role-name">Tên vai trò</label>
            <input id="role-name" className="c-login__input" value={name} onChange={e => this.setState({ name: e.target.value })} />
          </div>
        ) : (
          <h2 className="c-detail-panel__title">{role.name}</h2>
        )}
        <p className="c-detail-panel__subtitle">
          {`Mã: ${role.code} — `}
          {role.isSystem ? 'Vai trò hệ thống, chỉ đọc.' : 'Vai trò tùy chỉnh.'}
          {!canManage && !role.isSystem ? ' Bạn không có quyền quản lý vai trò (roles.manage) nên chỉ xem.' : ''}
        </p>

        {permissionsError && (
          <p className="c-form__error" role="alert">
            {`Không tải được danh mục quyền: ${permissionsError}. Hiển thị mã quyền hiện có của vai trò.`}
          </p>
        )}

        {groups.map(group => (
          <section className="c-detail-panel__section" key={group.module}>
            <h3>{group.module}</h3>
            {group.items.map(item => (
              <label key={item.code} className="c-checkbox" htmlFor={`role-perm-${item.code}`}>
                <input
                  id={`role-perm-${item.code}`}
                  type="checkbox"
                  checked={permissionCodes.indexOf(item.code) !== -1}
                  disabled={!editable}
                  onChange={() => this.togglePermission(item.code)}
                />
                {item.description}
              </label>
            ))}
          </section>
        ))}

        {unknown.length > 0 && (
          <section className="c-detail-panel__section">
            <h3>Quyền khác</h3>
            {unknown.map(code => (
              <label key={code} className="c-checkbox" htmlFor={`role-perm-${code}`}>
                <input
                  id={`role-perm-${code}`}
                  type="checkbox"
                  checked
                  disabled={!editable || Boolean(permissionsError)}
                  onChange={() => this.togglePermission(code)}
                />
                {code}
              </label>
            ))}
          </section>
        )}

        {stale && dirty && (
          <div className="c-form__error" role="alert">
            <p>Dữ liệu trên máy chủ đã thay đổi so với bản bạn đang sửa.</p>
            {role.name !== name.trim() && <p>{`Tên: máy chủ "${role.name}" / bản của bạn "${name.trim()}"`}</p>}
            {serverNotMine.length > 0 && <p>{`Bản của bạn có thêm so với máy chủ: ${serverNotMine.join(', ')}`}</p>}
            {mineNotServer.length > 0 && <p>{`Máy chủ có thêm so với bản của bạn: ${mineNotServer.join(', ')}`}</p>}
            <button type="button" className="c-login__secondary" onClick={() => this.resetFrom(role)}>Bỏ chỉnh sửa của tôi</button>
          </div>
        )}

        {error && (
          <div className="c-form__error" role="alert">
            {error.message}
            {error.conflict && (
              <button type="button" className="c-login__secondary" onClick={onReload}>Nạp lại danh sách</button>
            )}
          </div>
        )}

        <div className="c-confirm-dialog__actions" style={{ justifyContent: 'flex-start', marginTop: 16 }}>
          {editable && (
            <button type="button" className="c-login__submit" onClick={this.handleSave} disabled={saving || !dirty || !name.trim() || stale}>
              {saving ? 'Đang lưu…' : 'Lưu thay đổi'}
            </button>
          )}
          {canManage && (
            <button type="button" className="c-login__secondary" disabled={dirty} onClick={() => onClone(role)}>
              Nhân bản thành vai trò mới
            </button>
          )}
        </div>
      </div>
    );
  }
}

RoleDetail.propTypes = {
  role: PropTypes.shape({
    id: PropTypes.string.isRequired,
    code: PropTypes.string,
    name: PropTypes.string.isRequired,
    isSystem: PropTypes.bool,
    permissionCodes: PropTypes.arrayOf(PropTypes.string).isRequired,
    rowVersion: PropTypes.number,
  }).isRequired,
  permissions: PropTypes.arrayOf(PropTypes.object),
  permissionsError: PropTypes.string,
  canManage: PropTypes.bool.isRequired,
  onRename: PropTypes.func.isRequired,
  onSavePermissions: PropTypes.func.isRequired,
  onClone: PropTypes.func.isRequired,
  onReload: PropTypes.func,
  onDirtyChange: PropTypes.func,
};

RoleDetail.defaultProps = {
  permissions: [], permissionsError: null, onReload: () => {}, onDirtyChange: null,
};

export default RoleDetail;
