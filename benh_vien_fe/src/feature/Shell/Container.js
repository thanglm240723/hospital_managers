import React from 'react';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { logout } from 'feature/Auth/redux/actions';
import { switchWorkspace } from 'feature/Workspace/redux/action';
import { getAvailableWorkspaces, getMenuRoutes } from 'feature/Workspace/routeAccess';
import { FEATURE_AVAILABILITY } from 'feature/Workspace/availability';
import AppLayout from './component/AppLayout';

const ShellContainer = ({ children, ...rest }) => <AppLayout {...rest}>{children}</AppLayout>;

ShellContainer.propTypes = { children: PropTypes.node.isRequired };

const mapStateToProps = state => ({
  user: state.auth.user,
  selectedWorkspaceId: state.workspace.selectedId,
  availableWorkspaces: getAvailableWorkspaces(state.auth.permissions, FEATURE_AVAILABILITY),
  // Menu chỉ của khu vực đang chọn — không trộn menu khu vực khác dù có quyền.
  menuItems: getMenuRoutes(state.workspace.selectedId, state.auth.permissions, FEATURE_AVAILABILITY),
});

const mapDispatchToProps = dispatch => ({
  onChangeWorkspace: id => dispatch(switchWorkspace(id)),
  onLogout: () => dispatch(logout()),
});

export default connect(mapStateToProps, mapDispatchToProps)(ShellContainer);
