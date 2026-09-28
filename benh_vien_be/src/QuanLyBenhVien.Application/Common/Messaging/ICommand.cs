using MediatR;

namespace QuanLyBenhVien.Application.Common.Messaging;

public interface ICommand<TResponse> : IRequest<TResponse>;
