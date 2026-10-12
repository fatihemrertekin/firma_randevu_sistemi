// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import App from '../../App'
import MfaRecoveryCodes from './MfaRecoveryCodes'
import { getAccount, postWithCsrf } from '../../app/api'

let container: HTMLDivElement
let root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  window.history.replaceState(null, '', '/')
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => {
  await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); vi.restoreAllMocks()
})
async function input(id: string, value: string) {
  const element = container.querySelector<HTMLInputElement>('#' + id)
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  if (!element || !setter) throw new Error('Alan yok')
  await act(async () => { setter.call(element, value); element.dispatchEvent(new Event('input', { bubbles: true })) })
}
async function submit() {
  const form = container.querySelector('form')
  if (!form) throw new Error('Form yok')
  await act(async () => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
}
async function pendingMfa(logoutStatus: number) {
  const requests = vi.fn(async (path: string) => {
    if (path.endsWith('/me')) return new Response(null, { status: 401 })
    if (path.endsWith('/csrf')) return Response.json({ token: 'synthetic-csrf' })
    if (path.endsWith('/login')) return new Response(null, { status: 202 })
    return new Response(null, { status: logoutStatus })
  })
  vi.stubGlobal('fetch', requests)
  await act(async () => root.render(<App />))
  await input('email', 'owner@example.test'); await input('password', 'Synthetic!Owner123'); await submit()
  return requests
}
it('MFA iptalinde sunucudaki geçici oturumu kapatır ve normal girişe döner', async () => {
  const requests = await pendingMfa(204)
  expect(document.activeElement).toBe(container.querySelector('h1'))
  expect(container.textContent).toContain('sakladığınız MFA kurtarma kodunu')
  const method = [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent === 'Kurtarma kodu kullan')
  if (!method) throw new Error('MFA yöntem düğmesi yok')
  method.focus()
  await act(async () => method.click())
  expect(document.activeElement).toBe(method)
  expect(method.textContent).toBe('Uygulama kodu kullan')
  await input('mfa-code', '123456')
  const cancel = [...container.querySelectorAll<HTMLButtonElement>('button, a[data-navigation]')].find(button => button.textContent === 'Girişe dön')
  await act(async () => cancel?.click())
  expect(requests.mock.calls.some(([path]) => path.endsWith('/logout'))).toBe(true)
  expect(container.querySelector('#mfa-code')).toBeNull()
  expect(container.querySelector<HTMLInputElement>('#password')?.value).toBe('')
  expect(document.activeElement).toBe(container.querySelector('h1'))
})
it('sunucu çıkışı başarısızsa MFA oturumunu kapandı gibi göstermez', async () => {
  await pendingMfa(503)
  const cancel = [...container.querySelectorAll<HTMLButtonElement>('button, a[data-navigation]')].find(button => button.textContent === 'Girişe dön')
  await act(async () => cancel?.click())
  expect(container.querySelector('#mfa-code')).not.toBeNull()
  expect(container.querySelector('[role="alert"]')?.textContent).toContain('Çıkış yapılamadı')
  expect(document.activeElement).toBe(container.querySelector('[role="alert"]'))
})
it.each([401, 503])('oturum yüklemesi (%s) bitene kadar odak taşımaz; başlık veya hata odağını korur', async status => {
  let finish: ((response: Response) => void) | undefined
  vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(resolve => { finish = resolve })))
  await act(async () => root.render(<App />))
  expect(container.querySelector('h1')).toBeNull()
  expect(document.activeElement).toBe(document.body)
  await act(async () => finish?.(new Response(null, { status })))
  expect(document.activeElement).toBe(container.querySelector(status === 401 ? 'h1' : '[role="alert"]'))
  expect(document.activeElement?.tagName).not.toBe('INPUT')
})
it('MFA kurulumunda anahtar, kurtarma kodları ve giriş görevlerini başlık odağıyla açar', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => {
    if (path.endsWith('/me')) return Response.json({ email: 'owner@example.test', ownerAccess: true, staffAccess: false, mfaEnabled: false })
    if (path.endsWith('/csrf')) return Response.json({ token: 'synthetic-csrf' })
    if (path.endsWith('/setup')) return Response.json({ key: 'synthetic-key', uri: 'synthetic-uri' })
    if (path.endsWith('/enable')) return Response.json({ recoveryCodes: ['synthetic-recovery'] })
    throw new Error('Beklenmeyen istek')
  }))
  await act(async () => root.render(<App />))
  expect(document.activeElement).toBe(container.querySelector('h1'))
  await input('setup-password', 'Synthetic!Owner123'); await submit()
  expect(container.querySelector('#enable-password')).not.toBeNull()
  expect(document.activeElement).toBe(container.querySelector('h1'))
  await input('enable-password', 'Synthetic!Owner123'); await input('enable-code', '123456'); await submit()
  expect(document.activeElement?.textContent).toBe('Kurtarma kodlarını saklayın')
  await act(async () => container.querySelector<HTMLButtonElement>('button')?.click())
  expect(document.activeElement?.textContent).toBe('İşletme girişi')
})
it.each(['/yok', '/eposta-dogrula'])('anonim eksik bağlantı sayfasını başlık odağıyla açar (%s)', async path => {
  window.history.replaceState(null, '', path)
  vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 401 })))
  await act(async () => root.render(<App />))
  expect(document.activeElement).toBe(container.querySelector('h1'))
  expect(document.title).toBe(`${container.querySelector('h1')?.textContent} · Randevu`)
  await act(async () => {
    window.history.pushState(null, '', path === '/yok' ? '/eposta-dogrula' : '/yok')
    window.dispatchEvent(new PopStateEvent('popstate'))
  })
  expect(document.activeElement).toBe(container.querySelector('h1'))
  expect(document.title).toBe(`${container.querySelector('h1')?.textContent} · Randevu`)
})
it('girişte bekleyen isteği çoğaltmaz ve 429 durumunu açıklar', async () => {
  let finish: ((response: Response) => void) | undefined
  const delayed = new Promise<Response>(resolve => { finish = resolve })
  const requests = vi.fn(async (path: string) => path.endsWith('/me') ? new Response(null, { status: 401 })
    : path.endsWith('/csrf') ? Response.json({ token: 'synthetic-csrf' }) : delayed)
  vi.stubGlobal('fetch', requests)
  await act(async () => root.render(<App />))
  await input('email', 'owner@example.test'); await input('password', 'Synthetic!Owner123')
  await submit(); await submit()
  expect(requests.mock.calls.filter(([path]) => path.endsWith('/login'))).toHaveLength(1)
  expect(container.querySelector<HTMLInputElement>('#email')?.disabled).toBe(true)
  await act(async () => finish?.(new Response(null, { status: 429 })))
  expect(container.querySelector('[role="alert"]')?.textContent).toContain('Çok fazla deneme')
})
it('MFA kodlarını onay ve parola olmadan göndermez, beklerken çoğaltmaz', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => Response.json({ remaining: 2 })))
  let finish: ((response: Response) => void) | undefined
  const post = vi.fn(() => new Promise<Response>(resolve => { finish = resolve }))
  const done = vi.fn()
  await act(async () => root.render(<MfaRecoveryCodes post={post} disabled={false} onReplaced={done} />))
  expect(container.textContent).toContain('Kalan kullanılmamış kod: 2')
  await submit(); expect(post).not.toHaveBeenCalled()
  await input('recovery-password', 'Synthetic!Owner123')
  await act(async () => container.querySelector<HTMLInputElement>('#recovery-confirm')?.click())
  await submit(); await submit()
  expect(post).toHaveBeenCalledTimes(1)
  expect(container.querySelector<HTMLInputElement>('#recovery-password')?.disabled).toBe(true)
  const codes = Array.from({ length: 8 }, (_, i) => `synthetic-${i}`)
  await act(async () => finish?.(Response.json({ recoveryCodes: codes })))
  expect(done).toHaveBeenCalledWith(codes)
  expect(container.querySelector<HTMLInputElement>('#recovery-password')?.value).toBe('')
})
it.each([400, 401, 409, 429, 503])('MFA kod yenileme hatasında (%s) başarı göstermez, parolayı temizler', async status => {
  vi.stubGlobal('fetch', vi.fn(async () => Response.json({ remaining: 0 })))
  const done = vi.fn()
  await act(async () => root.render(<MfaRecoveryCodes post={vi.fn(async () => new Response(null, { status }))}
    disabled={false} onReplaced={done} />))
  await input('recovery-password', 'Synthetic!Owner123')
  await act(async () => container.querySelector<HTMLInputElement>('#recovery-confirm')?.click())
  await submit()
  expect(done).not.toHaveBeenCalled()
  expect(container.querySelector('[role="alert"]')).not.toBeNull()
  expect(container.querySelector<HTMLInputElement>('#recovery-password')?.value).toBe('')
})
it('CSRF ve işlem aynı sonlu süreyi kullanır; geçersiz hesap yanıtını reddeder', async () => {
  const timeout = vi.spyOn(AbortSignal, 'timeout')
  const requests = vi.fn(async (path: string) => path.endsWith('/csrf') ? Response.json({ token: 'synthetic-csrf' })
    : new Response(null, { status: 204 }))
  vi.stubGlobal('fetch', requests)
  await postWithCsrf('/api/auth/logout', {})
  expect(timeout).toHaveBeenCalledWith(15000)
  const calls = requests.mock.calls as unknown as [string, RequestInit][]
  expect(calls[0][1].signal).toBe(calls[1][1].signal)
  vi.stubGlobal('fetch', vi.fn(async () => Response.json({ email: 'owner@example.test', ownerAccess: 'true' })))
  await expect(getAccount()).rejects.toThrow('Hesap yanıtı geçersiz')
})
