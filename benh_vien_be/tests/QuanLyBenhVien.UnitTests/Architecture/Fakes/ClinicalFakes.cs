using QuanLyBenhVien.Application.Common.Authorization;
using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.UnitTests.Architecture.Fakes.Features.Clinical.X;

internal sealed record NoMarker : ICommand<Result>;
internal sealed record BothMarkers : ICommand<Result>, IScopedRequest, IUnscopedRequest;
internal sealed record Unscoped : IQuery<Result>, IUnscopedRequest;
internal sealed record Scoped : IQuery<Result>, IScopedRequest;
internal abstract record AbstractUnscoped : IUnscopedRequest;
internal sealed record NotARequest : IUnscopedRequest;
