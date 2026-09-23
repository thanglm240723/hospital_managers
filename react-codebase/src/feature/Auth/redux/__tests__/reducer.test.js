import authReducer from '../reducer';
import { AUTH_AUTHENTICATED, AUTH_LOGGED_OUT } from '../actionTypes';

const me = {
  id: 'u1', email: 'a@b.vn', fullName: 'A', avatarUrl: null,
  roles: [{ id: 'r1', code: 'doctor', name: 'Bác sĩ' }], permissions: ['users.read'], mustChangePassword: false,
};

it('starts in booting state', () => {
  expect(authReducer(undefined, { type: '@@INIT' }).status).toBe('booting');
});

it('stores user and permissions from /auth/me', () => {
  const state = authReducer(undefined, { type: AUTH_AUTHENTICATED, payload: me });

  expect(state.status).toBe('authenticated');
  expect(state.permissions).toEqual(['users.read']);
  expect(state.user.email).toBe('a@b.vn');
  expect(state.user.permissions).toBeUndefined();
});

it('clears everything on logout and keeps the reason message', () => {
  const loggedIn = authReducer(undefined, { type: AUTH_AUTHENTICATED, payload: me });

  const state = authReducer(loggedIn, { type: AUTH_LOGGED_OUT, payload: 'Phiên đã hết hiệu lực' });

  expect(state).toEqual({
    status: 'anonymous', user: null, permissions: [], mustChangePassword: false, sessionMessage: 'Phiên đã hết hiệu lực',
  });
});
