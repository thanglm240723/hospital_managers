import React from 'react';
import PropTypes from 'prop-types';

const GENDER_LABEL = { male: 'Nam', female: 'Nữ' };

const ResultsTable = ({ items, onIntake }) => (
  <table className="c-table">
    <thead>
      <tr>
        <th>Họ tên</th>
        <th>Giới</th>
        <th>Ngày sinh</th>
        <th>Điện thoại</th>
        <th>Giấy tờ</th>
        <th />
      </tr>
    </thead>
    <tbody>
      {items.length === 0 && (
        <tr><td colSpan={6} className="c-table__empty">Chưa có kết quả. Nhập điều kiện tìm và bấm Tìm.</td></tr>
      )}
      {items.map(patient => (
        <tr key={patient.id}>
          <td>{patient.fullName}</td>
          <td>{GENDER_LABEL[patient.gender] || '—'}</td>
          <td>{patient.birthYearOnly ? patient.birthDate.slice(0, 4) : patient.birthDate}</td>
          <td>{patient.phone}</td>
          <td>{patient.documentNumber || '—'}</td>
          <td>
            <button type="button" className="c-toolbar__action" style={{ marginLeft: 0 }} onClick={() => onIntake(patient.id)}>
              Tiếp nhận →
            </button>
          </td>
        </tr>
      ))}
    </tbody>
  </table>
);

ResultsTable.propTypes = {
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  onIntake: PropTypes.func.isRequired,
};

export default ResultsTable;
