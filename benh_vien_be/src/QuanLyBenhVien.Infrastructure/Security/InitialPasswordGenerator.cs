using System.Security.Cryptography;
using QuanLyBenhVien.Application.Common.Security;

namespace QuanLyBenhVien.Infrastructure.Security;

/// 16 ký tự ngẫu nhiên (CSPRNG) từ bảng chữ-số dễ đọc (bỏ 0/O/1/l/I) — khoảng 91 bit entropy.
public sealed class InitialPasswordGenerator : IInitialPasswordGenerator
{
    public const int Length = 16;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);
}
