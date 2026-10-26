// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import BookingWizard from './BookingWizard'
import BookingResult from './BookingResult'
import App from '../../App'
import { BookingError, readAccess, readResponse, isResult, isCatalog, isAccess, isAvailability, type Result } from './publicBookingApi'
const id = (n: number) => `00000000-0000-0000-0000-${String(n).padStart(12, '0')}`
const start = new Date(Date.now() + 86400000).toISOString(), end = new Date(Date.parse(start) + 1800000).toISOString()
const service = { id: id(1), version: id(2), name: 'Sentetik hizmet', price: '500.00', currency: 'TRY', durationMinutes: 30 }
const member = { id: id(3), name: 'Sentetik çalışan' }
const policy = { version: id(4), leadMinutes: 60, horizonDays: 30, slotMinutes: 15, timeZone: 'Europe/Istanbul', customerCancellationLeadMinutes: 120, requiresBusinessApproval: true }
const catalog = { business: { name: 'Sentetik işletme', phone: null, address: null }, policy, items: [service], page: 1, pageSize: 20, hasMore: false, totalCount: 1 }
const result: Result = { id: id(5), version: id(6), serviceName: service.name, staffName: member.name, startsAt: start, endsAt: end, price: '500.00', currency: 'TRY', status: 'Pending', pendingExpiresAt: new Date(Date.now() + 1800000).toISOString(), policyVersion: policy.version, customerCancellationLeadMinutes: 120, requiresBusinessApproval: true, cancellationDeadline: new Date(Date.parse(start) - 7200000).toISOString() }
const challenge = { challengeId: id(7), challengeSecret: 'a'.repeat(43), maskedPhone: '+90******0001', expiresAt: new Date(Date.now() + 300000).toISOString(), resendAt: new Date(Date.now() + 60000).toISOString() }
const access = { token: 'b'.repeat(43), version: id(8), expiresAt: new Date(Date.now() + 86400000).toISOString() }
let container: HTMLDivElement, root: Root, router: ReturnType<typeof createMemoryRouter> | undefined
type Call = { url: string; body: Record<string, unknown>; init?: RequestInit }
let calls: Call[], override: ((call: Call) => Response | Promise<Response> | undefined) | undefined
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); calls = []; override = undefined
  vi.stubGlobal('fetch', vi.fn(async (input: string, init?: RequestInit) => {
    const call = { url: input, body: init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : {}, init }; calls.push(call)
    const special = override?.(call); if (special) return special
    const url = new URL(input, 'http://localhost').pathname
    return Response.json(url.endsWith('/csrf') ? { token: 'csrf' } : url.endsWith('/catalog') ? catalog : url.endsWith('/staff') ? { ...catalog, items: [member] } : url.endsWith('/availability') ? { timeZone: 'Europe/Istanbul', serviceVersion: service.version, policyVersion: policy.version, startsAt: [start] } : url.endsWith('/request') ? challenge : url.endsWith('/verify') ? { verified: true, verifiedUntil: challenge.expiresAt } : url.endsWith('/access') ? access : url.endsWith('/cancel') ? { ...result, status: 'Cancelled', version: id(9) } : result)
  }))
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); router?.dispose(); router = undefined; container.remove(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
async function mount(element: React.ReactNode) { router = createMemoryRouter([{ path: '*', element }]); await act(async () => root.render(<RouterProvider router={router!}/>)) }
async function click(text: string) { const button = [...container.querySelectorAll('button')].find(item => (item.getAttribute('aria-label') ?? item.textContent) === text); if (!button) throw new Error('Düğme yok: ' + text); await act(async () => button.click()) }
async function radio(name: string) { const input = container.querySelector<HTMLInputElement>(`input[name="${name}"]`); if (!input) throw new Error(name); await act(async () => input.click()) }
async function input(id: string, value: string) { const el = container.querySelector<HTMLInputElement>('#' + id)!; await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(el, value); el.dispatchEvent(new Event('input', { bubbles: true })) }) }
async function submit() { await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))) }
async function throughContact(onCreated = vi.fn()) {
  await mount(<BookingWizard onCreated={onCreated} onBusiness={() => {}}/>); await radio('service'); await click('Devam et'); await radio('member'); await click('Devam et'); await radio('time'); await click('Devam et')
  await input('booking-customerName', 'Sentetik müşteri'); await input('booking-customerPhone', '05000000001'); await submit(); return onCreated
}
async function verified(onCreated = vi.fn()) { await throughContact(onCreated); await input('booking-code', '123456'); await submit(); return onCreated }

