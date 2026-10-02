import React from 'react';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { Redirect } from 'react-router-dom';
import { getAvailableWorkspaces, resolveStartTarget } from './routeAccess';
import { FEATURE_AVAILABILITY } from './availability';
import { selectWorkspace as selectWorkspaceAction, clearWorkspace as clearWorkspaceAction } from './redux/action';
import WorkspacePicker from './component/WorkspacePicker';

const isValid = (selectedId, workspaces) => Boolean(selectedId) && workspaces.some(w => w.id === selectedId);

// /start: 0 khu vực -> /no-access; 1 -> chọn sẵn rồi vào thẳng; nhiều -> picker. Lựa chọn chỉ trong RAM.
// Mọi dispatch nằm ở lifecycle (không trong render).
class StartContainer extends React.Component {
  componentDidMount() {
    this.sync();
  }

  componentDidUpdate() {
    this.sync();
  }

  sync() {
    const {
      workspaces, selectedId, selectWorkspace, clearWorkspace,
    } = this.props;
    if (selectedId && !isValid(selectedId, workspaces)) {
      // Khu vực đã bị thu hồi (quyền đổi sau /me): bỏ lựa chọn cũ.
      clearWorkspace();
      return;
    }
    if (!selectedId && workspaces.length === 1) selectWorkspace(workspaces[0].id);
  }

  render() {
    const {
      workspaces, selectedId, selectWorkspace, permissions, location,
    } = this.props;
    if (workspaces.length === 0) return <Redirect to="/no-access" />;
    if (isValid(selectedId, workspaces)) {
      const from = location && location.state ? location.state.from : null;
      return <Redirect to={resolveStartTarget(selectedId, from, permissions, FEATURE_AVAILABILITY)} />;
    }
    if (workspaces.length === 1) return null;
    return <WorkspacePicker workspaces={workspaces} onSelect={selectWorkspace} />;
  }
}

StartContainer.propTypes = {
  workspaces: PropTypes.arrayOf(PropTypes.shape({ id: PropTypes.string.isRequired })).isRequired,
  permissions: PropTypes.arrayOf(PropTypes.string),
  selectedId: PropTypes.string,
  location: PropTypes.shape({ state: PropTypes.object }), // eslint-disable-line react/forbid-prop-types
  selectWorkspace: PropTypes.func.isRequired,
  clearWorkspace: PropTypes.func.isRequired,
};

StartContainer.defaultProps = { selectedId: null, permissions: [], location: null };

const mapStateToProps = state => ({
  workspaces: getAvailableWorkspaces(state.auth.permissions, FEATURE_AVAILABILITY),
  permissions: state.auth.permissions,
  selectedId: state.workspace.selectedId,
});

export default connect(mapStateToProps, { selectWorkspace: selectWorkspaceAction, clearWorkspace: clearWorkspaceAction })(StartContainer);
