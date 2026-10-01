export type Account = { email: string; mfaEnabled: boolean; ownerAccess: boolean; staffAccess: boolean }

export async function getAccount(): Promise<Account | null> {
  const response = await fetch('/api/auth/me')
  if (response.status === 401) return null
  if (!response.ok) throw new Error('Hesap bilgisi alınamadı.')
  return (await response.json()) as Account
}

export async function postWithCsrf(path: string, body: object, signal?: AbortSignal): Promise<Response> {
  const tokenResponse = await fetch('/api/auth/csrf', { cache: 'no-store', ...(signal ? { signal } : {}) })
  if (!tokenResponse.ok) throw new Error('İstek doğrulaması alınamadı.')
  const { token } = (await tokenResponse.json()) as { token: string }
  return fetch(path, {
    method: 'POST',
    ...(signal ? { signal } : {}),
    headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
    body: JSON.stringify(body),
  })
}
