namespace Server.Features.Audit;

internal static class AuditLogQuery
{
    internal sealed class Row
    {
        public Guid Id { get; set; }
        public int Source { get; set; }
        public required string Kind { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
        public required string Actor { get; set; }
        public required string Target { get; set; }
    }

    // Yalnız mevcut audit alanları: sır, kod, grant, operator/request referansı veya eski/yeni değer okunmaz.
    // Önce sayfa sınırı uygulanır; görünen hesap/hedefler tek sorguda güncel adlarla eşleştirilir.
    internal const string Sql = """
        WITH events AS (
          SELECT "Id", "OccurredAt", 1 AS "Source", '' AS "Kind", "ActorId", NULL::uuid AS "TargetId", NULL::uuid AS "ExtraId" FROM "BusinessProfileAudits"
          UNION ALL SELECT "Id", "OccurredAt", 2, '', "ActorId", NULL::uuid, NULL::uuid FROM "BusinessLogoAudits"
          UNION ALL SELECT "Id", "OccurredAt", 3, '', "ActorId", NULL::uuid, NULL::uuid FROM "BusinessHoursAudits"
          UNION ALL SELECT "Id", "OccurredAt", 4, "Kind", "ActorId", "StaffMemberId", NULL::uuid FROM "StaffMemberAudits"
          UNION ALL SELECT "Id", "OccurredAt", 5, "Kind", "ActorId", "ServiceDefinitionId", NULL::uuid FROM "ServiceDefinitionAudits"
          UNION ALL SELECT "Id", "OccurredAt", 6, "Kind", "ActorId", "StaffMemberId", "ServiceDefinitionId" FROM "StaffServiceAssignmentAudits"
          UNION ALL SELECT "Id", "OccurredAt", 7, '', "ActorId", "StaffMemberId", NULL::uuid FROM "StaffHoursAudits"
          UNION ALL SELECT "Id", "OccurredAt", 8, "Kind", "ActorId", "InvitationId", NULL::uuid FROM "StaffInvitationAudits"
          UNION ALL SELECT "Id", "OccurredAt", 9, "Kind", "ActorId", "StaffId", NULL::uuid FROM "StaffPasswordResetAudits"
          UNION ALL SELECT "Id", "OccurredAt", 10, '', "ActorId", "StaffId", NULL::uuid FROM "StaffDeactivationAudits"
          UNION ALL SELECT "Id", "OccurredAt", 11, "Kind", CASE WHEN "Kind" IN ('SelfIssued','Completed') THEN "OwnerId" ELSE NULL::uuid END, "OwnerId", NULL::uuid FROM "OwnerPasswordResetAudits"
          UNION ALL SELECT "Id", "OccurredAt", 12, '', NULL::uuid, "OwnerId", NULL::uuid FROM "OwnerMfaRecoveryAudits"
        ), page AS (
          SELECT * FROM events
          WHERE "OccurredAt" <= @asOf
            AND (@category = 'all' OR (@category = 'definitions' AND "Source" <= 7) OR (@category = 'security' AND "Source" >= 8))
            AND (NOT @hasCursor OR ("OccurredAt", "Source", "Id") < (@beforeAt, @source, @id))
          ORDER BY "OccurredAt" DESC, "Source" DESC, "Id" DESC LIMIT @limit
        )
        SELECT p."Id", p."OccurredAt", p."Source", p."Kind",
          CASE WHEN p."ActorId" IS NULL THEN 'Yerel bakım' ELSE COALESCE(actor."Email", 'Hesap kaydı') END AS "Actor",
          CASE WHEN p."Source" <= 3 THEN 'İşletme'
               WHEN p."Source" = 6 THEN COALESCE(member."Name", 'Personel kaydı') || ' · ' || COALESCE(service."Name", 'Hizmet kaydı')
               WHEN p."Source" IN (4,7) THEN COALESCE(member."Name", 'Personel kaydı')
               WHEN p."Source" = 5 THEN COALESCE(service."Name", 'Hizmet kaydı')
               WHEN p."Source" = 8 THEN COALESCE(invitation."Email", 'Davet kaydı')
               ELSE COALESCE(target."Email", 'Hesap kaydı') END AS "Target"
        FROM page p
        LEFT JOIN "AspNetUsers" actor ON actor."Id" = p."ActorId"
        LEFT JOIN "StaffMembers" member ON p."Source" IN (4,6,7) AND member."Id" = p."TargetId"
        LEFT JOIN "ServiceDefinitions" service ON (p."Source" = 5 AND service."Id" = p."TargetId") OR (p."Source" = 6 AND service."Id" = p."ExtraId")
        LEFT JOIN "StaffInvitations" invitation ON p."Source" = 8 AND invitation."Id" = p."TargetId"
        LEFT JOIN "AspNetUsers" target ON p."Source" >= 9 AND target."Id" = p."TargetId"
        ORDER BY p."OccurredAt" DESC, p."Source" DESC, p."Id" DESC
        """;
}
