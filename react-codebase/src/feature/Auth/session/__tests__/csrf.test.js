import { readCsrfToken } from '../csrf';

it('reads the __Host-csrf cookie among others', () => {
  expect(readCsrfToken('a=1; __Host-csrf=tok_EN-1; b=2')).toBe('tok_EN-1');
});

it('returns null when the cookie is missing', () => {
  expect(readCsrfToken('a=1')).toBeNull();
  expect(readCsrfToken('')).toBeNull();
});
