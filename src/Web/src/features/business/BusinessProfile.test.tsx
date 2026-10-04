// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import BusinessProfile from './BusinessProfile'
import App from '../../App'

let container: HTMLDivElement
let root: Root
const initial = { name: '', phone: null, email: null, address: null, version: 'initial-version' }
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('fetch', vi.fn(async () => Response.json(initial)))
  container = document.createElement('div')
  document.body.append(container)
  root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals() })
async function fill(id: string, value: string) {
  const input = container.querySelector<HTMLInputElement | HTMLTextAreaElement>(id)
  if (!input) throw new Error('Alan yok')
  const prototype = input instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype
  const setter = Object.getOwnPropertyDescriptor(prototype, 'value')?.set
  if (!setter) throw new Error('Alan setter yok')
  await act(async () => { setter.call(input, value); input.dispatchEvent(new Event('input', { bubbles: true })) })
}
async function submit() {
  const form = container.querySelector('form[aria-label="İşletme profilini düzenle"]')
  if (!form) throw new Error('Profil formu yok')
  await act(async () => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
}
async function reload() {
  const button = Array.from(container.querySelectorAll('button')).find(item => item.textContent === 'Güncel bilgileri yükle')
  if (!button) throw new Error('Yenileme yok')
  await act(async () => button.click())
  const confirm = Array.from(container.querySelectorAll('button')).find(item => item.textContent === 'Değişiklikleri sil ve yükle')
  if (confirm) await act(async () => confirm.click())
}

describe('İşletme profili', () => {
  it('taslak özetini alanlarla günceller; başarıdan sonra yalnız sunucunun döndürdüğü bilgileri gösterir', async () => {
    const post = vi.fn(async () => Response.json({ ...initial, name: 'Sunucunun adı', phone: '0212 000 00 00', version: 'saved-version' }))
    await act(async () => root.render(<BusinessProfile post={post} />))
    await fill('#business-name', 'Taslak salon')
    await fill('#business-phone', 'Taslak telefon')
    const summary = container.querySelector('[aria-label="İşletme bilgilerinin özeti"]')
    expect(summary?.textContent).toContain('Taslak özeti')
    expect(summary?.textContent).toContain('Taslak salon')
    expect(summary?.textContent).toContain('Taslak telefon')
    await submit()
    expect(summary?.textContent).toContain('Profil özeti')
    expect(summary?.textContent).toContain('Sunucunun adı')
    expect(summary?.textContent).not.toContain('Taslak telefon')
    expect(container.querySelector<HTMLInputElement>('#business-name')?.value).toBe('Sunucunun adı')
  })
  it('belirsiz kayıt sonucunda özeti doğrulanmış bilgi olarak sunmaz', async () => {
    const post = vi.fn(async () => new Response(null, { status: 500 }))
    await act(async () => root.render(<BusinessProfile post={post} />))
    await fill('#business-name', 'Taslak salon'); await submit()
    const summary = container.querySelector('[aria-label="İşletme bilgilerinin özeti"]')
    expect(summary?.textContent).toContain('Doğrulanmamış bilgiler')
    expect(summary?.textContent).not.toContain('Sunucudan yüklenen')
    expect(container.textContent).not.toContain('İşletme profili kaydedildi.')
  })
  it('boş profili yükler; alanları gönderir, çift kaydı engeller ve sunucu yanıtını gösterir', async () => {
    let finish: ((response: Response) => void) | undefined
    const post = vi.fn<(path: string, body: object, signal?: AbortSignal) => Promise<Response>>(async () => new Promise<Response>(resolve => { finish = resolve }))
    await act(async () => root.render(<BusinessProfile post={post} />))
    expect(container.textContent).toContain('henüz doldurulmadı')
    expect(container.querySelector<HTMLInputElement>('#business-name')?.required).toBe(true)
    await fill('#business-name', 'Örnek Kuaför')
    await fill('#business-phone', '0212 000 00 00')
    await fill('#business-email', 'salon@example.test')
    await fill('#business-address', 'Örnek sokak')
    await submit(); await submit()
    expect(post).toHaveBeenCalledTimes(1)
    expect(post.mock.calls[0]?.slice(0, 2)).toEqual(['/api/business-profile/', {
      name: 'Örnek Kuaför', phone: '0212 000 00 00', email: 'salon@example.test', address: 'Örnek sokak', version: 'initial-version',
    }])
    expect(container.querySelector<HTMLTextAreaElement>('#business-address')?.disabled).toBe(true)
    if (!finish) throw new Error('Yanıt yok')
    await act(async () => finish?.(Response.json({ ...initial, name: 'Örnek Kuaför', phone: '+902120000000', version: 'next-version' })))
    expect(container.querySelector<HTMLInputElement>('#business-phone')?.value).toBe('+902120000000')
    expect(container.textContent).toContain('İşletme profili kaydedildi.')
    await fill('#business-name', 'Yeni ad')
    expect(container.textContent).not.toContain('İşletme profili kaydedildi.')
    await submit()
    expect(post.mock.calls[1]?.[1]).toMatchObject({ name: 'Yeni ad', version: 'next-version' })
  })

  it.each([400, 429])('düzeltilebilir hata (%i) form değerlerini korur ve başarı iddia etmez', async status => {
    const post = vi.fn<(path: string, body: object, signal?: AbortSignal) => Promise<Response>>(async () => Response.json({ title: 'Alanları kontrol edin.' }, { status }))
    await act(async () => root.render(<BusinessProfile post={post} />))
    await fill('#business-name', 'Salon')
    await submit()
    expect(container.querySelector('[role="alert"]')).not.toBeNull()
    expect(container.querySelector<HTMLInputElement>('#business-name')?.value).toBe('Salon')
    expect(container.querySelector<HTMLInputElement>('#business-name')?.disabled).toBe(false)
    expect(container.textContent).not.toContain('İşletme profili kaydedildi.')
  })

  it.each([401, 403, 409, 500])('yenileme gerektiren hata (%i) eski sürümle tekrar kaydetmeyi engeller', async status => {
    const post = vi.fn<(path: string, body: object, signal?: AbortSignal) => Promise<Response>>(async () => new Response(null, { status }))
    await act(async () => root.render(<BusinessProfile post={post} />))
    await fill('#business-name', 'Salon')
    await submit(); await submit()
    expect(post).toHaveBeenCalledTimes(1)
    expect(container.querySelector<HTMLInputElement>('#business-name')?.disabled).toBe(true)
    expect(container.querySelector('[role="alert"]')).not.toBeNull()
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ ...initial, name: 'Sunucudaki ad', version: 'fresh-version' })))
    await reload()
    expect(container.querySelector<HTMLInputElement>('#business-name')?.value).toBe('Sunucudaki ad')
    expect(container.querySelector<HTMLInputElement>('#business-name')?.disabled).toBe(false)
    await submit()
    expect(post.mock.calls[1]?.[1]).toMatchObject({ version: 'fresh-version' })
  })

  it('belirsiz bağlantıda otomatik tekrar yapmaz; başarı veya kayıp bilgi iddia etmez', async () => {
    const post = vi.fn<(path: string, body: object, signal?: AbortSignal) => Promise<Response>>(async () => { throw new TypeError('synthetic failure') })
    await act(async () => root.render(<BusinessProfile post={post} />))
    await fill('#business-name', 'Salon')
    await submit(); await submit()
    expect(post).toHaveBeenCalledTimes(1)
    expect(container.textContent).toContain('otomatik tekrar yapılmadı')
    expect(container.querySelector<HTMLInputElement>('#business-name')?.value).toBe('Salon')
    expect(container.textContent).not.toContain('İşletme profili kaydedildi.')
  })

  it('yüklenmeyen veya bozuk profili düzenlemeye açmaz; yeniden yüklenebilir', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ malformed: true })))
    await act(async () => root.render(<BusinessProfile post={vi.fn()} />))
    expect(container.querySelector('form')).toBeNull()
    expect(container.querySelector('[role="alert"]')).not.toBeNull()
    vi.stubGlobal('fetch', vi.fn(async () => Response.json(initial)))
    await reload()
    expect(container.querySelector('form')).not.toBeNull()
  })

  it.each([false, true])('profil ekranını yalnız MFA Owner yetkisine sunar (%s)', async ownerAccess => {
    vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/api/business-profile/' ? Response.json(initial) :
      path === '/api/staff-invitations/' ? Response.json([]) :
        Response.json({ email: 'synthetic@example.test', staffAccess: !ownerAccess, ownerAccess, mfaEnabled: ownerAccess })))
    await act(async () => root.render(<App />))
    expect(container.querySelector('#business-profile-title') !== null).toBe(ownerAccess)
  })
})
