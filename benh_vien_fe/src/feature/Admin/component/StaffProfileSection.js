import React from 'react';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import { hasPermission } from 'feature/Auth/permissions';
import { problemTitle } from 'feature/Auth/problem';
import { getFacilityTree } from 'feature/Facilities/api';
import {
  getStaffProfile, createStaffProfile, updateStaffProfile, setWorkScopes,
} from '../api/staffProfileClient';

export const NO_SCOPE_WARNING = 'Người này chưa có cơ sở/khoa làm việc nên không truy cập được dữ liệu nghiệp vụ.';
export const NO_FACILITIES_READ = 'Thiếu quyền facilities.read nên không tải được danh sách khoa';

const responseOf = error => (error && error.response) || {};
const scopeIds = profile => (profile.workScopes || []).map(s => s.departmentId);

export const describeStaffError = (error, nameOf = id => id) => {
  const { data, status } = responseOf(error);
  const code = data && data.code;
  if (status === 412 || code === 'staff_profile_version_conflict') {
    return {
      conflict: true,
      message: 'Hồ sơ nhân sự đã bị thay đổi (staff_profile_version_conflict). Đã nạp lại bản mới; lựa chọn của bạn được giữ và chưa được lưu.',
    };
  }
  if (code === 'staff_profile_exists') {
    return { conflict: true, message: 'Hồ sơ đã được tạo bởi người khác, đã nạp lại (staff_profile_exists).' };
  }
  if (code === 'invalid_departments') {
    const ids = (data.errors && data.errors.departmentIds) || [];
    return { conflict: false, message: `Khoa không hợp lệ (invalid_departments): ${[].concat(ids).map(nameOf).join(', ')}` };
  }
  const title = problemTitle(error);
  return { conflict: false, message: code ? `${title} (${code})` : title };
};

class StaffProfileSection extends React.Component {
  constructor(props) {
    super(props);
    this.state = {
      loading: true,
      loadError: null,
      profile: null,
      missing: false,
      tree: null,
      treeError: false,
      selected: [],
      form: { staffCode: '', isActive: true },
      editCode: null,
      busy: false,
      error: null,
    };
    this.unmounted = false;
  }

  componentDidMount() {
    this.load();
    this.loadTree();
  }

  componentDidUpdate(prev) {
    // Đổi user: bỏ dữ liệu cũ, nạp lại (Panel cũng đặt key={userId} nên đây chỉ là lớp bảo vệ).
    const { userId } = this.props;
    if (prev.userId !== userId) {
      // eslint-disable-next-line react/no-did-update-set-state
      this.setState({
        loading: true, loadError: null, profile: null, missing: false, selected: [], error: null, editCode: null,
      });
      this.load();
    }
  }

  componentWillUnmount() {
    this.unmounted = true;
  }

  stale = (userId) => {
    const { userId: current } = this.props;
    return this.unmounted || userId !== current;
  };

  // keepSelection: sau 412 giữ lựa chọn của người dùng để đối chiếu.
  load = async (keepSelection = false) => {
    const { userId } = this.props;
    try {
      const profile = await getStaffProfile(userId);
      if (this.stale(userId)) return;
      this.setState(state => ({
        loading: false, loadError: null, profile, missing: false, selected: keepSelection ? state.selected : scopeIds(profile),
      }));
    } catch (error) {
      if (this.stale(userId)) return;
      const { data, status } = responseOf(error);
      if (status === 404 && data && data.code === 'staff_profile_not_found') {
        this.setState({
          loading: false, loadError: null, profile: null, missing: true,
        });
      } else {
        this.setState({ loading: false, loadError: describeStaffError(error).message });
      }
    }
  };

  run = async (action, onOk) => {
    const { userId } = this.props;
    this.setState({ busy: true, error: null });
    try {
      const result = await action();
      if (!this.stale(userId)) onOk(result);
    } catch (error) {
      if (this.stale(userId)) return;
      const described = describeStaffError(error, this.departmentName);
      this.setState({ error: described.message });
      if (described.conflict) await this.load(true);
    } finally {
      if (!this.stale(userId)) this.setState({ busy: false });
    }
  };

  departmentName = (id) => {
    const { tree } = this.state;
    const found = (tree || []).reduce((acc, b) => acc || (b.departments || []).find(d => d.id === id), null);
    return found ? found.name : id;
  };

  create = (event) => {
    event.preventDefault();
    const { userId } = this.props;
    const { form } = this.state;
    this.run(
      () => createStaffProfile(userId, { staffCode: form.staffCode.trim(), isActive: form.isActive }),
      profile => this.setState({ profile, missing: false, selected: scopeIds(profile) }),
    );
  };

  saveProfile = (event) => {
    event.preventDefault();
    const { userId } = this.props;
    const { profile, editCode } = this.state;
    this.run(
      () => updateStaffProfile(userId, { staffCode: editCode.staffCode.trim(), isActive: editCode.isActive }, profile.rowVersion),
      updated => this.setState({ profile: updated, editCode: null }),
    );
  };

  saveScopes = () => {
    const { userId } = this.props;
    const { profile, selected } = this.state;
    this.run(
      () => setWorkScopes(userId, selected, profile.rowVersion),
      updated => this.setState({ profile: updated, selected: scopeIds(updated) }),
    );
  };

