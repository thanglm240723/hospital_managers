import React from 'react';
import PropTypes from 'prop-types';
import { Redirect } from 'react-router-dom';

// /start: 0 khu vực -> /no-access; 1 khu vực -> vào thẳng; nhiều -> cho chọn thẻ.
const WorkspacePicker = ({ workspaces, onSelect }) => {
  if (workspaces.length === 0) return <Redirect to="/no-access" />;
  if (workspaces.length === 1) {
    onSelect(workspaces[0].id);
    return <Redirect to={workspaces[0].defaultRoute} />;
  }

  return (
    <div className="c-workspace-picker">
      <h1 className="c-workspace-picker__title">Chọn khu vực làm việc</h1>
      <p className="c-workspace-picker__subtitle">Tài khoản của bạn có quyền truy cập nhiều khu vực. Chọn một khu vực để tiếp tục.</p>
      <div className="c-workspace-picker__grid">
        {workspaces.map(workspace => (
          <button
            key={workspace.id}
            type="button"
            className="c-workspace-picker__card"
            onClick={() => onSelect(workspace.id, workspace.defaultRoute)}
          >
            <span className="c-workspace-picker__card-name">{workspace.name}</span>
            <span className="c-workspace-picker__card-desc">{workspace.description}</span>
          </button>
        ))}
      </div>
    </div>
  );
};

WorkspacePicker.propTypes = {
  workspaces: PropTypes.arrayOf(PropTypes.shape({
    id: PropTypes.string.isRequired,
    name: PropTypes.string.isRequired,
    description: PropTypes.string,
    defaultRoute: PropTypes.string.isRequired,
  })).isRequired,
  onSelect: PropTypes.func.isRequired,
};

export default WorkspacePicker;
