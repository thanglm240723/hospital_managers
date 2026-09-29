import React from 'react';
import PropTypes from 'prop-types';
import { SYSTEM_ROLES } from 'feature/Roles/permissionCatalog';

class CreateUserDialog extends React.Component {
  state = {
    fullName: '', email: '', roleIds: [], submitting: false, error: null, result: null,
  };

  toggleRole = (roleId) => {
    this.setState(state => ({
      roleIds: state.roleIds.indexOf(roleId) === -1 ? [...state.roleIds, roleId] : state.roleIds.filter(id => id !== roleId),
    }));
  };

  handleSubmit = async (event) => {
    event.preventDefault();
    const { onCreate } = this.props;
    const { fullName, email, roleIds } = this.state;
    this.setState({ submitting: true, error: null });
    try {
      const result = await onCreate({ fullName, email, roleIds });
      this.setState({ submitting: false, result });
    } catch (error) {
      this.setState({ submitting: false, error: (error.response && error.response.data && error.response.data.title) || 'Không tạo được tài khoản.' });
    }
  };

  render() {
    const { onClose } = this.props;
    const {
      fullName, email, roleIds, submitting, error, result,
    } = this.state;

    if (result) {
      return (
        <div className="c-confirm-dialog" role="dialog" aria-modal="true" aria-label="Đã tạo tài khoản">
          <div className="c-confirm-dialog__backdrop" onClick={onClose} role="presentation" />
          <div className="c-confirm-dialog__box">
            <h2 className="c-confirm-dialog__title">Đã tạo tài khoản</h2>
            <p className="c-confirm-dialog__desc">
              Mật khẩu tạm thời (chỉ hiển thị một lần, hãy chuyển cho người dùng qua kênh an toàn):
            </p>
            <p className="c-confirm-dialog__secret">{result.temporaryPassword}</p>
            <div className="c-confirm-dialog__actions">
              <button type="button" className="c-login__submit" onClick={onClose}>Đóng</button>
            </div>
          </div>
        </div>
      );
    }

    return (
      <div className="c-confirm-dialog" role="dialog" aria-modal="true" aria-label="Tạo tài khoản">
        <div className="c-confirm-dialog__backdrop" onClick={onClose} role="presentation" />
        <form className="c-confirm-dialog__box" onSubmit={this.handleSubmit}>
          <h2 className="c-confirm-dialog__title">Tạo tài khoản mới</h2>
          {error && <p className="c-confirm-dialog__warning">{error}</p>}

          <div className="c-login__field">
            {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
            <label className="c-login__label" htmlFor="cu-fullname">Họ tên</label>
            <input
              id="cu-fullname"
              className="c-login__input"
              value={fullName}
              required
              onChange={e => this.setState({ fullName: e.target.value })}
            />
          </div>
          <div className="c-login__field">
            {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
            <label className="c-login__label" htmlFor="cu-email">Email</label>
            <input
              id="cu-email"
              type="email"
              className="c-login__input"
              value={email}
              required
              onChange={e => this.setState({ email: e.target.value })}
            />
          </div>
          <fieldset className="c-login__field">
            <legend className="c-login__label">Vai trò</legend>
            {SYSTEM_ROLES.map(role => (
              <label key={role.id} className="c-checkbox" htmlFor={`cu-role-${role.id}`}>
                <input
                  id={`cu-role-${role.id}`}
                  type="checkbox"
                  checked={roleIds.indexOf(role.id) !== -1}
                  onChange={() => this.toggleRole(role.id)}
                />
                {role.name}
              </label>
            ))}
          </fieldset>

          <div className="c-confirm-dialog__actions">
            <button type="button" className="c-login__secondary" onClick={onClose} disabled={submitting}>Hủy</button>
            <button type="submit" className="c-login__submit" disabled={submitting}>{submitting ? 'Đang tạo…' : 'Tạo tài khoản'}</button>
          </div>
        </form>
      </div>
    );
  }
}

CreateUserDialog.propTypes = {
  onCreate: PropTypes.func.isRequired,
  onClose: PropTypes.func.isRequired,
};

export default CreateUserDialog;
