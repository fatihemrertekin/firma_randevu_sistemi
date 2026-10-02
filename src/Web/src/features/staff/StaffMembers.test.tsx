// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import StaffMembers from './StaffMembers'
import type { StaffPost } from './staffMembersApi'

const member = { id: 'member-1', name: 'Deneme Kişi', isActive: true, version: 'version-1' }
let container: HTMLDivElement
let root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('fetch', vi.fn(async () => Response.json({ items: [member], page: 1, hasMore: false })))
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
async function click(text: string) {
  const button = Array.from(container.querySelectorAll('button')).find(item => item.textContent === text)
  if (!button) throw new Error('Düğme yok: ' + text)
  await act(async () => button.click())
}
async function fill(value: string) {
  const input = container.querySelector<HTMLInputElement>('#member-name')
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  if (!input || !setter) throw new Error('Alan yok')
  await act(async () => { setter.call(input, value); input.dispatchEvent(new Event('input', { bubbles: true })) })
}
async function submit() {
  const form = container.querySelector('form')
  if (!form) throw new Error('Form yok')
  await act(async () => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
}
async function render(post = vi.fn<StaffPost>(async () => Response.json(member)), dirty = vi.fn(), busy = vi.fn()) {
  await act(async () => root.render(<StaffMembers post={post} onDirtyChange={dirty} onBusyChange={busy} />))
  return { post, dirty, busy }
}

describe('Personel yönetimi', () => {
  it('çift eklemeyi engeller ve belirsiz sonuçta aynı kimlikle tekrar gönderir', async () => {
    let finish: ((response: Response) => void) | undefined
    const post = vi.fn<StaffPost>(() => new Promise<Response>(resolve => { finish = resolve }))
    const { busy } = await render(post)
    await click('Yeni personel'); await fill(' Yeni Kişi '); await submit(); await submit()
    expect(post).toHaveBeenCalledTimes(1); expect(busy).toHaveBeenLastCalledWith(true)
    expect(container.textContent).not.toContain('Personel kaydedildi.')
    const request = post.mock.calls[0]?.[1]
    expect(request).toEqual({ id: expect.any(String), name: 'Yeni Kişi' })
    await act(async () => finish?.(new Response(null, { status: 500 })))
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Sonuç doğrulanamadı.')
    await submit(); expect(post.mock.calls[1]?.[1]).toEqual(request)
    await act(async () => finish?.(Response.json({ ...member, name: 'Yeni Kişi' }, { status: 201 })))
    expect(container.textContent).toContain('Personel kaydedildi.'); expect(container.querySelector('form')).toBeNull()
    expect(busy).toHaveBeenLastCalledWith(false)
  })

  it('alan hatasını odaklar ve kaydedilmemiş taslağı vazgeçmeden önce onaylatır', async () => {
    const { post, dirty } = await render()
    await click('Yeni personel'); await fill('   '); await submit()
    expect(post).not.toHaveBeenCalled(); expect(container.querySelector('#member-name')?.getAttribute('aria-invalid')).toBe('true')
    expect(document.activeElement).toBe(container.querySelector('#member-name'))
    await fill('Taslak'); expect(dirty).toHaveBeenLastCalledWith(true)
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    await click('Vazgeç'); expect(container.querySelector<HTMLInputElement>('#member-name')?.value).toBe('Taslak')
    confirm.mockReturnValue(true); await click('Vazgeç'); expect(container.querySelector('form')).toBeNull()
    expect(document.activeElement?.textContent).toBe('Yeni personel')
  })

  it('eski sürümle kaydı ezmez; güncel sürümü yükledikten sonra düzenler', async () => {
    const post = vi.fn<StaffPost>(async () => new Response(null, { status: 409 }))
    await render(post); await click('Adı düzenle'); await fill('Yeni Ad'); await submit()
    expect(post.mock.calls[0]).toEqual(['/api/staff-members/member-1', { name: 'Yeni Ad', version: 'version-1' }, expect.any(AbortSignal)])
    expect(container.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBe(true)
    expect(container.querySelector<HTMLInputElement>('#member-name')?.value).toBe('Yeni Ad')
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ ...member, name: 'Başka Ad', version: 'version-2' }))
    await click('Güncel kaydı yükle'); await fill('Son Ad'); await submit()
    expect(post.mock.calls[1]?.[1]).toEqual({ name: 'Son Ad', version: 'version-2' })
  })

  it('durum onayında bağımsız giriş hesabını açıklar ve sunucu yanıtından sonra başarı gösterir', async () => {
    const post = vi.fn(async () => Response.json({ ...member, isActive: false, version: 'version-2' }))
    await render(post); await click('Pasifleştir')
    expect(container.querySelector('fieldset')?.textContent).toContain('Giriş hesabı ve açık oturumlar etkilenmez.')
    expect(post).not.toHaveBeenCalled(); expect(document.activeElement?.textContent).toBe('Durumu değiştir')
    vi.mocked(fetch).mockResolvedValue(Response.json({ items: [{ ...member, isActive: false }], page: 1, hasMore: false }))
    await click('Durumu değiştir')
    expect(post.mock.calls[0]).toEqual(['/api/staff-members/member-1/status', { isActive: false, version: 'version-1' }, expect.any(AbortSignal)])
    expect(container.textContent).toContain('pasifleştirildi. Giriş hesapları değişmedi.')
    expect(container.textContent).toContain('Aktifleştir'); expect(container.querySelector('fieldset')).toBeNull()
  })

  it.each([[400, 'İstek doğrulanamadı.'], [401, 'Oturumunuz sona erdi.'], [403, 'yetkiniz yok.'], [409, 'kaydı değişti.'], [429, 'Çok sık denendi.']])('%s hatasında durum değişikliğini tamamlandı saymaz ve yenileme ister', async (status, text) => {
    await render(vi.fn(async () => new Response(null, { status }))); await click('Pasifleştir'); await click('Durumu değiştir')
    expect(container.querySelector('[role="alert"]')?.textContent).toContain(text)
    expect(container.textContent).not.toContain('pasifleştirildi.')
    expect(Array.from(container.querySelectorAll('button')).find(button => button.textContent === 'Durumu değiştir')?.disabled).toBe(true)
    await click('Listeyi yenile'); expect(container.querySelector('fieldset')).toBeNull()
  })

  it('yükleme, boş, sayfalama ve bozuk yanıt durumlarını ayrı gösterir', async () => {
    let finish: ((response: Response) => void) | undefined
    vi.mocked(fetch).mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve }))
    await render(); expect(container.textContent).toContain('Personel yükleniyor…')
    await act(async () => finish?.(Response.json({ items: [], page: 1, hasMore: true })))
    expect(container.textContent).toContain('Bu sayfada personel yok.')
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ items: [member], page: 2, hasMore: false }))
    await click('Sonraki sayfa'); expect(vi.mocked(fetch).mock.calls.at(-1)?.[0]).toBe('/api/staff-members/?page=2')
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ wrong: true })); await click('Listeyi yenile')
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Liste yanıtı doğrulanamadı.')
    expect(container.textContent).not.toContain(member.name)
  })
})