it('kamu rotası Owner oturumu sorgulamadan katalog açar', async () => {
  window.history.replaceState(null, '', '/randevu'); await act(async () => root.render(<App/>))
  expect(container.textContent).toContain('Sentetik işletme'); expect(container.textContent).toContain('Randevu alın'); expect(calls.some(c => c.url === '/api/auth/me')).toBe(false)
})
it('sonuç yetkisini router ve ilk API çağrısından önce URL ve history state içinden temizler', async () => {
  window.history.replaceState({ secret: access.token }, '', `/randevu/sonuc/${result.id}#erisim=${access.token}`)
  await act(async () => root.render(<App/>))
  expect(window.location.hash).toBe(''); expect(window.history.state).not.toHaveProperty('secret')
  expect(calls.filter(c => c.url.endsWith(result.id))[0]?.init?.headers).toEqual({ Authorization: `Bearer ${access.token}` })
  expect(container.textContent).toContain('İşletme onayı bekleniyor'); expect(container.textContent).not.toContain(access.token)
})
it.each(['short', 'x'.repeat(44), 'a'.repeat(42) + '!', ''])('bozuk fragment %s temizlenir ve yetki sayılmaz', value => {
  window.history.replaceState(null, '', `/randevu/sonuc/${result.id}#erisim=${value}`); expect(readAccess()).toBeNull(); expect(window.location.hash).toBe('')
})
it('kod doğrulaması randevu oluşturmaz; ayrı son gönderim gerekir', async () => {
  const created = await verified(); expect(container.textContent).toContain('Talebiniz henüz gönderilmedi.'); expect(calls.filter(c => c.url.endsWith('/appointments'))).toHaveLength(0)
  await click('Randevu talebi gönder'); expect(created).toHaveBeenCalledTimes(1)
  const request = calls.find(c => c.url.endsWith('/request'))!, write = calls.find(c => c.url.endsWith('/appointments'))!
  expect(write.body).toMatchObject({ ...request.body, challengeSecret: challenge.challengeSecret })
})
it('yanıtı kaybolan yazımı aynı anahtar ve içerikle tekrarlar; yeni OTP veya kayıt açmaz', async () => {
  let writes = 0; override = c => c.url.endsWith('/appointments') && ++writes === 1 ? Promise.reject(new TypeError('lost')) : undefined
  const created = await verified(); await click('Randevu talebi gönder'); expect(container.textContent).toContain('Talebin sonucu doğrulanamadı')
  await click('Aynı talebi yeniden dene'); const sent = calls.filter(c => c.url.endsWith('/appointments'))
  expect(sent[0].body).toEqual(sent[1].body); expect(created).toHaveBeenCalledTimes(1); expect(calls.filter(c => c.url.endsWith('/request'))).toHaveLength(1)
})
it('seçim değiştirmek önceki doğrulamayı temizler', async () => {
  await verified(); await click('Hizmeti değiştir'); expect(container.querySelector('#booking-code')).toBeNull(); await click('Devam et')
  expect(calls.filter(c => c.url.endsWith('/appointments'))).toHaveLength(0)
})
it('ilk OTP yanıtı kayıpsa yeni kod isteği açıklamalı ayrı anahtar kullanır', async () => {
  let count = 0; override = c => c.url.endsWith('/request') ? (++count === 1 ? Promise.reject(new TypeError('lost')) : count === 2 ? Response.json({ code: 'verification_request_exists' }, { status: 409 }) : undefined) : undefined
  await throughContact(); await submit(); await click('Yeni kod iste')
  const requests = calls.filter(c => c.url.endsWith('/request')); expect(requests[0].body).toEqual(requests[1].body); expect(requests[2].body.requestKey).not.toBe(requests[0].body.requestKey)
})
it.each([400,403,409,429,503])('OTP %i reddi başarı veya SMS teslimi üretmez', async status => {
  override = c => c.url.endsWith('/request') ? Response.json({}, { status }) : undefined
  await throughContact(); expect(container.querySelector('[role="alert"]')).not.toBeNull(); expect(container.textContent).not.toContain('Kod isteğiniz alındı'); expect(calls.filter(c => c.url.endsWith('/appointments'))).toHaveLength(0)
})
it('resend sunucunun bekleme süresine kadar kapalıdır', async () => { await throughContact(); expect([...container.querySelectorAll('button')].find(b => b.textContent === 'Kodu tekrar iste')?.disabled).toBe(true) })
it('erişim üretimi başarısız olsa da kaydedilmiş randevu sonucunu korur', async () => {
  override = c => c.url.endsWith('/access') ? Response.json({}, { status: 503 }) : undefined
  await mount(<BookingResult id={result.id} initialResult={result} receipt={challenge}/>); expect(container.textContent).toContain('İşletme onayı bekleniyor'); expect(container.textContent).toContain('Randevunuz kaydedildi; sonuç bağlantısı henüz hazırlanamadı')
})
it('kayıp erişim yanıtından sonra tokenı otomatik döndürmez, açık yenileme ister', async () => {
  let requests = 0; override = c => c.url.endsWith('/access') && ++requests === 1 ? Promise.reject(new TypeError('lost')) : undefined
  await mount(<BookingResult id={result.id} initialResult={result} receipt={challenge}/>); expect(calls.filter(c => c.url.endsWith('/access'))).toHaveLength(1)
  await click('Yeni bağlantı oluştur'); expect(calls.filter(c => c.url.endsWith('/access')).map(c => c.body.replaceExisting)).toEqual([false,true])
})
it('iptal açık onay gerektirir ve kayıp yanıtta aynı version/requestKey kullanır', async () => {
  let requests = 0; override = c => c.url.endsWith('/cancel') && ++requests === 1 ? Promise.reject(new TypeError('lost')) : undefined
  await mount(<BookingResult id={result.id} initialToken={access.token}/>); expect(calls.filter(c => c.url.endsWith('/cancel'))).toHaveLength(0)
  await click('Randevuyu iptal et'); expect(calls.filter(c => c.url.endsWith('/cancel'))).toHaveLength(0)
  await click('Evet, randevuyu iptal et'); await click('Aynı iptali yeniden dene')
  const sent = calls.filter(c => c.url.endsWith('/cancel')); expect(sent[0].body).toEqual(sent[1].body)
  expect(sent[0].init?.headers).toMatchObject({ Authorization: `Bearer ${access.token}`, 'X-CSRF-TOKEN': 'csrf' }); expect(container.textContent).toContain('Randevu iptal edildi')
})
it('token, iletişim ve kodu tarayıcı deposuna yazmaz; kopyalama hatasında başarı söylemez', async () => {
  const storage = vi.spyOn(Storage.prototype, 'setItem'); vi.stubGlobal('navigator', { clipboard: { writeText: vi.fn().mockRejectedValue(new Error('denied')) } })
  await mount(<BookingResult id={result.id} initialToken={access.token}/>); await click('Sonuç bağlantısını kopyala')
  expect(container.textContent).toContain('Bağlantı kopyalanamadı'); expect(container.querySelector<HTMLInputElement>('#result-link')?.value).toContain('#erisim='); expect(storage).not.toHaveBeenCalled()
})
it('geçersiz bağlantı işletme girişine veya kişi aramasına yönlenmez', async () => {
  override = c => c.url.endsWith(result.id) ? Response.json({}, { status: 403 }) : undefined
  await mount(<BookingResult id={result.id} initialToken={access.token}/>); expect(container.textContent).toContain('Bağlantı geçersiz veya süresi dolmuş'); expect(container.querySelector('input[type="tel"]')).toBeNull(); expect(calls.some(c => c.url === '/api/auth/me')).toBe(false)
})
it('Pending süre dolması sunucu Cancelled demeden iptal başarısı sayılmaz', async () => {
  const expired = { ...result, pendingExpiresAt: new Date(Date.now() - 1000).toISOString() }; override = c => c.url.endsWith(result.id) ? Response.json(expired) : undefined
  await mount(<BookingResult id={result.id} initialToken={access.token}/>); expect(container.textContent).toContain('Onay süresi doldu'); expect(container.textContent).not.toContain('Randevu iptal edildi'); expect([...container.querySelectorAll('button')].some(b => b.textContent === 'Randevuyu iptal et')).toBe(false)
})
it.each([null, {}, { ...result, version: 'bad' }, { ...result, price: 1 }, { ...result, status: 'Admin' }])('sonuç DTO bozuksa sahte başarı göstermez: %j', value => { expect(isResult(value)).toBe(false) })
it('katalog ve uygunluk DTOları doğrulanır; string fiyat korunur', () => { expect(isCatalog(catalog)).toBe(true); expect(isCatalog({ ...catalog, items: [{ ...service, price: 1 }] })).toBe(false); expect(isAvailability({ startsAt: [start], timeZone: 'UTC', serviceVersion: id(1), policyVersion: id(2) })).toBe(false); expect(isAccess({ ...access, token: 'bad' })).toBe(false) })
it('429 başlığı yoksa uydurma saniye sayacı oluşturmaz', async () => { await expect(readResponse(Response.json({}, { status: 429 }), isResult)).rejects.toMatchObject({ status: 429, retryAt: 0 }); expect(new BookingError(409).message).not.toContain('Saat doldu') })
it('geçersiz kod için neyin düzeltileceğini söyler; kalan deneme sayısı uydurmaz', async () => {
  override = c => c.url.endsWith('/verify') ? Response.json({}, { status: 400 }) : undefined
  await throughContact(); await input('booking-code', '111111'); await submit()
  expect(container.querySelector('[role="alert"]')?.textContent).toContain('Kod geçersiz veya süresi dolmuş')
})
it('henüz okunmamış hizmet sayfalarını boş katalog saymaz', async () => {
  override = c => c.url.includes('/catalog') ? Response.json({ ...catalog, totalCount: 21, hasMore: true }) : undefined
  await mount(<BookingWizard onCreated={() => {}} onBusiness={() => {}}/>); expect(container.textContent).toContain('21 kayıttan'); expect(container.textContent).not.toContain('hizmet bulunmuyor')
})
it('sıfır hizmet ve boş işletme adı gerçek boş durumu gösterir', async () => {
  override = c => c.url.includes('/catalog') ? Response.json({ ...catalog, business: { ...catalog.business, name: '' }, totalCount: 0, items: [] }) : undefined
  await mount(<BookingWizard onCreated={() => {}} onBusiness={() => {}}/>); expect(container.textContent).toContain('Çevrim içi seçilebilecek hizmet bulunmuyor')
})
it('iptal 409 sonrası güncel sonucu okur ve yeni onay olmadan tekrar iptal etmez', async () => {
  let reads = 0; override = c => c.url.endsWith('/cancel') ? Response.json({}, { status: 409 }) : c.url.endsWith(result.id) ? Response.json({ ...result, version: ++reads > 1 ? id(10) : result.version }) : undefined
  await mount(<BookingResult id={result.id} initialToken={access.token}/>); await click('Randevuyu iptal et'); await click('Evet, randevuyu iptal et')
  expect(reads).toBe(2); expect(calls.filter(c => c.url.endsWith('/cancel'))).toHaveLength(1)
  expect(container.textContent).not.toContain('Evet, randevuyu iptal et')
})
it('iptal CSRF reddini belirsiz iptal saymaz', async () => {
  override = c => c.url.endsWith('/cancel') ? Response.json({ code: 'csrf_invalid' }, { status: 400 }) : undefined
  await mount(<BookingResult id={result.id} initialToken={access.token}/>); await click('Randevuyu iptal et'); await click('Evet, randevuyu iptal et')
  expect(container.textContent).toContain('işlem uygulanmadı'); expect(container.textContent).not.toContain('İptal sonucu doğrulanamadı')
})
it('eski uygunluk yanıtı yeni tarih seçimini ezmez', async () => {
  let release: ((response: Response) => void) | undefined, requested = 0
  const stale = new Date(Date.parse(start) + 3600000).toISOString()
  override = c => c.url.includes('/availability') && ++requested === 1 ? new Promise<Response>(resolve => { release = resolve }) : undefined
  await mount(<BookingWizard onCreated={() => {}} onBusiness={() => {}}/>); await radio('service'); await click('Devam et'); await radio('member'); await click('Devam et')
  const next = new Date(Date.now() + 172800000).toISOString().slice(0,10); await input('booking-date', next)
  await act(async () => release?.(Response.json({ timeZone: 'Europe/Istanbul', serviceVersion: service.version, policyVersion: policy.version, startsAt: [stale] })))
  expect(container.querySelector<HTMLInputElement>('input[name="time"]')?.closest('label')?.textContent).not.toContain(new Intl.DateTimeFormat('tr-TR', { timeZone: 'Europe/Istanbul', hour: '2-digit', minute: '2-digit' }).format(new Date(stale)).replace(':','.'))
  expect(container.querySelectorAll('input[name="time"]')).toHaveLength(1)
})
it.each(['rate', 'csrf'])('kayıp yazım sonrası %s reddi önceki belirsizliği ve anahtarı temizlemez', async kind => {
  let attempts = 0
  override = c => c.url.endsWith('/appointments') ? (++attempts === 1 ? Promise.reject(new TypeError('lost')) : attempts === 2 ? Response.json(kind === 'csrf' ? { code: 'csrf_invalid' } : {}, { status: kind === 'csrf' ? 400 : 429 }) : undefined) : undefined
  const created = await verified(); await click('Randevu talebi gönder'); await click('Aynı talebi yeniden dene')
  expect(container.querySelector<HTMLButtonElement>('[aria-label="Hizmeti değiştir"]')?.disabled).toBe(true)
  await click('Aynı talebi yeniden dene'); const writes = calls.filter(c => c.url.endsWith('/appointments'))
  expect(writes).toHaveLength(3); expect(writes[0].body).toEqual(writes[1].body); expect(writes[1].body).toEqual(writes[2].body); expect(created).toHaveBeenCalledTimes(1)
})
it.each(['rate', 'csrf'])('kayıp iptal sonrası %s reddinde aynı sürüm/anahtarla devam eder', async kind => {
  let attempts = 0
  override = c => c.url.endsWith('/cancel') ? (++attempts === 1 ? Promise.reject(new TypeError('lost')) : attempts === 2 ? Response.json(kind === 'csrf' ? { code: 'csrf_invalid' } : {}, { status: kind === 'csrf' ? 400 : 429 }) : undefined) : undefined
  await mount(<BookingResult id={result.id} initialToken={access.token}/>); await click('Randevuyu iptal et'); await click('Evet, randevuyu iptal et'); await click('Aynı iptali yeniden dene'); await click('Aynı iptali yeniden dene')
  const writes = calls.filter(c => c.url.endsWith('/cancel')); expect(writes).toHaveLength(3); expect(writes[0].body).toEqual(writes[1].body); expect(writes[1].body).toEqual(writes[2].body)
  expect(container.textContent).toContain('Randevu iptal edildi')
})
it.each([
  ['customerName', 'name-error', 'Ad 2–100 karakter olmalı.'],
  ['customerPhone', 'phone-help', 'Geçerli bir Türkiye cep numarası girin.'],
])('OTP talebindeki %s alan hatasını açıklar, odaklar ve düzeltmede temizler', async (name, description, message) => {
  override = c => c.url.endsWith('/verification/request') ? Response.json({ errors: { [name]: [message] } }, { status: 400 }) : undefined
  await throughContact()
  const field = container.querySelector<HTMLInputElement>('#booking-' + name)!
  expect(field.getAttribute('aria-invalid')).toBe('true'); expect(document.activeElement).toBe(field)
  expect(field.getAttribute('aria-describedby')).toBe(description); expect(container.querySelector('#' + description)?.textContent).toContain(message)
  expect(calls.some(c => c.url.endsWith('/appointments') || c.url.endsWith('/verification/verify'))).toBe(false)
  override = undefined; await input('booking-' + name, name === 'customerName' ? 'Düzeltilen ad' : '05000000002')
  expect(field.getAttribute('aria-invalid')).toBe('false'); expect(container.querySelector('#' + description)?.textContent ?? '').not.toContain(message)
  await submit(); expect(container.querySelector('#booking-code')).not.toBeNull()
})
it('gerçek uygunluk sözleşmesindeki tarih alan hatasını alanla bağlar ve odaklar', async () => {
  override = c => c.url.includes('/availability') ? Response.json({ errors: { date: ['Bugünden başlayarak geçerli aralıkta bir tarih seçin.'] } }, { status: 400 }) : undefined
  await mount(<BookingWizard onCreated={() => {}} onBusiness={() => {}}/>); await radio('service'); await click('Devam et'); await radio('member'); await click('Devam et')
  const field = container.querySelector<HTMLInputElement>('#booking-date')!
  expect(field.getAttribute('aria-invalid')).toBe('true'); expect(document.activeElement).toBe(field); expect(container.querySelector('#' + field.getAttribute('aria-describedby'))?.textContent).toContain('geçerli aralıkta')
})
