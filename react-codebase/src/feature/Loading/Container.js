import React from 'react';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { ClipLoader } from 'react-spinners';

export const LoadingModal = ({ isLoading }) => {
  if (!isLoading) return null;
  return (
    <div
      data-testid="loading-modal"
      style={{
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        zIndex: 9999,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'rgba(255, 255, 255, 0.5)',
      }}
    >
      <ClipLoader loading />
    </div>
  );
};

LoadingModal.propTypes = { isLoading: PropTypes.bool.isRequired };

const mapStateToProps = state => ({ isLoading: state.loadingModal.count > 0 });

export default connect(mapStateToProps)(LoadingModal);
