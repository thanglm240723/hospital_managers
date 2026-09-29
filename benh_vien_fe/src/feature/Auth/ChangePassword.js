import React from 'react';
import { connect } from 'react-redux';
import { Link } from 'react-router-dom';
import { changePassword as changePasswordAction, logout as logoutAction } from './redux/actions';
import { problemTitle, problemFieldErrors } from './problem';
import { AuthLayout, Alert, PasswordField } from './AuthLayout';

// Khớp PasswordPolicy ở BE (Application/Common/Security).
const MIN_LENGTH = 10;
const MAX_LENGTH = 128;

/* eslint-disable react/destructuring-assignment, react/prop-types */
class ChangePassword extends React.Component {
  state = {
    currentPassword: '',
    newPassword: '',
    confirmPassword: '',
    error: null,
    fieldErrors: {},
    submitting: false,
  };

  handleChange = event => this.setState({ [event.target.name]: event.target.value });

  handleSubmit = async (event) => {
    event.preventDefault();
    if (this.state.newPassword !== this.state.confirmPassword) {
      this.setState({ error: null, fieldErrors: { confirmPassword: ['Mật khẩu nhập lại không khớp.'] } });
      return;
    }
    this.setState({ submitting: true, error: null, fieldErrors: {} });
    try {
      await this.props.changePassword(this.state.currentPassword, this.state.newPassword);
      this.props.history.replace('/start');
    } catch (error) {
      this.setState({ submitting: false, error: problemTitle(error), fieldErrors: problemFieldErrors(error) });
    }
  };

  newPasswordHint() {
    const { length } = this.state.newPassword;
    if (length === 0) return `Từ ${MIN_LENGTH} đến ${MAX_LENGTH} ký tự. Nên dùng một cụm từ dễ nhớ.`;
    if (length < MIN_LENGTH) return `Còn thiếu ${MIN_LENGTH - length} ký tự.`;
    return 'Đủ độ dài.';
  }

  render() {
    const {
      currentPassword, newPassword, confirmPassword, error, fieldErrors, submitting,
    } = this.state;
    const { mustChangePassword, logout } = this.props;
    const firstError = key => fieldErrors[key] && fieldErrors[key][0];
    const hasFieldError = Object.keys(fieldErrors).length > 0;

    return (
      <AuthLayout>
        <form className="c-login__form" onSubmit={this.handleSubmit}>
          <div>
            <h1 className="c-login__title">Đổi mật khẩu</h1>
            <p className="c-login__subtitle">Sau khi đổi, các phiên đăng nhập khác của bạn sẽ bị đăng xuất.</p>
          </div>

          {mustChangePassword && (
            <Alert kind="info">Bạn cần đổi mật khẩu trước khi tiếp tục sử dụng hệ thống.</Alert>
          )}
          {error && !hasFieldError && <Alert kind="error">{error}</Alert>}

          <PasswordField
            id="cp-current"
            label="Mật khẩu hiện tại"
            name="currentPassword"
            autoComplete="current-password"
            value={currentPassword}
            onChange={this.handleChange}
            error={firstError('currentPassword')}
            required
            autoFocus // eslint-disable-line jsx-a11y/no-autofocus
          />

          <PasswordField
            id="cp-new"
            label="Mật khẩu mới"
            name="newPassword"
            autoComplete="new-password"
            minLength={MIN_LENGTH}
            maxLength={MAX_LENGTH}
            value={newPassword}
            onChange={this.handleChange}
            error={firstError('newPassword')}
            hint={this.newPasswordHint()}
            required
          />

          <PasswordField
            id="cp-confirm"
            label="Nhập lại mật khẩu mới"
            name="confirmPassword"
            autoComplete="new-password"
            value={confirmPassword}
            onChange={this.handleChange}
            error={firstError('confirmPassword')}
            required
          />

          <button type="submit" className="c-login__submit" disabled={submitting}>
            {submitting ? 'Đang lưu…' : 'Đổi mật khẩu'}
          </button>

          {mustChangePassword
            ? (
              <button type="button" className="c-login__secondary" onClick={() => logout()}>
                Đăng xuất
              </button>
            )
            : <Link className="c-login__secondary" to="/">Quay lại</Link>}
        </form>
      </AuthLayout>
    );
  }
}

export default connect(state => ({ mustChangePassword: state.auth.mustChangePassword }),
  { changePassword: changePasswordAction, logout: logoutAction })(ChangePassword);
