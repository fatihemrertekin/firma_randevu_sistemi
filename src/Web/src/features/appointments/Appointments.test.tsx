// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import Appointments from './Appointments'
import type { Appointment, AppointmentRow } from './appointmentsApi'
import type { postWithCsrf } from '../../app/api'
import { NavigationEvents } from '../../app/NavigationEvents'

const member = { id: '00000001-0000-0000-0000-000000000001', version: '00000001-0000-0000-0000-000000000002', name: 'Personel A', isActive: true }
const other = { ...member, id: '00000002-0000-0000-0000-000000000001', name: 'Personel B' }
const service = { id: '00000003-0000-0000-0000-000000000001', version: '00000003-0000-0000-0000-000000000002', name: 'Saç kesimi', durationMinutes: 25, price: '450.00', currency: 'TRY', isActive: true }
const appointment: Appointment = { id: '00000004-0000-0000-0000-000000000001', version: '00000004-0000-0000-0000-000000000002', serviceId: service.id, serviceName: service.name,
  staffMemberId: member.id, staffName: member.name, startsAt: '2026-10-16T06:30:00Z', endsAt: '2026-10-16T06:55:00Z', status: 'Confirmed',
  customerName: 'Müşteri 01', customerPhone: '+905000000001', price: 450, durationMinutes: 25, currency: 'TRY', pendingExpiresAt: null }
