import React from 'react';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { AppLayout } from 'feature/Shell';
import {
  loadEncounter, saveEncounterForm, addOrder, addPrescription, requestVitals, endEarly, waitForLabResult,
  admitPatient, confirmEncounter, clearEncounter, loadOrderCatalog, loadMedicineCatalog,
} from './redux/action';

const CLINICAL_STATUS_LABEL = {
  examining: 'Đang khám',
  waiting_result: 'Chờ kết quả CLS',
  admission_ordered: 'Đã chỉ định nhập viện',
  ended_early: 'Kết thúc sớm',
  completed: 'Đã kết thúc',
};

const RECORD_STATUS_LABEL = { draft: 'Bản nháp', confirmed: 'Đã xác nhận' };

const ORDER_LABEL = {
  performStatus: { pending: 'Chưa thực hiện', done: 'Đã thực hiện' },
  paymentStatus: { unpaid: 'Chưa thanh toán', paid: 'Đã thanh toán' },
  resultStatus: { pending: 'Chưa có kết quả', done: 'Có kết quả' },
};

const FORM_FIELDS = [
  ['reason', 'Lý do khám'], ['history', 'Bệnh sử'], ['pastHistory', 'Tiền sử'], ['examination', 'Khám'],
];

class EncounterContainer extends React.Component {
  state = {
    tab: 'form',
    orderCatalog: [],
    medicineCatalog: [],
    newOrderCode: '',
    newMed: {
      code: '', unit: '', dose: '', route: '', frequency: '', days: '', quantity: '', instruction: '',
    },
    endingEarly: false,
    endEarlyReason: '',
    confirming: false,
  };

  componentDidMount() {
    const {
      loadEncounter, match, loadOrderCatalog, loadMedicineCatalog,
    } = this.props;
    loadEncounter(match.params.id);
    loadOrderCatalog().then(orderCatalog => this.setState({ orderCatalog }));
    loadMedicineCatalog().then(medicineCatalog => this.setState({ medicineCatalog }));
  }

  componentWillUnmount() {
    // Dữ liệu bệnh án xóa khỏi state khi rời màn.
    const { clearEncounter } = this.props;
    clearEncounter();
  }

  handleFieldChange = (field, value) => {
    const { encounter, saveEncounterForm: onSave } = this.props;
    onSave(encounter.id, { [field]: value });
  };

  handleAddOrder = () => {
    const { addOrder, encounter } = this.props;
    const { newOrderCode } = this.state;
    if (!newOrderCode) return;
    addOrder(encounter.id, newOrderCode);
    this.setState({ newOrderCode: '' });
  };

  handleAddMedicine = () => {
    const { addPrescription, encounter } = this.props;
    const { newMed, medicineCatalog } = this.state;
    if (!newMed.code) return;
    const catalogItem = medicineCatalog.find(m => m.code === newMed.code);
    addPrescription(encounter.id, { ...newMed, name: catalogItem.name, unit: catalogItem.unit });
    this.setState({
      newMed: {
        code: '', unit: '', dose: '', route: '', frequency: '', days: '', quantity: '', instruction: '',
      },
    });
  };

  handleEndEarly = () => {
    const { endEarly, encounter } = this.props;
    const { endEarlyReason } = this.state;
    if (!endEarlyReason.trim()) return;
    endEarly(encounter.id, endEarlyReason.trim());
    this.setState({ endingEarly: false, endEarlyReason: '' });
  };

  handleConfirm = () => {
    const { confirmEncounter, encounter } = this.props;
    confirmEncounter(encounter.id);
    this.setState({ confirming: false });
  };

