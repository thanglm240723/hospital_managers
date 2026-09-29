import React from 'react';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { hasPermission } from 'feature/Auth/permissions';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import { PERMISSION_CATALOG } from '../permissionCatalog';

class RoleDetail extends React.Component {
  constructor(props) {
    super(props);
    this.state = {
      permissions: props.role.permissions, saving: false, cloning: false, cloneName: '',
    };
  }

  componentDidUpdate(prevProps) {
    const { role } = this.props;
    if (prevProps.role.id !== role.id) {
      this.setState({ permissions: role.permissions, cloning: false }); // eslint-disable-line react/no-did-update-set-state
    }
  }

  togglePermission = (code) => {
    this.setState(state => ({
      permissions: state.permissions.indexOf(code) === -1
        ? [...state.permissions, code]
        : state.permissions.filter(c => c !== code),
    }));
  };

  handleSave = async () => {
    const { onSave, role } = this.props;
    const { permissions } = this.state;
    this.setState({ saving: true });
    await onSave(role.id, permissions);
    this.setState({ saving: false });
  };

  handleClone = async () => {
    const { onClone, role } = this.props;
    const { cloneName } = this.state;
    if (!cloneName.trim()) return;
    await onClone(cloneName.trim(), role.permissions);
    this.setState({ cloning: false, cloneName: '' });
  };

  render() {
    const { role, canManage } = this.props;
    const {
      permissions, saving, cloning, cloneName,
    } = this.state;
    const editable = canManage && !role.system;
    const dirty = editable && JSON.stringify([...permissions].sort()) !== JSON.stringify([...role.permissions].sort());

    return (
      <div className="c-detail-panel">
        <h2 className="c-detail-panel__title">{role.name}</h2>
        <p className="c-detail-panel__subtitle">{role.system ? 'Vai trò hệ thống — chỉ đọc.' : 'Vai trò tùy chỉnh.'}</p>

        {PERMISSION_CATALOG.map(group => (
          <section className="c-detail-panel__section" key={group.module}>
            <h3>{group.module}</h3>
            {group.items.map(item => (
              <label key={item.code} className="c-checkbox" htmlFor={`role-perm-${item.code}`}>
                <input
                  id={`role-perm-${item.code}`}
                  type="checkbox"
                  checked={permissions.indexOf(item.code) !== -1}
                  disabled={!editable}
                  onChange={() => this.togglePermission(item.code)}
                />
                {item.label}
              </label>
            ))}
          </section>
        ))}

        <div className="c-confirm-dialog__actions" style={{ justifyContent: 'flex-start', marginTop: 16 }}>
          {editable && (
            <button type="button" className="c-login__submit" onClick={this.handleSave} disabled={saving || !dirty}>
              {saving ? 'Đang lưu…' : 'Lưu thay đổi'}
            </button>
          )}
          {canManage && (
            <button type="button" className="c-login__secondary" onClick={() => this.setState({ cloning: true })}>
              Nhân bản thành vai trò mới
            </button>
          )}
        </div>

        {cloning && (
          <div className="c-login__field" style={{ marginTop: 12 }}>
            {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
            <label className="c-login__label" htmlFor="clone-name">Tên vai trò mới</label>
            <input id="clone-name" className="c-login__input" value={cloneName} onChange={e => this.setState({ cloneName: e.target.value })} />
            <button type="button" className="c-login__submit" style={{ marginTop: 8 }} onClick={this.handleClone}>Tạo</button>
          </div>
        )}
      </div>
    );
  }
}

RoleDetail.propTypes = {
  role: PropTypes.shape({
    id: PropTypes.string.isRequired,
    name: PropTypes.string.isRequired,
    system: PropTypes.bool,
    permissions: PropTypes.arrayOf(PropTypes.string).isRequired,
  }).isRequired,
  canManage: PropTypes.bool.isRequired,
  onSave: PropTypes.func.isRequired,
  onClone: PropTypes.func.isRequired,
};

export default connect(state => ({ canManage: hasPermission(state.auth, PERMISSIONS.ROLES_MANAGE) }))(RoleDetail);
