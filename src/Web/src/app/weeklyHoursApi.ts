import type { postWithCsrf } from './api'

export const weekDays = ['Pazartesi', 'Salı', 'Çarşamba', 'Perşembe', 'Cuma', 'Cumartesi', 'Pazar'] as const
export type OpeningDay = { day: number; isClosed: boolean; opensAt: string | null; closesAt: string | null }
export type BusinessHours = { isConfigured: boolean; timeZone: 'Europe/Istanbul'; version: string; days: OpeningDay[] }
export type HoursPost = typeof postWithCsrf
const hour = /^(?:[01][0-9]|2[0-3]):[0-5][0-9]$/u
export function validHour(value: string | null): value is string { return value !== null && hour.test(value) }
export function hoursFailure(status: number, subject = 'İşletme saatleri') {
  if (status === 400) return 'Günleri ve saat alanlarını kontrol edin.'
  if (status === 401) return 'Oturumunuz sona erdi. Yeniden giriş yapın.'
  if (status === 403) return 'Bu işlem için yetkiniz yok.'
  if (status === 404) return 'Personel kaydı bulunamadı. Listeye dönüp yenileyin.'
  if (status === 409) return `${subject} veya ilgili kayıt başka bir işlemde değişti. Güncel saatleri yükleyin.`
  if (status === 429) return 'Çok sık denendi. Bir süre bekleyip yeniden deneyin.'
  return 'Sonuç doğrulanamadı. Güncel saatleri yükleyerek kontrol edin.'
}
export class HoursError extends Error {
  constructor(public status: number, public retryAfter = 0) { super(hoursFailure(status)) }
}
export function retrySeconds(response: Response) {
  const value = Number(response.headers.get('Retry-After'))
  return Number.isFinite(value) && value > 0 ? Math.min(120, Math.ceil(value)) : 60
}
function isDay(value: unknown): value is OpeningDay {
  if (typeof value !== 'object' || value === null || !('day' in value) || typeof value.day !== 'number' ||
    !Number.isInteger(value.day) || value.day < 0 || value.day > 6 || !('isClosed' in value) || typeof value.isClosed !== 'boolean' ||
    !('opensAt' in value) || !('closesAt' in value)) return false
  return value.isClosed ? value.opensAt === null && value.closesAt === null :
    typeof value.opensAt === 'string' && typeof value.closesAt === 'string' && validHour(value.opensAt) && validHour(value.closesAt) && value.opensAt < value.closesAt
}
export function parseHours(value: unknown): BusinessHours {
  if (typeof value !== 'object' || value === null || !('isConfigured' in value) || typeof value.isConfigured !== 'boolean' ||
    !('timeZone' in value) || value.timeZone !== 'Europe/Istanbul' || !('version' in value) || typeof value.version !== 'string' ||
    !/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/iu.test(value.version) || !('days' in value) || !Array.isArray(value.days) ||
    !value.days.every(isDay) || value.days.length !== (value.isConfigured ? 7 : 0) || new Set(value.days.map(item => item.day)).size !== value.days.length) throw new HoursError(500)
  return { isConfigured: value.isConfigured, timeZone: value.timeZone, version: value.version, days: [...value.days].sort((a, b) => a.day - b.day) }
}
export async function readHours(response: Response): Promise<BusinessHours> {
  if (!response.ok) throw new HoursError(response.status, response.status === 429 ? retrySeconds(response) : 0)
  return parseHours(await response.json())
}
export async function hoursFieldErrors(response: Response): Promise<Record<string, string>> {
  const value: unknown = await response.json(), errors: Record<string, string> = {}
  if (typeof value === 'object' && value !== null && 'errors' in value && typeof value.errors === 'object' && value.errors !== null) {
    for (const [key, messages] of Object.entries(value.errors)) {
      if (/^(?:days|day[0-6](?:Closed|OpensAt|ClosesAt))$/u.test(key) && Array.isArray(messages) && typeof messages[0] === 'string') errors[key] = messages[0]
    }
  }
  return errors
}
export function hoursDraft(hours: BusinessHours): OpeningDay[] {
  return hours.isConfigured ? hours.days.map(item => ({ ...item })) : weekDays.map((_, day) => ({ day, isClosed: true, opensAt: null, closesAt: null }))
}