  renderForm() {
    const { encounter, readOnly } = this.props;
    return (
      <div>
        {FORM_FIELDS.map(([field, label]) => (
          <div className="c-login__field" key={field}>
            {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
            <label className="c-login__label" htmlFor={`ef-${field}`}>{label}</label>
            <textarea
              id={`ef-${field}`}
              className="c-login__input"
              rows={2}
              disabled={readOnly}
              value={encounter[field] || ''}
              onChange={e => this.handleFieldChange(field, e.target.value)}
            />
          </div>
        ))}
        <div className="c-login__field">
          {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
          <label className="c-login__label" htmlFor="ef-diagnosis">Chẩn đoán (ICD-10: CẦN_XÁC_NHẬN)</label>
          <textarea
            id="ef-diagnosis"
            className="c-login__input"
            rows={2}
            disabled={readOnly}
            value={encounter.diagnosis || ''}
            onChange={e => this.handleFieldChange('diagnosis', e.target.value)}
          />
        </div>
        <div className="c-login__field">
          {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
          <label className="c-login__label" htmlFor="ef-conclusion">Kết luận</label>
          <textarea
            id="ef-conclusion"
            className="c-login__input"
            rows={2}
            disabled={readOnly}
            value={encounter.conclusion || ''}
            onChange={e => this.handleFieldChange('conclusion', e.target.value)}
          />
        </div>
        <div className="c-login__field">
          {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
          <label className="c-login__label" htmlFor="ef-plan">Hướng xử trí</label>
          <textarea
            id="ef-plan"
            className="c-login__input"
            rows={2}
            disabled={readOnly}
            value={encounter.plan || ''}
            onChange={e => this.handleFieldChange('plan', e.target.value)}
          />
        </div>
      </div>
    );
  }

  renderOrders() {
    const { encounter, readOnly } = this.props;
    const { orderCatalog, newOrderCode } = this.state;
    return (
      <div>
        <table className="c-table">
          <thead>
            <tr>
              <th>Chỉ định</th>
              <th>Thực hiện</th>
              <th>Thanh toán</th>
              <th>Kết quả</th>
            </tr>
          </thead>
          <tbody>
            {encounter.orders.map(o => (
              <tr key={o.id}>
                <td>{o.name}</td>
                <td><span className="c-badge c-badge--default">{ORDER_LABEL.performStatus[o.performStatus]}</span></td>
                <td><span className="c-badge c-badge--default">{ORDER_LABEL.paymentStatus[o.paymentStatus]}</span></td>
                <td><span className="c-badge c-badge--default">{ORDER_LABEL.resultStatus[o.resultStatus]}</span></td>
              </tr>
            ))}
            {encounter.orders.length === 0 && <tr><td colSpan={4} className="c-table__empty">Chưa có chỉ định.</td></tr>}
          </tbody>
        </table>
        {!readOnly && (
          <div className="c-toolbar" style={{ marginTop: 12 }}>
            <select className="c-toolbar__select" value={newOrderCode} onChange={e => this.setState({ newOrderCode: e.target.value })}>
              <option value="">— Chọn chỉ định —</option>
              {orderCatalog.map(o => <option key={o.code} value={o.code}>{o.name}</option>)}
            </select>
            <button type="button" className="c-toolbar__action" onClick={this.handleAddOrder}>+ Thêm chỉ định</button>
          </div>
        )}
      </div>
    );
  }

  renderPrescriptions() {
    const { encounter, readOnly } = this.props;
    const { medicineCatalog, newMed } = this.state;
    return (
      <div>
        <table className="c-table">
          <thead>
            <tr>
              <th>Thuốc</th>
              <th>ĐV</th>
              <th>Liều</th>
              <th>Đường dùng</th>
              <th>Tần suất</th>
              <th>Số ngày</th>
              <th>SL</th>
              <th>Hướng dẫn</th>
            </tr>
          </thead>
          <tbody>
            {encounter.prescriptions.map(p => (
              <tr key={p.id}>
                <td>{p.name}</td>
                <td>{p.unit}</td>
                <td>{p.dose}</td>
                <td>{p.route}</td>
                <td>{p.frequency}</td>
                <td>{p.days}</td>
                <td>{p.quantity}</td>
                <td>{p.instruction}</td>
              </tr>
            ))}
            {encounter.prescriptions.length === 0 && <tr><td colSpan={8} className="c-table__empty">Chưa có thuốc.</td></tr>}
          </tbody>
        </table>
        {!readOnly && (
          <div className="c-toolbar" style={{ marginTop: 12, flexWrap: 'wrap' }}>
            <select className="c-toolbar__select" value={newMed.code} onChange={e => this.setState({ newMed: { ...newMed, code: e.target.value } })}>
              <option value="">— Chọn thuốc —</option>
              {medicineCatalog.map(m => <option key={m.code} value={m.code}>{m.name}</option>)}
            </select>
            <input className="c-toolbar__search" placeholder="Liều" value={newMed.dose} onChange={e => this.setState({ newMed: { ...newMed, dose: e.target.value } })} />
            <input className="c-toolbar__search" placeholder="Đường dùng" value={newMed.route} onChange={e => this.setState({ newMed: { ...newMed, route: e.target.value } })} />
            <input className="c-toolbar__search" placeholder="Tần suất" value={newMed.frequency} onChange={e => this.setState({ newMed: { ...newMed, frequency: e.target.value } })} />
            <input className="c-toolbar__search" placeholder="Số ngày" value={newMed.days} onChange={e => this.setState({ newMed: { ...newMed, days: e.target.value } })} />
            <input className="c-toolbar__search" placeholder="Số lượng" value={newMed.quantity} onChange={e => this.setState({ newMed: { ...newMed, quantity: e.target.value } })} />
            <input className="c-toolbar__search" placeholder="Hướng dẫn" value={newMed.instruction} onChange={e => this.setState({ newMed: { ...newMed, instruction: e.target.value } })} />
            <button type="button" className="c-toolbar__action" onClick={this.handleAddMedicine}>+ Thêm thuốc</button>
          </div>
        )}
      </div>
    );
  }

  render() {
    const { encounter, readOnly, requestVitals: onRequestVitals, waitForLabResult: onWaitForLabResult, admitPatient: onAdmitPatient } = this.props;
    if (!encounter) return <AppLayout><p>Đang tải…</p></AppLayout>;
    const { tab, endingEarly, endEarlyReason, confirming } = this.state;

    return (
      <AppLayout>
        <div className="c-page">
          <div className="c-detail-panel">
            <h1 className="c-page__title">{encounter.patient.fullName}</h1>
            <p className="c-detail-panel__subtitle">
Ngày sinh:
              {encounter.patient.birthDate}
              {' '}
— Dị ứng:
              {encounter.patient.allergies}
            </p>
            <span className="c-badge c-badge--default">{CLINICAL_STATUS_LABEL[encounter.clinicalStatus]}</span>
            {' '}
            <span className="c-badge c-badge--default">{RECORD_STATUS_LABEL[encounter.recordStatus]}</span>
          </div>

          <div className="c-detail-panel" style={{ marginTop: 12 }}>
            <h3>Sinh hiệu</h3>
            {encounter.vitals ? (
              <p>
Mạch
                {encounter.vitals.pulse}
                {' '}
— HA
                {encounter.vitals.systolic}
/
                {encounter.vitals.diastolic}
                {' '}
— Nhiệt độ
                {encounter.vitals.temperature}
              </p>
            ) : (
              <button type="button" className="c-login__secondary" disabled={readOnly} onClick={() => onRequestVitals(encounter.id)}>
                Yêu cầu đo sinh hiệu
              </button>
            )}
          </div>

          <div className="c-toolbar" style={{ marginTop: 16 }}>
            {['form', 'orders', 'prescriptions'].map(t => (
              <button
                key={t}
                type="button"
                className={t === tab ? 'c-toolbar__action' : 'c-login__secondary'}
                style={{ marginLeft: 0 }}
                onClick={() => this.setState({ tab: t })}
              >
                {t === 'form' ? 'Phiếu khám' : t === 'orders' ? 'Chỉ định CLS' : 'Đơn thuốc'}
              </button>
            ))}
          </div>

          <div className="c-detail-panel">
            {tab === 'form' && this.renderForm()}
            {tab === 'orders' && this.renderOrders()}
            {tab === 'prescriptions' && this.renderPrescriptions()}
          </div>

          {!readOnly && (
            <div className="c-toolbar" style={{ marginTop: 16 }}>
              <button type="button" className="c-login__secondary" onClick={() => this.setState({ endingEarly: true })}>Kết thúc sớm</button>
              <button type="button" className="c-login__secondary" onClick={() => onWaitForLabResult(encounter.id)}>Chờ kết quả CLS</button>
              <button type="button" className="c-login__secondary" onClick={() => onAdmitPatient(encounter.id)}>Chỉ định nhập viện</button>
              <button type="button" className="c-toolbar__action" onClick={() => this.setState({ confirming: true })}>
                Kết luận &amp; xác nhận bệnh án
              </button>
            </div>
          )}

          {endingEarly && (
            <div className="c-confirm-dialog" role="dialog" aria-modal="true" aria-label="Kết thúc sớm">
              <div className="c-confirm-dialog__backdrop" role="presentation" onClick={() => this.setState({ endingEarly: false })} />
              <div className="c-confirm-dialog__box">
                <h2 className="c-confirm-dialog__title">Kết thúc sớm</h2>
                <div className="c-login__field">
                  {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
                  <label className="c-login__label" htmlFor="end-early-reason">Lý do (bắt buộc)</label>
                  <input id="end-early-reason" className="c-login__input" value={endEarlyReason} onChange={e => this.setState({ endEarlyReason: e.target.value })} />
                </div>
                <div className="c-confirm-dialog__actions">
                  <button type="button" className="c-login__secondary" onClick={() => this.setState({ endingEarly: false })}>Hủy</button>
                  <button type="button" className="c-login__submit" disabled={!endEarlyReason.trim()} onClick={this.handleEndEarly}>Xác nhận</button>
                </div>
              </div>
            </div>
          )}

          {confirming && (
            <div className="c-confirm-dialog" role="dialog" aria-modal="true" aria-label="Xác nhận bệnh án">
              <div className="c-confirm-dialog__backdrop" role="presentation" onClick={() => this.setState({ confirming: false })} />
              <div className="c-confirm-dialog__box">
                <h2 className="c-confirm-dialog__title">Xác nhận bệnh án?</h2>
                <p className="c-confirm-dialog__warning">Sau khi xác nhận, bệnh án chuyển thành chỉ đọc và không thể sửa trực tiếp.</p>
                <div className="c-confirm-dialog__actions">
                  <button type="button" className="c-login__secondary" onClick={() => this.setState({ confirming: false })}>Hủy</button>
                  <button type="button" className="c-login__submit" onClick={this.handleConfirm}>Xác nhận</button>
                </div>
              </div>
            </div>
          )}
        </div>
      </AppLayout>
    );
  }
}

EncounterContainer.propTypes = {
  match: PropTypes.shape({ params: PropTypes.shape({ id: PropTypes.string }) }).isRequired,
  encounter: PropTypes.shape({
    id: PropTypes.string,
    patient: PropTypes.object, // eslint-disable-line react/forbid-prop-types
    clinicalStatus: PropTypes.string,
    recordStatus: PropTypes.string,
    orders: PropTypes.array, // eslint-disable-line react/forbid-prop-types
    prescriptions: PropTypes.array, // eslint-disable-line react/forbid-prop-types
    vitals: PropTypes.object, // eslint-disable-line react/forbid-prop-types
    diagnosis: PropTypes.string,
    conclusion: PropTypes.string,
    plan: PropTypes.string,
  }),
  readOnly: PropTypes.bool.isRequired,
  loadEncounter: PropTypes.func.isRequired,
  saveEncounterForm: PropTypes.func.isRequired,
  addOrder: PropTypes.func.isRequired,
  addPrescription: PropTypes.func.isRequired,
  requestVitals: PropTypes.func.isRequired,
  endEarly: PropTypes.func.isRequired,
  waitForLabResult: PropTypes.func.isRequired,
  admitPatient: PropTypes.func.isRequired,
  confirmEncounter: PropTypes.func.isRequired,
  clearEncounter: PropTypes.func.isRequired,
  loadOrderCatalog: PropTypes.func.isRequired,
  loadMedicineCatalog: PropTypes.func.isRequired,
};

EncounterContainer.defaultProps = { encounter: null };

const mapStateToProps = state => ({
  encounter: state.clinic.current,
  readOnly: Boolean(state.clinic.current && state.clinic.current.recordStatus === 'confirmed'),
});

export default connect(mapStateToProps, {
  loadEncounter,
  saveEncounterForm,
  addOrder,
  addPrescription,
  requestVitals,
  endEarly,
  waitForLabResult,
  admitPatient,
  confirmEncounter,
  clearEncounter,
  loadOrderCatalog,
  loadMedicineCatalog,
})(EncounterContainer);
