export type Period = { id: string; startsOn: string; endsOn: string; dueOn: string; version: string; amountTry: string; paidTry: string; outstandingTry: string; overdue: boolean }
export type Payment = { id: string; paidOn: string; recordedAt: string; amountTry: string; reference: string | null; reason: string | null; reversed: boolean }
export type Summary = { recorded: boolean; latestStartedPeriod: { id: string; startsOn: string; endsOn: string; dueOn: string; amountTry: string; paidTry: string; outstandingTry: string; paymentState: 'Paid' | 'Partial' | 'Unpaid' } | null; overdueOutstandingTry: string; overduePeriodCount: number; restrictionsApplied: boolean }
export type Data = { targetId: string; targetName: string; asOf: string; page: number; total: number; selectedPeriodId: string | null; periods: Period[]; payments: Payment[]; paymentsPage: number; paymentCount: number; summary?: Summary }
export type Target = { id: string; name: string; status: 'available' | 'unavailable'; summary: Summary | null }
export type TargetPage = { page: number; pageSize: number; total: number; checkedAt: string; asOf: string; filterScope: 'page'; items: Target[] }
export class SessionExpired extends Error {}
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null
export const validDate = (v: unknown) => typeof v === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(v) && Number.isFinite(Date.parse(v + 'T12:00:00Z'))
const validMoney = (v: unknown) => typeof v === 'string' && /^-?\d{1,28}\.\d{2}$/.test(v)
function validSummary(v: unknown): v is Summary {
  if (!record(v) || typeof v.recorded !== 'boolean' || !validMoney(v.overdueOutstandingTry) || !Number.isInteger(v.overduePeriodCount) || typeof v.restrictionsApplied !== 'boolean') return false
  const p = v.latestStartedPeriod
  return p === null || record(p) && typeof p.id === 'string' && ['startsOn', 'endsOn', 'dueOn'].every(k => validDate(p[k])) &&
    ['amountTry', 'paidTry', 'outstandingTry'].every(k => validMoney(p[k])) && ['Paid', 'Partial', 'Unpaid'].includes(String(p.paymentState))
}
export const ledgerPath = (targetId?: string) => targetId ? `/api/targets/${encodeURIComponent(targetId)}/subscription` : '/api/subscription'
async function read(path: string, signal = AbortSignal.timeout(20000)): Promise<unknown> {
  let response: Response
  try { response = await fetch(path, { cache: 'no-store', signal }) }
  catch { throw new Error('Firma kayıtları okunamadı. Bağlantıyı kontrol edip yeniden deneyin.') }
  if (response.status === 401) throw new SessionExpired()
  if (!response.ok) throw new Error(response.status === 403 ? 'Bu işlem için operatör yetkiniz yok.' : response.status === 429 ? 'Çok fazla istek. Bekleyip yeniden deneyin.' : 'Firma kayıtları okunamadı. Hedefi ve bağlantıyı kontrol edip yeniden deneyin.')
  return response.json().catch(() => { throw new Error('Sunucu yanıtı doğrulanamadı. Yeniden deneyin.') })
}
export async function readData(targetId?: string, page = 1, periodId?: string, paymentsPage = 1): Promise<Data> {
  const query = new URLSearchParams({ page: String(page), paymentsPage: String(paymentsPage), ...(periodId ? { periodId } : {}) })
  const value = await read(ledgerPath(targetId) + '?' + query)
  if (!record(value) || typeof value.targetId !== 'string' || targetId !== undefined && value.targetId !== targetId || typeof value.targetName !== 'string' || !validDate(value.asOf) ||
    typeof value.page !== 'number' || typeof value.total !== 'number' || typeof value.paymentsPage !== 'number' || typeof value.paymentCount !== 'number' ||
    !(value.selectedPeriodId === null || typeof value.selectedPeriodId === 'string') || !Array.isArray(value.periods) || !Array.isArray(value.payments) ||
    (targetId !== undefined || value.summary !== undefined) && !validSummary(value.summary) ||
    value.periods.some(p => !record(p) || ['id','version'].some(k => typeof p[k] !== 'string') || ['startsOn','endsOn','dueOn'].some(k => !validDate(p[k])) || ['amountTry','paidTry','outstandingTry'].some(k => !validMoney(p[k])) || typeof p.overdue !== 'boolean') ||
    value.payments.some(p => !record(p) || typeof p.id !== 'string' || !validDate(p.paidOn) || typeof p.recordedAt !== 'string' || !Number.isFinite(Date.parse(p.recordedAt)) || !validMoney(p.amountTry) || typeof p.reversed !== 'boolean' || !(p.reference === null || typeof p.reference === 'string') || !(p.reason === null || typeof p.reason === 'string'))) throw new Error('Abonelik yanıtı doğrulanamadı. Yeniden yükleyin.')
  return value as Data
}
export async function readTargets(page: number, search: string, access: string, signal: AbortSignal): Promise<TargetPage> {
  const value = await read('/api/targets?' + new URLSearchParams({ page: String(page), search, access, includeSummary: 'true' }), signal)
  if (!record(value) || !Number.isInteger(value.page) || value.pageSize !== 12 || !Number.isInteger(value.total) || value.filterScope !== 'page' ||
    typeof value.checkedAt !== 'string' || !Number.isFinite(Date.parse(value.checkedAt)) || !validDate(value.asOf) || !Array.isArray(value.items) || value.items.length > 12 ||
    value.items.some(t => !record(t) || typeof t.id !== 'string' || typeof t.name !== 'string' ||
      !(t.status === 'unavailable' && t.summary === null || t.status === 'available' && validSummary(t.summary)))) throw new Error('Firma listesi doğrulanamadı. Yeniden deneyin.')
  const result = value as TargetPage
  if (new Set(result.items.map(t => t.id)).size !== result.items.length) throw new Error('Firma listesi doğrulanamadı. Yeniden deneyin.')
  return result
}
