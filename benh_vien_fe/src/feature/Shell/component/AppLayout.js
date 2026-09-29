import React from 'react';
import PropTypes from 'prop-types';
import { NavLink } from 'react-router-dom';
import Can from 'feature/Auth/Can';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import { HOSPITAL_NAME } from 'feature/Auth/AuthLayout';
import { WORKSPACES } from 'feature/Workspace/workspaces';

const NAV_ITEMS = [
  { to: '/admin/users', label: 'Tài khoản', permission: PERMISSIONS.USERS_READ },
  { to: '/admin/roles', label: 'Vai trò & quyền', permission: PERMISSIONS.ROLES_READ },
  { to: '/reception/patients', label: 'Tìm hồ sơ', permission: PERMISSIONS.PATIENTS_READ },
  { to: '/clinic/queue', label: 'Hàng chờ khám', permission: PERMISSIONS.ENCOUNTERS_EXAMINE },
  { to: '/vitals', label: 'Sinh hiệu', permission: PERMISSIONS.VITALS_RECORD },
];

const AppLayout = ({
  children, user, selectedWorkspaceId, onChangeWorkspace, onLogout, availableWorkspaces,
}) => {
  const currentWorkspace = WORKSPACES.find(w => w.id === selectedWorkspaceId);
  return (
    <div className="c-app-layout">
      <aside className="c-app-layout__sidebar">
        <div className="c-app-layout__brand">{HOSPITAL_NAME}</div>

        {availableWorkspaces.length > 1 && (
          <div className="c-app-layout__workspace">
            {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
            <label className="c-app-layout__workspace-label" htmlFor="workspace-select">Khu vực</label>
            <select
              id="workspace-select"
              className="c-app-layout__workspace-select"
              value={selectedWorkspaceId || ''}
              onChange={event => onChangeWorkspace(event.target.value)}
            >
              {availableWorkspaces.map(w => (
                <option key={w.id} value={w.id}>{w.name}</option>
              ))}
            </select>
          </div>
        )}
        {availableWorkspaces.length === 1 && (
          <div className="c-app-layout__workspace-fixed">{currentWorkspace && currentWorkspace.name}</div>
        )}

        <nav className="c-app-layout__nav">
          {NAV_ITEMS.map(item => (
            <Can key={item.to} permission={item.permission}>
              <NavLink to={item.to} className="c-app-layout__nav-link" activeClassName="is-active">
                {item.label}
              </NavLink>
            </Can>
          ))}
        </nav>

        <div className="c-app-layout__user">
          <div className="c-app-layout__user-name">{user && (user.fullName || user.email)}</div>
          <NavLink to="/change-password" className="c-app-layout__user-link">Đổi mật khẩu</NavLink>
          <button type="button" className="c-app-layout__user-link c-app-layout__user-link--button" onClick={onLogout}>
            Đăng xuất
          </button>
        </div>
      </aside>
      <main className="c-app-layout__content">{children}</main>
    </div>
  );
};

AppLayout.propTypes = {
  children: PropTypes.node.isRequired,
  user: PropTypes.shape({ fullName: PropTypes.string, email: PropTypes.string }),
  selectedWorkspaceId: PropTypes.string,
  availableWorkspaces: PropTypes.arrayOf(PropTypes.shape({ id: PropTypes.string, name: PropTypes.string })).isRequired,
  onChangeWorkspace: PropTypes.func.isRequired,
  onLogout: PropTypes.func.isRequired,
};

AppLayout.defaultProps = { user: null, selectedWorkspaceId: null };

export default AppLayout;
