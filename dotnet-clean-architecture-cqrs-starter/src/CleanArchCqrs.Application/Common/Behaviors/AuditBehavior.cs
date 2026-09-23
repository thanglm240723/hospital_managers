using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using MediatR;

namespace CleanArchCqrs.Application.Common.Behaviors;

/// Ghi audit bền vững TRƯỚC khi trả dữ liệu; lưu thất bại ⇒ exception ⇒ dữ liệu không rời server.
public sealed class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;

    public AuditBehavior(IAuditRecorder audit, IUnitOfWork unitOfWork)
    {
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IAuditedRequest audited) return await next();

        var response = await next();
        _audit.Record(audited.AuditAction, AuditResult.Succeeded,
            resourceType: audited.AuditResourceType, resourceId: audited.AuditResourceId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }
}
