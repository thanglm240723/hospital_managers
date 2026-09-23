import React from 'react';
import { connect } from 'react-redux';
import { changePassword as changePasswordAction } from './redux/actions';
import { problemTitle, problemFieldErrors } from './problem';

/* eslint-disable react/destructuring-assignment, react/prop-types */
class ChangePassword extends React.Component {
  state = {
    currentPassword: '', newPassword: '', error: null, fieldErrors: {}, submitting: false,
  };

  handleChange = event => this.setState({ [event.target.name]: event.target.value });

  handleSubmit = async (event) => {
    event.preventDefault();
    this.setState({ submitting: true, error: null, fieldErrors: {} });
    try {
      await this.props.changePassword(this.state.currentPassword, this.state.newPassword);
      this.props.history.replace('/');
    } catch (error) {
      this.setState({ submitting: false, error: problemTitle(error), fieldErrors: problemFieldErrors(error) });
    }
  };

  render() {
    const {
      currentPassword, newPassword, error, fieldErrors, submitting,
    } = this.state;
    return (
      <form className="c-card" onSubmit={this.handleSubmit}>
        <h1 className="c-heading">Đổi mật khẩu</h1>
        {this.props.mustChangePassword && <p>Bạn cần đổi mật khẩu trước khi tiếp tục sử dụng hệ thống.</p>}
        <input name="currentPassword" type="password" autoComplete="current-password" value={currentPassword} onChange={this.handleChange} placeholder="Mật khẩu hiện tại" required />
        {fieldErrors.currentPassword && <p className="c-text--error">{fieldErrors.currentPassword[0]}</p>}
        <input name="newPassword" type="password" autoComplete="new-password" minLength={10} maxLength={128} value={newPassword} onChange={this.handleChange} placeholder="Mật khẩu mới (tối thiểu 10 ký tự)" required />
        {fieldErrors.newPassword && <p className="c-text--error">{fieldErrors.newPassword[0]}</p>}
        {error && !fieldErrors.currentPassword && !fieldErrors.newPassword && <p className="c-text--error">{error}</p>}
        <button type="submit" disabled={submitting}>Đổi mật khẩu</button>
      </form>
    );
  }
}

export default connect(state => ({ mustChangePassword: state.auth.mustChangePassword }),
  { changePassword: changePasswordAction })(ChangePassword);
