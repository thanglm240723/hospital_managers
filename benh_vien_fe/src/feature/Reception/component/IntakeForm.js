import React from 'react';
import PropTypes from 'prop-types';
import { newIdempotencyKey } from 'service/idempotency';
import PrintTicket from './PrintTicket';

const PRIORITY_OPTIONS = [
  { value: 'none', label: 'Không' },
  { value: 'age80', label: 'Từ 80 tuổi' },
  { value: 'under6', label: 'Dưới 6 tuổi' },
  { value: 'pregnant', label: 'Mang thai' },
  { value: 'severe_disability', label: 'Khuyết tật nặng' },
];

class IntakeForm extends React.Component {
  constructor(props) {
    super(props);
    this.state = {
      departmentId: '',
      sessionId: '',
      reason: '',
      paymentMethod: 'bhyt',
      priorityGroup: 'none',
      priorityChecked: false,
      openVisitReason: '',
      submitting: false,
      visit: null,
      error: null,
      transferring: false,
      transferReason: '',
      transferDepartment: '',
    };
    this.idempotencyKey = newIdempotencyKey();
  }

  handleSubmit = async (event) => {
    event.preventDefault();
    const { hasOpenVisit, patient, onRegister } = this.props;
    const {
      openVisitReason, priorityGroup, priorityChecked, reason, paymentMethod,
    } = this.state;
    const dept = this.currentDepartment();
    const session = this.currentSession();
    if (!dept || !session) return;
    if (hasOpenVisit && !openVisitReason.trim()) return;
    if (priorityGroup !== 'none' && !priorityChecked) return;

    this.setState({ submitting: true, error: null });
    try {
      const visit = await onRegister({
        department: dept.department,
        sessionLabel: session.label,
        doctor: session.doctor,
        patientName: patient.fullName,
        reason,
        paymentMethod,
        priorityGroup,
        openVisitReason: openVisitReason || null,
      }, this.idempotencyKey);
      this.setState({ submitting: false, visit });
    } catch (error) {
      const title = (error.response && error.response.data && error.response.data.title) || 'Không cấp được số. Vui lòng thử lại.';
      this.setState({ submitting: false, error: title });
    }
  };

  handleTransfer = async () => {
    const { onTransfer } = this.props;
    const { transferReason, transferDepartment, visit } = this.state;
    if (!transferReason.trim() || !transferDepartment) return;
    const updated = await onTransfer(visit.id, { department: transferDepartment, reason: transferReason });
    this.setState(state => ({
      visit: { ...state.visit, department: updated.department },
      transferring: false,
      transferReason: '',
      transferDepartment: '',
    }));
  };

  currentDepartment() {
    const { departmentSessions } = this.props;
    const { departmentId } = this.state;
    return departmentSessions.find(d => d.id === departmentId);
  }

  currentSession() {
    const { sessionId } = this.state;
    const dept = this.currentDepartment();
    return dept && dept.sessions.find(s => s.id === sessionId);
  }

