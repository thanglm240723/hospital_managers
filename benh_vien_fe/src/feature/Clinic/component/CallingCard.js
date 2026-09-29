import React from 'react';
import PropTypes from 'prop-types';
import { MAX_CALL_ATTEMPTS } from '../queueLogic';

const CallingCard = ({
  item, canRecall, canMarkLate, onConfirmMatch, onRecall, onMarkLate,
}) => (
  <div className="c-calling-card">
    <p className="c-calling-card__label">
      {`Đang gọi — lần ${item.callAttempt}/${MAX_CALL_ATTEMPTS}`}
    </p>
    <p className="c-calling-card__number">{`Số ${item.queueNumber}`}</p>
    <p className="c-calling-card__name">{item.fullName}</p>
    <p className="c-calling-card__birth">{`Ngày sinh: ${item.birthDate}`}</p>
    <p className="c-calling-card__countdown">{`${item.callSecondsLeft}s`}</p>
    <div className="c-confirm-dialog__actions" style={{ justifyContent: 'flex-start' }}>
      <button type="button" className="c-toolbar__action" style={{ marginLeft: 0 }} onClick={onConfirmMatch}>
        Đúng người — bắt đầu khám
      </button>
      <button type="button" className="c-login__secondary" disabled={!canRecall} onClick={onRecall}>Gọi lại</button>
      {canMarkLate && (
        <button type="button" className="c-login__submit c-login__submit--danger" onClick={onMarkLate}>
          Ghi vắng → Hàng trễ
        </button>
      )}
    </div>
  </div>
);

CallingCard.propTypes = {
  item: PropTypes.shape({
    queueNumber: PropTypes.number,
    fullName: PropTypes.string,
    birthDate: PropTypes.string,
    callAttempt: PropTypes.number,
    callSecondsLeft: PropTypes.number,
  }).isRequired,
  canRecall: PropTypes.bool.isRequired,
  canMarkLate: PropTypes.bool.isRequired,
  onConfirmMatch: PropTypes.func.isRequired,
  onRecall: PropTypes.func.isRequired,
  onMarkLate: PropTypes.func.isRequired,
};

export default CallingCard;
