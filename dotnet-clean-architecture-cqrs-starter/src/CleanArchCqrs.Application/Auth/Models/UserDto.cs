

namespace CleanArchCqrs.Application.Auth.Models
{
    public class UserDto
    {
        public Guid? Id { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? AvatarUrl { get; set; } = default!;
        public string? Role { get; set; } = default!;

    }
}
