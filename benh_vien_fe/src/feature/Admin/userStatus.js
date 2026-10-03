// Trạng thái hiển thị suy ra từ UserSummaryDto/UserDetailDto: khóa > chờ đổi mật khẩu > hoạt động.
export const userStatus = (user) => {
  if (!user.isActive) return 'locked';
  return user.mustChangePassword ? 'must_change_password' : 'active';
};

export default userStatus;
