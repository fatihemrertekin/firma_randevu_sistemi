// @vitest-environment jsdom
import { act } from 'react'
import { createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { NavigationEvents } from '../../app/NavigationEvents'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import StaffMembers from './StaffMembers'
import type { StaffPost } from './staffMembersApi'

const member = { id: 'member-1', name: 'Deneme Kişi', isActive: true, version: 'version-1' }
let currentMember = { ...member }
let container: HTMLDivElement
let root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  currentMember = { ...member }
  vi.stubGlobal('fetch', vi.fn(async input => String(input).includes('?')
    ? Response.json({ items: [currentMember], page: 1, hasMore: false, pageSize: 20, totalCount: 1 }) : Response.json(currentMember)))
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
async function click(text: string) {
  const button = Array.from(container.querySelectorAll<HTMLButtonElement>('button, a[data-navigation]')).find(item => (item.textContent === text || item.getAttribute('aria-label') === text))
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
  const router = createMemoryRouter([{ path: '*', element: <StaffMembers post={post} onDirtyChange={dirty} onBusyChange={busy} /> }], { initialEntries: ['/yonetim/personel'] })
  await act(async () => root.render(<NavigationEvents value={listener => (() => { let key = router.state.location.key; return router.subscribe(state => { if (state.location.key !== key) { key = state.location.key; listener(state.location) } }) })()}><RouterProvider router={router} /></NavigationEvents>))
  return { post, dirty, busy }
}

describe('Personel yönetimi', () => {
  it('silme onayı iptal edilebilir; 204 sonrası listeye döner ve çift gönderimi engeller', async () => {
    let finish: ((response: Response) => void) | undefined
    const { post } = await render(vi.fn<StaffPost>(() => new Promise(resolve => { finish = resolve })))
    await click('Ayrıntılar'); await click('Sil'); expect(post).not.toHaveBeenCalled()
    expect(document.activeElement?.textContent).toBe('Personeli sil')
    await click('Vazgeç'); expect(document.activeElement?.textContent).toBe('Sil')
    await click('Sil'); await click('Personeli sil'); await click('İşlem sürüyor…')
    expect(post).toHaveBeenCalledTimes(1)
    expect(post.mock.calls[0]?.slice(0, 2)).toEqual(['/api/staff-members/member-1/delete', { version: 'version-1' }])
    expect(container.textContent).not.toContain('personel listesinden silindi.')
    vi.mocked(fetch).mockResolvedValue(Response.json({ items: [], page: 1, hasMore: false, pageSize: 20, totalCount: 0 }))
    await act(async () => finish?.(new Response(null, { status: 204 })))
    expect(container.textContent).toContain('Deneme Kişi personel listesinden silindi.')
    expect(container.querySelector('fieldset')).toBeNull()
    expect(container.querySelector('ul')?.textContent).not.toContain('Deneme Kişi')
  })
  it('silme 409 hatasında kaydı ve onayı korur; yenilemeden tekrar göndermez', async () => {
    const { post } = await render(vi.fn<StaffPost>(async () => new Response(null, { status: 409 })))
    await click('Ayrıntılar'); await click('Sil'); await click('Personeli sil')
    expect(container.textContent).toContain('Personel kaydı değişti.')
    expect(container.textContent).not.toContain('personel listesinden silindi.')
    await click('Personeli sil'); expect(post).toHaveBeenCalledTimes(1)
    await click('Güncel kaydı yükle'); expect(container.querySelector('fieldset')).toBeNull()
  })
  it('ayrıntıyı eski liste kaydından değil kişi isteğinden açar', async () => {
    await render()
    currentMember = { ...member, name: 'Güncel Kişi', version: 'version-2' }
    await click('Ayrıntılar')
    expect(vi.mocked(fetch).mock.calls.at(-1)?.[0]).toBe('/api/staff-members/member-1')
    expect(container.querySelector('h2')?.textContent).toBe('Güncel Kişi')
    expect(container.querySelector<HTMLInputElement>('#member-name')?.value).toBe('Güncel Kişi')
  })

  it('görev veya liste geçişi reddedilince taslağı korur; onaylanınca yeni görevi güncel kayıttan açar', async () => {
    const original = vi.mocked(fetch).getMockImplementation()
    vi.mocked(fetch).mockImplementation(async input => {
      if (String(input).includes('/services')) return Response.json({ member: currentMember, selected: [], items: [], page: 1, hasMore: false, pageSize: 10, totalCount: 0 })
      if (!original) throw new Error('Test isteği yok')
      return original(input)
    })
    const { dirty } = await render(); await click('Ayrıntılar'); await fill('Kaydedilmemiş Ad')
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    const calls = vi.mocked(fetch).mock.calls.length
    await click('Hizmetler'); await click('Personel listesi'); await click('Pasifleştir')
    expect(confirm).toHaveBeenCalledTimes(3)
    expect(vi.mocked(fetch).mock.calls).toHaveLength(calls)
    expect(container.querySelector<HTMLInputElement>('#member-name')?.value).toBe('Kaydedilmemiş Ad')
    currentMember = { ...member, name: 'Sunucudaki Ad', version: 'version-2' }
    confirm.mockReturnValue(true); await click('Hizmetler')
    expect(container.querySelector('h2')?.textContent).toBe('Sunucudaki Ad')
    expect(container.querySelector('nav [aria-current="page"]')?.textContent).toBe('Hizmetler')
    expect(dirty).toHaveBeenLastCalledWith(false)
  })

  it('hizmet kaydından sonraki ad değişikliğinde ortak güncel sürümü kullanır', async () => {
    const service = { id: 'service-1', name: 'Kesim', durationMinutes: 30, price: '350.00', currency: 'TRY', isActive: true, version: 'service-version' }
    vi.mocked(fetch).mockImplementation(async input => {
      const path = String(input)
      if (path.includes('/services')) return Response.json({ member: currentMember, selected: [], items: [service], page: 1, hasMore: false, pageSize: 10, totalCount: 1 })
      return path.includes('?') ? Response.json({ items: [currentMember], page: 1, hasMore: false, pageSize: 20, totalCount: 1 }) : Response.json(currentMember)
    })
    const post = vi.fn<StaffPost>(async (path, body) => {
      currentMember = { ...currentMember, version: 'version-2' }
      return path.endsWith('/services') ? Response.json({ member: currentMember, selected: [{ id: service.id, version: service.version }] })
        : Response.json({ ...currentMember, name: String((body as { name: string }).name) })
    })
    await render(post); await click('Ayrıntılar'); await click('Hizmetler')
    const choice = container.querySelector<HTMLInputElement>('input[type="checkbox"]')
    if (!choice) throw new Error('Hizmet seçimi yok')
    await act(async () => choice.click()); await submit()
    expect(container.textContent).toContain('Personelin hizmet seçimleri kaydedildi.')
    expect(container.querySelector('h2')?.textContent).toBe(member.name)
    await click('Bilgiler'); await fill('Son Ad'); await submit()
    expect(post.mock.calls.at(-1)?.[1]).toEqual({ name: 'Son Ad', version: 'version-2' })
  })

  it('ikinci sayfadaki ayrıntıdan aynı sayfaya ve açan düğmeye döner', async () => {
    vi.mocked(fetch).mockImplementation(async input => String(input).includes('?')
      ? Response.json({ items: [member], page: String(input).endsWith('2') ? 2 : 1, hasMore: !String(input).endsWith('2') , pageSize: 20, totalCount: ((String(input).endsWith('2') ? 2 : 1) - 1) * 20 + ((!String(input).endsWith('2')) ? 21 : ([member]).length) }) : Response.json(member))
    await render(); await click('Sonraki sayfa'); await click('Ayrıntılar'); await click('Personel listesi')
    expect(vi.mocked(fetch).mock.calls.at(-1)?.[0]).toBe('/api/staff-members/?page=2')
    expect(document.activeElement).toBe(container.querySelector('[data-member-id="member-1"]'))
  })

  it('başka kimlikli kişi yanıtını düzenlenebilir ayrıntı olarak göstermez', async () => {
    await render()
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ ...member, id: 'wrong-member', name: 'Yanlış Kişi' }))
    await click('Ayrıntılar')
    expect(container.querySelector('form')).toBeNull()
    expect(container.textContent).not.toContain('Yanlış Kişi')
    expect(container.querySelector('[role="alert"]')).not.toBeNull()
    await click('Güncel kaydı yükle')
    expect(container.querySelector<HTMLInputElement>('#member-name')?.value).toBe(member.name)
  })

  it.each(['Hizmetler', 'Saatler'])('%s yüklenirken görev/liste/vazgeç geçişlerini kilitler', async label => {
    currentMember = { ...member, version: '00000000-0000-0000-0000-000000000001' }
    let finish: ((response: Response) => void) | undefined
    vi.mocked(fetch).mockImplementation(async input => {
      const path = String(input)
      if (path.includes('/services') || path.endsWith('/hours')) return new Promise<Response>(resolve => { finish = resolve })
      return path.includes('?') ? Response.json({ items: [currentMember], page: 1, hasMore: false, pageSize: 20, totalCount: 1 }) : Response.json(currentMember)
    })
    const { busy } = await render(); await click('Ayrıntılar'); await click(label)
    expect(busy).toHaveBeenLastCalledWith(true)
    expect(Array.from(container.querySelectorAll('nav button, nav a[data-navigation]')).every(control => control.matches(':disabled, [aria-disabled="true"]'))).toBe(true)
    expect(Array.from(container.querySelectorAll('a')).find(button => button.textContent === 'Personel listesi')?.getAttribute('aria-disabled') === 'true').toBe(true)
    const cancel = label === 'Hizmetler' ? 'Vazgeç' : 'Listeye dön'
    expect(Array.from(container.querySelectorAll<HTMLButtonElement>('button, a[data-navigation]')).find(button => button.textContent === cancel)?.disabled).toBe(true)
    await act(async () => finish?.(Response.json(label === 'Hizmetler'
      ? { member: currentMember, selected: [], items: [], page: 1, hasMore: false, pageSize: 10, totalCount: 0 }
      : { member: currentMember, version: currentMember.version, isConfigured: false, days: [] })))
    expect(busy).toHaveBeenLastCalledWith(false)
  })

  it('hizmetten vazgeçince kişi bağlamını korur; listeye dönünce ayrıntı düğmesine odak döndürür', async () => {
    const original = vi.mocked(fetch).getMockImplementation()
    vi.mocked(fetch).mockImplementation(async input => {
      if (String(input).includes('/services')) return Response.json({ member, selected: [], items: [], page: 1, hasMore: false, pageSize: 10, totalCount: 0 })
      if (!original) throw new Error('Test isteği yok')
      return original(input)
    })
    await render(); await click('Ayrıntılar'); await click('Hizmetler'); expect(container.querySelector('form')).not.toBeNull()
    await click('Vazgeç'); expect(container.querySelector('nav [aria-current="page"]')?.textContent).toBe('Hizmetler')
    expect(container.querySelector('h2')?.textContent).toBe(member.name)
    await click('Personel listesi'); expect(container.querySelector('form')).toBeNull()
    expect(document.activeElement).toBe(container.querySelector('[data-member-id="member-1"]'))
  })
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
    currentMember = { ...member, name: 'Yeni Kişi' }
    await act(async () => finish?.(Response.json(currentMember, { status: 201 })))
    expect(container.textContent).toContain('Personel kaydedildi.')
    expect(container.querySelector('h2')?.textContent).toBe('Yeni Kişi')
    expect(container.querySelector<HTMLInputElement>('#member-name')?.value).toBe('Yeni Kişi')
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
    await render(post); await click('Ayrıntılar'); await fill('Yeni Ad'); await submit()
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
    await render(post); await click('Ayrıntılar'); await click('Pasifleştir')
    expect(container.querySelector('fieldset')?.textContent).toContain('Giriş hesabı ve açık oturumlar etkilenmez.')
    expect(post).not.toHaveBeenCalled(); expect(document.activeElement?.textContent).toBe('Durumu değiştir')
    currentMember = { ...member, isActive: false, version: 'version-2' }
    await click('Durumu değiştir')
    expect(post.mock.calls[0]).toEqual(['/api/staff-members/member-1/status', { isActive: false, version: 'version-1' }, expect.any(AbortSignal)])
    expect(container.textContent).toContain('pasifleştirildi. Giriş hesapları değişmedi.')
    expect(container.textContent).toContain('Aktifleştir'); expect(container.querySelector('fieldset')).toBeNull()
  })

  it.each([[400, 'İstek doğrulanamadı.'], [401, 'Oturumunuz sona erdi.'], [403, 'yetkiniz yok.'], [409, 'kaydı değişti.'], [429, 'Çok sık denendi.']])('%s hatasında durum değişikliğini tamamlandı saymaz ve yenileme ister', async (status, text) => {
    await render(vi.fn(async () => new Response(null, { status }))); await click('Ayrıntılar'); await click('Pasifleştir'); await click('Durumu değiştir')
    expect(container.querySelector('[role="alert"]')?.textContent).toContain(text)
    expect(container.textContent).not.toContain('pasifleştirildi.')
    expect(Array.from(container.querySelectorAll<HTMLButtonElement>('button')).find(button => button.textContent === 'Durumu değiştir')?.disabled).toBe(true)
    await click('Güncel kaydı yükle'); expect(container.querySelector('fieldset')).toBeNull()
  })

  it('yükleme, boş, sayfalama ve bozuk yanıt durumlarını ayrı gösterir', async () => {
    let finish: ((response: Response) => void) | undefined
    vi.mocked(fetch).mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve }))
    await render(); expect(container.textContent).toContain('Personel yükleniyor…')
    await act(async () => finish?.(Response.json({ items: [], page: 1, hasMore: true, pageSize: 20, totalCount: 21 })))
    expect(container.textContent).toContain('Bu sayfada personel yok.')
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ items: [member], page: 2, hasMore: false, pageSize: 20, totalCount: 21 }))
    await click('Sonraki sayfa'); expect(vi.mocked(fetch).mock.calls.at(-1)?.[0]).toBe('/api/staff-members/?page=2')
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ wrong: true })); await click('Listeyi yenile')
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Liste yanıtı doğrulanamadı.')
    expect(container.textContent).not.toContain(member.name)
  })
})
