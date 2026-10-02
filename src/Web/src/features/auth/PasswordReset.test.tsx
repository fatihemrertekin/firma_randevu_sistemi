// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'

let container: HTMLDivElement
let root: Root

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  container = document.createElement('div')
  document.body.append(container)
  root = createRoot(container)
})
afterEach(async () => {
  await act(async () => root.unmount())
  container.remove()
  vi.unstubAllGlobals()
})

async function openReset(handler: () => Promise<Response>, staff = false) {
  const requests = vi.fn(async (path: string) => {
    if (path === '/api/auth/me') return new Response(null, { status: 401 })
    if (path === '/api/auth/csrf') return Response.json({ token: 'synthetic-csrf' })
    if (path === '/api/auth/password-reset-options') return Response.json({ available: false })
    if (path === (staff ? '/api/staff-password-resets/complete' : '/api/auth/reset-password')) return handler()
    throw new Error('Beklenmeyen test isteği')
  })
  vi.stubGlobal('fetch', requests)
  await act(async () => root.render(<App />))
  const button = Array.from(container.querySelectorAll('button')).find(item => item.textContent === (staff ? 'Çalışan parolamı unuttum' : 'Parolamı unuttum'))
  if (!button) throw new Error('Sıfırlama bağlantısı yok')
  await act(async () => button.click())
  if (!staff) {
    const manual = Array.from(container.querySelectorAll('button')).find(item => item.textContent === 'Parola sıfırlama kodum var')
    if (!manual) throw new Error('Manuel kurtarma seçeneği yok')
    await act(async () => manual.click())
  }
  return requests
}
async function fill(id: string, value: string) {
  const input = container.querySelector<HTMLInputElement>(`#${id}`)
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  if (!input || !setter) throw new Error('Sıfırlama alanı yok')
  await act(async () => {
    setter.call(input, value)
    input.dispatchEvent(new Event('input', { bubbles: true }))
  })
}
async function fillReset(confirm = 'Synthetic!Reset456') {
  await fill('reset-token', 'synthetic-reset-token')
  await fill('reset-password', 'Synthetic!Reset456')
  await fill('reset-confirm', confirm)
}
async function submitReset() {
  const form = container.querySelector('form[aria-label="Parola sıfırlama"]')
  if (!form) throw new Error('Sıfırlama formu yok')
  await act(async () => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
}

describe.each([false, true])('Parola sıfırlama (Staff: %s)', staff => {
  const path = staff ? '/api/staff-password-resets/complete' : '/api/auth/reset-password'
  it('girişten açılır, çift gönderimi engeller ve başarıda MFA ile giriş ister', async () => {
    let finish: ((response: Response) => void) | undefined
    const pending = new Promise<Response>(resolve => { finish = resolve })
    const requests = await openReset(() => pending, staff)
    expect(Array.from(container.querySelectorAll<HTMLInputElement>('input')).map(input => input.autocomplete))
      .toEqual(['off', 'new-password', 'new-password'])
    await fillReset()
    await submitReset()
    expect(Array.from(container.querySelectorAll<HTMLInputElement>('input')).every(input => input.disabled)).toBe(true)
    expect(container.textContent).toContain('Parola sıfırlanıyor…')
    await submitReset()
    expect(requests.mock.calls.filter(([requestPath]) => requestPath === path)).toHaveLength(1)
    await act(async () => finish?.(new Response(null, { status: 204 })))
    expect(container.textContent).toContain('İşletme girişi')
    expect(container.querySelector('[role="status"]')?.textContent).toContain(staff ? 'Yeni parolanızla yeniden giriş' : 'ikinci adımla yeniden giriş')
    expect(container.querySelector('#reset-token')).toBeNull()
    expect(container.querySelector<HTMLInputElement>('#password')?.value).toBe('')
  })

  it('uyuşmayan parolada istek göndermez ve parolaları temizler', async () => {
    const requests = await openReset(async () => new Response(null, { status: 204 }), staff)
    await fillReset('different')
    await submitReset()
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('aynı olmalı')
    expect(requests.mock.calls.some(([requestPath]) => requestPath === path)).toBe(false)
    expect(container.querySelector<HTMLInputElement>('#reset-password')?.value).toBe('')
  })

  it.each([400, 409, 429, 500])('sunucu reddinde (%i) başarı göstermez ve hassas alanları temizler', async status => {
    await openReset(async () => Response.json({ title: 'Sıfırlama kodu geçersiz veya süresi dolmuş.' }, { status }), staff)
    await fillReset()
    await submitReset()
    expect(container.querySelector('[role="alert"]')).not.toBeNull()
    expect(container.querySelector('[role="status"]')).toBeNull()
    expect(Array.from(container.querySelectorAll<HTMLInputElement>('input')).every(input => input.value === '' && !input.disabled)).toBe(true)
  })

  it('bağlantı kopmasında sonucu belirsiz gösterir ve otomatik tekrar yapmaz', async () => {
    const requests = await openReset(async () => { throw new Error('synthetic network error') }, staff)
    await fillReset()
    await submitReset()
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Sonuç doğrulanamadı')
    expect(requests.mock.calls.filter(([requestPath]) => requestPath === path)).toHaveLength(1)
    expect(Array.from(container.querySelectorAll<HTMLInputElement>('input')).every(input => input.value === '')).toBe(true)
  })

  it('girişe dönünce kod ve parolalar yeni açılan forma taşınmaz', async () => {
    await openReset(async () => new Response(null, { status: 204 }), staff)
    await fillReset()
    const cancel = Array.from(container.querySelectorAll('button')).find(item => item.textContent === 'Girişe dön')
    if (!cancel) throw new Error('Dönüş düğmesi yok')
    await act(async () => cancel.click())
    expect(container.textContent).toContain('İşletme girişi')
    const reopen = Array.from(container.querySelectorAll('button')).find(item => item.textContent === (staff ? 'Çalışan parolamı unuttum' : 'Parolamı unuttum'))
    if (!reopen) throw new Error('Sıfırlama düğmesi yok')
    await act(async () => reopen.click())
    expect(Array.from(container.querySelectorAll<HTMLInputElement>('input')).every(input => input.value === '')).toBe(true)
  })
})
