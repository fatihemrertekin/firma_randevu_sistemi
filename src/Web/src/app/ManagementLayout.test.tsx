// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'

let container: HTMLDivElement
let root: Root
const owner = { email: 'owner@example.test', mfaEnabled: true, ownerAccess: true, staffAccess: false }
const profile = { name: 'Örnek Kuaför', phone: null, email: null, address: null, version: 'synthetic-version' }

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('fetch', vi.fn(async (path: string) => {
    if (path === '/api/auth/mfa/recovery-codes') return Response.json({ remaining: 8 })
    if (path === '/api/auth/me') return Response.json(owner)
    if (path === '/api/auth/recovery-email/') return Response.json({ email: owner.email, verifiedAt: null, deliveryAvailable: false })
    if (path === '/api/business-profile/') return Response.json(profile)
    if (path === '/api/business-hours/') return Response.json({ isConfigured: false, timeZone: 'Europe/Istanbul', version: 'd317d899-8208-41f1-9b8e-c6fbde437cde', days: [] })
    if (path === '/api/staff-invitations/') return Response.json([])
    if (path.startsWith('/api/staff-accounts/')) return Response.json({ items: [], page: 1, hasMore: false })
    if (path.startsWith('/api/staff-members/')) return Response.json({ items: [], page: 1, hasMore: false })
    if (path.startsWith('/api/services/')) return Response.json({ items: [], page: 1, hasMore: false })
    if (path === '/api/auth/csrf') return Response.json({ token: 'synthetic-csrf' })
    if (path === '/api/staff-password-resets/') return Response.json({ token: 'synthetic-delivery-code', expiresAt: '2026-10-01T23:00:00Z' })
    if (path === '/api/auth/logout') return new Response(null, { status: 204 })
    throw new Error('Beklenmeyen test isteği')
  }))
  container = document.createElement('div')
  document.body.append(container)
  root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals() })