let container: HTMLDivElement, root: Root
const pageOf = <T,>(items: T[]) => ({ items, page: 1, pageSize: 50, totalCount: items.length, hasMore: false })
function list(items: AppointmentRow[] = [appointment]) { return { ...pageOf(items), pageSize: 20, date: '2026-10-16', timeZone: 'Europe/Istanbul' } }
function api(input: RequestInfo | URL): Promise<Response> {
  const url = new URL(String(input), 'http://test.local'), path = url.pathname
  if (path === '/api/staff-members/') return Promise.resolve(Response.json(pageOf([member, other])))
  if (path === '/api/services/') return Promise.resolve(Response.json(pageOf([service])))
  if (path === '/api/services/' + service.id) return Promise.resolve(Response.json(service))
  if (path === '/api/appointments/availability') return Promise.resolve(Response.json({ startsAt: [appointment.startsAt, '2026-10-16T07:00:00Z'], unavailableReasons: [], serviceVersion: service.version, timeZone: 'Europe/Istanbul' }))
  if (path.endsWith('/move-availability')) return Promise.resolve(Response.json({ startsAt: ['2026-10-16T08:00:00Z'], unavailableReasons: [], version: appointment.version, timeZone: 'Europe/Istanbul' }))
  if (path === '/api/appointments/') return Promise.resolve(Response.json({ ...list(), date: url.searchParams.get('date') ?? '2026-10-16' }))
  if (path === '/api/appointments/' + appointment.id) return Promise.resolve(Response.json(appointment))
  throw new Error('Beklenmeyen istek: ' + input)
}
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); vi.stubGlobal('fetch', vi.fn(api)); vi.spyOn(Date, 'now').mockReturnValue(Date.parse('2026-10-16T06:00:00Z'))
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
async function render(path = '/yonetim/randevular?tarih=2026-10-16', post = vi.fn<typeof postWithCsrf>(async () => Response.json(appointment))) {
  const dirty = vi.fn(), busy = vi.fn(), unauthorized = vi.fn()
  const router = createMemoryRouter([{ path: '*', element: <Appointments post={post} onDirtyChange={dirty} onBusyChange={busy} onUnauthorized={unauthorized} /> }], { initialEntries: [path] })
  await act(async () => root.render(<NavigationEvents value={listener => { let key = router.state.location.key; return router.subscribe(state => {
    if (state.location.key !== key) { key = state.location.key; listener(state.location) }
  }) }}><RouterProvider router={router} /></NavigationEvents>))
  return { router, post, dirty, busy, unauthorized }
}
async function click(name: string) {
  const button = [...container.querySelectorAll<HTMLElement>('button,a')].find(item => item.textContent?.trim() === name || item.getAttribute('aria-label') === name)
  if (!button) throw new Error('Düğme bulunamadı: ' + name)
  await act(async () => button.click())
}
async function choose(id: string, value: string) {
  const input = container.querySelector<HTMLInputElement | HTMLSelectElement>('#' + id)
  if (!input) throw new Error('Alan bulunamadı: ' + id)
  const prototype = input instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype
  const setter = Object.getOwnPropertyDescriptor(prototype, 'value')?.set
  await act(async () => { setter?.call(input, value); input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true })) })
}
async function slot(value = appointment.startsAt) {
  const input = [...container.querySelectorAll<HTMLInputElement>('input[type=radio]')].find(item => item.value === value)
  if (!input) throw new Error('Saat bulunamadı')
  await act(async () => input.click())
}
async function submit() {
  const form = container.querySelector('form'); if (!form) throw new Error('Form yok')
  await act(async () => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
}
async function fillCreation() {
  await choose('appointment-serviceId', service.id); await choose('appointment-staffMemberId', member.id); await slot()
  await choose('appointment-customerName', 'Müşteri 01'); await choose('appointment-customerPhone', '05000000001')
}

describe('İşletmenin günlük randevu akışı', () => {
  it('aynı seçimdeki nedenleri saat alanında gösterir, yeni seçimde eski uyarıyı kaldırır', async () => {
    vi.mocked(fetch).mockImplementation(input => String(input).includes('/api/appointments/availability?') ? Promise.resolve(Response.json({
      startsAt: [], unavailableReasons: [{ code: 'ServiceNotAssigned', message: 'Bu hizmet seçili çalışana atanmamış.' }, { code: 'BusinessClosed', message: 'İşletme seçili tarihte kapalı.' }],
      serviceVersion: service.version, timeZone: 'Europe/Istanbul',
    })) : api(input))
    await render('/yonetim/randevular/yeni?tarih=2026-10-16')
    await choose('appointment-serviceId', service.id); await choose('appointment-staffMemberId', member.id)
    const notice = [...container.querySelectorAll('fieldset')].find(item => item.querySelector('legend')?.textContent === 'Uygun başlangıç saati')?.querySelector('[role=status]')
    expect(notice?.textContent).toContain('Bu hizmet seçili çalışana atanmamış.')
    expect(notice?.textContent).toContain('İşletme seçili tarihte kapalı.')
    expect(container.querySelectorAll('input[type=radio]')).toHaveLength(0)
    expect(container.querySelector<HTMLButtonElement>('button[type=submit]')?.disabled).toBe(true)
    vi.mocked(fetch).mockImplementation(api); await choose('appointment-staffMemberId', other.id)
    expect(container.textContent).not.toContain('çalışana atanmamış')
    expect(container.querySelectorAll('input[type=radio]')).toHaveLength(2)
  })
  it('tarih aralığı hatasını boş gün gibi sunmaz, nedeni saat alanında gösterir', async () => {
    vi.mocked(fetch).mockImplementation(input => String(input).includes('/api/appointments/availability?') ? Promise.resolve(Response.json({
      errors: { date: ['Geçmiş bir gün seçilemez. Tarihi değiştirin.'] },
    }, { status: 400 })) : api(input))
    await render('/yonetim/randevular/yeni?tarih=2026-10-16')
    await choose('appointment-serviceId', service.id); await choose('appointment-staffMemberId', member.id)
    expect(container.textContent).toContain('Uygun saatler alınamadı')
    expect(container.textContent).toContain('Geçmiş bir gün seçilemez. Tarihi değiştirin.')
    expect(container.textContent).not.toContain('Bu seçimle uygun saat bulunamıyor')
  })
  it('taşıma uygunluğundaki nedeni de gösterir ve mevcut randevuyu değiştirmez', async () => {
    vi.mocked(fetch).mockImplementation(input => String(input).includes('/move-availability?') ? Promise.resolve(Response.json({
      startsAt: [], unavailableReasons: [{ code: 'Leave', message: 'Çalışanın izinleri nedeniyle uygun saat kalmadı.' }], version: appointment.version, timeZone: 'Europe/Istanbul',
    })) : api(input))
    const { post } = await render('/yonetim/randevular/' + appointment.id + '/tasi?tarih=2026-10-16')
    expect(container.textContent).toContain('Çalışanın izinleri nedeniyle uygun saat kalmadı.')
    expect(container.textContent).toContain('Mevcut randevu')
    expect(post).not.toHaveBeenCalled()
  })
  it('URL veya geri/ileri değişiminde önceki çalışanın kayıtlarını yeni filtre altında göstermez', async () => {
    const { router } = await render()
    vi.mocked(fetch).mockImplementation(input => String(input).includes('/api/appointments/?') ? new Promise(() => {}) : api(input))
    await act(async () => { await router.navigate('/yonetim/randevular?tarih=2026-10-16&calisan=' + other.id) })
    expect(container.textContent).not.toContain('Müşteri 01')
    expect(container.textContent).toContain('Randevular yükleniyor')
  })
  it('çalışana göre gruplar, günlük listede telefon göstermez; filtre ve tarih URL ile taşınır', async () => {
    const second = { ...appointment, id: other.id, staffMemberId: other.id, staffName: other.name }
    vi.mocked(fetch).mockImplementation(input => String(input).includes('/api/appointments/?') ? Promise.resolve(Response.json(list([appointment, second]))) : api(input))
    const { router } = await render()
    expect(container.querySelectorAll('h3')).toHaveLength(2); expect(container.textContent).not.toContain(appointment.customerPhone)
    await choose('appointment-filter', member.id)
    expect(router.state.location.search).toContain('calisan=' + member.id)
    await click('Sonraki gün'); expect(router.state.location.search).toContain('tarih=2026-10-17')
  })
  it('sayfalı sonuçta çalışan bölümünü bütün gün gibi göstermez', async () => {
    vi.mocked(fetch).mockImplementation(input => String(input).includes('/api/appointments/?') ? Promise.resolve(Response.json({ ...list(), totalCount: 21, hasMore: true })) : api(input))
    await render()
    expect(container.textContent).toContain('yalnız bu sayfadaki kayıtları')
    expect(container.textContent).toContain('Bu sayfadaki randevular')
    expect(container.querySelector('[aria-label="Sonraki sayfa"]')).not.toBeNull()
  })
  it('yükleme hatasını boş güne dönüştürmez ve 401 mevcut giriş akışına döner', async () => {
    vi.mocked(fetch).mockImplementation(input => String(input).includes('/api/appointments/?') ? Promise.resolve(new Response(null, { status: 401 })) : api(input))
    const { unauthorized } = await render()
    expect(unauthorized).toHaveBeenCalledOnce(); expect(container.textContent).toContain('Oturumunuz sona erdi')
    expect(container.textContent).not.toContain('Seçili günde randevu yok.')
    expect(container.querySelector('nav[aria-label="Randevu sayfaları"]')).toBeNull()
  })
  it('geç gelen önceki gün yanıtı yeni günü ezmez', async () => {
    let resolve: ((response: Response) => void) | undefined
    vi.mocked(fetch).mockImplementation(input => String(input).includes('date=2026-10-16') ? new Promise(done => { resolve = done }) : api(input))
    const { router } = await render()
    await act(async () => { await router.navigate('/yonetim/randevular?tarih=2026-10-17') })
    await act(async () => resolve?.(Response.json(list([{ ...appointment, customerName: 'Eski gün' }]))))
    expect(container.textContent).not.toContain('Eski gün'); expect(container.querySelector<HTMLInputElement>('#appointment-day')?.value).toBe('2026-10-17')
  })
  it('aynı anda iki gönderimi önler, fiyat/süre göndermeden güncel saat ve hizmet sürümünü kullanır', async () => {
    let resolve: ((response: Response) => void) | undefined
    const post = vi.fn<typeof postWithCsrf>(() => new Promise(done => { resolve = done }))
    const { busy } = await render('/yonetim/randevular/yeni?tarih=2026-10-16', post)
    await fillCreation(); await submit(); await submit()
    expect(post).toHaveBeenCalledOnce(); expect(busy).toHaveBeenLastCalledWith(true)
    expect(post.mock.calls[0]?.[1]).toMatchObject({ serviceId: service.id, serviceVersion: service.version, staffMemberId: member.id, startsAt: appointment.startsAt })
    expect(post.mock.calls[0]?.[1]).not.toHaveProperty('price'); expect(post.mock.calls[0]?.[1]).not.toHaveProperty('durationMinutes')
    await act(async () => resolve?.(Response.json(appointment)))
    expect(container.textContent).toContain('Randevu kaydedildi.')
  })
  it('belirsiz kayıt sonucunda alanları kilitler ve açık tekrar aynı anahtarı/gövdeyi kullanır', async () => {
    const post = vi.fn<typeof postWithCsrf>().mockRejectedValueOnce(new TypeError('network')).mockResolvedValueOnce(Response.json(appointment))
    await render('/yonetim/randevular/yeni?tarih=2026-10-16', post); await fillCreation(); await submit()
    expect(container.textContent).toContain('İşlemin sonucu belirsiz')
    expect(container.querySelector<HTMLInputElement>('#appointment-customerName')?.closest('fieldset')?.disabled).toBe(true)
    await click('Aynı işlemi yeniden dene')
    expect(post).toHaveBeenCalledTimes(2); expect(post.mock.calls[0]?.[1]).toEqual(post.mock.calls[1]?.[1])
  })
  it('çalışan değişiminde eski saati temizler; 400 telefon hatası taslağı koruyup alanı odaklar', async () => {
    const post = vi.fn<typeof postWithCsrf>(async () => Response.json({ errors: { customerPhone: ['Geçerli telefon gerekli.'] } }, { status: 400 }))
    await render('/yonetim/randevular/yeni?tarih=2026-10-16', post); await fillCreation()
    await choose('appointment-staffMemberId', other.id)
    expect(container.querySelector('input[type=radio]:checked')).toBeNull(); await slot(); await submit()
    expect(document.activeElement?.id).toBe('appointment-customerPhone')
    expect(container.querySelector<HTMLInputElement>('#appointment-customerName')?.value).toBe('Müşteri 01')
  })
  it('409 sonrasında mevcut taslağı korur, saat seçimini bırakıp açık güncel yükleme ister', async () => {
    const post = vi.fn<typeof postWithCsrf>(async () => new Response(null, { status: 409 }))
    await render('/yonetim/randevular/yeni?tarih=2026-10-16', post); await fillCreation(); await submit()
    expect(container.querySelector<HTMLInputElement>('#appointment-customerName')?.value).toBe('Müşteri 01')
    expect(container.querySelector('input[type=radio]:checked')).toBeNull()
    await submit(); expect(post).toHaveBeenCalledOnce()
    await click('Güncel bilgileri ve saatleri yükle'); await slot(); await submit(); expect(post).toHaveBeenCalledTimes(2)
  })
  it('iptali ayrı onayla ve sürümle gönderir; idempotent yanıtın güncel durumunu gösterir', async () => {
    const post = vi.fn<typeof postWithCsrf>(async () => Response.json({ ...appointment, status: 'Cancelled', version: service.version }))
    await render('/yonetim/randevular/' + appointment.id + '?tarih=2026-10-16', post)
    await click('İptal et'); expect(post).not.toHaveBeenCalled(); await click('İşlemi onayla')
    expect(post.mock.calls[0]?.[0]).toBe('/api/appointments/' + appointment.id + '/cancel')
    expect(post.mock.calls[0]?.[1]).toMatchObject({ version: appointment.version })
    expect(container.textContent).toContain('Güncel durum: İptal edildi')
  })
  it('taşıma uygunluğu ve komutu randevunun sürümünü kullanır; başarısızlıkta eski zaman görünür kalır', async () => {
    const post = vi.fn<typeof postWithCsrf>(async () => new Response(null, { status: 409 }))
    await render('/yonetim/randevular/' + appointment.id + '/tasi?tarih=2026-10-16', post)
    await slot('2026-10-16T08:00:00Z'); await submit()
    expect(post.mock.calls[0]?.[0]).toBe('/api/appointments/' + appointment.id + '/move')
    expect(post.mock.calls[0]?.[1]).toMatchObject({ version: appointment.version, startsAt: '2026-10-16T08:00:00Z' })
    expect(container.textContent).toContain('09.30–09.55'); expect(container.textContent).toContain('mevcut randevu korunur')
  })
})
