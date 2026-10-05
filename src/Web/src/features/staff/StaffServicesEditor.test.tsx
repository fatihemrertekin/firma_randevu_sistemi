// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import StaffServicesEditor from './StaffServicesEditor'
import type { StaffPost } from './staffMembersApi'

const member = { id: 'member-1', name: 'Deneme Personel', isActive: true, version: 'member-version' }
const first = { id: 'service-1', name: 'Kesim', durationMinutes: 30, price: '0.29', currency: 'TRY', isActive: true, version: 'service-version-1' }
const second = { ...first, id: 'service-2', name: 'Boya', version: 'service-version-2' }
const reference = (item: typeof first) => ({ id: item.id, version: item.version })
const page = { member, selected: [], items: [first, second], page: 1, hasMore: false }
let container: HTMLDivElement, root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('fetch', vi.fn(async () => Response.json(page)))
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
async function click(name: string) {
  const button = Array.from(container.querySelectorAll('button')).find(item => item.textContent === name)
  if (!button) throw new Error('Düğme yok: ' + name)
  await act(async () => button.click())
}
function checkbox(name: string) {
  const input = container.querySelector<HTMLInputElement>(`[aria-label="${name} hizmetini seç"]`)
  if (!input) throw new Error('Hizmet yok: ' + name)
  return input
}
async function toggle(name: string) { await act(async () => checkbox(name).click()) }
async function submit() { await act(async () => container.querySelector('form')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))) }
async function render(post = vi.fn<StaffPost>(async () => Response.json({ member: { ...member, version: 'saved-version' }, selected: [reference(first)] }))) {
  const saved = vi.fn(), cancel = vi.fn(), dirty = vi.fn(), busy = vi.fn()
  await act(async () => root.render(<StaffServicesEditor memberId={member.id} post={post} onSaved={saved} onCancel={cancel} onDirtyChange={dirty} onBusyChange={busy} />))
  return { post, saved, cancel, dirty, busy }
}
describe('Personelin hizmet seçimleri', () => {
  it('sunucu sayfaları arasında mevcut ve taslak seçimleri koruyup tam kümeyi kaydeder', async () => {
    vi.mocked(fetch).mockImplementation(async input => Response.json({ ...page, selected: [reference(second)],
      items: String(input).includes('page=2') ? [second] : [first], page: String(input).includes('page=2') ? 2 : 1, hasMore: !String(input).includes('page=2') }))
    const { post, saved, dirty } = await render(); await toggle('Kesim'); expect(dirty).toHaveBeenLastCalledWith(true)
    await click('Sonraki hizmet sayfası'); expect(checkbox('Boya').checked).toBe(true); await toggle('Boya')
    await click('Önceki hizmet sayfası'); expect(checkbox('Kesim').checked).toBe(true); await submit()
    expect(post).toHaveBeenCalledWith('/api/staff-members/member-1/services', { version: member.version, services: [reference(first)] }, expect.any(AbortSignal))
    expect(saved).toHaveBeenCalledOnce(); expect(container.textContent).toContain('0,29 ₺')
  })
  it('çift gönderimi kapatır ve yalnız doğrulanmış sunucu yanıtından sonra başarı bildirir', async () => {
    let finish: ((response: Response) => void) | undefined
    const post = vi.fn<StaffPost>(() => new Promise<Response>(resolve => { finish = resolve }))
    const { saved, busy } = await render(post); await toggle('Kesim'); await submit(); await submit()
    expect(post).toHaveBeenCalledOnce(); expect(saved).not.toHaveBeenCalled(); expect(busy).toHaveBeenLastCalledWith(true)
    await act(async () => finish?.(Response.json({ member: { ...member, version: 'new-version' }, selected: [reference(first)] })))
    expect(saved).toHaveBeenCalledOnce(); expect(busy).toHaveBeenLastCalledWith(false)
  })
  it.each([401, 403, 404, 409, 429, 500])('%i hatasında taslağı korur ve güncel seçim yüklenene kadar göndermez', async status => {
    const { post, saved } = await render(vi.fn<StaffPost>(async () => new Response(null, { status })))
    await toggle('Kesim'); await submit(); expect(checkbox('Kesim').checked).toBe(true); expect(saved).not.toHaveBeenCalled()
    expect(container.querySelector('[role="alert"]')).not.toBeNull(); await submit(); expect(post).toHaveBeenCalledOnce()
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false); await click('Güncel seçimleri yükle'); expect(checkbox('Kesim').checked).toBe(true)
    confirm.mockReturnValue(true); await click('Güncel seçimleri yükle'); expect(checkbox('Kesim').checked).toBe(false)
  })
  it('400 alan hatasında ilk seçim odağını ve taslağı korur', async () => {
    const { saved } = await render(vi.fn<StaffPost>(async () => Response.json({ errors: { services: ['Geçersiz seçim'] } }, { status: 400 })))
    await toggle('Boya'); await submit(); expect(checkbox('Boya').checked).toBe(true); expect(document.activeElement).toBe(checkbox('Kesim'))
    expect(container.querySelector('fieldset')?.getAttribute('aria-invalid')).toBe('true'); expect(saved).not.toHaveBeenCalled()
  })
  it('katalog sayfalarken değişirse eski seçimi ezmez ve güncel sürüm ister', async () => {
    vi.mocked(fetch).mockImplementation(async input => Response.json(String(input).includes('page=2')
      ? { ...page, member: { ...member, version: 'changed-version' }, page: 2 } : { ...page, items: [first], hasMore: true }))
    const { post } = await render(); await toggle('Kesim'); await click('Sonraki hizmet sayfası')
    expect(checkbox('Kesim').checked).toBe(true); expect(container.textContent).toContain('Taslağın korundu'); await submit(); expect(post).not.toHaveBeenCalled()
  })
  it('pasif personel/hizmette eski eşleşmeyi kaldırıp geri seçebilir, yeni eşleşme kuramaz', async () => {
    vi.mocked(fetch).mockResolvedValue(Response.json({ ...page, member: { ...member, isActive: false }, selected: [reference(first)], items: [{ ...first, isActive: false }, second] }))
    await render(); expect(checkbox('Kesim').disabled).toBe(false); expect(checkbox('Boya').disabled).toBe(true)
    await toggle('Kesim'); expect(checkbox('Kesim').checked).toBe(false); expect(checkbox('Kesim').disabled).toBe(false)
    await toggle('Kesim'); expect(checkbox('Kesim').checked).toBe(true)
  })
  it('yükleme/boş durumu ve klavye odağını ayırır; taslağı vazgeçme/sekme kapanışında korur', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    const { cancel } = await render(); expect(document.activeElement).not.toBe(checkbox('Kesim')); await toggle('Kesim')
    const beforeUnload = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(beforeUnload); expect(beforeUnload.defaultPrevented).toBe(true)
    await click('Vazgeç'); expect(cancel).not.toHaveBeenCalled(); confirm.mockReturnValue(true); await click('Vazgeç'); expect(cancel).toHaveBeenCalledOnce()
  })
  it('boş liste ve bozuk yanıtı başarı saymaz', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ ...page, selected: [{ id: 'broken' }] })).mockResolvedValueOnce(Response.json({ ...page, items: [] }))
    await render(); expect(container.querySelector('[role="alert"]')).not.toBeNull(); expect(container.textContent).toContain('Sonuç doğrulanamadı.')
    await click('Güncel seçimleri yükle'); expect(container.textContent).toContain('Bu sayfada hizmet yok.'); expect(container.querySelector('[role="alert"]')).toBeNull()
  })
})
