import React from 'react';
import PropTypes from 'prop-types';

// Phiếu cấp số — chỉ hiển thị khi in (@media print ẩn phần còn lại của trang, xem _component.reception.scss).
const PrintTicket = ({ visit }) => (
  <div className="c-print-ticket">
    <p className="c-print-ticket__hospital">[TÊN BỆNH VIỆN]</p>
    <p className="c-print-ticket__label">Số thứ tự</p>
    <p className="c-print-ticket__number">{visit.queueNumber}</p>
    <p className="c-print-ticket__room">{visit.roomLabel}</p>
    <table className="c-print-ticket__info">
      <tbody>
        <tr>
          <td>Bệnh nhân</td>
          <td>{visit.patientName}</td>
        </tr>
        <tr>
          <td>Khoa</td>
          <td>{visit.department}</td>
        </tr>
        <tr>
          <td>Buổi</td>
          <td>{visit.sessionLabel}</td>
        </tr>
        <tr>
          <td>Bác sĩ</td>
          <td>{visit.doctor}</td>
        </tr>
      </tbody>
    </table>
  </div>
);

PrintTicket.propTypes = {
  visit: PropTypes.shape({
    queueNumber: PropTypes.number,
    roomLabel: PropTypes.string,
    patientName: PropTypes.string,
    department: PropTypes.string,
    sessionLabel: PropTypes.string,
    doctor: PropTypes.string,
  }).isRequired,
};

export default PrintTicket;
