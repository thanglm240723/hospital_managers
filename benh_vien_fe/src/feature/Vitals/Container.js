import React from 'react';
import { connect } from 'react-redux';
import { logout } from 'feature/Auth/redux/actions';
import { problemTitle } from 'feature/Auth/problem';
import {
  loadVitalsQueue, callNextVitals, completeVitals, clearVitals,
} from './redux/action';

const FIELDS = [
  ['pulse', 'Mạch (lần/phút)'],
  ['systolic', 'HA tâm thu (mmHg)'],
  ['diastolic', 'HA tâm trương (mmHg)'],
  ['temperature', 'Nhiệt độ (°C)'],
  ['respiratoryRate', 'Nhịp thở (lần/phút)'],
  ['spo2', 'SpO₂ (%)'],
  ['weight', 'Cân nặng (kg)'],
  ['height', 'Chiều cao (cm)'],
];

const emptyForm = () => ({
  pulse: '',
  systolic: '',
  diastolic: '',
  temperature: '',
  respiratoryRate: '',
  spo2: '',
  weight: '',
  height: '',
  notMeasurable: false,
  notMeasurableReason: '',
});

// Tablet dùng chung — không giữ dữ liệu sinh hiệu trong storage trình duyệt, chỉ giữ tạm trong state màn hình.
/* eslint-disable react/prop-types */
class VitalsContainer extends React.Component {
  state = { form: emptyForm(), error: null };

  componentDidMount() {
    const { loadVitalsQueue: load } = this.props;
    load().catch(() => {});
  }

  componentWillUnmount() {
    // Không giữ số đo/người bệnh trong store khi rời màn.
    const { clearVitals: clear } = this.props;
    clear();
  }

  handleCallNext = () => {
    const { callNextVitals: callNext } = this.props;
    callNext();
    this.setState({ form: emptyForm(), error: null });
  };

  handleField = (field, value) => this.setState(state => ({ form: { ...state.form, [field]: value } }));

  handleComplete = async (event) => {
    event.preventDefault();
    const { current, completeVitals: complete } = this.props;
    const { form } = this.state;
    if (!current) return;
    try {
      await complete(current.id, form);
      this.setState({ form: emptyForm(), error: null });
    } catch (error) {
      this.setState({ error: problemTitle(error) });
    }
  };

  render() {
    const { queue, current, logout: onLogout } = this.props;
    const { form, error } = this.state;
    return (
      <div className="c-vitals">
        <header className="c-vitals__topbar">
          <span className="c-vitals__brand">Sinh hiệu</span>
          <button type="button" className="c-login__secondary" onClick={() => onLogout()}>Đăng xuất</button>
        </header>

        <div className="c-vitals__body">
          <aside className="c-vitals__queue">
            <button type="button" className="c-toolbar__action" style={{ marginLeft: 0, width: '100%' }} onClick={this.handleCallNext} disabled={queue.length === 0}>
              Gọi lượt tiếp theo
            </button>
            <ul className="c-vitals__queue-list">
              {queue.map(item => (
                <li key={item.id}>
                  {item.queueNumber}
                  {' '}
—
                  {' '}
                  {item.fullName}
                </li>
              ))}
              {queue.length === 0 && <li>Hàng chờ trống</li>}
            </ul>
          </aside>

          <main className="c-vitals__form">
            {!current && <p>Chọn &quot;Gọi lượt tiếp theo&quot; để bắt đầu đo.</p>}
            {current && (
              <form onSubmit={this.handleComplete}>
                <h2>
Số
                  {current.queueNumber}
                  {' '}
—
                  {current.fullName}
                </h2>
                <p className="c-detail-panel__subtitle">
Ngày sinh:
                  {current.birthDate}
                </p>

                <div className="c-vitals__grid">
                  {FIELDS.map(([field, label]) => (
                    <div className="c-login__field" key={field}>
                      {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
                      <label className="c-login__label" htmlFor={`vt-${field}`}>{label}</label>
                      <input
                        id={`vt-${field}`}
                        className="c-vitals__input"
                        inputMode="decimal"
                        disabled={form.notMeasurable}
                        value={form[field]}
                        onChange={e => this.handleField(field, e.target.value)}
                      />
                    </div>
                  ))}
                </div>

                <label className="c-checkbox" htmlFor="vt-not-measurable">
                  <input id="vt-not-measurable" type="checkbox" checked={form.notMeasurable} onChange={e => this.handleField('notMeasurable', e.target.checked)} />
                  Có chỉ số không đo được
                </label>
                {form.notMeasurable && (
                  <div className="c-login__field">
                    {/* eslint-disable-next-line jsx-a11y/label-has-associated-control */}
                    <label className="c-login__label" htmlFor="vt-reason">Lý do (CẦN_XÁC_NHẬN quy tắc bắt buộc)</label>
                    <input
                      id="vt-reason"
                      className="c-login__input"
                      value={form.notMeasurableReason}
                      onChange={e => this.handleField('notMeasurableReason', e.target.value)}
                    />
                  </div>
                )}

                <p className="c-detail-panel__subtitle">Ngưỡng cảnh báo theo chỉ số: CẦN_XÁC_NHẬN — không tự tô màu cảnh báo khi chưa có quy tắc chính thức.</p>

                {error && <p className="c-confirm-dialog__warning">{error}</p>}
                <button type="submit" className="c-toolbar__action" style={{ marginLeft: 0 }}>Hoàn thành → trả về hàng khám</button>
              </form>
            )}
          </main>
        </div>
      </div>
    );
  }
}

export default connect(state => ({ queue: state.vitals.queue, current: state.vitals.current }), {
  logout, loadVitalsQueue, callNextVitals, completeVitals, clearVitals,
})(VitalsContainer);
