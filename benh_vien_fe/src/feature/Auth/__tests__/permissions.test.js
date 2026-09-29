import { hasPermission } from '../permissions';

it('checks the permission list of the auth slice', () => {
  const auth = { permissions: ['users.read'] };

  expect(hasPermission(auth, 'users.read')).toBe(true);
  expect(hasPermission(auth, 'roles.manage')).toBe(false);
  expect(hasPermission(undefined, 'users.read')).toBe(false);
});
