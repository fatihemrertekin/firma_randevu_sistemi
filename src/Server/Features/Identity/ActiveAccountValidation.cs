using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Server.Infrastructure;

namespace Server.Features.Identity;

public sealed class ActiveUserConfirmation : IUserConfirmation<AppUser>
{
    // E-posta doğrulaması Identity'nin ayrı RequireConfirmedEmail kontrolünde kalır.
    public Task<bool> IsConfirmedAsync(UserManager<AppUser> manager, AppUser user) => Task.FromResult(user.IsActive);
}

public sealed class ActiveSecurityStampValidator(
    IOptions<SecurityStampValidatorOptions> options,
    SignInManager<AppUser> signIn,
    ILoggerFactory loggerFactory) : SecurityStampValidator<AppUser>(options, signIn, loggerFactory)
{
    protected override async Task<AppUser?> VerifySecurityStamp(ClaimsPrincipal? principal)
    {
        var user = await base.VerifySecurityStamp(principal);
        return user is { IsActive: true } ? user : null;
    }
}
