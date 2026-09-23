using CleanArchCqrs.Application.Auth.Models;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Queries.ValidateSession;

public sealed record ValidateSessionQuery(Guid SessionFamilyId, int SecurityVersion) : IRequest<SessionValidationResult>;