  render() {
    const {
      patient, hasOpenVisit, departmentSessions,
    } = this.props;
    const {
      departmentId, sessionId, reason, paymentMethod, priorityGroup, priorityChecked, openVisitReason,
      submitting, visit, error, transferring, transferReason, transferDepartment,
    } = this.state;
    const dept = this.currentDepartment();
    const session = this.currentSession();

    if (visit) {
      return (
        <div className="c-page">
          <div className="c-intake-result">
            <h2>
Đã cấp số
              {visit.queueNumber}
            </h2>
            <p>
              {visit.department}
              {' '}
—
              {' '}
              {visit.sessionLabel}
              {' '}
—
              {' '}
              {visit.doctor}
            </p>
            <div className="c-confirm-dialog__actions" style={{ justifyContent: 'flex-start' }}>
              <button type="button" className="c-toolbar__action" style={{ marginLeft: 0 }} onClick={() => window.print()}>In lại</button>
              <button type="button" className="c-login__secondary" onClick={() => this.setState({ transferring: true })}>Điều chuyển</button>
            </div>
            <PrintTicket visit={visit} />
          </div>
          {transferring && (
            <div className="c-confirm-dialog" role="dialog" aria-modal="true" aria-label="Điều chuyển khoa khám">
              <div
                className="c-confirm-dialog__backdrop"
                role="presentation"
                onClick={() => this.setState({ transferring: false })}
              />
              <div className="c-confirm-dialog__box">
                <h2 className="c-confirm-dialog__title">Điều chuyển khoa khám</h2>
                <div className="c-login__field">
                  {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
                  <label className="c-login__label" htmlFor="transfer-dept">Khoa mới</label>
                  <select id="transfer-dept" className="c-toolbar__select" value={transferDepartment} onChange={e => this.setState({ transferDepartment: e.target.value })}>
                    <option value="">— Chọn khoa —</option>
                    {departmentSessions.map(d => <option key={d.id} value={d.department}>{d.department}</option>)}
                  </select>
                </div>
                <div className="c-login__field">
                  {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
                  <label className="c-login__label" htmlFor="transfer-reason">Lý do điều chuyển (bắt buộc)</label>
                  <input id="transfer-reason" className="c-login__input" value={transferReason} onChange={e => this.setState({ transferReason: e.target.value })} />
                </div>
                <div className="c-confirm-dialog__actions">
                  <button type="button" className="c-login__secondary" onClick={() => this.setState({ transferring: false })}>Hủy</button>
                  <button
                    type="button"
                    className="c-login__submit"
                    disabled={!transferReason.trim() || !transferDepartment}
                    onClick={this.handleTransfer}
                  >
                    Điều chuyển
                  </button>
                </div>
              </div>
            </div>
          )}
        </div>
      );
    }

    return (
      <form className="c-page" onSubmit={this.handleSubmit}>
        <h1 className="c-page__title">Tiếp nhận &amp; cấp số</h1>
        <div className="c-detail-panel">
          <h2 className="c-detail-panel__title">{patient.fullName}</h2>
          <p className="c-detail-panel__subtitle">
            {patient.phone}
            {' '}
—
            {' '}
            {patient.address}
          </p>
        </div>

        {hasOpenVisit && (
          <div className="c-confirm-dialog__warning" style={{ marginTop: 12 }}>
            Bệnh nhân đang có lượt khám chưa kết thúc. Nhập lý do để tạo tiếp lượt mới.
            <div className="c-login__field">
              {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
              <label className="c-login__label" htmlFor="open-visit-reason">Lý do tạo tiếp</label>
              <input
                id="open-visit-reason"
                className="c-login__input"
                value={openVisitReason}
                required
                onChange={e => this.setState({ openVisitReason: e.target.value })}
              />
            </div>
          </div>
        )}

        <fieldset className="c-login__field">
          <legend className="c-login__label">Khoa</legend>
          <select
            className="c-toolbar__select"
            value={departmentId}
            required
            onChange={e => this.setState({ departmentId: e.target.value, sessionId: '' })}
          >
            <option value="">— Chọn khoa —</option>
            {departmentSessions.map(d => <option key={d.id} value={d.id}>{d.department}</option>)}
          </select>
        </fieldset>

        {dept && (
          <fieldset className="c-login__field">
            <legend className="c-login__label">Buổi khám</legend>
            {dept.sessions.map(s => (
              <label key={s.id} className="c-checkbox" htmlFor={`session-${s.id}`}>
                <input id={`session-${s.id}`} type="radio" checked={sessionId === s.id} onChange={() => this.setState({ sessionId: s.id })} />
                {s.label}
                {' '}
—
                {s.doctor}
                {' '}
(
                {s.time}
) — đang chờ
                {s.waitingCount}
              </label>
            ))}
          </fieldset>
        )}

        <div className="c-login__field">
          {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
          <label className="c-login__label" htmlFor="intake-reason">Lý do khám (bắt buộc)</label>
          <input id="intake-reason" className="c-login__input" required value={reason} onChange={e => this.setState({ reason: e.target.value })} />
        </div>

        <fieldset className="c-login__field">
          <legend className="c-login__label">Hình thức thanh toán</legend>
          <label className="c-checkbox" htmlFor="intake-pay-bhyt">
            <input id="intake-pay-bhyt" type="radio" checked={paymentMethod === 'bhyt'} onChange={() => this.setState({ paymentMethod: 'bhyt' })} />
            {' BHYT'}
          </label>
          <label className="c-checkbox" htmlFor="intake-pay-service">
            <input id="intake-pay-service" type="radio" checked={paymentMethod === 'service'} onChange={() => this.setState({ paymentMethod: 'service' })} />
            {' Dịch vụ'}
          </label>
          <p className="c-detail-panel__subtitle">Thu phí khám do hệ thống/bộ phận riêng — ngoài phạm vi màn này.</p>
        </fieldset>

        <fieldset className="c-login__field">
          <legend className="c-login__label">Nhóm ưu tiên</legend>
          <select className="c-toolbar__select" value={priorityGroup} onChange={e => this.setState({ priorityGroup: e.target.value, priorityChecked: false })}>
            {PRIORITY_OPTIONS.map(opt => <option key={opt.value} value={opt.value}>{opt.label}</option>)}
          </select>
          {priorityGroup !== 'none' && (
            <label className="c-checkbox" htmlFor="intake-priority-checked">
              <input id="intake-priority-checked" type="checkbox" checked={priorityChecked} onChange={e => this.setState({ priorityChecked: e.target.checked })} />
              Đã kiểm tra căn cứ ưu tiên (giấy tờ/quan sát)
            </label>
          )}
        </fieldset>

        {error && <p className="c-confirm-dialog__warning">{error}</p>}

        <button type="submit" className="c-toolbar__action" style={{ marginLeft: 0 }} disabled={submitting || !session}>
          {submitting ? 'Đang cấp số…' : 'Cấp số'}
        </button>
      </form>
    );
  }
}

IntakeForm.propTypes = {
  patient: PropTypes.shape({ fullName: PropTypes.string, phone: PropTypes.string, address: PropTypes.string }).isRequired,
  hasOpenVisit: PropTypes.bool.isRequired,
  departmentSessions: PropTypes.arrayOf(PropTypes.object).isRequired,
  onRegister: PropTypes.func.isRequired,
  onTransfer: PropTypes.func.isRequired,
};

export default IntakeForm;
