import type { postWithCsrf } from '../../app/api'
export type Service = { id: string; name: string; durationMinutes: number; price: string; currency: 'TRY'; isActive: boolean; version: string }
export type ServicePage = { items: Service[]; page: number; hasMore: boolean }
export type ServicePost = typeof postWithCsrf
export class ServiceRequestError extends Error {
  constructor(public status: number, message = serviceFailure(status)) { super(message) }
}
export function serviceFailure(status: number) {
  if (status === 400) return 'Hizmet alanlarını kontrol edip yeniden deneyin.'
  if (status === 401) return 'Oturumunuz sona erdi. Yeniden giriş yapın.'
  if (status === 403) return 'Bu işlem için yetkiniz yok.'
  if (status === 404) return 'Hizmet kaydı bulunamadı. Listeyi yenileyin.'
  if (status === 409) return 'Hizmet kaydı değişti. Güncel kaydı yükleyip yeniden düzenleyin.'
  if (status === 429) return 'Çok sık denendi. Bir süre bekleyip yeniden deneyin.'
  return 'Sonuç doğrulanamadı. Güncel kaydı veya listeyi yükleyerek kontrol edin.'
}
function isService(value: unknown): value is Service {
  return typeof value === 'object' && value !== null && 'id' in value && typeof value.id === 'string' &&
    'name' in value && typeof value.name === 'string' && 'durationMinutes' in value && typeof value.durationMinutes === 'number' &&
    Number.isInteger(value.durationMinutes) && value.durationMinutes >= 1 && value.durationMinutes <= 1440 &&
    'price' in value && typeof value.price === 'string' && /^[0-9]{1,6}\.[0-9]{2}$/u.test(value.price) &&
    'currency' in value && value.currency === 'TRY' && 'isActive' in value && typeof value.isActive === 'boolean' &&
    'version' in value && typeof value.version === 'string'
}
export async function readService(response: Response): Promise<Service> {
  if (!response.ok) throw new ServiceRequestError(response.status)
  const value: unknown = await response.json()
  if (!isService(value)) throw new ServiceRequestError(500)
  return value
}
export async function readServicePage(response: Response): Promise<ServicePage> {
  if (!response.ok) throw new ServiceRequestError(response.status)
  const value: unknown = await response.json()
  if (typeof value !== 'object' || value === null || !('items' in value) || !Array.isArray(value.items) ||
    !value.items.every(isService) || !('page' in value) || typeof value.page !== 'number' || !Number.isInteger(value.page) ||
    !('hasMore' in value) || typeof value.hasMore !== 'boolean') throw new ServiceRequestError(500, 'Liste yanıtı doğrulanamadı. Yeniden yükleyin.')
  return { items: value.items, page: value.page, hasMore: value.hasMore }
}
export async function readFieldErrors(response: Response): Promise<Record<string, string>> {
  const value: unknown = await response.json()
  const errors: Record<string, string> = {}
  if (typeof value === 'object' && value !== null && 'errors' in value && typeof value.errors === 'object' && value.errors !== null) {
    for (const [key, messages] of Object.entries(value.errors)) {
      if (['name', 'durationMinutes', 'price'].includes(key) && Array.isArray(messages) && typeof messages[0] === 'string') errors[key] = messages[0]
    }
  }
  return errors
}
