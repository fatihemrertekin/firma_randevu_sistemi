using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Features.Business;
using Server.Features.Audit;
using Server.Features.Identity;
using Server.Features.Staff;
using Server.Features.Services;
using Server.Features.Scheduling;
using Server.Features.Subscriptions;
using Server.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IConfiguration>()
        .GetConnectionString("AppDatabase")));
builder.Services.AddProblemDetails();

builder.AddApplicationIdentity();
builder.Services.AddHostedService<AppointmentPendingWorker>();
builder.Services.AddHttpClient<IBookingSmsDelivery, NetgsmBookingSmsDelivery>(client => client.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    .RemoveAllLoggers();
builder.Services.AddSingleton<BookingVerificationSecurity>();
builder.Services.AddHostedService<BookingSmsWorker>();
builder.Services.AddHttpClient<IAppointmentReminderDelivery, NetgsmAppointmentReminderDelivery>(client => client.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    .RemoveAllLoggers();
builder.Services.AddSingleton<AppointmentReminders>();
builder.Services.AddHostedService<AppointmentReminderWorker>();

var app = builder.Build();
// İstekler ve teslim işçisi başlamadan son yapılandırmayı doğrula.
_ = app.Services.GetRequiredService<IOwnerEmailVerificationDelivery>();
_ = app.Services.GetRequiredService<OwnerSelfServiceResetFlow>();
var bookingSecurity = app.Services.GetRequiredService<BookingVerificationSecurity>();
var reminders = app.Services.GetRequiredService<AppointmentReminders>();
if (bookingSecurity.Enabled || reminders.Enabled)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
    await bookingSecurity.LockAsync(db, timeout.Token);
    var now = app.Services.GetRequiredService<TimeProvider>().GetUtcNow();
    if (bookingSecurity.DispatchEnabled &&
        (bookingSecurity.Enabled && await BookingSmsBudget.PolicyAsync(db, bookingSecurity, now, timeout.Token) is null ||
         reminders.Enabled && await BookingSmsBudget.PolicyAsync(db, bookingSecurity, now, timeout.Token,
             app.Services.GetRequiredService<IAppointmentReminderDelivery>().MaximumUnitPriceTry) is null))
        throw new InvalidOperationException("SMS bütçesi onaysız, süresi dolmuş, geri yükleme sonrası iptal edilmiş veya fiyatla uyuşmuyor. Gönderimi kapatıp operatör uzlaştırması yapın.");
    await transaction.CommitAsync(timeout.Token);
}

if (await app.TryRunIdentityCommandAsync(args))
{
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}
app.UseRouting();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/public-booking") ||
        context.Request.Path.StartsWithSegments("/randevu"))
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            return Task.CompletedTask;
        });
    await next(context);
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(SubscriptionAccess.HandleAsync);
app.MapAuthEndpoints();
app.MapBusinessProfileEndpoints();
app.MapBusinessHoursEndpoints();
app.MapAuditLogEndpoints();
app.MapStaffMemberEndpoints();
app.MapServiceDefinitionEndpoints();
app.MapAppointmentEndpoints();
app.MapBookingPolicyEndpoints();
app.MapSubscriptionEndpoints();
app.MapPublicBookingEndpoints();
app.MapBookingVerificationEndpoints();
app.MapPublicAppointmentEndpoints();
app.MapPublicAppointmentAccessEndpoints();

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
        // Havuzun ertelenmiş reset yanıtı da takılabilir; komut süresi ayrıca sınırlı olmalı.
        await using var command = new NpgsqlCommand("SELECT 1", connection) { CommandTimeout = 3 };
        await command.ExecuteScalarAsync(timeout.Token);
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
// Kaldırılmış/bilinmeyen API uçları SPA sayfasıyla başarılı görünmemeli.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
