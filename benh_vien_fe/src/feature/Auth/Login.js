import React from 'react';
import { connect } from 'react-redux';
import { Redirect } from 'react-router-dom';
import { login as loginAction } from './redux/actions';
import { problemTitle, retryAfterSeconds } from './problem';
import {
  AuthLayout, Alert, PasswordField, IT_EXTENSION,
} from './AuthLayout';

const formatClock = time => new Date(time).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });

/* eslint-disable react/destructuring-assignment, react/prop-types */
class Login extends React.Component {
  state = {
    email: '', password: '', error: null, retryUntil: null, submitting: false,
  };

  passwordField = React.createRef();

  componentWillUnmount() {
    clearTimeout(this.retryTimer);
  }

  handleChange = event => this.setState({ [event.target.name]: event.target.value });

  handleSubmit = async (event) => {
    event.preventDefault();
    this.setState({ submitting: true, error: null });
    try {
      await this.props.login(this.state.email, this.state.password);
    } catch (error) {
      // 401/429: hiện nguyên message server — server đã cố ý không phân biệt nguyên nhân.
      const wait = error.response && error.response.status === 429 ? retryAfterSeconds(error) : null;
      if (wait) {
        clearTimeout(this.retryTimer);
        this.retryTimer = setTimeout(() => this.setState({ retryUntil: null, error: null }), wait * 1000);
      }
      this.setState({
        submitting: false,
        error: problemTitle(error),
        retryUntil: wait ? Date.now() + wait * 1000 : null,
        password: '',
      }, () => this.passwordField.current && this.passwordField.current.focus());
    }
  };

  renderAlert() {
    const { error, retryUntil } = this.state;
    const { sessionMessage } = this.props;
    if (retryUntil) {
      return (
        <Alert kind="wait">
          {error}
          <br />
          <strong>{`Thử lại sau ${formatClock(retryUntil)}`}</strong>
        </Alert>
      );
    }
    if (error) return <Alert kind="error">{error}</Alert>;
    if (sessionMessage) return <Alert kind="info">{sessionMessage}</Alert>;
    return null;
  }

  render() {
    const { status, mustChangePassword, location } = this.props;
    if (status === 'authenticated') {
      const from = (location.state && location.state.from) || { pathname: '/start' };
      return <Redirect to={mustChangePassword ? '/change-password' : from} />;
    }

    const {
      email, password, submitting, retryUntil,
    } = this.state;
    return (
      <AuthLayout>
        <form className="c-login__form" onSubmit={this.handleSubmit}>
          <div>
            <h1 className="c-login__title">Đăng nhập</h1>
            <p className="c-login__subtitle">Dùng email công việc do bệnh viện cấp.</p>
          </div>

          {this.renderAlert()}

          <div className="c-login__field">
            {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
            <label className="c-login__label" htmlFor="login-email">Email</label>
            <input
              id="login-email"
              className="c-login__input"
              name="email"
              type="email"
              autoComplete="username"
              placeholder="ten.bacsi@benhvien.vn"
              value={email}
              onChange={this.handleChange}
              required
              autoFocus // eslint-disable-line jsx-a11y/no-autofocus
            />
          </div>

          <PasswordField
            ref={this.passwordField}
            id="login-password"
            label="Mật khẩu"
            name="password"
            autoComplete="current-password"
            value={password}
            onChange={this.handleChange}
            required
          />

          <button type="submit" className="c-login__submit" disabled={submitting || !!retryUntil}>
            {submitting ? 'Đang đăng nhập…' : 'Đăng nhập'}
          </button>

          <p className="c-login__help">
            Quên mật khẩu hoặc bị khoá tài khoản? Gọi phòng CNTT, máy lẻ
            {' '}
            <strong>{IT_EXTENSION}</strong>
            .
          </p>
        </form>
      </AuthLayout>
    );
  }
}

export default connect(state => ({
  status: state.auth.status,
  mustChangePassword: state.auth.mustChangePassword,
  sessionMessage: state.auth.sessionMessage,
}), { login: loginAction })(Login);
