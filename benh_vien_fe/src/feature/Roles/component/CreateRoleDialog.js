import React from 'react';
import PropTypes from 'prop-types';
import { describeError } from './RoleDetail';

// Tạo hoặc nhân bản vai trò: luôn nhập Code mới + Tên; quyền khởi tạo lấy từ vai trò đã lưu (nếu nhân bản).
// Cờ isSystem do server quyết định, client không gửi.
class CreateRoleDialog extends React.Component {
  constructor(props) {
    super(props);
    this.state = {
      code: '', name: '', saving: false, error: null,
    };
  }

  componentWillUnmount() {
    this.unmounted = true;
  }

  handleSubmit = async (event) => {
    event.preventDefault();
    const { onCreate, initialPermissionCodes } = this.props;
    const { code, name } = this.state;
    if (!code.trim() || !name.trim()) return;
    this.setState({ saving: true, error: null });
    try {
      await onCreate({ code: code.trim(), name: name.trim(), permissionCodes: initialPermissionCodes });
    } catch (error) {
      if (!this.unmounted) this.setState({ error: describeError(error).message });
    } finally {
      if (!this.unmounted) this.setState({ saving: false });
    }
  };

  render() {
    const { onCancel, sourceName } = this.props;
    const {
      code, name, saving, error,
    } = this.state;
    return (
      <form className="c-detail-panel" onSubmit={this.handleSubmit}>
        <h2 className="c-detail-panel__title">{sourceName ? `Nhân bản từ "${sourceName}"` : 'Tạo vai trò'}</h2>
        <div className="c-login__field">
          {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
          <label className="c-login__label" htmlFor="new-role-code">Mã vai trò</label>
          <input id="new-role-code" className="c-login__input" value={code} onChange={e => this.setState({ code: e.target.value })} />
        </div>
        <div className="c-login__field">
          {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
          <label className="c-login__label" htmlFor="new-role-name">Tên vai trò</label>
          <input id="new-role-name" className="c-login__input" value={name} onChange={e => this.setState({ name: e.target.value })} />
        </div>
        {error && <p className="c-form__error" role="alert">{error}</p>}
        <div className="c-confirm-dialog__actions" style={{ justifyContent: 'flex-start', marginTop: 16 }}>
          <button type="submit" className="c-login__submit" disabled={saving || !code.trim() || !name.trim()}>
            {saving ? 'Đang tạo…' : 'Tạo vai trò'}
          </button>
          <button type="button" className="c-login__secondary" onClick={onCancel} disabled={saving}>Hủy</button>
        </div>
      </form>
    );
  }
}

CreateRoleDialog.propTypes = {
  onCreate: PropTypes.func.isRequired,
  onCancel: PropTypes.func.isRequired,
  initialPermissionCodes: PropTypes.arrayOf(PropTypes.string),
  sourceName: PropTypes.string,
};

CreateRoleDialog.defaultProps = { initialPermissionCodes: [], sourceName: null };

export default CreateRoleDialog;
