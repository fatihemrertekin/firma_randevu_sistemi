// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'
import { authPaths, safeReturnPath, sectionPaths } from './routes'

const owner = { email: 'owner@example.test', mfaEnabled: true, ownerAccess: true, staffAccess: false }
const staff = { email: 'staff@example.test', mfaEnabled: false, ownerAccess: false, staffAccess: true }
const member = { id: 'member-1', name: 'Sentetik kişi', isActive: true, version: 'v1' }
const service = { id: 'service-1', name: 'Sentetik hizmet', durationMinutes: 30, price: '350.00', currency: 'TRY', isActive: true, version: 'v1' }
let account: typeof owner | null, container: HTMLDivElement, root: Root
beforeEach(() => {
  account = owner
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('fetch', vi.fn(async (input: string, options?: RequestInit) => {
    if (input === '/api/auth/me') return account ? Response.json(account) : new Response(null, { status: 401 })
    if (input === '/api/auth/csrf') return Response.json({ token: 'synthetic-csrf' })
    if (input === '/api/auth/login') return new Response(null, { status: 202 })
    if (input === '/api/auth/mfa/login') { account = owner; return new Response(null, { status: 204 }) }
    if (input === '/api/auth/logout') { account = null; return new Response(null, { status: 204 }) }
    if (input === '/api/business-profile/') return Response.json({ name: 'Sentetik salon', phone: null, email: null, address: null, version: 'v1' })
    if (input === '/api/auth/mfa/recovery-codes') return Response.json({ remaining: 8 })
    if (input === '/api/auth/recovery-email/') return Response.json({ email: owner.email, verifiedAt: null, deliveryAvailable: false })
    if (input.startsWith('/api/staff-members/?')) return Response.json({ items: [member], page: Number(new URLSearchParams(input.split('?')[1]).get('page')), hasMore: true })
    if (input === '/api/staff-members/member-1') return Response.json(member)
    if (input === '/api/staff-members/member-1/services?page=1') return Response.json({ member, selected: [], items: [], page: 1, hasMore: false })
    if (input.startsWith('/api/services/?')) return Response.json({ items: [service], page: 1, hasMore: false })
    if (input === '/api/services/service-1' && options?.method !== 'POST') return Response.json(service)
    throw new Error('Beklenmeyen sentetik istek: ' + input)
  }))
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.restoreAllMocks(); vi.unstubAllGlobals() })
async function render(path: string) {
  window.history.replaceState(null, '', path)
  await act(async () => root.render(<App />))
}
async function click(text: string) {
  const control = Array.from(container.querySelectorAll<HTMLElement>('button,a')).find(item => item.textContent === text)
  if (!control) throw new Error('Kontrol yok: ' + text)
  await act(async () => control.click())
}
async function fill(id: string, value: string) {
  const input = container.querySelector<HTMLInputElement>('#' + id)
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  if (!input || !setter) throw new Error('Alan yok: ' + id)
  await act(async () => { setter.call(input, value); input.dispatchEvent(new Event('input', { bubbles: true })) })
}
async function submit(label?: string) {
  const form = container.querySelector(label ? `form[aria-label="${label}"]` : 'form')
  await act(async () => form?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
}
async function history(direction: 'back' | 'forward') {
  await act(async () => { window.history[direction](); await new Promise(resolve => setTimeout(resolve, 80)) })
  // A confirmed POP queues a second native history event after the blocker effect.
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 80)) })
}

