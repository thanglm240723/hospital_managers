namespace QuanLyBenhVien.Application.Common.Authorization;

public abstract record AccessDecision
{
    public sealed record Allowed(RelationKind Relation, string? DutyRole) : AccessDecision;
    public sealed record Denied(string Reason) : AccessDecision;
}
