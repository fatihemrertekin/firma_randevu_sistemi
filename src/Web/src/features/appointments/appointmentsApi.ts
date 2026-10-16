import { isPageMetadata, type PageMetadata } from '../../app/pageMetadata'
export const appointmentEndpoint = '/api/appointments/'
export const statusLabels = { Pending: 'Onay bekliyor', Confirmed: 'Onaylı', Cancelled: 'İptal edildi', Completed: 'Tamamlandı', NoShow: 'Gelmedi' } as const
export type AppointmentStatus = keyof typeof statusLabels
export type AppointmentRow = { id: string; version: string; staffMemberId: string; staffName: string; serviceName: string;
  startsAt: string; endsAt: string; status: AppointmentStatus; customerName: string; pendingExpiresAt: string | null }
export type Appointment = AppointmentRow & { serviceId: string; durationMinutes: number; price: number; currency: 'TRY'; customerPhone: string }
export type AppointmentPage = PageMetadata & { items: AppointmentRow[]; date: string; timeZone: 'Europe/Istanbul' }
export type Availability = { timeZone: 'Europe/Istanbul'; startsAt: string[]; version?: string; serviceVersion?: string }
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null
export const isId = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/iu.test(value) && !/^0{8}(-0{4}){3}-0{12}$/u.test(value)
const timestamp = (value: unknown): value is string => typeof value === 'string' && /(?:Z|[+-]\d{2}:\d{2})$/u.test(value) && Number.isFinite(Date.parse(value))
export function isDay(value: string) {
  const parsed = new Date(value + 'T12:00:00Z')
  return /^\d{4}-\d{2}-\d{2}$/u.test(value) && value > '0001-01-01' && value < '9999-12-31' &&
    Number.isFinite(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value
}
export function businessDay(value = new Date()) {
  return new Intl.DateTimeFormat('sv-SE', { timeZone: 'Europe/Istanbul', year: 'numeric', month: '2-digit', day: '2-digit' }).format(value)
}
export function nextDay(day: string, offset: number) {
  const next = new Date(day + 'T12:00:00Z'); next.setUTCDate(next.getUTCDate() + offset)
  const value = next.toISOString().slice(0, 10)
  return isDay(value) ? value : day
}
const time = new Intl.DateTimeFormat('tr-TR', { timeZone: 'Europe/Istanbul', hour: '2-digit', minute: '2-digit' })
export const formatTime = (value: string) => time.format(new Date(value)).replace(':', '.')
export const formatDay = (value: string) => new Intl.DateTimeFormat('tr-TR', { timeZone: 'Europe/Istanbul', dateStyle: 'full' }).format(new Date(value + 'T12:00:00Z'))
export class AppointmentError extends Error {
  constructor(public status: number, public fields: Record<string, string> = {}, public retryAfter = 0) {
    super(status === 400 ? 'Alanları veya istek doğrulamasını kontrol edip yeniden deneyin.' : status === 401 ? 'Oturumunuz sona erdi. Yeniden giriş yapın.'
      : status === 403 ? 'Bu işlem için yetkiniz yok.' : status === 404 ? 'Randevu veya seçili tanım bulunamadı. Güncel bilgileri yükleyin.'
        : status === 409 ? 'Bilgiler veya uygun saatler değişti. Taslağınız korundu; güncel bilgileri yükleyin.'
          : status === 429 ? 'Çok sık denendi. Bir süre bekleyip yeniden deneyin.' : 'Sonuç doğrulanamadı. Bağlantınızı kontrol edip yeniden deneyin.')
  }
}
async function body(response: Response): Promise<unknown> {
  if (!response.ok) {
    const fields: Record<string, string> = {}
    if (response.status === 400) {
      const problem: unknown = await response.json().catch(() => null)
      if (record(problem) && record(problem.errors)) for (const [key, messages] of Object.entries(problem.errors)) {
        if (['serviceId', 'staffMemberId', 'startsAt', 'customerName', 'customerPhone'].includes(key) && Array.isArray(messages) && typeof messages[0] === 'string') fields[key] = messages[0]
      }
    }
    const retry = response.headers.get('Retry-After'), until = retry && /^\d+$/u.test(retry) ? Date.now() + Number(retry) * 1000 : Date.parse(retry ?? '')
    throw new AppointmentError(response.status, fields, Number.isFinite(until) ? Math.min(Date.now() + 86400000, Math.max(Date.now(), until)) : 0)
  }
  return response.json()
}
function isRow(value: unknown): value is AppointmentRow {
  return record(value) && isId(value.id) && isId(value.version) && isId(value.staffMemberId) && typeof value.staffName === 'string' &&
    typeof value.serviceName === 'string' && typeof value.customerName === 'string' && timestamp(value.startsAt) && timestamp(value.endsAt) &&
    Date.parse(value.endsAt) > Date.parse(value.startsAt) && typeof value.status === 'string' && Object.hasOwn(statusLabels, value.status) &&
    (value.pendingExpiresAt === null || timestamp(value.pendingExpiresAt))
}
export async function readAppointmentPage(response: Response): Promise<AppointmentPage> {
  const value = await body(response)
  if (!isPageMetadata(value)) throw new AppointmentError(500)
  if (!('items' in value) || !Array.isArray(value.items) || !value.items.every(isRow) ||
    !('date' in value) || typeof value.date !== 'string' || !isDay(value.date) || !('timeZone' in value) || value.timeZone !== 'Europe/Istanbul' ||
    value.items.length > value.pageSize) throw new AppointmentError(500)
  return { ...value, items: value.items, date: value.date, timeZone: value.timeZone }
}
export async function readAppointment(response: Response): Promise<Appointment> {
  const value = await body(response)
  if (!isRow(value) || !('serviceId' in value) || !isId(value.serviceId) || !('durationMinutes' in value) || typeof value.durationMinutes !== 'number' || !Number.isInteger(value.durationMinutes) || value.durationMinutes < 1 ||
    !('price' in value) || typeof value.price !== 'number' || !Number.isFinite(value.price) || value.price < 0 || !('currency' in value) || value.currency !== 'TRY' ||
    !('customerPhone' in value) || typeof value.customerPhone !== 'string') throw new AppointmentError(500)
  return { ...value, serviceId: value.serviceId, durationMinutes: value.durationMinutes, price: value.price, currency: value.currency, customerPhone: value.customerPhone }
}
export async function readAvailability(response: Response, moving: boolean): Promise<Availability> {
  const value = await body(response)
  if (!record(value) || value.timeZone !== 'Europe/Istanbul' || !Array.isArray(value.startsAt) || !value.startsAt.every(timestamp) || value.startsAt.length > 1440 ||
    !(moving ? isId(value.version) : isId(value.serviceVersion))) throw new AppointmentError(500)
  return { timeZone: value.timeZone, startsAt: value.startsAt, version: moving ? String(value.version) : undefined, serviceVersion: moving ? undefined : String(value.serviceVersion) }
}
export const signal = (controller: AbortController) => AbortSignal.any([controller.signal, AbortSignal.timeout(15000)])
export function appointmentSearch(day: string, staff = '', page = 1, size = 20) {
  const query = new URLSearchParams({ tarih: day })
  if (staff) query.set('calisan', staff)
  if (page > 1) query.set('sayfa', String(page))
  if (size !== 20) query.set('boyut', String(size))
  return '?' + query
}
