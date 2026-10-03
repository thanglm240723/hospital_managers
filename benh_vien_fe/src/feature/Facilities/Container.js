import React from 'react';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { AppLayout } from 'feature/Shell';
import { hasPermission } from 'feature/Auth/permissions';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import {
  loadFacilities, createBranch, createDepartment, createRoom, updateFacility,
} from './redux/action';
import FacilityTree from './component/FacilityTree';
import FacilityDialog from './component/FacilityDialog';

const flatten = branches => branches.reduce((acc, b) => acc.concat(
  [{ type: 'branches', item: b }],
  b.departments.reduce((accD, d) => accD.concat(
    [{ type: 'departments', item: d }],
    d.rooms.map(r => ({ type: 'rooms', item: r })),
  ), []),
), []);

class FacilitiesContainer extends React.Component {
  constructor(props) {
    super(props);
    // dialog: null | { mode: 'create', type, parent } | { mode: 'edit', type, id }
    this.state = { dialog: null };
  }

  componentDidMount() {
    const { loadFacilities: onLoad } = this.props;
    onLoad();
  }

  findItem = (dialog) => {
    const { branches } = this.props;
    const found = flatten(branches).find(entry => entry.type === dialog.type && entry.item.id === dialog.id);
    return found ? found.item : null;
  };

  handleSubmit = async (values) => {
    const {
      createBranch: onCreateBranch, createDepartment: onCreateDepartment, createRoom: onCreateRoom, updateFacility: onUpdate, loadFacilities: onLoad,
    } = this.props;
    const { dialog } = this.state;
    try {
      if (dialog.mode === 'edit') {
        const current = this.findItem(dialog);
        await onUpdate(dialog.type, dialog.id, values, current ? current.rowVersion : '');
      } else if (dialog.type === 'branches') {
        await onCreateBranch(values);
      } else if (dialog.type === 'departments') {
        await onCreateDepartment({ ...values, branchId: dialog.parent.id });
      } else {
        await onCreateRoom({ ...values, departmentId: dialog.parent.id });
      }
    } catch (error) {
      const status = error && error.response && error.response.status;
      // 412/404: nạp lại cây (rowVersion mới), giữ dialog mở với giá trị người dùng đã nhập; không tự gửi lại.
      if (status === 412 || status === 404) await onLoad();
      throw error;
    }
    this.setState({ dialog: null });
  };

  render() {
    const {
      branches, canManage, loading, error, loadFacilities: onReload,
    } = this.props;
    const { dialog } = this.state;
    return (
      <AppLayout>
        <div className="c-page">
          <h1 className="c-page__title">Cơ cấu tổ chức</h1>
          {error && (
            <p className="c-form__error" role="alert">
              {`Không tải được cơ cấu tổ chức: ${error}`}
              <button type="button" className="c-login__secondary" onClick={onReload}>Thử lại</button>
            </p>
          )}
          {canManage && (
            <div className="c-toolbar">
              <button type="button" className="c-toolbar__action" onClick={() => this.setState({ dialog: { mode: 'create', type: 'branches', parent: null } })}>
                + Thêm cơ sở
              </button>
            </div>
          )}
          <div className="c-admin-users__layout">
            <FacilityTree
              branches={branches}
              canManage={canManage}
              onAdd={(type, parent) => this.setState({ dialog: { mode: 'create', type, parent } })}
              onEdit={(type, id) => this.setState({ dialog: { mode: 'edit', type, id } })}
            />
            {canManage && dialog && (
              <FacilityDialog
                key={`${dialog.mode}-${dialog.type}-${dialog.id || (dialog.parent && dialog.parent.id) || 'root'}`}
                mode={dialog.mode}
                type={dialog.type}
                parent={dialog.parent}
                item={dialog.mode === 'edit' ? this.findItem(dialog) : null}
                onSubmit={this.handleSubmit}
                onCancel={() => this.setState({ dialog: null })}
              />
            )}
          </div>
          {!branches.length && !loading && !error && <p>Chưa có cơ sở nào.</p>}
        </div>
      </AppLayout>
    );
  }
}

FacilitiesContainer.propTypes = {
  branches: PropTypes.arrayOf(PropTypes.object).isRequired,
  canManage: PropTypes.bool.isRequired,
  loading: PropTypes.bool.isRequired,
  error: PropTypes.string,
  loadFacilities: PropTypes.func.isRequired,
  createBranch: PropTypes.func.isRequired,
  createDepartment: PropTypes.func.isRequired,
  createRoom: PropTypes.func.isRequired,
  updateFacility: PropTypes.func.isRequired,
};

FacilitiesContainer.defaultProps = { error: null };

const mapStateToProps = state => ({
  branches: state.facilities.branches,
  loading: state.facilities.loading,
  error: state.facilities.error,
  canManage: hasPermission(state.auth, PERMISSIONS.FACILITIES_MANAGE),
});

export default connect(mapStateToProps, {
  loadFacilities, createBranch, createDepartment, createRoom, updateFacility,
})(FacilitiesContainer);
