import React from 'react';
import PropTypes from 'prop-types';

export const KIND_LABELS = {
  clinical: 'Lâm sàng',
  laboratory: 'Xét nghiệm',
  pharmacy: 'Dược',
  billing: 'Viện phí',
  administrative: 'Hành chính',
};

const Row = ({
  label, item, canManage, addLabel, onAdd, onEdit,
}) => (
  <div className="c-facility-tree__row">
    <span className="c-facility-tree__name">{label}</span>
    {!item.isActive && <span className="c-badge c-badge--danger">Ngừng dùng</span>}
    {canManage && onAdd && <button type="button" className="c-login__secondary" onClick={onAdd}>{addLabel}</button>}
    {canManage && <button type="button" className="c-login__secondary" onClick={onEdit}>Sửa</button>}
  </div>
);
Row.propTypes = {
  label: PropTypes.string.isRequired,
  item: PropTypes.shape({ isActive: PropTypes.bool }).isRequired,
  canManage: PropTypes.bool.isRequired,
  addLabel: PropTypes.string,
  onAdd: PropTypes.func,
  onEdit: PropTypes.func.isRequired,
};
Row.defaultProps = { addLabel: null, onAdd: null };

const FacilityTree = ({
  branches, canManage, onAdd, onEdit,
}) => (
  <ul className="c-facility-tree">
    {branches.map(branch => (
      <li key={branch.id} className="c-facility-tree__branch">
        <Row
          label={`${branch.code} — ${branch.name}`}
          item={branch}
          canManage={canManage}
          addLabel="Thêm khoa"
          onAdd={() => onAdd('departments', branch)}
          onEdit={() => onEdit('branches', branch.id)}
        />
        <ul className="c-facility-tree__children">
          {branch.departments.map(department => (
            <li key={department.id} className="c-facility-tree__department">
              <Row
                label={`${department.code} — ${department.name} (${KIND_LABELS[department.kind] || department.kind})`}
                item={department}
                canManage={canManage}
                addLabel="Thêm phòng"
                onAdd={() => onAdd('rooms', department)}
                onEdit={() => onEdit('departments', department.id)}
              />
              <ul className="c-facility-tree__children">
                {department.rooms.map(room => (
                  <li key={room.id} className="c-facility-tree__room">
                    <Row label={`${room.code} — ${room.name}`} item={room} canManage={canManage} onEdit={() => onEdit('rooms', room.id)} />
                  </li>
                ))}
              </ul>
            </li>
          ))}
        </ul>
      </li>
    ))}
  </ul>
);

FacilityTree.propTypes = {
  branches: PropTypes.arrayOf(PropTypes.object).isRequired,
  canManage: PropTypes.bool.isRequired,
  onAdd: PropTypes.func.isRequired,
  onEdit: PropTypes.func.isRequired,
};

export default FacilityTree;
