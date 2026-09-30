// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import StaffPasswordReset from './StaffPasswordReset'
import App from './App'

let container: HTMLDivElement
let root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  container = document.createElement('div')
  document.body.append(container)
  root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals() })
async function prepare() {
  const input = container.querySelector<HTMLInputElement>('#staff-reset-email')
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  const checkbox = container.querySelector<HTMLInputElement>('input[type="checkbox"]')
  if (!input || !setter || !checkbox) throw new Error('Sıfırlama formu yok')
  await act(async () => {
    setter.call(input, 'staff@example.test')
    input.dispatchEvent(new Event('input', { bubbles: true }))
    checkbox.click()
  })
}
async function submit() {
  const form = container.querySelector('form')
  if (!form) throw new Error('Form yok')
  await act(async () => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
}
async function click(label: string) {
  const button = Array.from(container.querySelectorAll('button')).find(item => item.textContent === label)
  if (!button) throw new Error('Buton yok')
  await act(async () => button.click())
}

describe('Staff parola sıfırlama kodu üretme', () => {
  it('alıcı onayı ister, çift gönderimi engeller; kodu göster/seç/gizle/temizle ile geçici sunar', async () => {
    let finish: ((response: Response) => void) | undefined
    const waiting = new Promise<Response>(resolve => { finish = resolve })
    const post = vi.fn(async () => waiting)
    await act(async () => root.render(<StaffPasswordReset post={post} />))
    await submit()
    expect(post).not.toHaveBeenCalled()
    await prepare()
    await submit()
    await submit()
    expect(post).toHaveBeenCalledExactlyOnceWith('/api/staff-password-resets/', { email: 'staff@example.test', verifiedRecipient: true })
    expect(Array.from(container.querySelectorAll('input')).every(input => input.disabled)).toBe(true)
    if (!finish) throw new Error('Yanıt yok')
    await act(async () => finish?.(Response.json({ token: 'synthetic-reset', expiresAt: '2026-10-01T01:00:00Z' })))
    expect(container.querySelector<HTMLInputElement>('#staff-reset-email')?.value).toBe('')
    expect(container.querySelector<HTMLInputElement>('input[type="checkbox"]')?.checked).toBe(false)
    const code = container.querySelector<HTMLInputElement>('#issued-staff-reset')
    if (!code) throw new Error('Kod yok')
    expect(code.type).toBe('password')
    expect(code.readOnly).toBe(true)
    expect(container.textContent).not.toContain('synthetic-reset')
    expect(Array.from(container.querySelectorAll('button')).some(button => button.textContent?.includes('kopyala'))).toBe(false)
    await click('Kodu göster')
    await act(async () => code.focus())
    expect(code.type).toBe('text')
    expect(code.selectionStart).toBe(0)
    expect(code.selectionEnd).toBe('synthetic-reset'.length)
    await click('Kodu gizle')
    expect(code.type).toBe('password')
    await click('Kodu teslim ettim, temizle')
    expect(container.querySelector('#issued-staff-reset')).toBeNull()
  })

  it.each([400, 401, 403, 429, 500])('hata (%i) halinde kod teslimi/başarı iddia etmez, alanları temizler', async status => {
    const post = vi.fn(async () => status === 400 ? Response.json({ title: 'Yalnız mevcut Staff hesabı.' }, { status }) : new Response(null, { status }))
    await act(async () => root.render(<StaffPasswordReset post={post} />))
    await prepare()
    await submit()
    expect(post).toHaveBeenCalledTimes(1)
    expect(container.querySelector('[role="alert"]')).not.toBeNull()
    expect(container.querySelector('#issued-staff-reset')).toBeNull()
    expect(container.querySelector<HTMLInputElement>('#staff-reset-email')?.value).toBe('')
    expect(container.querySelector<HTMLInputElement>('input[type="checkbox"]')?.checked).toBe(false)
  })

  it('belirsiz bağlantıda otomatik tekrar yapmaz', async () => {
    const post = vi.fn(async () => { throw new TypeError('synthetic network failure') })
    await act(async () => root.render(<StaffPasswordReset post={post} />))
    await prepare()
    await submit()
    expect(post).toHaveBeenCalledTimes(1)
    expect(container.textContent).toContain('otomatik tekrar yapılmadı')
    expect(container.querySelector('#issued-staff-reset')).toBeNull()
  })

  it.each([false, true])('kod üretme ekranını yalnız MFA Owner yetkisine sunar (%s)', async ownerAccess => {
    vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/api/staff-invitations/' ? Response.json([]) :
      Response.json({ email: 'synthetic@example.test', staffAccess: !ownerAccess, ownerAccess, mfaEnabled: ownerAccess })))
    await act(async () => root.render(<App />))
    expect(container.querySelector('form[aria-label="Staff sıfırlama kodu üret"]') !== null).toBe(ownerAccess)
  })
})
