

namespace CleanArchCqrs.Application.Auth.Models
{
    public class LoginResult
    {
        public string? AccessToken { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
        public UserDto UserDto { get; set; } = default!;
    }
}
