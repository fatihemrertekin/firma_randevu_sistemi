export const bookingPolicyEndpoint = '/api/booking-policy/'
export const policyLimits = { leadMinutes: [0, 43200], horizonDays: [1, 365], slotMinutes: [1, 60], customerCancellationLeadMinutes: [0, 43200] } as const
export type PolicyField = keyof typeof policyLimits
export const policyFields = Object.keys(policyLimits) as PolicyField[]
export type PolicyErrors = Partial<Record<PolicyField | 'requiresBusinessApproval', string>>
export type BookingPolicy = Record<PolicyField, number> & { version: string; timeZone: 'Europe/Istanbul'; requiresBusinessApproval: boolean }
export class BookingPolicyError extends Error {
  constructor(public status: number) { super(bookingPolicyFailure(status)) }
}
export function bookingPolicyFailure(status: number) {
  if (status === 400) return 'İstek doğrulanamadı. Güncel ayarları yükleyip yeniden deneyin.'
  if (status === 401) return 'Oturumunuz sona erdi. Yeniden giriş yapın.'
  if (status === 403) return 'Rezervasyon ayarlarını yalnız işletme sahibi değiştirebilir.'
  if (status === 404) return 'Rezervasyon ayarları alınamadı. Yeniden deneyin; sorun sürerse destek alın.'
  if (status === 409) return 'Ayarlar başka bir işlemde değişti. Taslağınız korundu. Güncel ayarları yükleyin.'
  if (status === 429) return 'Kısa sürede çok fazla işlem yapıldı. Biraz bekleyip güncel ayarları yükleyin.'
  return 'Kayıt sonucu doğrulanamadı. Ayarlar kaydedilmiş olabilir. Tekrar kaydetmeden önce güncel ayarları yükleyin.'
}
export async function readBookingPolicy(response: Response): Promise<BookingPolicy> {
  if (!response.ok) throw new BookingPolicyError(response.status)
  const value: unknown = await response.json()
  if (response.status !== 200 || typeof value !== 'object' || value === null ||
    !('version' in value) || typeof value.version !== 'string' ||
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu.test(value.version) ||
    value.version === '00000000-0000-0000-0000-000000000000' ||
    !('timeZone' in value) || value.timeZone !== 'Europe/Istanbul' ||
    !('requiresBusinessApproval' in value) || typeof value.requiresBusinessApproval !== 'boolean') throw new BookingPolicyError(500)
  const numbers: Record<PolicyField, number> = { leadMinutes: 0, horizonDays: 0, slotMinutes: 0, customerCancellationLeadMinutes: 0 }
  for (const key of policyFields) {
    const field: unknown = key in value ? value[key as keyof typeof value] : undefined
    const [min, max] = policyLimits[key]
    if (typeof field !== 'number' || !Number.isInteger(field) || field < min || field > max) throw new BookingPolicyError(500)
    numbers[key] = field
  }
  if (numbers.leadMinutes >= numbers.horizonDays * 1440) throw new BookingPolicyError(500)
  return { ...numbers, version: value.version, timeZone: value.timeZone, requiresBusinessApproval: value.requiresBusinessApproval }
}
export async function policyFieldErrors(response: Response): Promise<PolicyErrors> {
  const value: unknown = await response.json(), errors: PolicyErrors = {}
  if (typeof value === 'object' && value !== null && 'errors' in value && typeof value.errors === 'object' && value.errors !== null) {
    for (const [key, messages] of Object.entries(value.errors)) {
      if ((key === 'requiresBusinessApproval' || policyFields.includes(key as PolicyField)) && Array.isArray(messages) && typeof messages[0] === 'string' && messages[0]) errors[key as keyof PolicyErrors] = messages[0]
    }
  }
  return errors
}
