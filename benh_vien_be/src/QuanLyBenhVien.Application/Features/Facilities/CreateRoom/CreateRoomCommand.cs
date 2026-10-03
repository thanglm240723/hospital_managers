using QuanLyBenhVien.Application.Common.Authorization;
using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Facilities.Common;

namespace QuanLyBenhVien.Application.Features.Facilities.CreateRoom;

public sealed record CreateRoomCommand(Guid DepartmentId, string Code, string Name) : ICommand<Result<RoomDto>>, IUnscopedRequest;
