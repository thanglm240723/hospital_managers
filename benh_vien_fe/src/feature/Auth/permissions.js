// Chỉ để ẩn/hiện UI. Quyết định thật nằm ở API.
export const hasPermission = (authState, code) => Boolean(
  authState && authState.permissions && authState.permissions.indexOf(code) !== -1,
);
