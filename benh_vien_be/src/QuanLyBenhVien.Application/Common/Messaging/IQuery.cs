using MediatR;

namespace QuanLyBenhVien.Application.Common.Messaging;

public interface IQuery<TResponse> : IRequest<TResponse>;
