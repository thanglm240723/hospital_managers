import React from 'react';
import PropTypes from 'prop-types';

const DisplayBoard = ({ rows }) => (
  <div className="c-display-board">
    <h1 className="c-display-board__title">Bảng gọi số</h1>
    <div className="c-display-board__grid">
      {rows.map(row => (
        <div className="c-display-board__cell" key={row.room}>
          <span className="c-display-board__number">{row.number}</span>
          <span className="c-display-board__room">{row.room}</span>
        </div>
      ))}
    </div>
  </div>
);

DisplayBoard.propTypes = {
  rows: PropTypes.arrayOf(PropTypes.shape({ room: PropTypes.string, number: PropTypes.number })).isRequired,
};

export default DisplayBoard;
