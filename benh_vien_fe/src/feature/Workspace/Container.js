import React from 'react';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { Redirect } from 'react-router-dom';
import { WORKSPACES, workspacesForUser } from './workspaces';
import { selectWorkspace as selectWorkspaceAction } from './redux/action';
import WorkspacePicker from './component/WorkspacePicker';

/* eslint-disable react/destructuring-assignment */
class StartContainer extends React.Component {
  componentDidMount() {
    const { workspaces, selectedId, selectWorkspace } = this.props;
    if (!selectedId && workspaces.length === 1) selectWorkspace(workspaces[0].id);
  }

  render() {
    const { workspaces, selectedId, selectWorkspace } = this.props;
    const selected = selectedId && WORKSPACES.find(w => w.id === selectedId);
    if (selected) return <Redirect to={selected.defaultRoute} />;
    return <WorkspacePicker workspaces={workspaces} onSelect={selectWorkspace} />;
  }
}

StartContainer.propTypes = {
  workspaces: PropTypes.array.isRequired, // eslint-disable-line react/forbid-prop-types
  selectedId: PropTypes.string,
  selectWorkspace: PropTypes.func.isRequired,
};

StartContainer.defaultProps = { selectedId: null };

const mapStateToProps = state => ({
  workspaces: workspacesForUser(state.auth.permissions),
  selectedId: state.workspace.selectedId,
});

export default connect(mapStateToProps, { selectWorkspace: selectWorkspaceAction })(StartContainer);