  toggle = (id) => {
    this.setState(state => ({
      selected: state.selected.indexOf(id) === -1 ? [...state.selected, id] : state.selected.filter(x => x !== id),
    }));
  };

  async loadTree() {
    const { canReadFacilities } = this.props;
    if (!canReadFacilities) return;
    try {
      const tree = await getFacilityTree();
      if (!this.unmounted) this.setState({ tree: tree || [] });
    } catch (error) {
      if (!this.unmounted) this.setState({ treeError: true });
    }
  }

  renderScopes(profile) {
    const groups = [];
    (profile.workScopes || []).forEach((s) => {
      let group = groups.find(g => g.branchId === s.branchId);
      if (!group) {
        group = { branchId: s.branchId, branchName: s.branchName, items: [] };
        groups.push(group);
      }
      group.items.push(s);
    });
    if (groups.length === 0) return <p>Chưa có phạm vi làm việc.</p>;
    return groups.map(g => (
      <div key={g.branchId}>
        <strong>{g.branchName}</strong>
        <ul>{g.items.map(s => <li key={s.departmentId}>{s.departmentName}</li>)}</ul>
      </div>
    ));
  }

  renderPicker() {
    const { canReadFacilities } = this.props;
    const {
      tree, treeError, selected, busy,
    } = this.state;
    if (!canReadFacilities) return <p className="c-detail-panel__subtitle">{NO_FACILITIES_READ}</p>;
    if (treeError) return <p className="c-form__error" role="alert">Không tải được danh sách khoa.</p>;
    if (!tree) return <p>Đang tải danh sách khoa…</p>;
    return (
      <div>
        {tree.map(branch => (
          <div key={branch.id}>
            <strong>{branch.name}</strong>
            {(branch.departments || []).filter(d => d.isActive || selected.indexOf(d.id) !== -1).map(d => (
              <label key={d.id} className="c-checkbox" htmlFor={`sp-dep-${d.id}`}>
                <input id={`sp-dep-${d.id}`} type="checkbox" checked={selected.indexOf(d.id) !== -1} disabled={busy} onChange={() => this.toggle(d.id)} />
                {d.name}
              </label>
            ))}
          </div>
        ))}
        <button type="button" className="c-login__submit" disabled={busy} onClick={this.saveScopes}>Lưu phạm vi</button>
      </div>
    );
  }

  renderForm(values, onChange, onSubmit, submitLabel) {
    const { busy } = this.state;
    return (
      <form onSubmit={onSubmit}>
        <div className="c-login__field">
          {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
          <label className="c-login__label" htmlFor="sp-code">Mã nhân sự</label>
          <input id="sp-code" className="c-login__input" value={values.staffCode} maxLength={50} onChange={e => onChange({ ...values, staffCode: e.target.value })} />
        </div>
        <label className="c-checkbox" htmlFor="sp-active">
          <input id="sp-active" type="checkbox" checked={values.isActive} onChange={e => onChange({ ...values, isActive: e.target.checked })} />
          Đang làm việc
        </label>
        <button type="submit" className="c-login__submit" disabled={busy || !values.staffCode.trim()}>{submitLabel}</button>
      </form>
    );
  }

  renderBody() {
    const { canManage } = this.props;
    const {
      profile, missing, form, editCode,
    } = this.state;
    if (missing) {
      return (
        <div>
          <p>Chưa có hồ sơ nhân sự</p>
          {canManage && this.renderForm(form, f => this.setState({ form: f }), this.create, 'Tạo hồ sơ')}
        </div>
      );
    }
    if (!profile) return null;
    const noScope = !profile.isActive || (profile.workScopes || []).length === 0;
    return (
      <div>
        <p>{`Mã nhân sự: ${profile.staffCode}`}</p>
        <p>{profile.isActive ? 'Đang làm việc' : 'Ngừng làm việc'}</p>
        {noScope && <p className="c-confirm-dialog__warning" role="alert">{NO_SCOPE_WARNING}</p>}
        {this.renderScopes(profile)}
        {canManage && (
          <div>
            {editCode
              ? this.renderForm(editCode, f => this.setState({ editCode: f }), this.saveProfile, 'Lưu hồ sơ')
              : (
                <button
                  type="button"
                  className="c-login__secondary"
                  onClick={() => this.setState({ editCode: { staffCode: profile.staffCode, isActive: profile.isActive } })}
                >
                  Sửa mã/trạng thái
                </button>
              )}
            {this.renderPicker()}
          </div>
        )}
      </div>
    );
  }

  render() {
    const { loading, loadError, error } = this.state;
    return (
      <section className="c-detail-panel__section">
        <h3>Hồ sơ nhân sự</h3>
        {loading && <p>Đang tải…</p>}
        {loadError && <p className="c-form__error" role="alert">{loadError}</p>}
        {!loading && !loadError && this.renderBody()}
        {error && <div className="c-form__error" role="alert">{error}</div>}
      </section>
    );
  }
}

StaffProfileSection.propTypes = {
  userId: PropTypes.string.isRequired,
  canManage: PropTypes.bool,
  canReadFacilities: PropTypes.bool,
};

StaffProfileSection.defaultProps = { canManage: false, canReadFacilities: false };

export default connect(state => ({
  canManage: hasPermission(state.auth, PERMISSIONS.STAFF_PROFILES_MANAGE),
  canReadFacilities: hasPermission(state.auth, PERMISSIONS.FACILITIES_READ),
}))(StaffProfileSection);
