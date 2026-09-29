import {
  sortQueue, nextToCall, startCall, recall, tickCountdown, handleTimeout, canRecall, canMarkLate, returnFromLate,
  MAX_CALL_ATTEMPTS,
} from '../queueLogic';

const item = (id, group, enqueuedAt, backReason) => ({
  id, group, enqueuedAt, backReason, status: 'waiting',
});

describe('sortQueue', () => {
  it('xếp nhóm quay lại trước ưu tiên, ưu tiên trước thường', () => {
    const items = [item('n1', 'normal', 1), item('p1', 'priority', 2), item('b1', 'back', 3, 'results')];
    const ordered = sortQueue(items);
    expect(ordered.map(i => i.id)).toEqual(['b1', 'p1', 'n1']);
  });

  it('trong nhóm quay lại, người vừa đo sinh hiệu xong lên đầu trước người chờ đọc kết quả', () => {
    const items = [item('b-results', 'back', 1, 'results'), item('b-vitals', 'back', 2, 'vitals')];
    const ordered = sortQueue(items);
    expect(ordered.map(i => i.id)).toEqual(['b-vitals', 'b-results']);
  });

  it('FIFO trong cùng nhóm theo thời điểm vào hàng', () => {
    const items = [item('p2', 'priority', 20), item('p1', 'priority', 10)];
    const ordered = sortQueue(items);
    expect(ordered.map(i => i.id)).toEqual(['p1', 'p2']);
  });
});

describe('nextToCall', () => {
  it('bỏ qua người không ở trạng thái waiting', () => {
    const items = [{ ...item('n1', 'normal', 1), status: 'late' }, item('n2', 'normal', 2)];
    expect(nextToCall(items).id).toBe('n2');
  });

  it('trả về null khi hàng rỗng', () => {
    expect(nextToCall([])).toBeNull();
  });
});

describe('vòng đời cuộc gọi', () => {
  it('startCall khởi tạo lần gọi 1/3 và đếm ngược 60s', () => {
    const called = startCall(item('n1', 'normal', 1));
    expect(called.status).toBe('calling');
    expect(called.callAttempt).toBe(1);
    expect(called.callSecondsLeft).toBe(60);
  });

  it('tickCountdown giảm dần và không âm', () => {
    let called = startCall(item('n1', 'normal', 1));
    for (let i = 0; i < 61; i += 1) called = tickCountdown(called);
    expect(called.callSecondsLeft).toBe(0);
  });

  it('hết giờ ở lần gọi chưa tới 3 thì vẫn calling, cho phép gọi lại', () => {
    let called = startCall(item('n1', 'normal', 1));
    called = { ...called, callSecondsLeft: 0 };
    called = handleTimeout(called);
    expect(called.status).toBe('calling');
    expect(canRecall(called)).toBe(true);
    expect(canMarkLate(called)).toBe(false);
  });

  it('hết giờ ở lần gọi thứ 3 thì chuyển sang late và cho phép ghi vắng', () => {
    let called = startCall(item('n1', 'normal', 1));
    called = { ...called, callAttempt: MAX_CALL_ATTEMPTS, callSecondsLeft: 0 };
    expect(canMarkLate(called)).toBe(true);
    called = handleTimeout(called);
    expect(called.status).toBe('late');
  });

  it('recall tăng số lần gọi và reset đếm ngược', () => {
    let called = startCall(item('n1', 'normal', 1));
    called = { ...called, callSecondsLeft: 0 };
    called = recall(called);
    expect(called.callAttempt).toBe(2);
    expect(called.callSecondsLeft).toBe(60);
  });
});

describe('returnFromLate', () => {
  it('xếp cuối hàng thường, mất ưu tiên, giữ nguyên id (số thứ tự)', () => {
    const late = { ...item('b1', 'back', 1, 'vitals'), status: 'late' };
    const returned = returnFromLate(late, 999);
    expect(returned.status).toBe('waiting');
    expect(returned.group).toBe('normal');
    expect(returned.enqueuedAt).toBe(999);
    expect(returned.id).toBe('b1');
  });
});
