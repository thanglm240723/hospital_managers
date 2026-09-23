import { refreshAccessToken } from '../refreshCoordinator';
import { startRefreshScheduler, stopRefreshScheduler } from '../refreshScheduler';
import { setAccessToken, clearAccessToken } from '../tokenStore';

jest.mock('../refreshCoordinator', () => ({ refreshAccessToken: jest.fn() }));
jest.useFakeTimers();

beforeEach(() => {
  refreshAccessToken.mockReset();
  refreshAccessToken.mockReturnValue(Promise.resolve('t'));
  // Deterministic by default (no jitter); the jitter test below overrides this.
  jest.spyOn(Math, 'random').mockReturnValue(0);
});

afterEach(() => {
  stopRefreshScheduler();
  clearAccessToken();
  Math.random.mockRestore();
});

it('refreshes sixty seconds before the access token expires', () => {
  startRefreshScheduler(jest.fn());
  setAccessToken('a', new Date(Date.now() + 5 * 60 * 1000).toISOString());

  jest.advanceTimersByTime(3 * 60 * 1000 + 55 * 1000);
  expect(refreshAccessToken).not.toHaveBeenCalled();

  jest.advanceTimersByTime(10 * 1000);
  expect(refreshAccessToken).toHaveBeenCalledTimes(1);
});

it('reports a failed scheduled refresh', async () => {
  const onFailure = jest.fn();
  refreshAccessToken.mockReturnValue(Promise.reject(new Error('expired')));
  startRefreshScheduler(onFailure);
  setAccessToken('a', new Date(Date.now() + 30 * 1000).toISOString());   // đã trong vùng 60s ⇒ refresh ngay

  jest.advanceTimersByTime(0);
  await Promise.resolve();
  await Promise.resolve();

  expect(onFailure).toHaveBeenCalled();
});

it('does nothing after the token is cleared', () => {
  startRefreshScheduler(jest.fn());
  setAccessToken('a', new Date(Date.now() + 5 * 60 * 1000).toISOString());
  clearAccessToken();

  jest.advanceTimersByTime(10 * 60 * 1000);

  expect(refreshAccessToken).not.toHaveBeenCalled();
});

it('adds up to five seconds of jitter to the scheduled refresh delay, spreading tabs apart', () => {
  Math.random.mockReturnValue(1); // jitter = floor(1 * 5000) = 5000ms
  startRefreshScheduler(jest.fn());
  setAccessToken('a', new Date(Date.now() + 5 * 60 * 1000).toISOString());

  // Base delay (no jitter) would be 4 minutes (5min - 60s lead). With max jitter it is 4min5s.
  jest.advanceTimersByTime(4 * 60 * 1000 + 5000 - 1);
  expect(refreshAccessToken).not.toHaveBeenCalled();

  jest.advanceTimersByTime(1);
  expect(refreshAccessToken).toHaveBeenCalledTimes(1);
});
