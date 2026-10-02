namespace Server.Infrastructure;

// Counts all SMTP attempts, including retries, across restarts. No addresses or message bodies.
public sealed class IdentityEmailQuota
{
    public DateOnly Day { get; set; }
    public int Attempts { get; set; }
}
