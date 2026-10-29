// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import SubscriptionOperator from './SubscriptionOperator'
import OperatorApplication from './OperatorApplication'

const period = { id: 'abc8d34f-9159-49b1-b268-64c5d57ff7ec', startsOn: '2026-10-01', endsOn: '2026-11-01', dueOn: '2026-10-01', version: 'abc8d34f-9159-49b1-b268-64c5d57ff7ed', amountTry: '1000.00', paidTry: '0.00', outstandingTry: '1000.00', overdue: true }
const data = { targetId: 'synthetic-firm', targetName: 'Sentetik Salon', asOf: '2026-10-29', page: 1, total: 1, selectedPeriodId: period.id, periods: [period], payments: [], paymentsPage: 1, paymentCount: 0 }
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
  const form = container.querySelector('form'); if (!form) throw new Error('Form bulunamadı')
  await act(async () => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
}
describe('Ayrı operatör abonelik akışı', () => {
  it('parola adımından sonra MFA olmadan firma bilgilerini yüklemez', async () => {
    let signed = false
    const fetch = requests(async (path) => {
      if (path === '/api/auth/me') return signed ? Response.json({ email: 'operator@example.test', mfaEnabled: true, operatorAccess: true }) : new Response(null, { status: 401 })
      if (path === '/api/auth/login') return Response.json({ requiresTwoFactor: true }, { status: 202 })
      if (path === '/api/auth/mfa/login') { signed = true; return new Response(null, { status: 204 }) }
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
    await act(async () => root.render(<SubscriptionOperator onExpired={vi.fn()} />)); await click('Ödeme kaydet'); await fill('payment-reference', 'SYNTHETIC-REF')
    await submit(); expect(container.textContent).toContain('Sonuç belirsiz'); expect(container.textContent).not.toContain('Doğruladığınız ödeme kaydedildi.')
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
