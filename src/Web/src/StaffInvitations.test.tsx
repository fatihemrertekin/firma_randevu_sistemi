// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import StaffInvitations from './StaffInvitations'

let container: HTMLDivElement
let root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  container = document.createElement('div')
  document.body.append(container)
  root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals() })
async function fill(id: string, value: string) {
  const input = container.querySelector<HTMLInputElement>(`#${id}`)
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  if (!input || !setter) throw new Error('Davet alanı yok')
  await act(async () => { setter.call(input, value); input.dispatchEvent(new Event('input', { bubbles: true })) })
}
async function click(text: string) {
  const button = Array.from(container.querySelectorAll('button')).find(item => item.textContent === text)
  if (!button) throw new Error('Davet butonu yok')
  await act(async () => button.click())
}
async function submit(label: string) {
  const form = container.querySelector(`form[aria-label="${label}"]`)
  if (!form) throw new Error('Davet formu yok')
  await act(async () => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
}
async function acceptApp(response: () => Promise<Response>) {
  const requests = vi.fn(async (path: string) => {
    if (path === '/api/auth/me') return new Response(null, { status: 401 })
    if (path === '/api/auth/csrf') return Response.json({ token: 'synthetic-csrf' })
    if (path === '/api/staff-invitations/accept') return response()
    throw new Error('Beklenmeyen istek')
  })
  vi.stubGlobal('fetch', requests)
  await act(async () => root.render(<App />))
  await click('Staff davetim var')
  return requests
}
async function fillAccept(confirm = 'Synthetic!Staff123') {
  await fill('accept-email', 'staff@example.test')
  await fill('accept-token', 'synthetic-invitation')
  await fill('accept-password', 'Synthetic!Staff123')
  await fill('accept-confirm', confirm)
}

describe('Staff daveti', () => {
  it('alıcı onayı ister, çift üretimi engeller; kodu geçici/maskeli gösterir ve iptalde temizler', async () => {
    let issued = false
    vi.stubGlobal('fetch', vi.fn(async () => Response.json(issued ? [{ id: 'invite-1', email: 'staff@example.test', expiresAt: '2026-10-02T00:00:00Z' }] : [])))
    let finish: ((response: Response) => void) | undefined
    const waiting = new Promise<Response>(resolve => { finish = resolve })
    const post = vi.fn(async (path: string) => {
      if (path.endsWith('/revoke')) { issued = false; return new Response(null, { status: 204 }) }
      return waiting
    })
    await act(async () => root.render(<StaffInvitations post={post} />))
    await fill('invite-email', 'staff@example.test')
    expect(container.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBe(true)
    await submit('Staff daveti oluştur')
    expect(post).not.toHaveBeenCalled()
    const checkbox = container.querySelector<HTMLInputElement>('input[type="checkbox"]')
    if (!checkbox) throw new Error('Alıcı onayı yok')
    await act(async () => checkbox.click())
    await submit('Staff daveti oluştur')
    await submit('Staff daveti oluştur')
    expect(post).toHaveBeenCalledTimes(1)
    expect(container.querySelector<HTMLInputElement>('#invite-email')?.disabled).toBe(true)
    if (!finish) throw new Error('Yanıt yok')
    issued = true
    await act(async () => finish?.(Response.json({ id: 'invite-1', token: 'synthetic-token', expiresAt: '2026-10-02T00:00:00Z' })))
    expect(container.querySelector<HTMLInputElement>('#issued-invitation')?.type).toBe('password')
    expect(container.querySelector<HTMLInputElement>('#issued-invitation')?.value).toBe('synthetic-token')
    expect(container.textContent).not.toContain('synthetic-token')
    await click('Daveti iptal et')
    expect(container.querySelector('#issued-invitation')).toBeNull()
    expect(container.textContent).toContain('Geçerli bekleyen davet yok')
  })

  it('kabulde çift gönderimi engeller ve otomatik giriş yerine normal girişe döner', async () => {
    let finish: ((response: Response) => void) | undefined
    const waiting = new Promise<Response>(resolve => { finish = resolve })
    const requests = await acceptApp(() => waiting)
    await fillAccept()
    await submit('Staff davetini kabul et')
    await submit('Staff davetini kabul et')
    expect(requests.mock.calls.filter(([path]) => path === '/api/staff-invitations/accept')).toHaveLength(1)
    expect(Array.from(container.querySelectorAll<HTMLInputElement>('input')).every(input => input.disabled)).toBe(true)
    if (!finish) throw new Error('Yanıt yok')
    await act(async () => finish?.(new Response(null, { status: 204 })))
    expect(container.textContent).toContain('Staff hesabınız açıldı')
    expect(container.textContent).toContain('İşletme girişi')
    expect(container.querySelector('#accept-token')).toBeNull()
    expect(container.querySelector<HTMLInputElement>('#password')?.value).toBe('')
  })

  it('kodu açık onayla gösterip elle seçmeye izin verir, temizlemede kodu kaldırır', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json([])))
    const post = vi.fn(async () => Response.json({ id: 'invite-1', token: 'synthetic-token', expiresAt: '2026-10-02T00:00:00Z' }))
    await act(async () => root.render(<StaffInvitations post={post} />))
    await fill('invite-email', 'staff@example.test')
    const checkbox = container.querySelector<HTMLInputElement>('input[type="checkbox"]')
    if (!checkbox) throw new Error('Alıcı onayı yok')
    await act(async () => checkbox.click())
    await submit('Staff daveti oluştur')
    expect(Array.from(container.querySelectorAll('button')).some(button => button.textContent === 'Davet kodunu kopyala')).toBe(false)
    expect(container.querySelector<HTMLInputElement>('#issued-invitation')?.type).toBe('password')
    await click('Kodu göster')
    const input = container.querySelector<HTMLInputElement>('#issued-invitation')
    if (!input) throw new Error('Kod alanı yok')
    await act(async () => input.focus())
    expect(input.type).toBe('text')
    expect(input.selectionStart).toBe(0)
    expect(input.selectionEnd).toBe('synthetic-token'.length)
    await click('Kodu gizle')
    expect(input.type).toBe('password')
    await click('Kodu teslim ettim, temizle')
    expect(container.querySelector('#issued-invitation')).toBeNull()
  })

  it('parola uyuşmazlığını göndermeden reddeder; iptal/yeniden açmada sırları temizler', async () => {
    const requests = await acceptApp(async () => new Response(null, { status: 204 }))
    await fillAccept('different')
    await submit('Staff davetini kabul et')
    expect(container.textContent).toContain('aynı olmalı')
    expect(requests.mock.calls.some(([path]) => path === '/api/staff-invitations/accept')).toBe(false)
    expect(container.querySelector<HTMLInputElement>('#accept-password')?.value).toBe('')
    await click('Girişe dön')
    await click('Staff davetim var')
    expect(container.querySelector<HTMLInputElement>('#accept-token')?.value).toBe('')
  })

  it.each([400, 429, 500])('hata/limit (%i) yanıtında sırları temizler, başarı iddia etmez', async status => {
    await acceptApp(async () => status === 400 ? Response.json({ title: 'Davet geçersiz.' }, { status }) : new Response(null, { status }))
    await fillAccept()
    await submit('Staff davetini kabul et')
    expect(container.querySelector('[role="alert"]')).not.toBeNull()
    expect(container.querySelector<HTMLInputElement>('#accept-token')?.value).toBe('')
    expect(container.querySelector<HTMLInputElement>('#accept-password')?.value).toBe('')
    expect(container.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBe(false)
  })

  it('bağlantı belirsizliğinde otomatik tekrar yapmaz', async () => {
    const requests = await acceptApp(async () => { throw new TypeError('synthetic network failure') })
    await fillAccept()
    await submit('Staff davetini kabul et')
    expect(container.textContent).toContain('otomatik tekrar yapılmadı')
    expect(requests.mock.calls.filter(([path]) => path === '/api/staff-invitations/accept')).toHaveLength(1)
  })

  it('Staff kendi hesabını görür, Owner formlarını göremez', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ email: 'staff@example.test', staffAccess: true, ownerAccess: false, mfaEnabled: false })))
    await act(async () => root.render(<App />))
    expect(container.textContent).toContain('Staff hesabınız açık')
    expect(container.textContent).toContain('staff@example.test')
    expect(container.querySelector('form')).toBeNull()
    expect(container.textContent).not.toContain('İki aşamalı girişi kurun')
  })
})
