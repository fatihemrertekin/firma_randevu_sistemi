using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Features.Business;
using Server.Features.Identity;
using Server.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IConfiguration>()
        .GetConnectionString("AppDatabase")));
builder.Services.AddProblemDetails();

builder.AddApplicationIdentity();

var app = builder.Build();
// İstekler ve teslim işçisi başlamadan son yapılandırmayı doğrula.
_ = app.Services.GetRequiredService<IOwnerEmailVerificationDelivery>();
_ = app.Services.GetRequiredService<OwnerSelfServiceResetFlow>();

if (await app.TryRunIdentityCommandAsync(args))
{
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapAuthEndpoints();
app.MapBusinessProfileEndpoints();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));

app.MapGet("/health/ready", async (HttpContext context) =>
{
    var connectionString = app.Configuration.GetConnectionString("AppDatabase");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    timeout.CancelAfter(TimeSpan.FromSeconds(3));

    try
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(timeout.Token);
        return Results.Ok(new { status = "ready" });
    }
    catch (Exception exception) when (
        exception is NpgsqlException or TimeoutException ||
        exception is OperationCanceledException && !context.RequestAborted.IsCancellationRequested)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
