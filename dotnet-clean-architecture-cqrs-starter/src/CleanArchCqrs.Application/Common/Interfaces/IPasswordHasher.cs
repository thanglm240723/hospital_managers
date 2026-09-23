namespace CleanArchCqrs.Application.Common.Interfaces
{
    public interface IPasswordHasher 
    {
        string Hash(string password);
        bool Verify(string password, string passwordHash);

        /// Chạy một phép verify giả để nhánh "email không tồn tại" tốn thời gian như nhánh "sai mật khẩu".
        void SimulateVerify(string password);
    }
}