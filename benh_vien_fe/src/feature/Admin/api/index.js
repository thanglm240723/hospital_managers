import { isMockApiEnabled } from 'service/mockMode';
import * as real from './usersClient';
import * as mock from './usersMock';

const impl = isMockApiEnabled() ? mock : real;

export const {
  listUsers, getUser, createUser, assignRoles, grantPermissions, revokePermissions, resetTemporaryPassword,
  activateUser, deactivateUser,
} = impl;
