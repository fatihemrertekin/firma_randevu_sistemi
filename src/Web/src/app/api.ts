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

export async function postWithCsrf(path: string, body: object, signal?: AbortSignal): Promise<Response> {
  const requestSignal = signal ?? AbortSignal.timeout(15000)
  const tokenResponse = await fetch('/api/auth/csrf', { cache: 'no-store', signal: requestSignal })
  if (!tokenResponse.ok) throw new Error('İstek doğrulaması alınamadı.')
  const { token } = (await tokenResponse.json()) as { token: string }
  return fetch(path, {
    method: 'POST',
    signal: requestSignal,
    headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
    body: JSON.stringify(body),
  })
}
