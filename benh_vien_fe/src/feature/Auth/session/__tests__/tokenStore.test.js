import {
  getAccessToken, getExpiresAt, setAccessToken, clearAccessToken, subscribe,
} from '../tokenStore';

afterEach(() => clearAccessToken());

it('keeps the access token and its expiry in memory', () => {
  setAccessToken('abc', '2026-09-23T08:15:00Z');

  expect(getAccessToken()).toBe('abc');
  expect(getExpiresAt()).toBe(Date.parse('2026-09-23T08:15:00Z'));
});

it('notifies subscribers on set and clear, and stops after unsubscribe', () => {
  const listener = jest.fn();
  const unsubscribe = subscribe(listener);

  setAccessToken('abc', '2026-09-23T08:15:00Z');
  clearAccessToken();
  unsubscribe();
  setAccessToken('later', '2026-09-23T08:30:00Z');

  expect(listener).toHaveBeenCalledTimes(2);
  expect(listener).toHaveBeenLastCalledWith(null);
});

it('never writes to web storage', () => {
  const spy = jest.spyOn(Storage.prototype, 'setItem');

  setAccessToken('abc', '2026-09-23T08:15:00Z');

  expect(spy).not.toHaveBeenCalled();
  spy.mockRestore();
});
