using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Identity.Events;

namespace CleanArchCqrs.Domain.Identity;

public sealed class User : AggregateRoot<Guid>
{

	public string FullName { get;  private set; } = default!;
	public string Email { get;  private set; } = default!;
	public string PasswordHash { get;  private set; } = default!;
	public string? AvatarUrl  { get; private set; } 
	public string Role { get; private set; } 
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? LastLoginAt { get; private set; }
	public DateTimeOffset CreatedAt { get; private set; }
	public DateTimeOffset? UpdatedAt { get; private set; }
	

	private User() { }

	private User(Guid id, string fullName, string email, string passwordHash, string? avatarUrl , string role)
	{
		Id = id;
		FullName = fullName;
		Email = email;
		PasswordHash = passwordHash;
		CreatedAt = DateTimeOffset.UtcNow;
	    AvatarUrl = avatarUrl;
	    Role = role;
    }
	

	public static User Create(string fullName, string email, string passwordHash, string? avatarUrl, string role)
    {
       if(string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Full name cannot be empty.", nameof(fullName));
        }
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email cannot be empty.", nameof(email));
        }
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password hash cannot be empty.", nameof(passwordHash));
        }
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("Role cannot be empty.", nameof(role));
        }


        var emailLower = email.Trim().ToLowerInvariant();

        var user = new User(Guid.CreateVersion7(), fullName.Trim(),
                            emailLower, passwordHash, avatarUrl, role);

        user.Raise(new UserRegisteredDomainEvent(user.Id, email, role));
        return user;
    }

    public void ChangePassword(string newPasswordHash)
    {
        if(string.IsNullOrWhiteSpace(newPasswordHash))
        {
            throw new ArgumentException("New password hash cannot be empty.", nameof(newPasswordHash));
        }
        if(newPasswordHash == PasswordHash)
        {
            throw new InvalidOperationException("New password hash cannot be the same as the current password hash.");
        }

        PasswordHash = newPasswordHash;
        Touch();
        Raise(new UserPasswordChangedDomainEvent(Id));
    }

    public void UpdateProfile(string fullName, string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Full name cannot be empty.", nameof(fullName));
        }
        FullName = fullName.Trim();
        AvatarUrl = avatarUrl;
        Touch();
        Raise(new UserProfileUpdatedDomainEvent(Id));
    }

    public void Deactivate()
    {
        if (IsActive)
        {
            IsActive = false;
            Touch();
            Raise(new UserDeactivatedDomainEvent(Id));
        }
    }


    public void Activate()
    {
        if (!IsActive)
        {
            IsActive = true;
            Touch();
            Raise(new UserActivatedDomainEvent(Id));
        }
    }

    public void RecordLogin()
{
    LastLoginAt = DateTimeOffset.UtcNow;
    Touch();
}

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

}