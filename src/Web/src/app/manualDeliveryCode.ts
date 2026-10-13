export function isManualDeliveryCode(value: unknown): value is { token: string; expiresAt: string } {
  return typeof value === 'object' && value !== null &&
    'token' in value && typeof value.token === 'string' && value.token.trim().length > 0 && value.token.length <= 8192 &&
    'expiresAt' in value && typeof value.expiresAt === 'string' && Number.isFinite(Date.parse(value.expiresAt))
}
