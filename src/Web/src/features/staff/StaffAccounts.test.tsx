// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import StaffAccounts from './StaffAccounts'
import { createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'

const account = { id: 'staff-1', email: 'staff@example.test', isActive: true, version: 'version-1' }
let container: HTMLDivElement
let root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('fetch', vi.fn(async () => Response.json({ items: [account], page: 1, hasMore: false, pageSize: 20, totalCount: 1 })))
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals() })
async function click(label: string) {
  const button = Array.from(container.querySelectorAll('button')).find(item => item.textContent === label || item.getAttribute('aria-label') === label)
  if (!button) throw new Error('Düğme yok: ' + label)
  await act(async () => button.click())
}
async function render(post = vi.fn(async () => new Response(null, { status: 204 }))) {
  const router = createMemoryRouter([{ path: '*', element: <StaffAccounts post={post} /> }], { initialEntries: ['/yonetim/calisan-erisimleri'] })
  await act(async () => root.render(<RouterProvider router={router} />))
  return post
}

describe('Çalışan hesapları', () => {
  it('pasif hesabı onayla yeniden etkinleştirir; sürüm ve çift gönderim korumasını kullanır', async () => {
    vi.mocked(fetch).mockResolvedValue(Response.json({ items: [{ ...account, isActive: false }], page: 1, hasMore: false, pageSize: 20, totalCount: 1 }))
    let finish: ((response: Response) => void) | undefined
    const post = vi.fn(() => new Promise<Response>(resolve => { finish = resolve }))
    await render(post)
    await click('Etkinleştir')
    expect(container.querySelector('[aria-label="Hesabı etkinleştirme onayı"]')?.textContent).toContain(account.email)
    expect(document.activeElement?.textContent).toBe('Hesabı etkinleştir')
    expect(post).not.toHaveBeenCalled()
    await click('Vazgeç')
    expect(document.activeElement?.textContent).toBe('Etkinleştir')
    await click('Etkinleştir'); await click('Hesabı etkinleştir'); await click('Etkinleştiriliyor…')
    expect(post).toHaveBeenCalledTimes(1)
    expect(post.mock.calls[0]).toEqual(['/api/staff-accounts/staff-1/activate', { version: 'version-1' }, expect.any(AbortSignal)])
    expect(container.textContent).not.toContain('Çalışan hesabı etkinleştirildi.')
    vi.mocked(fetch).mockResolvedValue(Response.json({ items: [account], page: 1, hasMore: false, pageSize: 20, totalCount: 1 }))
    if (!finish) throw new Error('Bekleyen istek yok')
    await act(async () => finish?.(new Response(null, { status: 204 })))
    expect(container.textContent).toContain('Çalışan hesabı etkinleştirildi.')
    expect(container.textContent).toContain('Pasifleştir')
    expect(container.querySelector('fieldset')).toBeNull()
  })

  it('etkinleştirme çatışmasında başarı uydurmaz; yenileyerek sunucu durumunu gösterir', async () => {
    vi.mocked(fetch).mockResolvedValue(Response.json({ items: [{ ...account, isActive: false }], page: 1, hasMore: false, pageSize: 20, totalCount: 1 }))
    await render(vi.fn(async () => new Response(null, { status: 409 })))
    await click('Etkinleştir'); await click('Hesabı etkinleştir')
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Hesap değişti.')
    expect(container.textContent).not.toContain('Çalışan hesabı etkinleştirildi.')
    vi.mocked(fetch).mockResolvedValue(Response.json({ items: [account], page: 1, hasMore: false, pageSize: 20, totalCount: 1 }))
    await click('Listeyi yenile')
    expect(container.querySelector('fieldset')).toBeNull()
    expect(container.textContent).toContain('Pasifleştir')
  })

  it('pasifleştirmeden önce hesabı ve etkilerini onaylatır, vazgeçince odağı geri verir', async () => {
    const post = await render()
    await click('Pasifleştir')
    const confirmation = container.querySelector('[aria-label="Hesabı pasifleştirme onayı"]')
    expect(confirmation?.textContent).toContain(account.email)
    expect(confirmation?.textContent).toContain('Açık oturumları')
    expect(document.activeElement?.textContent).toBe('Hesabı pasifleştir')
    expect(post).not.toHaveBeenCalled()
    await click('Vazgeç')
    expect(confirmation?.isConnected).toBe(false)
    expect(document.activeElement?.textContent).toBe('Pasifleştir')
  })

  it('çift gönderimi engeller, sürümü gönderir ve başarıyı yalnız sunucu yanıtından sonra gösterir', async () => {
    let finish: ((response: Response) => void) | undefined
    const post = vi.fn(() => new Promise<Response>(resolve => { finish = resolve }))
    await render(post)
    await click('Pasifleştir'); await click('Hesabı pasifleştir')
    expect(post).toHaveBeenCalledTimes(1)
    expect(post.mock.calls[0]).toEqual(['/api/staff-accounts/staff-1/deactivate', { version: 'version-1' }, expect.any(AbortSignal)])
    expect(container.textContent).not.toContain('Çalışan hesabı pasifleştirildi.')
    expect(container.querySelector<HTMLFieldSetElement>('fieldset')?.disabled).toBe(true)
    vi.mocked(fetch).mockResolvedValue(Response.json({ items: [{ ...account, isActive: false }], page: 1, hasMore: false, pageSize: 20, totalCount: 1 }))
    if (!finish) throw new Error('Bekleyen istek yok')
    await act(async () => finish?.(new Response(null, { status: 204 })))
    expect(container.textContent).toContain('Çalışan hesabı pasifleştirildi.')
    expect(container.textContent).toContain('Pasif')
    expect(container.querySelector('fieldset')).toBeNull()
    expect(document.activeElement).toBe(container.querySelector('h2'))
    expect(Array.from(container.querySelectorAll('button')).some(item => item.textContent === 'Pasifleştir')).toBe(false)
  })

  it.each([
    [401, 'Oturumun sona erdi.'], [403, 'Bu işlem için yetkin yok.'],
    [409, 'Hesap değişti.'], [429, 'Çok sık denendi.'], [500, 'Sonuç doğrulanamadı.'],
  ])('%s yanıtında başarı uydurmaz ve listeyi yenileme olanağı verir', async (status, text) => {
    await render(vi.fn(async () => new Response(null, { status })))
    await click('Pasifleştir'); await click('Hesabı pasifleştir')
    expect(container.querySelector('[role="alert"]')?.textContent).toContain(text)
    expect(container.textContent).not.toContain('Çalışan hesabı pasifleştirildi.')
    expect(container.querySelector('fieldset')).not.toBeNull()
    await click('Listeyi yenile')
    expect(container.querySelector('fieldset')).toBeNull()
  })

  it('belirsiz ağ sonucunu başarı saymaz; yenileme ile gerçek pasif durumu gösterir', async () => {
    await render(vi.fn(async () => { throw new TypeError('synthetic network failure') }))
    await click('Pasifleştir'); await click('Hesabı pasifleştir')
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Sonuç doğrulanamadı.')
    vi.mocked(fetch).mockResolvedValue(Response.json({ items: [{ ...account, isActive: false }], page: 1, hasMore: false, pageSize: 20, totalCount: 1 }))
    await click('Listeyi yenile')
    expect(container.textContent).toContain('Pasif')
    expect(container.textContent).not.toContain('Çalışan hesabı pasifleştirildi.')
  })

  it('yükleme, boş, bozuk yanıt ve sayfalama durumlarını ayrı gösterir', async () => {
    let finish: ((response: Response) => void) | undefined
    vi.mocked(fetch).mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve }))
    await render()
    expect(container.textContent).toContain('Çalışan hesapları yükleniyor…')
    if (!finish) throw new Error('Liste isteği yok')
    await act(async () => finish?.(Response.json({ items: [], page: 1, hasMore: true, pageSize: 20, totalCount: 21 })))
    expect(container.textContent).toContain('Bu sayfada çalışan hesabı yok.')
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ items: [account], page: 2, hasMore: false, pageSize: 20, totalCount: 21 }))
    await click('Sonraki sayfa')
    expect(vi.mocked(fetch).mock.calls.at(-1)?.[0]).toBe('/api/staff-accounts/?page=2')
    expect(container.querySelector('[aria-current="page"][aria-label="Sayfa 2"]')).not.toBeNull()
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ wrong: 'shape' }))
    await click('Listeyi yenile')
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Liste yanıtı doğrulanamadı.')
    expect(container.textContent).not.toContain(account.email)
  })
})
