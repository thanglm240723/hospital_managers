namespace CleanArchCqrs.Domain.Common
{
	public interface IUnitOfWork
	{
		//Task<int>ef trả về số dòng bị ảnh hưởng
		Task<int> SaveChangesAsync(CancellationToken ct = default);		

	}
}
	