import { refreshAccessToken } from '../refreshCoordinator';
import { startRefreshScheduler, stopRefreshScheduler } from '../refreshScheduler';
import { setAccessToken, clearAccessToken } from '../tokenStore';

jest.mock('../refreshCoordinator', () => ({ refreshAccessToken: jest.fn() }));
jest.useFakeTimers();

beforeEach(() => {
  refreshAccessToken.mockReset();
  refreshAccessToken.mockReturnValue(Promise.resolve('t'));
});

afterEach(() => {
  stopRefreshScheduler();
  clearAccessToken();
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
