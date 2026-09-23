using CleanArchCqrs.Application.Common.Models;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IRefreshTokenGenerator
{
    GeneratedRefreshToken Generate();
    string Hash(string token);
}
