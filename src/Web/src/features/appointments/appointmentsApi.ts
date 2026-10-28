import { isPageMetadata, type PageMetadata } from '../../app/pageMetadata'
export const appointmentEndpoint = '/api/appointments/'
export const statusLabels = { Pending: 'Onay bekliyor', Confirmed: 'Onaylı', Cancelled: 'İptal edildi', Completed: 'Tamamlandı', NoShow: 'Gelmedi' } as const
export type AppointmentStatus = keyof typeof statusLabels
export type AppointmentRow = { id: string; version: string; staffMemberId: string; staffName: string; serviceName: string;
  startsAt: string; endsAt: string; status: AppointmentStatus; customerName: string; pendingExpiresAt: string | null }
export type Appointment = AppointmentRow & { serviceId: string; durationMinutes: number; price: number; currency: 'TRY'; customerPhone: string }
export type Reminder = { appointmentVersion: string; suppressed: boolean; hasStarted: boolean; scheduledFor: string | null; dispatchEnabled: boolean }
export type AppointmentPage = PageMetadata & { items: AppointmentRow[]; date: string; timeZone: 'Europe/Istanbul' }
export type AppointmentWeekPage = AppointmentPage & { weekStart: string; weekEndExclusive: string; days: { date: string; totalCount: number }[] }
export type Availability = { timeZone: 'Europe/Istanbul'; startsAt: string[]; unavailableReasons: { code: string; message: string }[]; version?: string; serviceVersion?: string }
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null
const isUnavailableReason = (value: unknown): value is Availability['unavailableReasons'][number] => record(value) && typeof value.code === 'string' && /^[A-Za-z]{1,40}$/u.test(value.code) &&
  typeof value.message === 'string' && value.message.trim().length > 0 && value.message.length <= 500
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
export function weekDates(day: string): string[] {
  if (!isDay(day)) return []
  const value = new Date(day + 'T12:00:00Z'), offset = (value.getUTCDay() + 6) % 7
  value.setUTCDate(value.getUTCDate() - offset)
  const start = value.toISOString().slice(0, 10)
  if (!isDay(start)) return []
  const days = Array.from({ length: 7 }, (_, index) => nextDay(start, index))
  return days.every((date, index) => index === 0 || date > days[index - 1]) && nextDay(start, 7) > days[6] ? days : []
}
const time = new Intl.DateTimeFormat('tr-TR', { timeZone: 'Europe/Istanbul', hour: '2-digit', minute: '2-digit' })
export const formatTime = (value: string) => time.format(new Date(value)).replace(':', '.')
export const formatDay = (value: string) => new Intl.DateTimeFormat('tr-TR', { timeZone: 'Europe/Istanbul', dateStyle: 'full' }).format(new Date(value + 'T12:00:00Z'))
export function formatWeek(day: string) {
  const dates = weekDates(day)
  if (dates.length !== 7) return 'Hafta'
  const first = new Intl.DateTimeFormat('tr-TR', { timeZone: 'Europe/Istanbul', day: 'numeric', month: 'long' })
  const last = new Intl.DateTimeFormat('tr-TR', { timeZone: 'Europe/Istanbul', day: 'numeric', month: 'long', year: 'numeric' })
  return first.format(new Date(dates[0] + 'T12:00:00Z')) + ' – ' + last.format(new Date(dates[6] + 'T12:00:00Z'))
}
export class AppointmentError extends Error {
  constructor(public status: number, public fields: Record<string, string> = {}, public retryAfter = 0, message?: string) {
    super(message ?? (status === 400 ? (Object.keys(fields).length ? 'İşaretli alanları kontrol edip yeniden deneyin.' : 'İstek doğrulanamadı. Bilgilerinizi koruyarak yeniden deneyin.') : status === 401 ? 'Oturumunuz sona erdi. Yeniden giriş yapın.'
      : status === 403 ? 'Bu işlem için yetkiniz yok.' : status === 404 ? 'Randevu veya seçili tanım bulunamadı. Güncel bilgileri yükleyin.'
        : status === 409 ? 'Bilgiler veya uygun saatler değişti. Taslağınız korundu; güncel bilgileri yükleyin.'
          : status === 429 ? 'Çok sık denendi. Bir süre bekleyip yeniden deneyin.' : 'Sonuç doğrulanamadı. Bağlantınızı kontrol edip yeniden deneyin.'))
  }
}
async function body(response: Response): Promise<unknown> {
  if (!response.ok) {
    const fields: Record<string, string> = {}
    if (response.status === 400) {
      const problem: unknown = await response.json().catch(() => null)
      if (record(problem) && record(problem.errors)) for (const [key, messages] of Object.entries(problem.errors)) {
        if (['serviceId', 'staffMemberId', 'startsAt', 'customerName', 'customerPhone', 'date'].includes(key) && Array.isArray(messages) && typeof messages[0] === 'string') fields[key] = messages[0]
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
function appointmentPage(value: unknown): AppointmentPage {
  if (!isPageMetadata(value)) throw new AppointmentError(500)
  if (!('items' in value) || !Array.isArray(value.items) || !value.items.every(isRow) ||
    !('date' in value) || typeof value.date !== 'string' || !isDay(value.date) || !('timeZone' in value) || value.timeZone !== 'Europe/Istanbul' ||
    value.items.length > value.pageSize) throw new AppointmentError(500)
  return { ...value, items: value.items, date: value.date, timeZone: value.timeZone }
}
export async function readAppointmentPage(response: Response): Promise<AppointmentPage> {
  return appointmentPage(await body(response))
}
export async function readAppointmentWorkbook(response: Response): Promise<Blob> {
  if (!response.ok) await body(response)
  if (response.headers.get('Content-Type')?.split(';')[0] !== 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet')
    throw new AppointmentError(500)
  const file = await response.blob()
  if (file.size < 4 || file.size > 8 * 1024 * 1024) throw new AppointmentError(500)
  const signature = new Uint8Array(await file.slice(0, 4).arrayBuffer())
  if (!signature.every((byte, index) => byte === [80, 75, 3, 4][index])) throw new AppointmentError(500)
  return file
}
export async function readAppointmentWeekPage(response: Response): Promise<AppointmentWeekPage> {
  const value = await body(response), page = appointmentPage(value), dates = weekDates(page.date)
  if (!record(value) || dates.length !== 7 || value.weekStart !== dates[0] || value.weekEndExclusive !== nextDay(dates[0], 7) ||
    !Array.isArray(value.days) || value.days.length !== 7) throw new AppointmentError(500)
  const days = value.days.map((day: unknown, index) => {
    if (!record(day) || day.date !== dates[index] || typeof day.totalCount !== 'number' || !Number.isSafeInteger(day.totalCount) || day.totalCount < 0)
      throw new AppointmentError(500)
    return { date: dates[index], totalCount: day.totalCount }
  })
  if (days.find(day => day.date === page.date)?.totalCount !== page.totalCount) throw new AppointmentError(500)
  return { ...page, weekStart: dates[0], weekEndExclusive: nextDay(dates[0], 7), days }
}
export async function readAppointment(response: Response): Promise<Appointment> {
  const value = await body(response)
  if (!isRow(value) || !('serviceId' in value) || !isId(value.serviceId) || !('durationMinutes' in value) || typeof value.durationMinutes !== 'number' || !Number.isInteger(value.durationMinutes) || value.durationMinutes < 1 ||
    !('price' in value) || typeof value.price !== 'number' || !Number.isFinite(value.price) || value.price < 0 || !('currency' in value) || value.currency !== 'TRY' ||
    !('customerPhone' in value) || typeof value.customerPhone !== 'string') throw new AppointmentError(500)
  return { ...value, serviceId: value.serviceId, durationMinutes: value.durationMinutes, price: value.price, currency: value.currency, customerPhone: value.customerPhone }
}
export async function readReminder(response: Response): Promise<Reminder> {
  const value = await body(response)
  if (!record(value) || !isId(value.appointmentVersion) || typeof value.suppressed !== 'boolean' || typeof value.hasStarted !== 'boolean' ||
    typeof value.dispatchEnabled !== 'boolean' || !(value.scheduledFor === null || timestamp(value.scheduledFor)) ||
    value.suppressed && value.scheduledFor !== null) throw new AppointmentError(500)
  return { appointmentVersion: value.appointmentVersion, suppressed: value.suppressed, hasStarted: value.hasStarted,
    scheduledFor: value.scheduledFor, dispatchEnabled: value.dispatchEnabled }
}
export async function readReminderDisabled(response: Response): Promise<void> {
  if (!response.ok) await body(response)
  if (response.status !== 204) throw new AppointmentError(500)
}
export async function readAvailability(response: Response, moving: boolean): Promise<Availability> {
  const value = await body(response)
  if (!record(value) || value.timeZone !== 'Europe/Istanbul' || !Array.isArray(value.startsAt) || !value.startsAt.every(timestamp) || value.startsAt.length > 1440 ||
    !Array.isArray(value.unavailableReasons) || value.unavailableReasons.length > 8 || !value.unavailableReasons.every(isUnavailableReason) ||
    (value.startsAt.length === 0 ? value.unavailableReasons.length === 0 : value.unavailableReasons.length > 0) ||
    !(moving ? isId(value.version) : isId(value.serviceVersion))) throw new AppointmentError(500)
  return { timeZone: value.timeZone, startsAt: value.startsAt, unavailableReasons: value.unavailableReasons, version: moving ? String(value.version) : undefined, serviceVersion: moving ? undefined : String(value.serviceVersion) }
}
export const signal = (controller: AbortController) => AbortSignal.any([controller.signal, AbortSignal.timeout(15000)])
export function appointmentSearch(day: string, staff = '', page = 1, size = 20, weekly = false) {
  const query = new URLSearchParams({ tarih: day })
  if (weekly) query.set('gorunum', 'hafta')
  if (staff) query.set('calisan', staff)
  if (page > 1) query.set('sayfa', String(page))
  if (size !== (weekly ? 10 : 20)) query.set('boyut', String(size))
  return '?' + query
}
