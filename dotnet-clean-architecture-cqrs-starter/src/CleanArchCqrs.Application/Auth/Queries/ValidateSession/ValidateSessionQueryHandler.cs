using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Interfaces;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Queries.ValidateSession;

public sealed class ValidateSessionQueryHandler : IRequestHandler<ValidateSessionQuery, SessionValidationResult>
{
    private readonly ISessionValidationService _validation;

    public ValidateSessionQueryHandler(ISessionValidationService validation) => _validation = validation;

    public Task<SessionValidationResult> Handle(ValidateSessionQuery request, CancellationToken ct)
        => _validation.ValidateAsync(request.SessionFamilyId, request.SecurityVersion, ct);
}
