import { postWithCsrf, RequestVerificationError } from '../../app/api'
import { isPageMetadata, type PageMetadata } from '../../app/pageMetadata'
import { isId, businessDay, nextDay, formatDay, formatTime } from '../appointments/appointmentsApi'
import { policyFields, policyLimits } from '../booking-policy/bookingPolicyApi'
export { businessDay, nextDay, formatDay, formatTime }
export type Service = { id: string; name: string; version: string; price: string; currency: string; durationMinutes: number }
export type Member = { id: string; name: string }
export type Policy = { version: string; leadMinutes: number; horizonDays: number; slotMinutes: number; timeZone: string; customerCancellationLeadMinutes: number; requiresBusinessApproval: boolean }
export type Catalog = PageMetadata & { business: { name: string; phone: string | null; address: string | null }; policy: Policy; items: Service[] }
export type Members = PageMetadata & { items: Member[] }
export type Availability = { startsAt: string[]; timeZone: string; serviceVersion: string; policyVersion: string }
export type Intent = { requestKey: string; serviceId: string; serviceVersion: string; policyVersion: string; staffMemberId: string; startsAt: string; customerName: string; customerPhone: string }
export type Receipt = { challengeId: string; challengeSecret: string }
export type Challenge = Receipt & { maskedPhone: string; expiresAt: string; resendAt: string }
export type Result = { id: string; version: string; serviceName: string; staffName: string; startsAt: string; endsAt: string; price: string; currency: string; status: 'Pending' | 'Confirmed' | 'Cancelled' | 'Completed' | 'NoShow'; pendingExpiresAt: string | null; policyVersion: string; customerCancellationLeadMinutes: number; requiresBusinessApproval: boolean; cancellationDeadline: string }
export type Access = { token: string; version: string; expiresAt: string }
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null
const text = (v: unknown): v is string => typeof v === 'string' && v.length > 0 && v.length <= 1000
const integer = (v: unknown): v is number => Number.isSafeInteger(v) && typeof v === 'number' && v >= 0
const date = (v: unknown): v is string => typeof v === 'string' && /(?:Z|[+-]\d{2}:\d{2})$/u.test(v) && Number.isFinite(Date.parse(v))
const nullableText = (v: unknown) => v === null || text(v)
const money = (v: unknown): v is string => typeof v === 'string' && /^\d+(\.\d{2})$/u.test(v)
const secret = (v: unknown): v is string => typeof v === 'string' && /^[A-Za-z0-9_-]{43}$/u.test(v)
const isMember = (v: unknown): v is Member => record(v) && isId(v.id) && text(v.name)
const isService = (v: unknown): v is Service => record(v) && isId(v.version) && money(v.price) && text(v.currency) && integer(v.durationMinutes) && isMember(v)
const isPolicy = (v: unknown): v is Policy => record(v) && isId(v.version) && v.timeZone === 'Europe/Istanbul' && typeof v.requiresBusinessApproval === 'boolean' && policyFields.every(key => {
  const value = v[key]; return typeof value === 'number' && Number.isInteger(value) && value >= policyLimits[key][0] && value <= policyLimits[key][1]
})
export const isCatalog = (v: unknown): v is Catalog => record(v) && record(v.business) && typeof v.business.name === 'string' && v.business.name.length <= 1000 && nullableText(v.business.phone) && nullableText(v.business.address) && isPolicy(v.policy) && Array.isArray(v.items) && v.items.every(isService) && isPageMetadata(v)
export const isMembers = (v: unknown): v is Members => record(v) && Array.isArray(v.items) && v.items.every(isMember) && isPageMetadata(v)
export const isAvailability = (v: unknown): v is Availability => record(v) && v.timeZone === 'Europe/Istanbul' && isId(v.serviceVersion) && isId(v.policyVersion) && Array.isArray(v.startsAt) && v.startsAt.every(date)
export const isChallenge = (v: unknown): v is Challenge => record(v) && isId(v.challengeId) && secret(v.challengeSecret) && text(v.maskedPhone) && date(v.expiresAt) && date(v.resendAt)
export const isResent = (v: unknown): v is { challengeId: string; expiresAt: string; resendAt: string } => record(v) && isId(v.challengeId) && date(v.expiresAt) && date(v.resendAt)
export const isVerified = (v: unknown): v is { verified: true; verifiedUntil: string } => record(v) && v.verified === true && date(v.verifiedUntil)
export const isAccess = (v: unknown): v is Access => record(v) && secret(v.token) && isId(v.version) && date(v.expiresAt)
export const isResult = (v: unknown): v is Result => record(v) && isId(v.id) && isId(v.version) && text(v.serviceName) && text(v.staffName) && date(v.startsAt) && date(v.endsAt) && money(v.price) && text(v.currency) && typeof v.status === 'string' && ['Pending','Confirmed','Cancelled','Completed','NoShow'].includes(v.status) && (v.pendingExpiresAt === null || date(v.pendingExpiresAt)) && isId(v.policyVersion) && integer(v.customerCancellationLeadMinutes) && typeof v.requiresBusinessApproval === 'boolean' && date(v.cancellationDeadline)
export class BookingError extends Error {
  constructor(public status: number, public code = '', public retryAt = 0, public fields: Record<string, string> = {}) {
    super(status === 400 ? code === 'verification_code_invalid' ? 'Kod geçersiz veya süresi dolmuş. Kontrol edip yeniden deneyin; gerekirse yeni kod isteyin.' : 'İstek doğrulanamadı. Bilgilerinizi kontrol ederek yeniden deneyin.' : status === 403 ? 'Doğrulama yetkisi geçersiz veya süresi dolmuş.' : status === 404 ? 'Seçili bilgi bulunamadı. Güncel seçenekleri yükleyin.' : status === 409 ? 'Seçim veya koşullar değişmiş olabilir. Güncel seçenekleri kontrol edin.' : status === 429 ? 'Çok sık denendi. Yeniden denemeden önce bekleyin.' : status === 503 ? 'Bu işlem şu anda kullanılamıyor. Daha sonra yeniden deneyin veya işletmeyle iletişime geçin.' : 'İşlem tamamlanamadı. Bu ekranda yeniden deneyin.')
  }
}
export const errorMessage = (e: unknown) => e instanceof BookingError || e instanceof RequestVerificationError ? e.message : 'Sonuç doğrulanamadı. Bağlantınızı kontrol ederek aynı işlemi yeniden deneyin.'
const prefix = '/api/public-booking'
export async function readResponse<T>(response: Response, valid: (v: unknown) => v is T): Promise<T> {
  const value: unknown = await response.json().catch(() => null)
  if (!response.ok) {
    const fields: Record<string, string> = {}
    if (record(value) && record(value.errors)) for (const [key, items] of Object.entries(value.errors)) {
      if (Array.isArray(items) && items.every(text)) fields[key] = items.join(' ')
    }
    const retry = response.headers.get('Retry-After')
    const retryAt = retry && /^\d+$/u.test(retry) ? Date.now() + Number(retry) * 1000 : retry ? Date.parse(retry) : 0
    throw new BookingError(response.status, record(value) && typeof value.code === 'string' ? value.code : '', Number.isFinite(retryAt) ? retryAt : 0, fields)
  }
  if (!valid(value)) throw new Error('invalid-response')
  return value
}
export async function get<T>(path: string, valid: (v: unknown) => v is T, signal: AbortSignal, token?: string) {
  return readResponse(await fetch(prefix + path, { cache: 'no-store', signal, headers: token ? { Authorization: `Bearer ${token}` } : {} }), valid)
}
export async function post<T>(path: string, body: object, valid: (v: unknown) => v is T, token?: string) {
  return readResponse(await postWithCsrf(prefix + path, body, AbortSignal.timeout(15000), token), valid)
}
export function resultId(path: string) { const match = /^\/randevu\/sonuc\/([^/]+)\/?$/u.exec(path); return match && isId(match[1]) ? match[1] : null }
export type LinkAccess = { id: string; token: string } | null
export function readAccess(): LinkAccess {
  if (typeof window === 'undefined') return null
  const id = resultId(window.location.pathname), fragment = new URLSearchParams(window.location.hash.slice(1))
  if (!window.location.pathname.startsWith('/randevu')) return null
  const token = fragment.get('erisim')
  // Remove even malformed proofs before the router can retain the URL; no secret in history state.
  if (window.location.hash || window.location.search) window.history.replaceState(null, '', window.location.pathname)
  return id && secret(token) && fragment.getAll('erisim').length === 1 ? { id, token } : null
}
export const priceLabel = (price: string, currency: string) => `${new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(Number(price))} ${currency === 'TRY' ? 'TL' : currency}`
