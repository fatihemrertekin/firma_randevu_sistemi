export const auditEndpoint = '/api/audit-log/'
export type Category = 'all' | 'definitions' | 'security'
export type AuditEntry = { id: string; occurredAt: string; module: string; action: string; actor: string; target: string }
export type AuditPage = { items: AuditEntry[]; category: Category; timeZone: 'Europe/Istanbul'; cursor: string; nextCursor: string | null }
export class AuditError extends Error {
  constructor(public status: number, public wait = 0) { super('Kayıt yanıtı doğrulanamadı.') }
}
function isEntry(value: unknown): value is AuditEntry {
  if (typeof value !== 'object' || value === null) return false
  const fields = value as Record<string, unknown>
  return 'id' in value && typeof value.id === 'string' && /^(?:[1-9]|1[0-2]):[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/iu.test(value.id) &&
    'occurredAt' in value && typeof value.occurredAt === 'string' && /^\d{4}-\d{2}-\d{2}T.*(?:Z|\+00:00)$/u.test(value.occurredAt) && Number.isFinite(Date.parse(value.occurredAt)) &&
    ['module', 'action', 'actor', 'target'].every(key => { const field = fields[key]; return typeof field === 'string' && field.length > 0 && field.length <= 512 })
}
export async function readAuditPage(response: Response, category: Category): Promise<AuditPage> {
  if (!response.ok) {
    const retry = Number(response.headers.get('Retry-After'))
    throw new AuditError(response.status, response.status === 429 ? Number.isFinite(retry) && retry > 0 ? Math.min(120, Math.ceil(retry)) : 60 : 0)
  }
  const body: unknown = await response.json()
  if (typeof body !== 'object' || body === null || !('items' in body) || !Array.isArray(body.items) || body.items.length > 20 || !body.items.every(isEntry) ||
    new Set(body.items.map(item => item.id)).size !== body.items.length || !('category' in body) || body.category !== category ||
    !('timeZone' in body) || body.timeZone !== 'Europe/Istanbul' || !('cursor' in body) || typeof body.cursor !== 'string' || !/^[A-Za-z0-9_-]{1,512}$/u.test(body.cursor) || !('nextCursor' in body) ||
    !(body.nextCursor === null || typeof body.nextCursor === 'string' && /^[A-Za-z0-9_-]{1,512}$/u.test(body.nextCursor) && body.nextCursor !== body.cursor && body.items.length === 20)) throw new AuditError(500)
  return { items: body.items, category, timeZone: 'Europe/Istanbul', cursor: body.cursor, nextCursor: body.nextCursor }
}
export function auditFailure(status: number) {
  if (status === 400) return 'Sayfa bilgisi geçersiz. Listeyi yenile.'
  if (status === 401) return 'Oturumun sona erdi. Yeniden giriş yap.'
  if (status === 403) return 'Bu işlem için yetkin yok.'
  if (status === 429) return 'Çok sık denendi. Bekleyip yeniden dene.'
  return 'Kayıtlar alınamadı. Listeyi yenileyerek yeniden dene.'
}
