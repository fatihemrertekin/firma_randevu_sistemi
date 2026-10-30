// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import SubscriptionOperator from './SubscriptionOperator'
import OperatorApplication from './OperatorApplication'
import OperatorFirms from './OperatorFirms'

const period = { id: 'abc8d34f-9159-49b1-b268-64c5d57ff7ec', startsOn: '2026-10-01', endsOn: '2026-11-01', dueOn: '2026-10-01', version: 'abc8d34f-9159-49b1-b268-64c5d57ff7ed', amountTry: '1000.00', paidTry: '0.00', outstandingTry: '1000.00', overdue: true }
const data = { targetId: 'synthetic-firm', targetName: 'Sentetik Salon', asOf: '2026-10-29', page: 1, total: 1, selectedPeriodId: period.id, periods: [period], payments: [], paymentsPage: 1, paymentCount: 0 }
const summary = { recorded: true, latestStartedPeriod: { ...period, paymentState: 'Paid' }, overdueOutstandingTry: '2500.00', overduePeriodCount: 3, restrictionsApplied: true }
const firms = { page: 1, pageSize: 12, total: 2, checkedAt: '2026-10-31T08:00:00Z', asOf: '2026-10-31', filterScope: 'page', items: [
  { id: data.targetId, name: data.targetName, status: 'available', summary },
  { id: 'firm-b', name: 'Sentetik B', status: 'unavailable', summary: null },
] }
let container: HTMLDivElement, root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  Object.defineProperty(HTMLElement.prototype, 'showPopover', { configurable: true, value: vi.fn() })
  Object.defineProperty(HTMLElement.prototype, 'hidePopover', { configurable: true, value: vi.fn() })
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
function requests(handler: (path: string, init?: RequestInit) => Promise<Response>) {
  const mock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => String(input) === '/api/auth/csrf' ? Response.json({ token: 'synthetic-csrf' }) : handler(String(input), init))
  vi.stubGlobal('fetch', mock); return mock
}
async function click(text: string) {
  const button = [...container.querySelectorAll<HTMLButtonElement>('button')].find(b => b.textContent === text)
  if (!button) throw new Error('Düğme bulunamadı: ' + text)
  await act(async () => button.click())
}
async function fill(id: string, value: string) {
  const input = container.querySelector<HTMLInputElement>('#' + id), setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  if (!input || !setter) throw new Error('Alan bulunamadı: ' + id)
  await act(async () => { setter.call(input, value); input.dispatchEvent(new Event('input', { bubbles: true })) })
}
async function submit() {
  const form = container.querySelector('[role=dialog] form') ?? [...container.querySelectorAll('form')].find(f => !f.closest('[hidden]')); if (!form) throw new Error('Form bulunamadı')
  await act(async () => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
}
describe('Ayrı operatör abonelik akışı', () => {
  it('zaman aşımı ve bozuk JSON yanıtını anlaşılır Türkçe hatayla açıklar', async () => {
    let malformed = false
    requests(async () => { if (!malformed) throw new DOMException('The operation was aborted', 'TimeoutError'); return new Response('{') })
    await act(async () => root.render(<SubscriptionOperator targetId="synthetic-firm" onExpired={vi.fn()} />))
    expect(container.textContent).toContain('Bağlantıyı kontrol edip yeniden deneyin.'); expect(container.textContent).not.toContain('aborted')
    malformed = true; await click('Yeniden dene')
    expect(container.textContent).toContain('Sunucu yanıtı doğrulanamadı.'); expect(container.querySelector('select')).toBeNull()
  })
  it('bilinen CSRF hatasını belirsiz ödeme saymaz, önceki belirsiz ödeme varsa hedefi korur', async () => {
    let csrfFail = true, loseResponse = false
    const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input) === '/api/auth/csrf') return csrfFail ? new Response(null, { status: 503 }) : Response.json({ token: 'synthetic-csrf' })
      if (init?.method === 'POST') { if (loseResponse) throw new TypeError('lost'); return Response.json({ id: 'synthetic-payment' }) }
      return Response.json({ ...data, summary })
    })
    vi.stubGlobal('fetch', fetch)
    await act(async () => root.render(<SubscriptionOperator targetId="synthetic-firm" onExpired={vi.fn()} />))
    await click('Ödeme kaydet'); await fill('payment-reference', 'SYNTHETIC-CSRF'); await submit()
    expect(container.textContent).toContain('işlem gönderilmedi'); expect(container.textContent).not.toContain('Sonuç belirsiz;')
    expect(container.querySelector('fieldset')?.disabled).toBe(false); expect(fetch.mock.calls.filter(([, i]) => i?.method === 'POST')).toHaveLength(0)
    csrfFail = false; loseResponse = true; await submit()
    csrfFail = true; await submit()
    expect(container.textContent).toContain('Sonuç belirsiz;'); expect(container.querySelector('fieldset')?.disabled).toBe(true)
    expect([...container.querySelectorAll('button')].find(b => b.textContent === 'Vazgeç')?.disabled).toBe(true)
  })
  it('firma listesinde ödenen ayın eski borcu gizlemesine ve bağlantı yokken sıfır borç gösterilmesine izin vermez', async () => {
    const fetch = requests(async () => Response.json(firms))
    await act(async () => root.render(<OperatorFirms onExpired={vi.fn()} onUnsaved={vi.fn()} onBusy={vi.fn()} />))
    expect(container.textContent).toContain('2.500,00 ₺'); expect(container.textContent).toContain('Ödendi'); expect(container.textContent).toContain('Salt okunur')
    const rows = container.querySelectorAll('tbody tr'); expect(rows[1].textContent).toContain('Bilinmiyor'); expect(rows[1].textContent).not.toContain('0,00 ₺')
    expect(String(fetch.mock.calls[0][0])).toContain('includeSummary=true')
  })
  it('yalnız seçilen hedefe yazar, belirsiz işlemde hedefi kilitler ve dönüşte odağı geri verir', async () => {
    const sent: { path: string; body: string }[] = []
    requests(async (path, init) => {
      if (path.startsWith('/api/targets?')) return Response.json(firms)
      if (init?.method === 'POST') { sent.push({ path, body: String(init.body) }); if (sent.length === 1) throw new TypeError('network'); return Response.json({ id: 'synthetic-payment' }) }
      return Response.json({ ...data, summary })
    })
    await act(async () => root.render(<OperatorFirms onExpired={vi.fn()} onUnsaved={vi.fn()} onBusy={vi.fn()} />))
    const opener = container.querySelector<HTMLButtonElement>(`button[aria-label="${data.targetName} firmasını aç"]`)
    if (!opener) throw new Error('Firma düğmesi yok')
    await act(async () => opener.click()); await click('Ödeme kaydet'); await fill('payment-reference', 'SYNTHETIC-A')
    vi.mocked(window.confirm).mockReturnValue(false); await click('Firmalara dön'); expect(container.querySelector('[role=dialog]')).not.toBeNull()
    await submit(); await click('Firmalara dön'); await click('Vazgeç')
    expect(container.querySelector('[role=dialog]')).not.toBeNull()
    expect([...container.querySelectorAll('button')].find(b => b.textContent === 'Firmalara dön')?.disabled).toBe(true)
    await submit(); expect(sent).toHaveLength(2); expect(sent[1]).toEqual(sent[0]); expect(sent[0].path).toBe('/api/targets/synthetic-firm/subscription/payments')
    expect(JSON.parse(sent[0].body).targetId).toBe('synthetic-firm')
    await click('Firmalara dön'); expect(document.activeElement).toBe(opener)
  })
  it('yanlış firma yanıtını reddeder ve eski/geç gelen liste yanıtını kullanmaz', async () => {
    requests(async () => Response.json({ ...data, targetId: 'other-firm', summary }))
    await act(async () => root.render(<SubscriptionOperator targetId="synthetic-firm" onExpired={vi.fn()} />))
    expect(container.textContent).toContain('Abonelik yanıtı doğrulanamadı'); expect(container.querySelector('select')).toBeNull()
    await act(async () => root.unmount()); root = createRoot(container)
    let resolveOld: (value: Response) => void = () => { throw new Error('Eski istek başlamadı') }
    let count = 0
    requests(async () => ++count === 1 ? new Promise<Response>(resolve => { resolveOld = resolve }) : Response.json({ ...firms, items: [] }))
    await act(async () => root.render(<OperatorFirms onExpired={vi.fn()} onUnsaved={vi.fn()} onBusy={vi.fn()} />))
    const select = container.querySelector<HTMLSelectElement>('#firm-access')
    if (!select) throw new Error('Filtre yok')
    await act(async () => { select.value = 'open'; select.dispatchEvent(new Event('change', { bubbles: true })) })
    await act(async () => resolveOld(Response.json(firms)))
    expect(container.textContent).toContain('filtreyi değiştirin'); expect(container.querySelector('tbody')).toBeNull()
  })
  it('oturum yenilenirken belirsiz mali işlemi bellekte ve aynı hedef/anahtarda korur', async () => {
    let signed = true, writes = 0
    const sent: string[] = []
    requests(async (path, init) => {
      if (path === '/api/auth/me') return signed ? Response.json({ email: 'operator@example.test', mfaEnabled: true, operatorAccess: true }) : new Response(null, { status: 401 })
      if (path === '/api/auth/login') return new Response(null, { status: 202 })
      if (path === '/api/auth/mfa/login') { signed = true; return new Response(null, { status: 204 }) }
      if (path.startsWith('/api/targets?')) return Response.json(firms)
      if (init?.method === 'POST') { sent.push(String(init.body)); writes++; if (writes === 1) throw new TypeError('lost'); if (writes === 2) { signed = false; return new Response(null, { status: 401 }) } return Response.json({ id: 'synthetic-payment' }) }
      return Response.json({ ...data, summary })
    })
    await act(async () => root.render(<OperatorApplication />))
    await act(async () => container.querySelector<HTMLButtonElement>('button[aria-label="Sentetik Salon firmasını aç"]')?.click())
    await click('Ödeme kaydet'); await fill('payment-reference', 'SYNTHETIC-SESSION'); await submit(); await submit()
    expect(container.textContent).toContain('Abonelik yönetimine giriş')
    expect(vi.mocked(HTMLElement.prototype.hidePopover)).toHaveBeenCalled()
    await fill('operator-email', 'operator@example.test'); await fill('operator-password', 'Synthetic!Password123');
    const authForm = [...container.querySelectorAll('form')].find(f => !f.closest('[hidden]'))
    if (!authForm) throw new Error('Giriş formu yok')
    await act(async () => authForm.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
    await fill('operator-code', '123456')
    await act(async () => authForm.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
    expect(container.querySelector<HTMLInputElement>('#payment-reference')?.value).toBe('SYNTHETIC-SESSION')
    await submit(); expect(writes).toBe(3); expect(new Set(sent).size).toBe(1)
    expect(container.textContent).toContain('Doğruladığınız ödeme kaydedildi.')
  })
  it('parola adımından sonra MFA olmadan firma bilgilerini yüklemez', async () => {
    let signed = false
    const fetch = requests(async (path) => {
      if (path === '/api/auth/me') return signed ? Response.json({ email: 'operator@example.test', mfaEnabled: true, operatorAccess: true }) : new Response(null, { status: 401 })
      if (path === '/api/auth/login') return Response.json({ requiresTwoFactor: true }, { status: 202 })
      if (path === '/api/auth/mfa/login') { signed = true; return new Response(null, { status: 204 }) }
      if (path.startsWith('/api/targets?')) return Response.json(firms)
      return Response.json(data)
    })
    await act(async () => root.render(<OperatorApplication />))
    await fill('operator-email', 'operator@example.test'); await fill('operator-password', 'Synthetic!Password123'); await submit()
    expect(container.textContent).toContain('İkinci adımı tamamlayın'); expect(fetch.mock.calls.some(([p]) => String(p).startsWith('/api/subscription'))).toBe(false)
    await fill('operator-code', '123456'); await submit()
    expect(container.textContent).toContain('Sentetik Salon'); expect(container.querySelector('input[type=password]')).toBeNull()
  })
  it('belirsiz yanıtta aynı anahtar ve içerikle tekrarlar, başarıyı yalnız sunucudan sonra gösterir', async () => {
    const bodies: string[] = []
    requests(async (_path, init) => {
      if (init?.method === 'POST') { bodies.push(String(init.body)); if (bodies.length === 1) throw new TypeError('network'); return Response.json({ id: 'payment-1' }) }
      return Response.json(data)
    })
    await act(async () => root.render(<SubscriptionOperator onExpired={vi.fn()} />))
    await click('Dönem ekle'); expect(container.querySelector<HTMLInputElement>('#subscription-amount')?.value).toBe(''); await click('Vazgeç')
    await click('Ödeme kaydet'); await fill('payment-reference', 'SYNTHETIC-REF')
    expect(container.textContent).toContain('1 Ekim 2026 – 31 Ekim 2026')
    expect([...container.querySelectorAll('button')].some(b => b.textContent === 'Önceki dönemler')).toBe(false)
    const pattern = container.querySelector<HTMLInputElement>('#payment-reference')?.pattern
    expect(pattern).toBeTruthy()
    const browserPattern = new RegExp(`^(?:${pattern})$`, 'v')
    expect(browserPattern.test('BANK/123:ABC_0.1-XYZ')).toBe(true)
    expect(browserPattern.test('BANK REF')).toBe(false)
    await submit(); expect(container.textContent).toContain('Sonuç belirsiz'); expect(container.textContent).not.toContain('Doğruladığınız ödeme kaydedildi.')
    expect(container.textContent).toContain('Sonuç doğrulanamadı. Aynı işlemle yeniden deneyin.')
    expect(container.textContent).not.toContain('network')
    expect(container.querySelector<HTMLInputElement>('#payment-reference')?.disabled).toBe(false)
    expect(container.querySelector('fieldset')?.disabled).toBe(true)
    await submit(); expect(bodies).toHaveLength(2); expect(bodies[0]).toBe(bodies[1]); expect(JSON.parse(bodies[0]).amountTry).toBe('1000.00')
    expect(container.textContent).toContain('Doğruladığınız ödeme kaydedildi.'); expect(container.querySelector('[role=dialog]')).toBeNull()
  })
  it('409 sonrası güncel sürüm yüklenmeden yeniden yazmayı engeller ve vazgeçmede bilgileri korur', async () => {
    let writes = 0
    const fetch = requests(async (_path, init) => init?.method === 'POST' ? ++writes === 1 ? Response.json({ title: 'Dönem değişti.' }, { status: 409 }) : Response.json({ id: 'payment-1' }) : Response.json(data))
    await act(async () => root.render(<SubscriptionOperator onExpired={vi.fn()} />)); await click('Ödeme kaydet'); await fill('payment-reference', 'SYNTHETIC-REF'); await submit()
    expect(container.textContent).toContain('Dönem değişti.'); const count = fetch.mock.calls.filter(([, i]) => i?.method === 'POST').length
    await submit(); expect(fetch.mock.calls.filter(([, i]) => i?.method === 'POST')).toHaveLength(count)
    vi.mocked(window.confirm).mockReturnValue(false); await click('Vazgeç'); expect(container.querySelector('[role=dialog]')).not.toBeNull()
    const originalKey = JSON.parse(String(fetch.mock.calls.find(([, i]) => i?.method === 'POST')?.[1]?.body)).requestKey
    await click('Güncel kayıtları yükle'); expect(container.querySelector<HTMLInputElement>('#payment-reference')?.value).toBe('SYNTHETIC-REF')
    await submit(); expect(container.textContent).toContain('Doğruladığınız ödeme kaydedildi.')
    const lastKey = JSON.parse(String(fetch.mock.calls.filter(([, i]) => i?.method === 'POST').at(-1)?.[1]?.body)).requestKey
    expect(lastKey).not.toBe(originalKey)
    await click('Ödeme kaydet'); await fill('payment-reference', 'SYNTHETIC-NEXT')
    vi.mocked(window.confirm).mockReturnValue(true); await click('Vazgeç'); expect(container.querySelector('[role=dialog]')).toBeNull()
  })
  it('boş, hata ve yetkisizlik durumlarında uydurma dönem veya ödeme göstermez', async () => {
    let fail = false
    requests(async () => fail ? new Response(null, { status: 503 }) : Response.json({ ...data, total: 0, periods: [], selectedPeriodId: null }))
    await act(async () => root.render(<SubscriptionOperator onExpired={vi.fn()} />)); expect(container.textContent).toContain('henüz abonelik dönemi kaydedilmedi')
    expect([...container.querySelectorAll('button')].some(b => b.textContent === 'Ödeme kaydet')).toBe(false)
    fail = true
    await act(async () => root.unmount()); root = createRoot(container)
    await act(async () => root.render(<SubscriptionOperator onExpired={vi.fn()} />)); expect(container.textContent).toContain('Firma kayıtları okunamadı.'); expect(container.querySelector('select')).toBeNull()
  })
})
