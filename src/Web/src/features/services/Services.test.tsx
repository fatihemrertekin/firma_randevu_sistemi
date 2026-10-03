// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import Services from './Services'
import type { ServicePost } from './servicesApi'

const service = { id: 'service-1', name: 'Saç kesimi', durationMinutes: 30, price: '350.00', currency: 'TRY', isActive: true, version: 'version-1' }
let container: HTMLDivElement
let root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('fetch', vi.fn(async () => Response.json({ items: [service], page: 1, hasMore: false })))
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
async function click(text: string) {
  const button = Array.from(container.querySelectorAll('button')).find(item => item.textContent === text)
  if (!button) throw new Error('Düğme yok: ' + text)
  await act(async () => button.click())
}
async function fill(id: string, value: string) {
  const input = container.querySelector<HTMLInputElement>('#service-' + id)
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  if (!input || !setter) throw new Error('Alan yok')
  await act(async () => { setter.call(input, value); input.dispatchEvent(new Event('input', { bubbles: true })) })
}
async function submit() {
  const form = container.querySelector('form')
  if (!form) throw new Error('Form yok')
  await act(async () => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
}
async function render(post = vi.fn<ServicePost>(async () => Response.json(service)), dirty = vi.fn(), busy = vi.fn()) {
  await act(async () => root.render(<Services post={post} onDirtyChange={dirty} onBusyChange={busy} />))
  return { post, dirty, busy }
}
async function newService() { await click('Yeni hizmet'); await fill('name', 'Yeni Hizmet'); await fill('duration', '45'); await fill('price', '0,29') }

describe('Hizmet yönetimi', () => {
  it('tutarı ondalık metin olarak gönderir; çift ve belirsiz eklemede aynı kimliği korur', async () => {
    let finish: ((response: Response) => void) | undefined
    const post = vi.fn<ServicePost>(() => new Promise<Response>(resolve => { finish = resolve }))
    const { busy } = await render(post); await newService(); await submit(); await submit()
    expect(post).toHaveBeenCalledTimes(1); expect(busy).toHaveBeenLastCalledWith(true)
    const request = post.mock.calls[0]?.[1]
    expect(request).toEqual({ id: expect.any(String), name: 'Yeni Hizmet', durationMinutes: 45, price: '0.29' })
    expect(container.textContent).not.toContain('Hizmet kaydedildi.')
    await act(async () => finish?.(new Response(null, { status: 500 })))
    await submit(); expect(post.mock.calls[1]?.[1]).toEqual(request)
    await act(async () => finish?.(Response.json(service, { status: 201 })))
    expect(container.textContent).toContain('Hizmet kaydedildi.'); expect(container.querySelector('form')).toBeNull()
    expect(busy).toHaveBeenLastCalledWith(false)
  })
  it('ad/süre/fiyat alan hatasını sırayla odaklar ve fazla basamağı yuvarlamaz', async () => {
    const { post } = await render(); await click('Yeni hizmet'); await submit()
    expect(document.activeElement).toBe(container.querySelector('#service-name'))
    await fill('name', 'Hizmet'); await fill('duration', '1.5'); await fill('price', '1.005'); await submit()
    expect(document.activeElement).toBe(container.querySelector('#service-duration'))
    await fill('duration', '30'); await submit()
    expect(document.activeElement).toBe(container.querySelector('#service-price'))
    expect(container.querySelector('#service-price')?.getAttribute('aria-invalid')).toBe('true')
    expect(post).not.toHaveBeenCalled()
  })
  it('sunucunun 400 alan hatasını işlem bittikten sonra odaklar ve taslağı korur', async () => {
    await render(vi.fn<ServicePost>(async () => Response.json({ errors: { price: ['Sentetik fiyat doğrulaması.'] } }, { status: 400 })))
    await newService(); await submit()
    expect(container.querySelector<HTMLInputElement>('#service-price')?.value).toBe('0,29')
    expect(document.activeElement).toBe(container.querySelector('#service-price'))
    expect(container.textContent).toContain('Sentetik fiyat doğrulaması.')
    expect(container.textContent).not.toContain('Hizmet kaydedildi.')
  })
  it('409 taslağını korur; güncel ad/süre/fiyatı yükleyip yeni sürümle düzenler', async () => {
    const post = vi.fn<ServicePost>(async () => new Response(null, { status: 409 }))
    await render(post); await click('Düzenle'); await fill('price', '499,99'); await submit()
    expect(post.mock.calls[0]?.[1]).toEqual({ name: 'Saç kesimi', durationMinutes: 30, price: '499.99', version: 'version-1' })
    expect(container.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBe(true)
    expect(container.querySelector<HTMLInputElement>('#service-price')?.value).toBe('499,99')
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ ...service, durationMinutes: 60, price: '900.00', version: 'version-2' }))
    await click('Güncel kaydı yükle'); await fill('price', '999,99'); await submit()
    expect(post.mock.calls[1]?.[1]).toEqual({ name: 'Saç kesimi', durationMinutes: 60, price: '999.99', version: 'version-2' })
  })
  it('durum onayını ve klavye odağını korur; aktif/pasif başarıyı sunucudan sonra gösterir', async () => {
    const post = vi.fn<ServicePost>(async () => Response.json({ ...service, isActive: false, version: 'version-2' }))
    await render(post); expect(container.textContent).toContain('30 dk · 350,00 ₺')
    await click('Pasifleştir'); expect(post).not.toHaveBeenCalled()
    expect(container.querySelector('fieldset')?.textContent).toContain('Kayıt silinmez; hizmetin adı, süresi ve fiyatı korunur.')
    expect(document.activeElement?.textContent).toBe('Durumu değiştir')
    await click('Vazgeç'); expect(document.activeElement?.textContent).toBe('Pasifleştir')
    await click('Pasifleştir')
    vi.mocked(fetch).mockResolvedValue(Response.json({ items: [{ ...service, isActive: false }], page: 1, hasMore: false }))
    await click('Durumu değiştir')
    expect(post.mock.calls[0]?.[1]).toEqual({ isActive: false, version: 'version-1' })
    expect(container.textContent).toContain('Saç kesimi pasifleştirildi.'); expect(container.textContent).toContain('Aktifleştir')
  })
  it.each([[401, 'Oturumunuz sona erdi.'], [403, 'yetkiniz yok.'], [409, 'kaydı değişti.'], [429, 'Çok sık denendi.'], [500, 'Sonuç doğrulanamadı.']])('%s hatasında başarı üretmez; listeyi yenileme sunar', async (status, text) => {
    await render(vi.fn<ServicePost>(async () => new Response(null, { status }))); await click('Pasifleştir'); await click('Durumu değiştir')
    expect(container.querySelector('[role="alert"]')?.textContent).toContain(text)
    expect(container.textContent).not.toContain('pasifleştirildi.')
    await click('Listeyi yenile'); expect(container.querySelector('fieldset')).toBeNull()
  })
  it('yükleme/boş/sayfalama ve bozuk para yanıtını ayırır', async () => {
    let finish: ((response: Response) => void) | undefined
    vi.mocked(fetch).mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve }))
    await render(); expect(container.textContent).toContain('Hizmetler yükleniyor…')
    await act(async () => finish?.(Response.json({ items: [], page: 1, hasMore: true })))
    expect(container.textContent).toContain('Bu sayfada hizmet yok.')
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ items: [service], page: 2, hasMore: false }))
    await click('Sonraki sayfa'); expect(vi.mocked(fetch).mock.calls.at(-1)?.[0]).toBe('/api/services/?page=2')
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ items: [{ ...service, price: 350.01 }], page: 2, hasMore: false }))
    await click('Listeyi yenile'); expect(container.querySelector('[role="alert"]')?.textContent).toContain('Liste yanıtı doğrulanamadı.')
    expect(container.textContent).not.toContain('350,01 ₺')
  })
})
