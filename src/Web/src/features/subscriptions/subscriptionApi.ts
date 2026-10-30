import { isPageMetadata, type PageMetadata } from '../../app/pageMetadata'
import { retrySeconds } from '../../app/weeklyHoursApi'

export type Period = { id: string; startsOn: string; endsOn: string; dueOn: string; amountTry: string; paidTry: string; outstandingTry: string; paymentState: 'Paid' | 'Partial' | 'Unpaid'; overdue: boolean }
export type Subscription = PageMetadata & { asOf: string; recorded: boolean; restrictionsApplied: boolean; currentPeriod: Period | null; periods: Period[] }
export class SubscriptionError extends Error {
  constructor(public status: number, public wait = 0) { super('Abonelik bilgileri doğrulanamadı.') }
}
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null
function date(value: unknown): value is string {
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/u.test(value) &&
    Number.isFinite(Date.parse(value + 'T12:00:00Z')) && new Date(value + 'T12:00:00Z').toISOString().slice(0, 10) === value
}
function period(value: unknown): value is Period {
  return record(value) && typeof value.id === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/iu.test(value.id) &&
    date(value.startsOn) && date(value.endsOn) && date(value.dueOn) && value.startsOn < value.endsOn &&
    ['amountTry', 'paidTry', 'outstandingTry'].every(key => typeof value[key] === 'string' && /^(0|[1-9]\d{0,9})\.\d{2}$/u.test(value[key])) &&
    typeof value.paymentState === 'string' && ['Paid', 'Partial', 'Unpaid'].includes(value.paymentState) && typeof value.overdue === 'boolean'
}
export async function readSubscription(response: Response): Promise<Subscription> {
  if (!response.ok) throw new SubscriptionError(response.status, response.status === 429 ? retrySeconds(response) : 0)
  const value: unknown = await response.json()
  const metadata: unknown = value
  if (!record(value) || !isPageMetadata(metadata) || metadata.pageSize !== 12 || !date(value.asOf) ||
    value.timeZone !== 'Europe/Istanbul' || value.currency !== 'TRY' || typeof value.recorded !== 'boolean' ||
    typeof value.restrictionsApplied !== 'boolean' || !(value.currentPeriod === null || period(value.currentPeriod)) ||
    !Array.isArray(value.periods) || !value.periods.every(period) || value.periods.length !== Math.min(12, Math.max(0, metadata.totalCount - (metadata.page - 1) * 12)) ||
    new Set(value.periods.map(p => p.id)).size !== value.periods.length || value.recorded !== (metadata.totalCount > 0) ||
    (value.currentPeriod !== null && !(value.currentPeriod.startsOn <= value.asOf && value.asOf < value.currentPeriod.endsOn))) throw new SubscriptionError(500)
  return { page: metadata.page, pageSize: metadata.pageSize, totalCount: metadata.totalCount, hasMore: metadata.hasMore, asOf: value.asOf, recorded: value.recorded, restrictionsApplied: value.restrictionsApplied, currentPeriod: value.currentPeriod, periods: value.periods }
}
export function subscriptionFailure(status: number) {
  if (status === 400) return 'Dönem sayfası geçersiz. İlk sayfadan yeniden deneyin.'
  if (status === 401) return 'Oturumunuz sona erdi. Yeniden giriş yapın.'
  if (status === 403) return 'Abonelik bilgilerini yalnız işletme sahibi görüntüleyebilir.'
  if (status === 429) return 'Abonelik bilgileri çok sık istendi. Belirtilen süreyi bekleyip yeniden deneyin.'
  return 'Abonelik bilgileri alınamadı. Bağlantınızı kontrol edip yeniden deneyin; güncel tutar doğrulanamadı.'
}