describe('Ekran URL ve geçmiş kabulü', () => {
  it('doğrudan adres ve yeni mount seçili ekranı korur; menüler gerçek bağlantıdır', async () => {
    await render(sectionPaths.services)
    expect(container.querySelector('h1')?.textContent).toBe('Hizmetler')
    expect(container.querySelector('nav a[aria-current="page"]')?.getAttribute('href')).toBe(sectionPaths.services)
    await act(async () => root.unmount()); root = createRoot(container)
    await act(async () => root.render(<App />))
    expect(container.querySelector('h1')?.textContent).toBe('Hizmetler')
    await click('Personel'); expect(window.location.pathname).toBe(sectionPaths.personnel)
    await history('back'); expect(window.location.pathname).toBe(sectionPaths.services)
    await history('forward'); expect(container.querySelector('h1')?.textContent).toBe('Personel')
  })
  it('liste sayfasını ayrıntı bağlantısında taşır ve geri dönüşte aynı sayfayı açar', async () => {
    await render(sectionPaths.personnel + '?sayfa=2')
    await click('Ayrıntılar')
    expect(window.location.pathname).toBe(sectionPaths.personnel + '/member-1')
    expect(window.location.search).toBe('?sayfa=2')
    await click('Personel listesine dön')
    expect(window.location.search).toBe('?sayfa=2')
    expect(container.textContent).toContain('Sayfa 2')
    expect(document.activeElement?.textContent).toBe('Ayrıntılar')
  })
  it('doğrudan personel görevi ve hizmet düzenleme adresini sunucudan yükler', async () => {
    await render(sectionPaths.personnel + '/member-1/hizmetler')
    expect(container.querySelector('nav[aria-label="Personel görevleri"] [aria-current="page"]')?.textContent).toBe('Hizmetler')
    await click('Hizmetler')
    // The section link, rather than the selected personnel task, opens the service list.
    const link = container.querySelector<HTMLAnchorElement>(`a[href="${sectionPaths.services}"]`)
    await act(async () => link?.click())
    await click('Düzenle')
    expect(window.location.pathname).toBe(sectionPaths.services + '/service-1/duzenle')
    expect(container.querySelector<HTMLInputElement>('#service-name')?.value).toBe(service.name)
    expect(vi.mocked(fetch).mock.calls.some(([input]) => input === '/api/services/service-1')).toBe(true)
  })
  it('taslakta geri geçişini reddedince adresi ve alanları korur; onaylayınca geçer', async () => {
    await render(sectionPaths.services); await click('Yeni hizmet'); await fill('service-name', 'Kaydedilmemiş')
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    await history('back')
    expect(confirm).toHaveBeenCalledOnce()
    expect(window.location.pathname).toBe(sectionPaths.services + '/yeni')
    expect(container.querySelector<HTMLInputElement>('#service-name')?.value).toBe('Kaydedilmemiş')
    confirm.mockReturnValue(true); await history('back')
    expect(window.location.pathname).toBe(sectionPaths.services)
    expect(container.querySelector('#service-name')).toBeNull()
  })
  it('bekleyen kayıt sırasında geçmiş geçişini reddeder; sunucu onayı formu kapatır', async () => {
    await render(sectionPaths.services); await click('Yeni hizmet')
    await fill('service-name', 'Yeni'); await fill('service-duration', '30'); await fill('service-price', '350')
    let finish: ((response: Response) => void) | undefined
    const original = vi.mocked(fetch).getMockImplementation()
    vi.mocked(fetch).mockImplementation(async (input, options) => input === '/api/services/' && options?.method === 'POST'
      ? new Promise<Response>(resolve => { finish = resolve }) : original ? original(input, options) : new Response(null, { status: 500 }))
    const confirm = vi.spyOn(window, 'confirm')
    await submit('Hizmet ekle'); await history('back')
    expect(window.location.pathname).toBe(sectionPaths.services + '/yeni'); expect(confirm).not.toHaveBeenCalled()
    await act(async () => finish?.(Response.json(service, { status: 201 })))
    expect(window.location.pathname).toBe(sectionPaths.services)
    expect(container.textContent).toContain('Hizmet kaydedildi.')
  })
  it('profil taslağını menü ve geçmiş geçişinde bellekte tutar', async () => {
    await render(sectionPaths.business); await fill('business-name', 'Taslak ad'); await click('Personel')
    await history('back')
    expect(container.querySelector<HTMLInputElement>('#business-name')?.value).toBe('Taslak ad')
  })
  it('giriş ve MFA sonrasında yalnız istenen yetkili adrese döner', async () => {
    account = null; await render(sectionPaths.services)
    expect(window.location.pathname).toBe(authPaths.login)
    expect(new URLSearchParams(window.location.search).get('donus')).toBe(sectionPaths.services)
    await fill('email', owner.email); await fill('password', 'Synthetic!Owner123'); await submit()
    expect(container.textContent).toContain('İkinci adımı tamamlayın')
    expect(container.querySelector('#management-main')).toBeNull()
    await fill('mfa-code', '123456'); await submit()
    expect(window.location.pathname).toBe(sectionPaths.services)
    expect(container.querySelector('h1')?.textContent).toBe('Hizmetler')
  })
  it('Staff doğrudan Owner adresinde veri istemez; çıkıştan sonra geri yetki kazandırmaz', async () => {
    account = staff; await render(sectionPaths.services)
    expect(container.querySelector('h1')?.textContent).toBe('Erişim izni yok')
    expect(vi.mocked(fetch).mock.calls.some(([input]) => String(input).startsWith('/api/services'))).toBe(false)
    await click('Yetkili ekrana dön'); await click('Çıkış yap'); await history('back')
    expect(container.querySelector('h1')?.textContent).toBe('İşletme girişi')
    expect(container.querySelector('nav')).toBeNull()
  })
  it('bilinmeyen ekran için hata ve yetkili dönüş bağlantısı verir', async () => {
    await render('/yonetim/olmayan-ekran')
    expect(container.querySelector('h1')?.textContent).toBe('Sayfa bulunamadı')
    expect(container.querySelector<HTMLAnchorElement>(`a[href="${sectionPaths.business}"]`)).not.toBeNull()
  })
  it.each(['https://example.test', '//example.test', '/\\example.test', '/api/auth/me', '/yonetim/olmayan', '/yonetim/personel#secret'])('güvensiz dönüş adresini kabul etmez: %s', value => {
    expect(safeReturnPath(value)).toBeNull()
  })
  it('dönüş adresindeki bilinmeyen parametreleri taşımadan liste sayfasını tutar', () => {
    expect(safeReturnPath('/yonetim/personel?sayfa=2&token=discard')).toBe('/yonetim/personel?sayfa=2')
  })
})