async function render() { await act(async () => root.render(<App />)) }
async function click(text: string) {
  const button = Array.from(container.querySelectorAll('button')).find(item => item.textContent === text)
  if (!button) throw new Error('Düğme yok: ' + text)
  await act(async () => button.click())
}
async function fill(id: string, value: string) {
  const input = container.querySelector<HTMLInputElement>('#' + id)
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  if (!input || !setter) throw new Error('Alan yok')
  await act(async () => { setter.call(input, value); input.dispatchEvent(new Event('input', { bubbles: true })) })
}
async function submit(label: string) {
  const form = container.querySelector(`form[aria-label="${label}"]`)
  if (!form) throw new Error('Form yok')
  await act(async () => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
}

describe('Yönetim gezinmesi', () => {
  it('ana grupları ilgili bölüme bağlar; menüyü kapatıp yeniden açar', async () => {
    await render()
    await click('Ekip')
    expect(container.querySelector('h1')?.textContent).toBe('Personel')
    expect(container.querySelector('button[aria-pressed="true"]')?.textContent).toBe('Ekip')
    await click('Çalışan erişimleri')
    expect(container.querySelector('h1')?.textContent).toBe('Çalışan erişimleri')
    await click('Menüyü kapat')
    expect(container.querySelector('#management-context')?.hasAttribute('hidden')).toBe(true)
    await click('Ekip')
    expect(container.querySelector('#management-context')?.hasAttribute('hidden')).toBe(false)
    expect(container.querySelector('h1')?.textContent).toBe('Çalışan erişimleri')
    await click('Hesap')
    expect(container.querySelector('h1')?.textContent).toBe('Hesap ve güvenlik')
  })
  it('mobil menüyü Escape ile kapatır ve odağı menü düğmesine döndürür', async () => {
    vi.stubGlobal('innerWidth', 390)
    await render(); await click('Menü')
    expect(container.querySelector('[aria-controls="management-navigation"]')?.getAttribute('aria-expanded')).toBe('true')
    await act(async () => container.querySelector('button')?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })))
    expect(container.querySelector('[aria-controls="management-navigation"]')?.getAttribute('aria-expanded')).toBe('false')
    expect(document.activeElement).toBe(container.querySelector('[aria-controls="management-navigation"]'))
  })
  it('korunan profil taslağı için çıkışı onaylatır; reddedilince taslağı ve oturumu tutar', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    await render(); await fill('business-name', 'Taslak salon')
    await click('Hesap'); await click('Çıkış yap')
    expect(confirm).toHaveBeenCalledOnce()
    expect(vi.mocked(fetch).mock.calls.some(([path]) => path === '/api/auth/logout')).toBe(false)
    await click('İşletme')
    expect(container.querySelector<HTMLInputElement>('#business-name')?.value).toBe('Taslak salon')
    confirm.mockRestore()
  })
  it('logo bölümü, görüntüsü ve API isteği içermez', async () => {
    await render()
    expect(container.textContent).not.toContain('İşletme logosu')
    expect(container.querySelector('img[alt="İşletme logosu"]')).toBeNull()
    expect(vi.mocked(fetch).mock.calls.some(([path]) => String(path).startsWith('/api/business-logo'))).toBe(false)
  })
  it('işletme saatleri taslağında gezinme ve çıkışı korur', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    await render(); await click('İşletme saatleri')
    await act(async () => container.querySelector<HTMLInputElement>('[aria-label="Pazartesi kapalı"]')?.click())
    await click('Personel'); await click('Çıkış yap'); expect(confirm).toHaveBeenCalledTimes(2)
    expect(container.querySelector<HTMLInputElement>('[aria-label="Pazartesi kapalı"]')?.checked).toBe(false)
    confirm.mockReturnValue(true); await click('Personel'); expect(container.querySelector('[aria-label="Pazartesi kapalı"]')).toBeNull(); confirm.mockRestore()
  })
  it('personel hizmet taslağında gezinme/çıkışı korur; bekleyen kayıt sırasında ikisini de kapatır', async () => {
    const member = { id: 'member-1', name: 'Deneme Personel', isActive: true, version: 'member-version' }
    const service = { id: 'service-1', name: 'Kesim', durationMinutes: 30, price: '350.00', currency: 'TRY', isActive: true, version: 'service-version' }
    let finish: ((response: Response) => void) | undefined
    const original = vi.mocked(fetch).getMockImplementation()
    vi.mocked(fetch).mockImplementation(async (input, options) => {
      if (String(input).startsWith('/api/staff-members/') && String(input).includes('/services')) {
        if (options?.method === 'POST') return new Promise<Response>(resolve => { finish = resolve })
        return Response.json({ member, selected: [], items: [service], page: 1, hasMore: false })
      }
      if (String(input).startsWith('/api/staff-members/')) return Response.json({ items: [member], page: 1, hasMore: false })
      if (!original) throw new Error('Test isteği yok')
      return original(input, options)
    })
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    await render(); await click('Personel'); await click('Hizmetleri seç')
    await act(async () => container.querySelector<HTMLInputElement>('[aria-label="Kesim hizmetini seç"]')?.click())
    await click('Hizmetler'); await click('Çıkış yap'); expect(confirm).toHaveBeenCalledTimes(2)
    expect(container.querySelector<HTMLInputElement>('[aria-label="Kesim hizmetini seç"]')?.checked).toBe(true)
    await submit('Personelin hizmet seçimleri')
    expect(Array.from(container.querySelectorAll('button')).find(button => button.textContent === 'Hizmetler')?.disabled).toBe(true)
    expect(Array.from(container.querySelectorAll('button')).find(button => button.textContent === 'Çıkış yap')?.disabled).toBe(true)
    await act(async () => finish?.(Response.json({ member: { ...member, version: 'new-version' }, selected: [{ id: service.id, version: service.version }] })))
    expect(container.textContent).toContain('Personelin hizmet seçimleri kaydedildi.')
  })
  it('hizmet taslağında gezinme/çıkışı onaylatır ve bekleyen kayıt sırasında ikisini de kapatır', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    await render(); await click('Hizmetler'); await click('Yeni hizmet'); await fill('service-name', 'Taslak Hizmet')
    await click('Personel'); await click('Çıkış yap'); expect(confirm).toHaveBeenCalledTimes(2)
    expect(container.querySelector<HTMLInputElement>('#service-name')?.value).toBe('Taslak Hizmet')
    await fill('service-duration', '30'); await fill('service-price', '350,00')
    let finish: ((response: Response) => void) | undefined
    const original = vi.mocked(fetch).getMockImplementation()
    vi.mocked(fetch).mockImplementation(async (input, options) => {
      if (input === '/api/services/' && options?.method === 'POST') return new Promise<Response>(resolve => { finish = resolve })
      if (!original) throw new Error('Test isteği yok')
      return original(input, options)
    })
    await submit('Hizmet ekle')
    expect(Array.from(container.querySelectorAll<HTMLButtonElement>('nav button')).every(button => button.disabled)).toBe(true)
    expect(Array.from(container.querySelectorAll('button')).find(button => button.textContent === 'Çıkış yap')?.disabled).toBe(true)
    await act(async () => finish?.(Response.json({ id: 'service-1', name: 'Taslak Hizmet', durationMinutes: 30, price: '350.00', currency: 'TRY', isActive: true, version: 'version-1' }, { status: 201 })))
    await click('Personel'); expect(container.querySelector('#service-name')).toBeNull()
    confirm.mockRestore()
  })
  it('kaydedilmemiş personel formundan gezinme ve çıkışı onaylatır; reddedince taslağı korur', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    await render(); await click('Personel'); await click('Yeni personel'); await fill('member-name', 'Taslak Kişi')
    await click('İşletme bilgileri'); await click('Çıkış yap')
    expect(confirm).toHaveBeenCalledTimes(2)
    expect(container.querySelector<HTMLInputElement>('#member-name')?.value).toBe('Taslak Kişi')
    expect(vi.mocked(fetch).mock.calls.some(([path]) => path === '/api/auth/logout')).toBe(false)
    confirm.mockReturnValue(true); await click('İşletme bilgileri'); await click('Personel')
    expect(container.querySelector('#member-name')).toBeNull()
    confirm.mockRestore()
  })
  it('bölüm değiştirirken profil taslağını korur; parola alanlarını temizler ve başlığa odaklanır', async () => {
    await render()
    await fill('business-name', 'Kaydedilmemiş ad')
    await click('Hesap ve güvenlik')
    expect(document.activeElement).toBe(container.querySelector('h1'))
    expect(container.querySelector('nav [aria-current="page"]')?.textContent).toBe('Hesap ve güvenlik')
    expect(container.querySelector('#business-profile-title')?.closest('[hidden]')).not.toBeNull()
    await fill('current-password', 'Synthetic!Password123')
    await click('İşletme bilgileri')
    expect(container.querySelector<HTMLInputElement>('#business-name')?.value).toBe('Kaydedilmemiş ad')
    await click('Hesap ve güvenlik')
    expect(container.querySelector<HTMLInputElement>('#current-password')?.value).toBe('')
  })

  it('profil yenilemesini onaylatır; vazgeçince taslağı, onaylayınca sunucu bilgisini tutar', async () => {
    await render()
    await fill('business-name', 'Taslak')
    await click('Güncel bilgileri yükle')
    expect(container.querySelector<HTMLInputElement>('#business-name')?.value).toBe('Taslak')
    await click('Değişiklikleri koru')
    expect(container.querySelector('[aria-label="Kaydedilmemiş değişiklikler"]')).toBeNull()
    await click('Güncel bilgileri yükle')
    await click('Değişiklikleri sil ve yükle')
    expect(container.querySelector<HTMLInputElement>('#business-name')?.value).toBe(profile.name)
  })

  it('işlem beklerken gezinmeyi ve çıkışı kapatır; teslim kodlarını bölümden ayrılınca temizler', async () => {
    await render()
    await click('Çalışan erişimleri')
    let finish: ((response: Response) => void) | undefined
    const requests = vi.mocked(fetch)
    const original = requests.getMockImplementation()
    requests.mockImplementation(async (input, options) => {
      if (input === '/api/staff-password-resets/') return new Promise<Response>(resolve => { finish = resolve })
      if (!original) throw new Error('Test isteği yok')
      return original(input, options)
    })
    await fill('staff-reset-email', 'staff@example.test')
    const checkbox = container.querySelector<HTMLInputElement>('form[aria-label="Çalışan sıfırlama kodu üret"] input[type="checkbox"]')
    if (!checkbox) throw new Error('Onay yok')
    await act(async () => checkbox.click())
    await submit('Çalışan sıfırlama kodu üret')
    expect(Array.from(container.querySelectorAll<HTMLButtonElement>('nav button')).every(button => button.disabled)).toBe(true)
    expect(Array.from(container.querySelectorAll('button')).find(button => button.textContent === 'Çıkış yap')?.disabled).toBe(true)
    if (!finish) throw new Error('Yanıt yok')
    await act(async () => finish?.(Response.json({ token: 'synthetic-delivery-code', expiresAt: '2026-10-01T23:00:00Z' })))
    expect(container.querySelector('#issued-staff-reset')).not.toBeNull()
    await click('Hesap ve güvenlik')
    expect(container.querySelector('#issued-staff-reset')).toBeNull()
    await click('Çalışan erişimleri')
    expect(container.querySelector('#issued-staff-reset')).toBeNull()
  })

  it('çıkışta yönetim ve taslakları kaldırır; giriş ekranına döner', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    await render()
    await fill('business-name', 'Taslak')
    await click('Çıkış yap')
    expect(confirm).toHaveBeenCalledOnce()
    expect(container.querySelector('nav')).toBeNull()
    expect(container.querySelector('#business-name')).toBeNull()
    expect(container.querySelector('h1')?.textContent).toBe('İşletme girişi')
  })

  it('eşzamanlı iki erişim isteğinin ikisi de bitmeden gezinmeyi açmaz', async () => {
    await render()
    await click('Çalışan erişimleri')
    const completions: Record<string, (response: Response) => void> = {}
    const requests = vi.mocked(fetch)
    const original = requests.getMockImplementation()
    requests.mockImplementation(async (input, options) => {
      if ((input === '/api/staff-invitations/' || input === '/api/staff-password-resets/') && options?.method === 'POST') {
        return new Promise<Response>(resolve => { completions[String(input)] = resolve })
      }
      if (!original) throw new Error('Test isteği yok')
      return original(input, options)
    })
    await fill('invite-email', 'invite@example.test')
    await fill('staff-reset-email', 'staff@example.test')
    for (const checkbox of container.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')) await act(async () => checkbox.click())
    await submit('Çalışan daveti oluştur')
    await submit('Çalışan sıfırlama kodu üret')
    await act(async () => completions['/api/staff-invitations/']?.(Response.json({ id: 'invite', token: 'synthetic-invite', expiresAt: '2026-10-02T01:00:00Z' })))
    expect(Array.from(container.querySelectorAll<HTMLButtonElement>('nav button')).every(button => button.disabled)).toBe(true)
    await act(async () => completions['/api/staff-password-resets/']?.(Response.json({ token: 'synthetic-reset', expiresAt: '2026-10-01T23:00:00Z' })))
    expect(Array.from(container.querySelectorAll<HTMLButtonElement>('nav button')).every(button => !button.disabled)).toBe(true)
  })

  it.each([
    { ...owner, ownerAccess: false, staffAccess: true, mfaEnabled: false },
    { ...owner, staffAccess: true },
  ])('çalışan görünümünde işletme ve erişim yönetimini açmaz', async account => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json(account)))
    await render()
    const navigationText = Array.from(container.querySelectorAll('nav')).map(nav => nav.textContent).join(' ')
    expect(navigationText).toContain('Hesap')
    expect(navigationText).toContain('Hesap ve güvenlik')
    for (const label of ['İşletme', 'Ekip', 'Personel', 'Hizmetler', 'Çalışan erişimleri', 'Değişiklik kayıtları']) {
      expect(navigationText).not.toContain(label)
    }
    expect(container.querySelector('#business-name')).toBeNull()
    expect(container.textContent).not.toContain('Davet oluştur')
    expect(container.querySelector('form[aria-label="Parola değiştirme"]')).not.toBeNull()
  })

  it('geçici MFA oturumuna yönetim bölümleri sunmaz', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ ...owner, ownerAccess: false })))
    await render()
    expect(container.querySelector('nav')).toBeNull()
    expect(container.querySelector('h1')?.textContent).toBe('Yeniden giriş yapın')
  })

  it('profil alanı hatasını alanla ilişkilendirir; sunucu hatasında açıklamaya odaklanır', async () => {
    await render()
    await fill('business-email', 'gecersiz-adres')
    const button = container.querySelector<HTMLButtonElement>('button[type="submit"]')
    if (!button) throw new Error('Kaydet yok')
    await act(async () => button.click())
    expect(container.querySelector('#business-email')?.getAttribute('aria-invalid')).toBe('true')
    expect(container.querySelector('#business-email')?.getAttribute('aria-describedby')).toBe('business-field-error')
    await fill('business-email', 'salon@example.test')
    expect(container.querySelector('#business-email')?.hasAttribute('aria-invalid')).toBe(false)
    const requests = vi.mocked(fetch)
    const original = requests.getMockImplementation()
    requests.mockImplementation(async (input, options) => {
      if (input === '/api/business-profile/' && options?.method === 'POST') return Response.json({ title: 'Telefon alanını kontrol edin.' }, { status: 400 })
      if (!original) throw new Error('Test isteği yok')
      return original(input, options)
    })
    await submit('İşletme profilini düzenle')
    expect(document.activeElement?.getAttribute('role')).toBe('alert')
    expect(document.activeElement?.textContent).toContain('Telefon alanını kontrol edin.')
  })

  it('giriş ve ikinci adım tamamlanmadan işletme yönetimini göstermez', async () => {
    let loggedIn = false
    const requests = vi.fn(async (path: string) => {
      if (path === '/api/auth/me') return loggedIn ? Response.json(owner) : new Response(null, { status: 401 })
      if (path === '/api/auth/csrf') return Response.json({ token: 'synthetic-csrf' })
      if (path === '/api/auth/login') return new Response(null, { status: 202 })
      if (path === '/api/auth/mfa/login') { loggedIn = true; return new Response(null, { status: 204 }) }
      if (path === '/api/business-profile/') return Response.json(profile)
      throw new Error('Beklenmeyen test isteği')
    })
    vi.stubGlobal('fetch', requests)
    await render()
    await fill('email', owner.email)
    await fill('password', 'Synthetic!Password123')
    const form = container.querySelector('form')
    if (!form) throw new Error('Giriş yok')
    await act(async () => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
    expect(container.querySelector('nav')).toBeNull()
    expect(container.querySelector('h1')?.textContent).toBe('İkinci adımı tamamlayın')
    await fill('mfa-code', '123456')
    const mfaForm = container.querySelector('form')
    if (!mfaForm) throw new Error('İkinci adım yok')
    await act(async () => mfaForm.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
    expect(container.querySelector('nav')).not.toBeNull()
    expect(container.querySelector('h1')?.textContent).toBe('İşletme bilgileri')
    expect(requests.mock.calls.filter(([path]) => path === '/api/auth/csrf')).toHaveLength(2)
  })
})
