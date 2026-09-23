import React from 'react';
import { connect } from 'react-redux';
import { Redirect } from 'react-router-dom';
import { login as loginAction } from './redux/actions';
import { problemTitle } from './problem';

/* eslint-disable react/destructuring-assignment, react/prop-types */
class Login extends React.Component {
  state = {
    email: '', password: '', error: null, submitting: false,
  };

  handleChange = event => this.setState({ [event.target.name]: event.target.value });

  handleSubmit = async (event) => {
    event.preventDefault();
    this.setState({ submitting: true, error: null });
    try {
      await this.props.login(this.state.email, this.state.password);
    } catch (error) {
      // 401/429: hiện nguyên message server — server đã cố ý không phân biệt nguyên nhân.
      this.setState({ submitting: false, error: problemTitle(error) });
    }
  };

  render() {
    const {
      status, mustChangePassword, sessionMessage, location,
    } = this.props;
    if (status === 'authenticated') {
      const from = (location.state && location.state.from) || { pathname: '/' };
      return <Redirect to={mustChangePassword ? '/change-password' : from} />;
    }

    const {
      email, password, error, submitting,
    } = this.state;
    return (
      <form className="c-card" onSubmit={this.handleSubmit}>
        <h1 className="c-heading">Đăng nhập</h1>
        {sessionMessage && <p className="c-text--error">{sessionMessage}</p>}
        <input name="email" type="email" autoComplete="username" value={email} onChange={this.handleChange} placeholder="Email" required />
        <input name="password" type="password" autoComplete="current-password" value={password} onChange={this.handleChange} placeholder="Mật khẩu" required />
        {error && <p className="c-text--error">{error}</p>}
        <button type="submit" disabled={submitting}>Đăng nhập</button>
      </form>
    );
  }
}

export default connect(state => ({
  status: state.auth.status,
  mustChangePassword: state.auth.mustChangePassword,
  sessionMessage: state.auth.sessionMessage,
}), { login: loginAction })(Login);
