import { bootAuth, logout } from '../actions';
import { AUTH_AUTHENTICATED, AUTH_ANONYMOUS, AUTH_BOOTING, AUTH_LOGGED_OUT } from '../actionTypes';
import { refreshAccessToken, broadcastLogout } from '../../session/refreshCoordinator';
import { logout as logoutRequest } from '../../api/authClient';
import http from '../../../../service/http';
import { getAccessToken, setAccessToken } from '../../session/tokenStore';

jest.mock('../../session/refreshCoordinator', () => ({ refreshAccessToken: jest.fn(), broadcastLogout: jest.fn() }));
jest.mock('../../api/authClient', () => ({ login: jest.fn(), logout: jest.fn() }));
jest.mock('../../../../service/http', () => ({ get: jest.fn(), post: jest.fn() }));

const dispatchFor = (actions) => {
  const dispatch = (action) => (typeof action === 'function' ? action(dispatch) : actions.push(action));
  return dispatch;
};

it('bootAuth: refresh cookie works ⇒ loads /me and becomes authenticated', async () => {
  refreshAccessToken.mockResolvedValue('t');
  http.get.mockResolvedValue({ data: { id: 'u1', permissions: [], mustChangePassword: false } });
  const actions = [];

  await bootAuth()(dispatchFor(actions));

  expect(actions.map(a => a.type)).toEqual([AUTH_BOOTING, AUTH_AUTHENTICATED]);
});

it('bootAuth: no valid refresh cookie ⇒ anonymous', async () => {
  refreshAccessToken.mockRejectedValue(new Error('401'));
  const actions = [];

  await bootAuth()(dispatchFor(actions));

  expect(actions.map(a => a.type)).toEqual([AUTH_BOOTING, AUTH_ANONYMOUS]);
});

it('logout: clears the token, notifies other tabs even if the server call fails', async () => {
  setAccessToken('t', new Date(Date.now() + 60000).toISOString());
  logoutRequest.mockRejectedValue(new Error('offline'));
  const actions = [];

  await logout()(dispatchFor(actions));

  expect(getAccessToken()).toBeNull();
  expect(broadcastLogout).toHaveBeenCalled();
  expect(actions[0].type).toBe(AUTH_LOGGED_OUT);
});
