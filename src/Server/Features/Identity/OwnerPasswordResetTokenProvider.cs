using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Server.Infrastructure;

namespace Server.Features.Identity;

// A separate provider keeps the reset lifetime independent from future invitations.
public sealed class OwnerPasswordResetTokenProvider(
    IDataProtectionProvider protection, ILogger<DataProtectorTokenProvider<AppUser>> logger)
    : DataProtectorTokenProvider<AppUser>(protection,
        Microsoft.Extensions.Options.Options.Create(new DataProtectionTokenProviderOptions
        {
            Name = "OwnerPasswordReset",
            TokenLifespan = TimeSpan.FromMinutes(30)
        }), logger);
