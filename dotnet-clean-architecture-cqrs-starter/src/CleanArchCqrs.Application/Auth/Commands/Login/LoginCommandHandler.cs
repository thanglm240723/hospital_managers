
using MediatR;
using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Application.Auth.Commands.Login
{
    public class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResult>
    {
    

        private readonly IUserRepository _userRepository;
        private readonly ITokenService _tokenService;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IUnitOfWork _unitOfWork;

        public  LoginCommandHandler(IUserRepository userRepository, ITokenService tokenService, IPasswordHasher passwordHasher, IUnitOfWork unitOfWork)
        {
            _userRepository = userRepository;
            _tokenService = tokenService;
            _passwordHasher = passwordHasher;
            _unitOfWork = unitOfWork;
        }

        public async Task<LoginResult> Handle(LoginCommand request, CancellationToken cancellationToken)
        {
            var emailnormaline = User.NormalizeEmail(request.email);

            var user = await _userRepository.GetByEmailAsync(emailnormaline, cancellationToken);
            if (user is null || !user.IsActive)
            {
                throw new UnauthorizedAccessException("Invalid email or password.");
            }
            var PasswordValid = _passwordHasher.Verify(request.password, user.PasswordHash);
            if (!PasswordValid)
            {
                throw new UnauthorizedAccessException("Invalid email or password.");
            }

            user.RecordLogin();
            _userRepository.UpdateUser(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);


            var token = _tokenService.CreateAccessToken(user);
            if (token is null) { 
                throw new InvalidOperationException("Failed to create access token.");
            }

             return new LoginResult
            {
                AccessToken = token.Token,
                ExpiresAtUtc = token.ExpiresAtUtc,
                UserDto = new UserDto
                {
                    Id= user.Id,
                    Email = user.Email,
                    FullName = user.FullName,
                    AvatarUrl = user.AvatarUrl,
                    Role = user.Role
                }
            };


        }

    }
}
