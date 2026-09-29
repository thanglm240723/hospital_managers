import authReducer from '../redux/reducer';
import { AUTH_AUTHENTICATED } from '../redux/actionTypes';

it('AUTH_AUTHENTICATED tách đúng user/permissions/mustChangePassword và đổi status', () => {
  const payload = {
    id: 'u1', email: 'a@b.com', fullName: 'A B', permissions: ['users.read', 'roles.manage'], mustChangePassword: true,
  };

  const state = authReducer(undefined, { type: AUTH_AUTHENTICATED, payload });

  expect(state.status).toBe('authenticated');
  expect(state.permissions).toEqual(['users.read', 'roles.manage']);
  expect(state.mustChangePassword).toBe(true);
  expect(state.user).toEqual({ id: 'u1', email: 'a@b.com', fullName: 'A B' });
  expect(state.sessionMessage).toBeNull();
});
