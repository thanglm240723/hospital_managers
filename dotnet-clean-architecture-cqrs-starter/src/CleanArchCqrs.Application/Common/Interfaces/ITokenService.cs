using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Application.Common.Models;
namespace CleanArchCqrs.Application.Common.Interfaces;

    public interface ITokenService
{
    AccessToken CreateAccessToken(User user);
}   