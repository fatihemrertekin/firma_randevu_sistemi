// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { BrowserRouter, useLocation } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import Subscription from './Subscription'
import { readSubscription } from './subscriptionApi'

const period = { id: '797cffc0-1b9a-4d24-872f-94f04e9da614', startsOn: '2026-10-01', endsOn: '2026-11-01', dueOn: '2026-10-01', amountTry: '1000.00', paidTry: '600.00', outstandingTry: '400.00', paymentState: 'Partial', overdue: true }
const original = { asOf: '2026-10-30', timeZone: 'Europe/Istanbul', currency: 'TRY', recorded: true, restrictionsApplied: false, currentPeriod: period, periods: [period], page: 1, pageSize: 12, totalCount: 1, hasMore: false }
let container: HTMLDivElement, root: Root
const unauthorized = vi.fn()
function Screen() { const location = useLocation(); return <Subscription key={location.search} onUnauthorized={unauthorized} /> }
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); vi.stubGlobal('fetch', vi.fn(async () => Response.json(original)))
  unauthorized.mockReset(); window.history.replaceState(null, '', '/yonetim/abonelik')
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); vi.useRealTimers() })
async function render() { await act(async () => root.render(<BrowserRouter><Screen /></BrowserRouter>)) }
async function click(text: string) {
  const button = Array.from(container.querySelectorAll('button')).find(node => node.textContent === text)
  if (!button) throw new Error('Düğme yok: ' + text)
  await act(async () => button.click())
}
describe('İşletme sahibinin aboneliği', () => {
  it('gerçek ondalık metinleri ve hariç bitişi gösterir; yalnız GET yapar, özel alan veya ödeme eylemi açmaz', async () => {
    const privateField = 'SYNTHETIC-PRIVATE-REFERENCE'
    expect(await readSubscription(Response.json({ ...original, reference: privateField }))).not.toHaveProperty('reference')
    await render()
    expect(container.textContent).toContain('31 Ekim 2026'); expect(container.textContent).toContain('Kısmen ödendi · Vadesi geçti')
    expect(container.textContent).toContain('1.000,00 ₺'); expect(container.textContent).toContain('400,00 ₺')
    expect(container.textContent).toContain('Şu anda abonelik nedeniyle erişim kısıtlaması uygulanmıyor.')
    expect(container.querySelector('details')?.open).toBe(false)
    expect(container.querySelector('form,input')).toBeNull(); expect(container.textContent).not.toContain('Ödeme kaydet')
    expect(fetch).toHaveBeenCalledExactlyOnceWith('/api/subscription?page=1&pageSize=12', expect.objectContaining({ cache: 'no-store', signal: expect.any(AbortSignal) }))
  })
  it('yükleme tamamlanana kadar tutar veya erişim durumu uydurmaz; geç yanıtı unmount sonrası atar', async () => {
    let finish: (value: Response) => void = () => { throw new Error('GET beklemiyor') }
    vi.mocked(fetch).mockImplementation(() => new Promise(resolve => { finish = resolve }))
    await render(); expect(container.textContent).toContain('yükleniyor'); expect(container.querySelector('dl')).toBeNull()
    await act(async () => root.render(<BrowserRouter><p>Başka ekran</p></BrowserRouter>))
    await act(async () => finish(Response.json(original))); expect(container.textContent).toBe('Başka ekran')
  })
  it('yenileme sırasında ve hata halinde önceki bakiyeyi göstermez; yeniden denemede güncel yanıtı gösterir', async () => {
    await render(); vi.mocked(fetch).mockRejectedValueOnce(new TypeError('Failed to fetch'))
    await click('Bilgileri yenile'); expect(container.querySelector('dl')).toBeNull(); expect(container.textContent).toContain('güncel tutar doğrulanamadı')
    expect(container.textContent).not.toContain('Failed to fetch')
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ ...original, currentPeriod: { ...period, paymentState: 'Paid', overdue: false, paidTry: '1000.00', outstandingTry: '0.00' } }))
    await click('Yeniden dene'); expect(container.textContent).toContain('Ödendi'); expect(container.textContent).toContain('0,00 ₺')
  })
  it.each([false, true])('bugünkü dönem yokken kayıt var=%s durumunu borç/erişim varsaymadan açıklar', async recorded => {
    vi.mocked(fetch).mockResolvedValue(Response.json({ ...original, recorded, currentPeriod: null, periods: recorded ? [period] : [], totalCount: recorded ? 1 : 0 }))
    await render(); expect(container.textContent).toContain(recorded ? 'Bugün için tanımlanmış dönem yok' : 'Henüz abonelik kaydı yok')
    expect(container.querySelector('details') !== null).toBe(recorded)
    expect(container.textContent).not.toContain('Randevu işlevleri açık')
  })
  it.each([400, 401, 403])('%s yanıtında tutar göstermez ve oturum/yetki/ilk sayfa davranışını korur', async status => {
    vi.mocked(fetch).mockResolvedValue(new Response(null, { status }))
    await render(); expect(container.querySelector('[role=alert]')).not.toBeNull(); expect(container.querySelector('dl')).toBeNull()
    expect(unauthorized).toHaveBeenCalledTimes(status === 401 ? 1 : 0)
    if (status === 403) expect(container.querySelector('button')).toBeNull()
  })
  it('429 Retry-After süresince yeniden denemeyi durdurur', async () => {
    vi.useFakeTimers(); vi.mocked(fetch).mockResolvedValueOnce(new Response(null, { status: 429, headers: { 'Retry-After': '2' } }))
    await render(); expect(container.querySelector<HTMLButtonElement>('button')?.disabled).toBe(true)
    await act(async () => { vi.advanceTimersByTime(1000) }); await act(async () => { vi.advanceTimersByTime(1000) })
    expect(container.querySelector<HTMLButtonElement>('button')?.disabled).toBe(false)
    await click('Yeniden dene'); expect(container.textContent).toContain('Mevcut dönem')
  })
  it('dönemleri sayfalar, URL geri dönüşünde yeniden okur ve gizli kalan geçmişi dar tutar', async () => {
    const periods = Array.from({ length: 12 }, (_, i) => ({ ...period, id: `797cffc0-1b9a-4d24-872f-${String(i).padStart(12, '0')}` }))
    vi.mocked(fetch).mockImplementation(async input => Response.json(String(input).includes('page=2&') ? { ...original, periods: [period], page: 2, totalCount: 13 } : { ...original, periods, totalCount: 13, hasMore: true }))
    await render(); await click('Sonraki sayfa'); expect(location.search).toBe('?sayfa=2'); expect(container.querySelector('details')?.open).toBe(true)
    expect(container.textContent).toContain('Sayfa 2 / 2')
    await act(async () => { window.history.replaceState(null, '', '/yonetim/abonelik'); window.dispatchEvent(new PopStateEvent('popstate')) })
    expect(container.querySelector('details')?.open).toBe(false); expect(fetch).toHaveBeenCalledTimes(3)
  })
  it.each([
    { currency: 'USD' }, { currentPeriod: { ...period, amountTry: 1000 } },
    { currentPeriod: { ...period, dueOn: '2026-02-30' } }, { totalCount: 2 },
  ])('bozuk sözleşmeyi geçerli para bilgisi olarak sunmaz: %j', async invalid => {
    await expect(readSubscription(Response.json({ ...original, ...invalid }))).rejects.toThrow()
  })
})
