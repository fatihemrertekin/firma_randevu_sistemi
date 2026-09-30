using System.Data;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Identity;

public static class BootstrapOwner
{
    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var email = Environment.GetEnvironmentVariable("APP_BOOTSTRAP_OWNER_EMAIL");
        var password = Environment.GetEnvironmentVariable("APP_BOOTSTRAP_OWNER_PASSWORD");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) ||
            !new EmailAddressAttribute().IsValid(email.Trim()))
        {
            Console.Error.WriteLine("Owner kurulumu için geçerli e-posta ve parola gerekli.");
            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (await db.Users.AnyAsync(cancellationToken))
        {
            Console.Error.WriteLine("Hesap zaten var; Owner kurulumu yalnız boş kurulumda çalışır.");
            return 1;
        }

        var roleResult = await roles.CreateAsync(new IdentityRole<Guid>("Owner"));
        if (!roleResult.Succeeded)
        {
            Console.Error.WriteLine("Owner rolü oluşturulamadı.");
            return 1;
        }

        var user = new AppUser
        {
            UserName = email.Trim(),
            Email = email.Trim(),
            EmailConfirmed = true
        };
        var userResult = await users.CreateAsync(user, password);
        if (!userResult.Succeeded)
        {
            Console.Error.WriteLine("Owner hesabı oluşturulamadı: " +
                string.Join(", ", userResult.Errors.Select(error => error.Code)));
            return 1;
        }

        var assignment = await users.AddToRoleAsync(user, "Owner");
        if (!assignment.Succeeded)
        {
            Console.Error.WriteLine("Owner rolü hesaba atanamadı.");
            return 1;
        }

        await transaction.CommitAsync(cancellationToken);
        Console.WriteLine("Owner hesabı oluşturuldu.");
        return 0;
    }
}
