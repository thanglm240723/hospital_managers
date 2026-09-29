import React from 'react';
import PropTypes from 'prop-types';

class NoAccess extends React.Component {
  state = { checking: false };

  handleRecheck = async () => {
    const { loadMe } = this.props;
    this.setState({ checking: true });
    try {
      await loadMe();
    } finally {
      this.setState({ checking: false });
    }
  };

  render() {
    const { logout } = this.props;
    const { checking } = this.state;
    return (
      <div className="c-no-access">
        <div className="c-no-access__card">
          <h1 className="c-no-access__title">Tài khoản chưa được cấp quyền</h1>
          <p className="c-no-access__desc">
            Tài khoản của bạn hiện chưa có quyền truy cập vào bất kỳ khu vực làm việc nào. Liên hệ quản trị hệ thống
            để được cấp quyền, sau đó bấm &quot;Kiểm tra lại&quot;.
          </p>
          <div className="c-no-access__actions">
            <button type="button" className="c-login__submit" onClick={this.handleRecheck} disabled={checking}>
              {checking ? 'Đang kiểm tra…' : 'Kiểm tra lại'}
            </button>
            <button type="button" className="c-login__secondary" onClick={() => logout()}>
              Đăng xuất
            </button>
          </div>
        </div>
      </div>
    );
  }
}

NoAccess.propTypes = {
  loadMe: PropTypes.func.isRequired,
  logout: PropTypes.func.isRequired,
};

export default NoAccess;
