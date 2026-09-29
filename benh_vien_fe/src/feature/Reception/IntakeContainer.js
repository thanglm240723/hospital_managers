import React from 'react';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { AppLayout } from 'feature/Shell';
import {
  getPatient, getOpenVisits, listDepartmentSessions, registerVisit, transferVisit, clearVisit,
} from './redux/action';
import IntakeForm from './component/IntakeForm';

class IntakeContainer extends React.Component {
  state = {
    patient: null, hasOpenVisit: false, departmentSessions: [], loading: true,
  };

  componentDidMount() {
    this.load();
  }

  componentWillUnmount() {
    // Bệnh án/lượt khám không giữ lại trong state khi rời màn.
    const { clearVisit: onClear } = this.props;
    onClear();
  }

  async load() {
    const {
      match, getPatient: fetchPatient, getOpenVisits: fetchOpenVisits, listDepartmentSessions: fetchSessions,
    } = this.props;
    const { patientId } = match.params;
    const [patient, openVisits, departmentSessions] = await Promise.all([
      fetchPatient(patientId),
      fetchOpenVisits(patientId),
      fetchSessions(),
    ]);
    this.setState({
      patient, hasOpenVisit: openVisits.length > 0, departmentSessions, loading: false,
    });
  }

  render() {
    const {
      patient, hasOpenVisit, departmentSessions, loading,
    } = this.state;
    const { registerVisit: onRegister, transferVisit: onTransfer } = this.props;

    return (
      <AppLayout>
        {loading && <p>Đang tải…</p>}
        {!loading && !patient && <p>Không tìm thấy hồ sơ bệnh nhân.</p>}
        {!loading && patient && (
          <IntakeForm
            patient={patient}
            hasOpenVisit={hasOpenVisit}
            departmentSessions={departmentSessions}
            onRegister={onRegister}
            onTransfer={onTransfer}
          />
        )}
      </AppLayout>
    );
  }
}

IntakeContainer.propTypes = {
  match: PropTypes.shape({ params: PropTypes.shape({ patientId: PropTypes.string }) }).isRequired,
  getPatient: PropTypes.func.isRequired,
  getOpenVisits: PropTypes.func.isRequired,
  listDepartmentSessions: PropTypes.func.isRequired,
  registerVisit: PropTypes.func.isRequired,
  transferVisit: PropTypes.func.isRequired,
  clearVisit: PropTypes.func.isRequired,
};

export default connect(null, {
  getPatient, getOpenVisits, listDepartmentSessions, registerVisit, transferVisit, clearVisit,
})(IntakeContainer);
