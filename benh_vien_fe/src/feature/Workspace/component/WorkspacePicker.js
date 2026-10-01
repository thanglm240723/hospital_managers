import React from 'react';
import PropTypes from 'prop-types';

// Chỉ hiển thị; chọn thẻ gọi onSelect từ sự kiện click. Điều hướng do StartContainer quyết định.
const WorkspacePicker = ({ workspaces, onSelect }) => (
  <div className="c-workspace-picker">
    <h1 className="c-workspace-picker__title">Chọn khu vực làm việc</h1>
    <p className="c-workspace-picker__subtitle">Tài khoản của bạn có quyền truy cập nhiều khu vực. Chọn một khu vực để tiếp tục.</p>
    <div className="c-workspace-picker__grid">
      {workspaces.map(workspace => (
        <button key={workspace.id} type="button" className="c-workspace-picker__card" onClick={() => onSelect(workspace.id)}>
          <span className="c-workspace-picker__card-name">{workspace.name}</span>
          <span className="c-workspace-picker__card-desc">{workspace.description}</span>
          {!workspace.ready && <span className="c-workspace-picker__card-badge">Đang hoàn thiện</span>}
        </button>
      ))}
    </div>
  </div>
);

WorkspacePicker.propTypes = {
  workspaces: PropTypes.arrayOf(
    PropTypes.shape({
      id: PropTypes.string.isRequired,
      name: PropTypes.string.isRequired,
      description: PropTypes.string,
      ready: PropTypes.bool,
    }),
  ).isRequired,
  onSelect: PropTypes.func.isRequired,
};

export default WorkspacePicker;
