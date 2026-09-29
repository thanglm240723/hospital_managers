import React from 'react';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { logout } from 'feature/Auth/redux/actions';
import { selectWorkspace, workspacesForUser } from 'feature/Workspace';
import AppLayout from './component/AppLayout';

const ShellContainer = ({ children, ...rest }) => <AppLayout {...rest}>{children}</AppLayout>;

ShellContainer.propTypes = { children: PropTypes.node.isRequired };

const mapStateToProps = state => ({
  user: state.auth.user,
  selectedWorkspaceId: state.workspace.selectedId,
  availableWorkspaces: workspacesForUser(state.auth.permissions),
});

const mapDispatchToProps = dispatch => ({
  onChangeWorkspace: id => dispatch(selectWorkspace(id)),
  onLogout: () => dispatch(logout()),
});

export default connect(mapStateToProps, mapDispatchToProps)(ShellContainer);
