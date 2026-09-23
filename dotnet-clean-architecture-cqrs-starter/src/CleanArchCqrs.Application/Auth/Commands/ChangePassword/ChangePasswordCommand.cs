using CleanArchCqrs.Application.Auth.Models;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.ChangePassword;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<AccessTokenResult>;
