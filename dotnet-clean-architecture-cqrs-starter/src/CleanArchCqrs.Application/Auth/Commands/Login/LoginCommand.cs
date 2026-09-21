
using CleanArchCqrs.Application.Auth.Models;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.Login
{
    public record LoginCommand(string email, string password) : IRequest<LoginResult>;
}
