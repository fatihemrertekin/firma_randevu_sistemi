export type Account = { email: string; mfaEnabled: boolean; ownerAccess: boolean; staffAccess: boolean }

export async function getAccount(signal: AbortSignal = AbortSignal.timeout(15000)): Promise<Account | null> {
  const response = await fetch('/api/auth/me', { cache: 'no-store', signal })
  if (response.status === 401) return null
  if (!response.ok) throw new Error('Hesap bilgisi alınamadı.')
  const body: unknown = await response.json()
  if (typeof body !== 'object' || body === null || !('email' in body) || typeof body.email !== 'string' ||
    !('mfaEnabled' in body) || typeof body.mfaEnabled !== 'boolean' ||
    !('ownerAccess' in body) || typeof body.ownerAccess !== 'boolean' ||
    !('staffAccess' in body) || typeof body.staffAccess !== 'boolean') throw new Error('Hesap yanıtı geçersiz.')
  return { email: body.email, mfaEnabled: body.mfaEnabled, ownerAccess: body.ownerAccess, staffAccess: body.staffAccess }
}

export class RequestVerificationError extends Error {}
export function requestErrorMessage(problem: unknown, fallback: string) {
  return problem instanceof RequestVerificationError ? problem.message : fallback
}

export async function postWithCsrf(path: string, body: object, signal?: AbortSignal): Promise<Response> {
  const requestSignal = signal ?? AbortSignal.timeout(15000)
  let token: string
  try {
    const response = await fetch('/api/auth/csrf', { cache: 'no-store', signal: requestSignal })
    if (!response.ok) throw new Error('csrf')
    const value: unknown = await response.json()
    if (typeof value !== 'object' || value === null || !('token' in value) || typeof value.token !== 'string' || !value.token) throw new Error('csrf')
    token = value.token
  } catch {
    throw new RequestVerificationError('İstek güvenlik doğrulaması alınamadı; işlem gönderilmedi. Bu ekranda yeniden deneyin. Sayfayı yenilerseniz kaydedilmemiş bilgileri yeniden girmeniz gerekebilir.')
  }
  const response = await fetch(path, {
    method: 'POST',
    signal: requestSignal,
    headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
    body: JSON.stringify(body),
  })
  if (response.status === 400) {
    const problem: unknown = await response.clone().json().catch(() => null)
    if (typeof problem === 'object' && problem !== null && 'code' in problem && problem.code === 'csrf_invalid') {
      throw new RequestVerificationError('İstek güvenlik doğrulaması geçersiz veya süresi dolmuş; işlem uygulanmadı. Bu ekranda yeniden deneyin. Sayfayı yenilerseniz kaydedilmemiş bilgileri yeniden girmeniz gerekebilir.')
    }
  }
  return response
}
