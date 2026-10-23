using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Features.Identity;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

public sealed class AppointmentReminders
{
    public const string PhonePurpose = "AppointmentReminder.Phone.v1";
    public bool Enabled { get; }
    private readonly IDataProtector protector;
    private readonly BookingVerificationSecurity security;

    public AppointmentReminders(IConfiguration config, IDataProtectionProvider protection,
        IAppointmentReminderDelivery delivery, BookingVerificationSecurity security)
    {
        Enabled = config.GetValue<bool>("AppointmentReminders:Enabled");
        protector = protection.CreateProtector(PhonePurpose);
        this.security = security;
        if (Enabled && !delivery.Available) throw new InvalidOperationException("Hatırlatma için kullanılabilir taşıyıcı gerekli; gönderimi kapatın.");
    }

    internal static Task SyncAsync(HttpContext context, AppDbContext db, Appointment appointment,
        AppointmentTimeBlock block, DateTimeOffset now, CancellationToken token) =>
        context.RequestServices.GetRequiredService<AppointmentReminders>().SyncAsync(db, appointment, block, now, token);

    // Çağıran randevu kilidini (yeni kayıtta aynı oluşturma transaction'ını) tutar.
    internal async Task SyncAsync(AppDbContext db, Appointment appointment, AppointmentTimeBlock block,
        DateTimeOffset now, CancellationToken token)
    {
        var job = await db.AppointmentReminderJobs.FromSqlInterpolated($"""
            SELECT * FROM "AppointmentReminderJobs" WHERE "AppointmentId" = {appointment.Id} FOR UPDATE
            """).SingleOrDefaultAsync(token);
        if (job is null)
        {
            if (!Enabled || block.Status != "Confirmed" || !appointment.CustomerPhone.StartsWith("+905", StringComparison.Ordinal)) return;
            job = new() { Id = Guid.NewGuid(), AppointmentId = appointment.Id, Status = "Pending", BudgetApprovalId = security.BudgetApprovalId };
            db.AppointmentReminderJobs.Add(job);
        }
        else if (job.SuppressedAt is not null || job.StartedOnceAt is not null)
        {
            if (job.AppointmentVersion != appointment.Version && job.Status is "Pending" or "Processing") Finish(job, "Cancelled");
            return;
        }
        else if (job.AppointmentVersion == appointment.Version && Enabled) return;
        job.AppointmentVersion = appointment.Version;
        job.StartsAt = new DateTimeOffset(block.StartsAt);
        job.DueAt = job.StartsAt.AddHours(-3); job.Deadline = job.DueAt.AddMinutes(15);
        job.NextAttemptAt = job.DueAt;
        Finish(job, !Enabled || block.Status != "Confirmed" ? "Cancelled" : now > job.DueAt ? "TooLate" : "Pending");
        if (job.Status == "Pending") job.ProtectedPhone = protector.Protect(appointment.CustomerPhone);
    }

    internal static void Finish(AppointmentReminderJob job, string status)
    {
        job.Status = status; job.LeaseId = null; job.LeaseUntil = null; job.ProtectedPhone = null;
    }

    public sealed record DisableRequest(Guid Version);

    internal static void MapAppointmentReminderEndpoints(RouteGroupBuilder appointments) =>
        appointments.MapPost("/{id:guid}/reminder/disable", DisableAsync);

    private static async Task<IResult> DisableAsync(Guid id, DisableRequest request, HttpContext context,
        IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, AppointmentReminders reminders, TimeProvider clock)
    {
        using var timeout = AppointmentEndpoints.Timeout(context); var token = timeout.Token;
        if (!await AuthEndpoints.HasValidCsrfAsync(csrf, context)) return AuthEndpoints.InvalidCsrf();
        if (!reminders.Enabled) return Results.Problem(statusCode: 503, title: "Hatırlatma özelliği kapalı.");
        if (id == Guid.Empty || request.Version == Guid.Empty) return Results.Problem(statusCode: 400, title: "Randevu ve güncel sürüm gerekli.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var owner = await OwnerMutationAuthorization.LockAsync(context, db, users, token);
        if (owner is null) return Results.Unauthorized();
        var appointment = await db.Appointments.FromSqlInterpolated($"SELECT * FROM \"Appointments\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(token);
        if (appointment is null) return Results.NotFound();
        if (appointment.Version != request.Version) return Results.Problem(statusCode: 409, title: "Randevu değişti. Güncel kaydı yükleyin.");
        var block = await db.AppointmentTimeBlocks.SingleAsync(x => x.AppointmentId == id, token);
        await reminders.SyncAsync(db, appointment, block, clock.GetUtcNow(), token);
        var job = db.AppointmentReminderJobs.Local.SingleOrDefault(x => x.AppointmentId == id);
        if (job is null)
        {
            var start = new DateTimeOffset(block.StartsAt);
            job = new()
            {
                Id = Guid.NewGuid(),
                AppointmentId = id,
                AppointmentVersion = appointment.Version,
                StartsAt = start,
                DueAt = start.AddHours(-3),
                Deadline = start.AddHours(-3).AddMinutes(15),
                NextAttemptAt = start.AddHours(-3),
                Status = "Suppressed"
            };
            db.AppointmentReminderJobs.Add(job);
        }
        if (job.SuppressedAt is null) { job.SuppressedAt = clock.GetUtcNow(); job.SuppressedBy = owner.Id; }
        Finish(job, "Suppressed");
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return Results.NoContent();
    }
}
