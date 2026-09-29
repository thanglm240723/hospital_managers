// Hàm thuần cho hàng chờ khám — không phụ thuộc React/Redux để dễ unit test.
export const CALL_TIMEOUT_SECONDS = 60;
export const MAX_CALL_ATTEMPTS = 3;

// group: 'back' (quay lại) | 'priority' (ưu tiên) | 'normal' (thường)
// backReason (chỉ dùng khi group === 'back'): 'vitals' (vừa đo sinh hiệu xong) | 'results' (chờ đọc kết quả)
export function sortQueue(items) {
  const back = items.filter(i => i.group === 'back');
  const priority = items.filter(i => i.group === 'priority');
  const normal = items.filter(i => i.group === 'normal');

  const byEnqueued = (a, b) => a.enqueuedAt - b.enqueuedAt;
  const backOrdered = [
    ...back.filter(i => i.backReason === 'vitals').sort(byEnqueued),
    ...back.filter(i => i.backReason !== 'vitals').sort(byEnqueued),
  ];

  return [...backOrdered, ...priority.sort(byEnqueued), ...normal.sort(byEnqueued)];
}

// Người tiếp theo được gọi — chỉ trong số người đang ở trạng thái 'waiting', không được chọn tay.
export function nextToCall(items) {
  const ordered = sortQueue(items.filter(i => i.status === 'waiting'));
  return ordered.length > 0 ? ordered[0] : null;
}

export function startCall(item) {
  return { ...item, status: 'calling', callAttempt: 1, callSecondsLeft: CALL_TIMEOUT_SECONDS };
}

export function recall(item) {
  return { ...item, callAttempt: item.callAttempt + 1, callSecondsLeft: CALL_TIMEOUT_SECONDS };
}

export function tickCountdown(item) {
  return { ...item, callSecondsLeft: Math.max(0, item.callSecondsLeft - 1) };
}

// Sau khi hết giờ ở lần gọi thứ 3, chuyển sang trạng thái 'late' (Hàng trễ). Ngược lại vẫn 'calling', chờ nhân viên bấm Gọi lại.
export function handleTimeout(item) {
  if (item.callSecondsLeft > 0) return item;
  if (item.callAttempt >= MAX_CALL_ATTEMPTS) return { ...item, status: 'late' };
  return item;
}

export const canRecall = item => item.status === 'calling' && item.callSecondsLeft === 0 && item.callAttempt < MAX_CALL_ATTEMPTS;
export const canMarkLate = item => item.status === 'calling' && item.callSecondsLeft === 0 && item.callAttempt >= MAX_CALL_ATTEMPTS;

// "Đã quay lại" từ hàng trễ: xếp cuối hàng, mất ưu tiên, giữ số thứ tự đã cấp.
export function returnFromLate(item, now) {
  return {
    ...item, status: 'waiting', group: 'normal', backReason: null, enqueuedAt: now, callAttempt: 0, callSecondsLeft: null,
  };
}
