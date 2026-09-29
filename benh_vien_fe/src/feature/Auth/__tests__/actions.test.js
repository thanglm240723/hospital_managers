import * as authClient from '../api/authClient';
import http from '../../../service/http';
import { getAccessToken, clearAccessToken } from '../session/tokenStore';
import { login } from '../redux/actions';
import { AUTH_AUTHENTICATED } from '../redux/actionTypes';

jest.mock('../api/authClient');
jest.mock('../../../service/http');

describe('login thunk', () => {
  afterEach(() => {
    clearAccessToken();
    jest.clearAllMocks();
  });

  it('đăng nhập thành công: lưu token mới rồi dispatch AUTH_AUTHENTICATED với payload /me', async () => {
    const meData = { id: 'u1', email: 'a@b.com', permissions: ['users.read'], mustChangePassword: false };
    authClient.login.mockResolvedValue({ accessToken: 'token-123', expiresAtUtc: '2026-09-28T00:10:00Z' });
    http.get.mockResolvedValue({ data: meData });

    const dispatch = jest.fn(action => (typeof action === 'function' ? action(dispatch) : action));

    await login('a@b.com', 'secret')(dispatch);

    expect(authClient.login).toHaveBeenCalledWith('a@b.com', 'secret');
    expect(getAccessToken()).toBe('token-123');
    expect(http.get).toHaveBeenCalledWith('v1/auth/me');
    expect(dispatch).toHaveBeenCalledWith({ type: AUTH_AUTHENTICATED, payload: meData });
  });

  it('authClient.login trả 401: thunk ném lại lỗi, không dispatch AUTH_AUTHENTICATED, token không bị set', async () => {
    const error = { response: { status: 401 } };
    authClient.login.mockRejectedValue(error);

    const dispatch = jest.fn();

    await expect(login('a@b.com', 'wrong')(dispatch)).rejects.toBe(error);

    expect(getAccessToken()).toBeNull();
    expect(http.get).not.toHaveBeenCalled();
    expect(dispatch).not.toHaveBeenCalledWith(expect.objectContaining({ type: AUTH_AUTHENTICATED }));
  });
});
