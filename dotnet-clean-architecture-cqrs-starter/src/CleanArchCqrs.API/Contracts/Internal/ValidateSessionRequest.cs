namespace CleanArchCqrs.API.Contracts.Internal;

public sealed record ValidateSessionRequest(Guid FamilyId, int Sv);
