using QuanLyBenhVien.Application.Common.Authorization;
using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Facilities.Common;

namespace QuanLyBenhVien.Application.Features.Facilities.UpdateRoom;

public sealed record UpdateRoomCommand(Guid Id, string Name, bool IsActive, uint ExpectedVersion) : ICommand<Result<RoomDto>>, IUnscopedRequest;
