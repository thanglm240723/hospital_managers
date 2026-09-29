import React from 'react';
import { nextSnapshot } from './api/displayMock';
import DisplayBoard from './component/DisplayBoard';

// Dữ liệu giả — CẦN_XÁC_NHẬN nguồn thật (kênh realtime hàng chờ) khi BE có API.
class DisplayContainer extends React.Component {
  state = { rows: nextSnapshot() };

  componentDidMount() {
    this.timer = setInterval(() => this.setState({ rows: nextSnapshot() }), 4000);
  }

  componentWillUnmount() {
    clearInterval(this.timer);
  }

  render() {
    const { rows } = this.state;
    return <DisplayBoard rows={rows} />;
  }
}

export default DisplayContainer;
