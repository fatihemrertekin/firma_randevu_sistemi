// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'
import OwnerPasswordResetRequest from './OwnerPasswordResetRequest'

let container: HTMLDivElement
let root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  window.history.replaceState(null, '', '/')
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => {
  await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals()
  window.history.replaceState(null, '', '/')
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
describe('Owner otomatik parola sıfırlama', () => {
  it('bekleyen talebi çoğaltmaz ve hazırlamayı teslim edilmiş gibi göstermez', async () => {
    let finish: ((value: Response) => void) | undefined
    const delayed = new Promise<Response>(resolve => { finish = resolve })
    const requests = vi.fn(async (path: string) => {
      if (path.endsWith('options')) return Response.json({ available: true })
      if (path.endsWith('csrf')) return Response.json({ token: 'synthetic-csrf' })
      return delayed
    })
    vi.stubGlobal('fetch', requests)
    await act(async () => root.render(<OwnerPasswordResetRequest onCancel={vi.fn()} onDone={vi.fn()} />))
    await input('reset-email', 'owner@example.test')
    await submit(); await submit()
    expect(requests.mock.calls.filter(([path]) => path.endsWith('request'))).toHaveLength(1)
    expect([...container.querySelectorAll('button')].every(button => button.disabled)).toBe(true)
    await act(async () => finish?.(new Response(null, { status: 202 })))
    expect(container.querySelector('[role="status"]')?.textContent).toContain('Bilgiler uygunsa')
    expect(container.textContent).not.toContain('gönderildi')
  })
  it.each([429, 503])('talep reddinde (%s) başarı iddiası göstermez', async status => {
    vi.stubGlobal('fetch', vi.fn(async (path: string) => path.endsWith('options') ? Response.json({ available: true })
      : path.endsWith('csrf') ? Response.json({ token: 'synthetic' }) : new Response(null, { status })))
    await act(async () => root.render(<OwnerPasswordResetRequest onCancel={vi.fn()} onDone={vi.fn()} />))
    await input('reset-email', 'owner@example.test'); await submit()
    expect(container.querySelector('[role="alert"]')).not.toBeNull()
    expect(container.querySelector('[role="status"]')).toBeNull()
  })
  it('gönderim kapalıysa talep göndermez ve özel kod yolu korunur', async () => {
    const requests = vi.fn(async () => Response.json({ available: false }))
    vi.stubGlobal('fetch', requests)
    await act(async () => root.render(<OwnerPasswordResetRequest onCancel={vi.fn()} onDone={vi.fn()} />))
    expect(container.querySelector<HTMLInputElement>('#reset-email')?.disabled).toBe(false)
    await input('reset-email', 'owner@example.test')
    expect(container.querySelector<HTMLInputElement>('#reset-email')?.value).toBe('owner@example.test')
    expect(container.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBe(true)
    await submit()
    expect(requests).toHaveBeenCalledTimes(1)
    expect(container.textContent).toContain('henüz kullanılamıyor')
    const manual = [...container.querySelectorAll('button')].find(button => button.textContent === 'Parola sıfırlama kodum var')
    await act(async () => manual?.click())
    expect(container.querySelector('#reset-token')).not.toBeNull()
  })
  it('durum bağlantı hatasında formu açmaz ve yükleme sona erer', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new Error('synthetic connection') }))
    await act(async () => root.render(<OwnerPasswordResetRequest onCancel={vi.fn()} onDone={vi.fn()} />))
    expect(container.querySelector('[role="alert"]')).not.toBeNull()
    expect(container.textContent).not.toContain('kontrol ediliyor')
  })
  it('fragment tokenını gizler; açılış etkisizdir; yalnız form gönderimi sıfırlar ve normal girişe döner', async () => {
    const token = 'synthetic-owner-reset-proof'
    const requests = vi.fn(async (path: string) => path.endsWith('/me') ? new Response(null, { status: 401 })
      : path.endsWith('/csrf') ? Response.json({ token: 'synthetic-csrf' })
        : path.endsWith('/password-reset-options') ? Response.json({ available: true }) : new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', requests)
    await act(async () => root.render(<App />))
    // Follow the real browser path: open the request screen, then the received link in the same tab.
    const forgot = [...container.querySelectorAll('button')].find(button => button.textContent === 'Parolamı unuttum')
    await act(async () => forgot?.click())
    expect(container.textContent).toContain('Parolanızı yenileyin')
    await act(async () => {
      window.history.replaceState(null, '', '/#reset-owner-password=' + token)
      window.dispatchEvent(new HashChangeEvent('hashchange'))
    })
    expect(window.location.hash).toBe('')
    expect(container.innerHTML).not.toContain(token)
    expect(container.querySelector('#reset-token')).toBeNull()
    expect(requests.mock.calls.some(([path]) => path.endsWith('reset-password'))).toBe(false)
    await input('reset-password', 'Synthetic!Reset456'); await input('reset-confirm', 'Synthetic!Reset456'); await submit()
    expect(requests.mock.calls.filter(([path]) => path.endsWith('reset-password'))).toHaveLength(1)
    expect(container.textContent).toContain('İşletme girişi')
    expect(container.textContent).toContain('ikinci adımla')
  })
})
