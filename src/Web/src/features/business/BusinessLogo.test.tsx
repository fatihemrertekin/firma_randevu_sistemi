// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import BusinessLogo from './BusinessLogo'
import { readLogo, type Logo } from './businessLogoApi'
import type { postWithCsrf } from '../../app/api'

const initial: Logo = { hasLogo: false, version: 'a4ae913c-2ed5-4ebd-8b86-8ae3d311b348', imageUrl: null, width: null, height: null }
const saved: Logo = { hasLogo: true, version: 'b4ae913c-2ed5-4ebd-8b86-8ae3d311b348', imageUrl: '/api/business-logo/image/b4ae913c-2ed5-4ebd-8b86-8ae3d311b348', width: 40, height: 20 }
let container: HTMLDivElement, root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); vi.stubGlobal('fetch', vi.fn(async () => Response.json(initial)))
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.useRealTimers(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
async function render(post = vi.fn<typeof postWithCsrf>(async () => Response.json(saved))) {
  const dirty = vi.fn(), busy = vi.fn(), updated = vi.fn()
  await act(async () => root.render(<BusinessLogo post={post} onDirtyChange={dirty} onBusyChange={busy} onSaved={updated} />))
  return { post, dirty, busy, updated }
}
function field() { const input = container.querySelector<HTMLInputElement>('#logo-file'); if (!input) throw new Error('Dosya alanı yok'); return input }
async function choose(file = new File(['synthetic-image'], 'logo.png', { type: 'image/png' })) {
  await act(async () => {
    Object.defineProperty(field(), 'files', { configurable: true, value: [file] }); field().dispatchEvent(new Event('change', { bubbles: true }))
    await new Promise(resolve => setTimeout(resolve, 30))
  })
}
async function submit() { await act(async () => container.querySelector('form')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))) }
async function reload() { await act(async () => Array.from(container.querySelectorAll('button')).find(button => button.textContent === 'Güncel logoyu yükle')?.click()) }

describe('İşletme logosu', () => {
  it('dosya seçimini yalnız önizler; kaydetmeden sunucuya göndermez', async () => {
    const { post, dirty, updated } = await render(); expect(container.textContent).toContain('Henüz işletme logosu yüklenmedi.')
    await choose(); expect(post).not.toHaveBeenCalled(); expect(dirty).toHaveBeenLastCalledWith(true)
    expect(container.querySelector('img[alt="Yeni logo önizlemesi"]')).not.toBeNull()
    await submit(); expect(post).toHaveBeenCalledWith('/api/business-logo/', { version: initial.version, image: btoa('synthetic-image') }, expect.any(AbortSignal))
    expect(updated).toHaveBeenCalledOnce(); expect(dirty).toHaveBeenLastCalledWith(false); expect(container.textContent).toContain('İşletme logosu kaydedildi.')
  })
  it('uygunsuz veya büyük dosyayı göndermeden görünür hata ve odak verir', async () => {
    const { post } = await render()
    for (const file of [new File(['<svg/>'], 'logo.svg', { type: 'image/svg+xml' }), new File([new Uint8Array(1024 * 1024 + 1)], 'large.png', { type: 'image/png' })]) {
      await choose(file); await submit(); expect(post).not.toHaveBeenCalled(); expect(field().getAttribute('aria-invalid')).toBe('true'); expect(document.activeElement).toBe(field())
    }
  })
  it('iki gönderimi engeller; başarılı yanıt gelmeden başarı göstermez', async () => {
    let finish: ((response: Response) => void) | undefined
    const post = vi.fn<typeof postWithCsrf>(() => new Promise<Response>(resolve => { finish = resolve }))
    const { busy } = await render(post); await choose(); await submit(); await submit()
    expect(post).toHaveBeenCalledOnce(); expect(busy).toHaveBeenLastCalledWith(true); expect(container.textContent).not.toContain('İşletme logosu kaydedildi.')
    await act(async () => finish?.(Response.json(saved))); expect(busy).toHaveBeenLastCalledWith(false)
  })
  it.each([401, 403, 409, 500])('%i hatasında seçimi korur ve güncel sürüm okunmadan tekrar kaydetmez', async status => {
    const { post } = await render(vi.fn<typeof postWithCsrf>(async () => new Response(null, { status }))); await choose(); await submit(); await submit()
    expect(post).toHaveBeenCalledOnce(); expect(container.querySelector('img[alt="Yeni logo önizlemesi"]')).not.toBeNull()
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false); await reload(); expect(container.querySelector('img[alt="Yeni logo önizlemesi"]')).not.toBeNull()
    confirm.mockReturnValue(true); await reload(); expect(container.querySelector('img[alt="Yeni logo önizlemesi"]')).toBeNull()
  })
  it('400 alan hatasını ve 413 boyut hatasını dosya yanında gösterir', async () => {
    const { post } = await render(vi.fn<typeof postWithCsrf>(async () => Response.json({ errors: { image: ['Piksel sınırını kontrol edin.'] } }, { status: 400 })))
    await choose(); await submit(); expect(container.textContent).toContain('Piksel sınırını kontrol edin.'); expect(document.activeElement).toBe(field())
    post.mockResolvedValue(new Response(null, { status: 413 })); await submit(); expect(container.textContent).toContain('Logo dosyası çok büyük.')
  })
  it('429 geri sayımı boyunca kaydı engeller ve sonra tekrar denenebilir', async () => {
    const { post } = await render(vi.fn<typeof postWithCsrf>(async () => new Response(null, { status: 429, headers: { 'Retry-After': '2' } })))
    await choose(); vi.useFakeTimers(); await submit(); await submit(); expect(post).toHaveBeenCalledOnce(); expect(container.textContent).toContain('2 saniye sonra')
    await act(async () => { await vi.advanceTimersByTimeAsync(1000) }); await act(async () => { await vi.advanceTimersByTimeAsync(1000) }); vi.useRealTimers(); await submit(); expect(post).toHaveBeenCalledTimes(2)
  })
  it('seçimde sekme kapanışını uyarır; başarısız yeniden okumada seçimi korur', async () => {
    await render(); await choose(); const event = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(event); expect(event.defaultPrevented).toBe(true)
    vi.spyOn(window, 'confirm').mockReturnValue(true); vi.mocked(fetch).mockRejectedValueOnce(new Error('ağ')); await reload()
    expect(container.querySelector('img[alt="Yeni logo önizlemesi"]')).not.toBeNull()
  })
  it('ilk yükleme hatası tekrar okunabilir; dış URL veya bozuk yanıt başarı sayılmaz', async () => {
    vi.mocked(fetch).mockRejectedValueOnce(new Error('ağ')); await render(); expect(container.querySelector('form')).toBeNull(); await reload(); expect(container.querySelector('form')).not.toBeNull()
    for (const value of [{ ...saved, imageUrl: 'https://example.test/logo.png' }, { ...saved, width: 513 }, { ...initial, width: 1 }]) await expect(readLogo(Response.json(value))).rejects.toThrow()
  })
})
