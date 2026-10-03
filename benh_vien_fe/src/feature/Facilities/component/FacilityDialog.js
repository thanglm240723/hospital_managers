import React from 'react';
import PropTypes from 'prop-types';
import { problemTitle } from 'feature/Auth/problem';
import { KIND_LABELS } from './FacilityTree';

const CODE_MESSAGES = {
  facility_code_taken: 'Mã đã tồn tại trong cùng cấp',
  parent_facility_inactive: 'Đơn vị cấp trên đang ngừng dùng, không thể thêm đơn vị con',
  facility_has_active_children: 'Còn đơn vị con đang dùng, hãy ngừng dùng các đơn vị con trước',
  facility_not_found: 'Không tìm thấy đơn vị, đã nạp lại danh sách',
  invalid_if_match: 'Thiếu hoặc sai phiên bản dữ liệu, hãy nạp lại',
};

export const describeFacilityError = (error) => {
  const response = error && error.response;
  const data = response && response.data;
  const code = data && data.code;
  if (response && response.status === 412) return 'Dữ liệu đã thay đổi, đã nạp lại. Kiểm tra lại giá trị rồi lưu lần nữa.';
  if (code && CODE_MESSAGES[code]) return `${CODE_MESSAGES[code]} (${code})`;
  const title = problemTitle(error);
  return code ? `${title} (${code})` : title;
};

const TITLES = { branches: 'cơ sở', departments: 'khoa', rooms: 'phòng' };

// mode 'create': nhập mã/tên (+ loại cho khoa). mode 'edit': sửa tên + đang dùng. Giữ giá trị người dùng nhập khi lỗi (kể cả 412).
class FacilityDialog extends React.Component {
  constructor(props) {
    super(props);
    const { item } = props;
    this.state = {
      code: '', name: item ? item.name : '', isActive: item ? item.isActive : true, kind: 'clinical', saving: false, error: null,
    };
  }

  componentWillUnmount() {
    this.unmounted = true;
  }

  handleSubmit = async (event) => {
    event.preventDefault();
    const { mode, type, onSubmit } = this.props;
    const {
      code, name, isActive, kind,
    } = this.state;
    if (!name.trim() || (mode === 'create' && !code.trim())) return;
    this.setState({ saving: true, error: null });
    try {
      if (mode === 'create') {
        await onSubmit({
          code: code.trim().toUpperCase(), name: name.trim(), ...(type === 'departments' ? { kind } : {}),
        });
      } else {
        await onSubmit({ name: name.trim(), isActive });
      }
    } catch (error) {
      if (!this.unmounted) this.setState({ error: describeFacilityError(error) });
    } finally {
      if (!this.unmounted) this.setState({ saving: false });
    }
  };

  render() {
    const {
      mode, type, parent, onCancel,
    } = this.props;
    const {
      code, name, isActive, kind, saving, error,
    } = this.state;
    const create = mode === 'create';
    const title = `${create ? 'Thêm' : 'Sửa'} ${TITLES[type]}${create && parent ? ` — ${parent.name}` : ''}`;
    return (
      <form className="c-detail-panel" onSubmit={this.handleSubmit} aria-label={title}>
        <h2 className="c-detail-panel__title">{title}</h2>
        {create && (
          <div className="c-login__field">
            {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
            <label className="c-login__label" htmlFor="facility-code">Mã</label>
            <input id="facility-code" className="c-login__input" value={code} onChange={e => this.setState({ code: e.target.value.toUpperCase() })} />
          </div>
        )}
        <div className="c-login__field">
          {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
          <label className="c-login__label" htmlFor="facility-name">Tên</label>
          <input id="facility-name" className="c-login__input" value={name} onChange={e => this.setState({ name: e.target.value })} />
        </div>
        {create && type === 'departments' && (
          <div className="c-login__field">
            {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
            <label className="c-login__label" htmlFor="facility-kind">Loại</label>
            <select id="facility-kind" className="c-login__input" value={kind} onChange={e => this.setState({ kind: e.target.value })}>
              {Object.keys(KIND_LABELS).map(k => <option key={k} value={k}>{KIND_LABELS[k]}</option>)}
            </select>
          </div>
        )}
        {!create && (
          <div className="c-login__field">
            <label htmlFor="facility-active">
              <input id="facility-active" type="checkbox" checked={isActive} onChange={e => this.setState({ isActive: e.target.checked })} />
              {' Đang dùng'}
            </label>
          </div>
        )}
        {error && <p className="c-form__error" role="alert">{error}</p>}
        <div className="c-confirm-dialog__actions" style={{ justifyContent: 'flex-start', marginTop: 16 }}>
          <button type="submit" className="c-login__submit" disabled={saving || !name.trim() || (create && !code.trim())}>
            {saving ? 'Đang lưu…' : 'Lưu'}
          </button>
          <button type="button" className="c-login__secondary" onClick={onCancel} disabled={saving}>Hủy</button>
        </div>
      </form>
    );
  }
}

FacilityDialog.propTypes = {
  mode: PropTypes.oneOf(['create', 'edit']).isRequired,
  type: PropTypes.oneOf(['branches', 'departments', 'rooms']).isRequired,
  parent: PropTypes.shape({ name: PropTypes.string }),
  item: PropTypes.shape({ name: PropTypes.string, isActive: PropTypes.bool }),
  onSubmit: PropTypes.func.isRequired,
  onCancel: PropTypes.func.isRequired,
};

FacilityDialog.defaultProps = { parent: null, item: null };

export default FacilityDialog;
