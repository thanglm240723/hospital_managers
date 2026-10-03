import { isMockApiEnabled } from 'service/mockMode';
import * as real from './rolesClient';
import * as mock from './rolesMock';

const impl = isMockApiEnabled() ? mock : real;

export const {
  listRoles, listPermissions, createRole, renameRole, updateRolePermissions,
} = impl;
