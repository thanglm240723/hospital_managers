namespace QuanLyBenhVien.Domain.Common
{
	public interface IUnitOfWork
	{
		//Task<int>ef trả về số dòng bị ảnh hưởng
		Task<int> SaveChangesAsync(CancellationToken ct = default);

		/// Cần khi phải khoá hàng (SELECT ... FOR UPDATE) hoặc commit trước khi trả lỗi.
		Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default);

	}
}
	