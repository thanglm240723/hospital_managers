import React from 'react';
import PropTypes from 'prop-types';
import { AppLayout } from 'feature/Shell';
import * as clinicApi from './api';
import {
  sortQueue, nextToCall, startCall, recall, tickCountdown, handleTimeout, canRecall, canMarkLate, returnFromLate,
} from './queueLogic';
import CallingCard from './component/CallingCard';

const GROUP_LABEL = { back: 'Quay lại', priority: 'Ưu tiên', normal: 'Thường' };

// Giờ hiển thị theo múi giờ Việt Nam.
const formatTime = ts => new Date(ts).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit', timeZone: 'Asia/Ho_Chi_Minh' });

class QueueContainer extends React.Component {
  state = { queue: [], late: [], calling: null };

  componentDidMount() {
    clinicApi.listQueue().then(({ queue, late }) => this.setState({ queue, late })); // eslint-disable-line no-shadow
    this.timer = setInterval(this.tick, 1000);
  }

  componentWillUnmount() {
    clearInterval(this.timer);
  }

  tick = () => {
    this.setState((state) => {
      if (!state.calling || state.calling.status !== 'calling') return null;
      const ticked = handleTimeout(tickCountdown(state.calling));
      if (ticked.status === 'late') {
        return {
          calling: null,
          late: [...state.late, ticked],
          queue: state.queue.filter(i => i.id !== ticked.id),
        };
      }
      return { calling: ticked };
    });
  };

  handleCallNext = () => {
    const { queue } = this.state;
    const candidate = nextToCall(queue);
    if (!candidate) return;
    this.setState(state => ({
      calling: startCall(candidate),
      queue: state.queue.map(i => (i.id === candidate.id ? { ...i, status: 'calling' } : i)),
    }));
  };

  handleRecall = () => {
    this.setState(state => ({ calling: state.calling ? recall(state.calling) : null }));
  };

  handleMarkLate = () => {
    this.setState(state => ({
      calling: null,
      late: state.calling ? [...state.late, { ...state.calling, status: 'late' }] : state.late,
      queue: state.queue.filter(i => !state.calling || i.id !== state.calling.id),
    }));
  };

  handleConfirmMatch = () => {
    const { calling } = this.state;
    const { history } = this.props;
    if (!calling) return;
    history.push(`/clinic/encounters/${calling.id}`);
  };

  handleReturnFromLate = (id) => {
    this.setState(state => ({
      late: state.late.filter(i => i.id !== id),
      queue: [...state.queue, returnFromLate(state.late.find(i => i.id === id), Date.now())],
    }));
  };

  render() {
    const { queue, late, calling } = this.state;
    const ordered = sortQueue(queue.filter(i => i.status === 'waiting'));
    const groups = ['back', 'priority', 'normal'];

    return (
      <AppLayout>
        <div className="c-page">
          <h1 className="c-page__title">Hàng chờ khám</h1>

          {calling && (
            <CallingCard
              item={calling}
              canRecall={canRecall(calling)}
              canMarkLate={canMarkLate(calling)}
              onConfirmMatch={this.handleConfirmMatch}
              onRecall={this.handleRecall}
              onMarkLate={this.handleMarkLate}
            />
          )}

          {!calling && (
            <button type="button" className="c-toolbar__action" style={{ marginLeft: 0 }} onClick={this.handleCallNext} disabled={ordered.length === 0}>
              Gọi lượt tiếp theo
            </button>
          )}

          {groups.map(group => (
            <section key={group} className="c-detail-panel" style={{ marginTop: 16 }}>
              <h3>{GROUP_LABEL[group]}</h3>
              <table className="c-table">
                <thead>
                  <tr>
                    <th>Số</th>
                    <th>Họ tên</th>
                    <th>Vào hàng lúc</th>
                  </tr>
                </thead>
                <tbody>
                  {ordered.filter(i => i.group === group).map(i => (
                    <tr key={i.id}>
                      <td>{i.queueNumber}</td>
                      <td>{i.fullName}</td>
                      <td>{formatTime(i.enqueuedAt)}</td>
                    </tr>
                  ))}
                  {ordered.filter(i => i.group === group).length === 0 && (
                    <tr><td colSpan={3} className="c-table__empty">Trống</td></tr>
                  )}
                </tbody>
              </table>
            </section>
          ))}

          <section className="c-detail-panel" style={{ marginTop: 16 }}>
            <h3>Hàng trễ</h3>
            <table className="c-table">
              <thead>
                <tr>
                  <th>Số</th>
                  <th>Họ tên</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {late.map(i => (
                  <tr key={i.id}>
                    <td>{i.queueNumber}</td>
                    <td>{i.fullName}</td>
                    <td>
                      <button type="button" className="c-login__secondary" onClick={() => this.handleReturnFromLate(i.id)}>
                        Đã quay lại
                      </button>
                    </td>
                  </tr>
                ))}
                {late.length === 0 && <tr><td colSpan={3} className="c-table__empty">Không có</td></tr>}
              </tbody>
            </table>
          </section>
        </div>
      </AppLayout>
    );
  }
}

QueueContainer.propTypes = {
  history: PropTypes.shape({ push: PropTypes.func }).isRequired,
};

export default QueueContainer;
