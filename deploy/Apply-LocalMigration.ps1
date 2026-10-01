$ErrorActionPreference = 'Stop'

Set-Location (Join-Path $PSScriptRoot '..')
$env:ASPNETCORE_ENVIRONMENT = 'Development'

$migrationSql = dotnet tool run dotnet-ef migrations script --idempotent --no-build --project src/Server --startup-project src/Server
if ($LASTEXITCODE -ne 0) {
    throw 'Migration SQL üretilemedi.'
}

$migrationSql | docker compose --env-file deploy/.env -f deploy/compose.local.yaml exec -T db psql -U postgres -d firma_randevu -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) {
    throw 'Migration uygulanamadı.'
}

@'
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE
  "AspNetRoles", "AspNetRoleClaims", "AspNetUsers", "AspNetUserClaims",
  "AspNetUserLogins", "AspNetUserRoles", "AspNetUserTokens" TO app_user;
GRANT SELECT, INSERT ON TABLE "OwnerMfaRecoveryAudits" TO app_user;
REVOKE UPDATE, DELETE, TRUNCATE ON TABLE "OwnerMfaRecoveryAudits" FROM app_user;
GRANT SELECT, INSERT ON TABLE "OwnerPasswordResetAudits" TO app_user;
REVOKE UPDATE, DELETE, TRUNCATE ON TABLE "OwnerPasswordResetAudits" FROM app_user;
GRANT SELECT, INSERT, UPDATE ON TABLE "StaffInvitations" TO app_user;
REVOKE DELETE, TRUNCATE ON TABLE "StaffInvitations" FROM app_user;
GRANT SELECT, INSERT ON TABLE "StaffInvitationAudits" TO app_user;
REVOKE UPDATE, DELETE, TRUNCATE ON TABLE "StaffInvitationAudits" FROM app_user;
GRANT SELECT, INSERT ON TABLE "StaffPasswordResetAudits" TO app_user;
REVOKE UPDATE, DELETE, TRUNCATE ON TABLE "StaffPasswordResetAudits" FROM app_user;
GRANT SELECT, UPDATE ON TABLE "BusinessProfiles" TO app_user;
REVOKE INSERT, DELETE, TRUNCATE ON TABLE "BusinessProfiles" FROM app_user;
GRANT SELECT, INSERT ON TABLE "BusinessProfileAudits" TO app_user;
REVOKE UPDATE, DELETE, TRUNCATE ON TABLE "BusinessProfileAudits" FROM app_user;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO app_user;
'@ | docker compose --env-file deploy/.env -f deploy/compose.local.yaml exec -T db psql -U postgres -d firma_randevu -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) {
    throw 'Uygulama rolü yetkileri verilemedi.'
}
