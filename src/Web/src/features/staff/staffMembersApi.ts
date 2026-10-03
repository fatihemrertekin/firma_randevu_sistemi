export type StaffMember = { id: string; name: string; isActive: boolean; version: string }
export type StaffMemberPage = { items: StaffMember[]; page: number; hasMore: boolean }
export type StaffPost = (path: string, body: object, signal?: AbortSignal) => Promise<Response>

export class MemberRequestError extends Error {
  constructor(public status: number, message = memberFailure(status)) { super(message) }
}
export function memberFailure(status: number) {
  if (status === 400) return 'İstek doğrulanamadı. Ad soyadı kontrol edip yeniden deneyin.'
  if (status === 401) return 'Oturumunuz sona erdi. Yeniden giriş yapın.'
  if (status === 403) return 'Bu işlem için yetkiniz yok.'
  if (status === 404) return 'Personel kaydı bulunamadı. Listeyi yenileyin.'
  if (status === 409) return 'Personel kaydı değişti. Güncel kaydı yükleyip yeniden düzenleyin.'
  if (status === 429) return 'Çok sık denendi. Bir süre bekleyip yeniden deneyin.'
  return 'Sonuç doğrulanamadı. Güncel kaydı veya listeyi yükleyerek kontrol edin.'
}
export function isMember(value: unknown): value is StaffMember {
  return typeof value === 'object' && value !== null &&
    'id' in value && typeof value.id === 'string' && 'name' in value && typeof value.name === 'string' &&
    'isActive' in value && typeof value.isActive === 'boolean' && 'version' in value && typeof value.version === 'string'
}
export async function readMember(response: Response): Promise<StaffMember> {
  if (!response.ok) throw new MemberRequestError(response.status)
  const value: unknown = await response.json()
  if (!isMember(value)) throw new MemberRequestError(500)
  return value
}
export async function readMemberPage(response: Response): Promise<StaffMemberPage> {
  if (!response.ok) throw new MemberRequestError(response.status)
  const value: unknown = await response.json()
  if (typeof value !== 'object' || value === null || !('items' in value) || !Array.isArray(value.items) ||
    !value.items.every(isMember) || !('page' in value) || !Number.isInteger(value.page) || typeof value.page !== 'number' ||
    !('hasMore' in value) || typeof value.hasMore !== 'boolean') throw new MemberRequestError(500, 'Liste yanıtı doğrulanamadı. Yeniden yükleyin.')
  return { items: value.items, page: value.page, hasMore: value.hasMore }
}
