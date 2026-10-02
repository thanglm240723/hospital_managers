import React from 'react';
import PropTypes from 'prop-types';
import { problemTitle, problemFieldErrors } from 'feature/Auth/problem';

const fieldMessage = (errors, name) => {
  const key = Object.keys(errors).find(k => k.toLowerCase() === name.toLowerCase());
  return key ? [].concat(errors[key]).join(' ') : null;
};

// Mật khẩu ban đầu chỉ nằm trong state cục bộ của dialog này (không vào Redux/storage/log) và mất khi đóng dialog.
class CreateUserDialog extends React.Component {
  state = {
    fullName: '', email: '', roleIds: [], submitting: false, error: null, fieldErrors: {}, result: null, copied: false,
  };

  componentWillUnmount() {
    this.unmounted = true;
  }

  toggleRole = (roleId) => {
    this.setState(state => ({
      roleIds: state.roleIds.indexOf(roleId) === -1 ? [...state.roleIds, roleId] : state.roleIds.filter(id => id !== roleId),
    }));
  };

  validate = () => {
    const { fullName, email, roleIds } = this.state;
    const errors = {};
    if (!email.trim()) errors.email = 'Vui lòng nhập email.';
    if (!fullName.trim()) errors.fullName = 'Vui lòng nhập họ tên.';
    if (roleIds.length === 0) errors.roleIds = 'Vui lòng chọn ít nhất một vai trò.';
    return errors;
  };

  handleSubmit = async (event) => {
    event.preventDefault();
    const { onCreate } = this.props;
    const { fullName, email, roleIds, submitting } = this.state;
    if (submitting) return;
    const clientErrors = this.validate();
    if (Object.keys(clientErrors).length > 0) {
      this.setState({ fieldErrors: clientErrors, error: null });
      return;
    }
    this.setState({ submitting: true, error: null, fieldErrors: {} });
    try {
      const result = await onCreate({ fullName: fullName.trim(), email: email.trim(), roleIds });
      if (!this.unmounted) this.setState({ result });
    } catch (error) {
      const data = error && error.response && error.response.data;
      if (!this.unmounted) {
        this.setState({
          error: data && data.code ? `${problemTitle(error)} (${data.code})` : problemTitle(error),
          fieldErrors: problemFieldErrors(error),
        });
      }
    } finally {
      if (!this.unmounted) this.setState({ submitting: false });
    }
  };

  handleCopy = async (password) => {
    try {
      await navigator.clipboard.writeText(password);
      this.setState({ copied: true });
    } catch (error) {
      this.setState({ copied: false });
    }
  };

  render() {
    const { onClose, roleOptions, roleOptionsReason } = this.props;
    const {
      fullName, email, roleIds, submitting, error, fieldErrors, result, copied,
    } = this.state;

    if (result) {
      return (
        <div className="c-confirm-dialog" role="dialog" aria-modal="true" aria-label="Đã tạo tài khoản">
          <div className="c-confirm-dialog__backdrop" role="presentation" />
          <div className="c-confirm-dialog__box">
            <h2 className="c-confirm-dialog__title">Đã tạo tài khoản</h2>
            <p className="c-confirm-dialog__desc">{`Tài khoản ${result.user.email} đã được tạo. Mật khẩu ban đầu:`}</p>
            <p className="c-confirm-dialog__secret">{result.initialPassword}</p>
            <p className="c-confirm-dialog__warning">
              Mật khẩu chỉ hiển thị một lần, không xem lại được sau khi đóng. Hãy chuyển cho người dùng qua kênh an toàn;
              người dùng phải đổi mật khẩu ở lần đăng nhập đầu.
            </p>
            {copied && <p role="status">Đã sao chép.</p>}
            <div className="c-confirm-dialog__actions">
              <button type="button" className="c-login__secondary" onClick={() => this.handleCopy(result.initialPassword)}>Sao chép</button>
              <button type="button" className="c-login__submit" onClick={onClose}>Đóng</button>
            </div>
          </div>
        </div>
      );
    }

    return (
      <div className="c-confirm-dialog" role="dialog" aria-modal="true" aria-label="Tạo tài khoản">
        <div className="c-confirm-dialog__backdrop" onClick={onClose} role="presentation" />
        <form className="c-confirm-dialog__box" onSubmit={this.handleSubmit} noValidate>
          <h2 className="c-confirm-dialog__title">Tạo tài khoản mới</h2>
          <p className="c-confirm-dialog__desc">Hệ thống sinh mật khẩu ban đầu và chỉ hiển thị một lần sau khi tạo.</p>
          {error && <p className="c-confirm-dialog__warning" role="alert">{error}</p>}

          <div className="c-login__field">
            {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
            <label className="c-login__label" htmlFor="cu-fullname">Họ tên</label>
            <input id="cu-fullname" className="c-login__input" value={fullName} onChange={e => this.setState({ fullName: e.target.value })} />
            {fieldMessage(fieldErrors, 'fullName') && <p className="c-form__error" data-field="fullName">{fieldMessage(fieldErrors, 'fullName')}</p>}
          </div>
          <div className="c-login__field">
            {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
            <label className="c-login__label" htmlFor="cu-email">Email</label>
            <input id="cu-email" type="email" className="c-login__input" value={email} onChange={e => this.setState({ email: e.target.value })} />
            {fieldMessage(fieldErrors, 'email') && <p className="c-form__error" data-field="email">{fieldMessage(fieldErrors, 'email')}</p>}
          </div>
          <fieldset className="c-login__field">
            <legend className="c-login__label">Vai trò</legend>
            {roleOptionsReason && (
              <p className="c-form__error">{`Không tải được danh sách vai trò: ${roleOptionsReason}. Không thể chọn vai trò nên chưa tạo được tài khoản.`}</p>
            )}
            {roleOptions.map(role => (
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
            {fieldMessage(fieldErrors, 'roleIds') && <p className="c-form__error" data-field="roleIds">{fieldMessage(fieldErrors, 'roleIds')}</p>}
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
  roleOptions: PropTypes.arrayOf(PropTypes.object),
  roleOptionsReason: PropTypes.string,
};

CreateUserDialog.defaultProps = { roleOptions: [], roleOptionsReason: null };

export default CreateUserDialog;
