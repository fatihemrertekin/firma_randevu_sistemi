// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import OwnerRecoveryEmail from './OwnerRecoveryEmail'
import App from '../../App'

let container: HTMLDivElement
let root: Root
const status = { email: 'owner@example.test', verifiedAt: null, deliveryAvailable: true }
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('fetch', vi.fn(async () => Response.json(status)))
  window.history.replaceState(null, '', '/')
  container = document.createElement('div'); document.body.append(container)
  root = createRoot(container)
})
afterEach(async () => {
  await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals()
  window.history.replaceState(null, '', '/')
})
async function submit(label: string) {
  const form = container.querySelector(`form[aria-label="${label}"]`)
  if (!form) throw new Error('Form yok')
  await act(async () => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
}
async function render(post = vi.fn(async () => Response.json({ expiresAt: 'synthetic-expiry' }))) {
  await act(async () => root.render(<OwnerRecoveryEmail post={post} />))
  return post
}

describe('İşletme sahibi kurtarma e-postası', () => {
  it('gönderim beklerken çift isteği engeller ve onay gelene kadar doğrulanmış göstermez', async () => {
    let finish: ((response: Response) => void) | undefined
    const promise = new Promise<Response>(resolve => { finish = resolve })
    const post = vi.fn(() => promise)
    await render(post)
    expect(container.textContent).toContain('Henüz doğrulanmadı')
    await submit('Kurtarma e-postasını doğrulama')
    await submit('Kurtarma e-postasını doğrulama')
    expect(post).toHaveBeenCalledTimes(1)
    expect(Array.from(container.querySelectorAll('button')).every(button => button.disabled)).toBe(true)
    await act(async () => finish?.(Response.json({ expiresAt: 'synthetic-expiry' })))
    expect(container.textContent).toContain('Doğrulama iletisi hazırlandı')
    expect(container.textContent).toContain('Henüz doğrulanmadı')
    expect(post.mock.calls[0]).toBeDefined()
  })

  it.each([429, 503])('gönderim hatasında (%s) başarı iddiası göstermez', async code => {
    await render(vi.fn(async () => new Response(null, { status: code })))
    await submit('Kurtarma e-postasını doğrulama')
    expect(container.querySelector('[role="alert"]')).not.toBeNull()
    expect(container.textContent).not.toContain('Doğrulama iletisi hazırlandı')
  })

  it('gönderim kapalıyken veya doğrulama tamamlandığında gönderemez', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ ...status, deliveryAvailable: false })))
    const post = await render()
    await submit('Kurtarma e-postasını doğrulama')
    expect(post).not.toHaveBeenCalled()
    expect(container.textContent).toContain('şu anda kullanılamıyor')
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ ...status, verifiedAt: '2026-10-01T18:00:00Z' })))
    const reload = Array.from(container.querySelectorAll('button')).find(button => button.textContent === 'Durumu yenile')
    await act(async () => reload?.click())
    expect(container.textContent).toContain('Doğrulandı')
    expect(container.querySelector('form')).toBeNull()
  })

  it('durum okunamazsa doğrulanmış göstermez ve yeniden yüklenebilir', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 500 })))
    await render()
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('alınamadı')
    expect(container.querySelector('form')).toBeNull()
    vi.stubGlobal('fetch', vi.fn(async () => Response.json(status)))
    const reload = Array.from(container.querySelectorAll('button')).find(button => button.textContent === 'Durumu yenile')
    await act(async () => reload?.click())
    expect(container.textContent).toContain('Henüz doğrulanmadı')
  })

  it('bağlantıyı açmak doğrulama yapmaz; token URL ve ekrandan temizlenir, yalnız onay POST eder', async () => {
    const token = 'synthetic-email-proof'
    window.history.replaceState(null, '', '/#verify-owner-email=' + token)
    const fetcher = vi.fn(async (path: string) => {
      if (path === '/api/auth/me') return new Response(null, { status: 401 })
      if (path === '/api/auth/csrf') return Response.json({ token: 'synthetic-csrf' })
      if (path === '/api/auth/recovery-email/confirm') return new Response(null, { status: 204 })
      throw new Error('Beklenmeyen istek')
    })
    vi.stubGlobal('fetch', fetcher)
    await act(async () => root.render(<App />))
    expect(window.location.hash).toBe('')
    expect(container.textContent).not.toContain(token)
    expect(fetcher.mock.calls.some(([path]) => path.endsWith('/confirm'))).toBe(false)
    await submit('Hesap e-postasını doğrulama')
    expect(container.querySelector('[role="status"]')?.textContent).toContain('doğrulandı')
    expect(fetcher.mock.calls.some(([path]) => path.endsWith('/confirm'))).toBe(true)
    await act(async () => {
      window.history.replaceState(null, '', '/#verify-owner-email=another-synthetic-proof')
      window.dispatchEvent(new HashChangeEvent('hashchange'))
    })
    expect(window.location.hash).toBe('')
    expect(container.querySelector('[role="status"]')).toBeNull()
    expect(container.querySelector('form')).not.toBeNull()
    expect(fetcher.mock.calls.filter(([path]) => path.endsWith('/confirm'))).toHaveLength(1)
  })

  it('geçersiz bağlantıda hata gösterir ve doğrulanmış göstermez', async () => {
    window.history.replaceState(null, '', '/#verify-owner-email=expired')
    vi.stubGlobal('fetch', vi.fn(async (path: string) => {
      if (path === '/api/auth/me') return new Response(null, { status: 401 })
      if (path === '/api/auth/csrf') return Response.json({ token: 'synthetic-csrf' })
      return new Response(null, { status: 400 })
    }))
    await act(async () => root.render(<App />))
    await submit('Hesap e-postasını doğrulama')
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('geçersiz veya süresi dolmuş')
    expect(container.querySelector('[role="status"]')).toBeNull()
  })
})
