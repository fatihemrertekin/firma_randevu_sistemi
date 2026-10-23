// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { RequestVerificationError, type postWithCsrf } from '../../app/api'
import AppointmentReminder from './AppointmentReminder'
import type { Reminder } from './appointmentsApi'

const id = '00000001-0000-0000-0000-000000000001', version = '00000001-0000-0000-0000-000000000002'
let container: HTMLDivElement, root: Root, data: Reminder
const post = vi.fn<typeof postWithCsrf>(), busy = vi.fn(), unauthorized = vi.fn(), reload = vi.fn()
beforeEach(() => {
  data = { appointmentVersion: version, suppressed: false, hasStarted: false, scheduledFor: null, dispatchEnabled: false }
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('fetch', vi.fn(async () => Response.json(data)))
  post.mockReset(); busy.mockReset(); unauthorized.mockReset(); reload.mockReset()
  post.mockImplementation(async () => { data = { ...data, suppressed: true, scheduledFor: null }; return new Response(null, { status: 204 }) })
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.restoreAllMocks(); vi.unstubAllGlobals() })
async function render(currentVersion = version) {
  await act(async () => root.render(<AppointmentReminder key={currentVersion} id={id} version={currentVersion} disabled={false} post={post}
    onBusyChange={busy} onUnauthorized={unauthorized} onReload={reload} />))
}
function button(name: string) {
  const value = [...container.querySelectorAll('button')].find(item => item.textContent === name)
  if (!value) throw new Error('Düğme yok: ' + name)
  return value
}
async function click(name: string) { await act(async () => button(name).click()) }
async function confirm() { await click('Bu randevuda hatırlatmayı kapat'); await click('Hatırlatmayı kapat') }

it('kapalı gönderimde ret kaydeder, önce onay ister ve 204 ardından GET ile başarıyı doğrular', async () => {
  await render(); expect(container.textContent).toContain('Gönderim bu kurulumda kapalı.')
  await click('Bu randevuda hatırlatmayı kapat'); expect(post).not.toHaveBeenCalled()
  expect(document.activeElement?.textContent).toContain('kapatılsın mı?')
  await click('Hatırlatmayı kapat')
  expect(post).toHaveBeenCalledWith(`/api/appointments/${id}/reminder/disable`, { version }, expect.any(AbortSignal))
  expect(fetch).toHaveBeenCalledTimes(2); expect(container.textContent).toContain('Bu randevunun hatırlatması kapalı.')
  expect(container.querySelectorAll('button')).toHaveLength(0); expect(document.activeElement?.getAttribute('role')).toBe('status')
})
it('Vazgeç/Escape göndermez ve odağı ilk işleme döndürür', async () => {
  await render(); await click('Bu randevuda hatırlatmayı kapat'); await click('Vazgeç')
  expect(document.activeElement).toBe(button('Bu randevuda hatırlatmayı kapat'))
  await click('Bu randevuda hatırlatmayı kapat')
  await act(async () => document.activeElement?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })))
  expect(post).not.toHaveBeenCalled(); expect(document.activeElement).toBe(button('Bu randevuda hatırlatmayı kapat'))
})
it('kayıp POST yanıtını salt okunur doğrular, ikinci SMS veya otomatik POST üretmez', async () => {
  post.mockImplementation(async () => { data.suppressed = true; throw new TypeError('Synthetic response loss') })
  await render(); await confirm()
  expect(post).toHaveBeenCalledTimes(1); expect(container.textContent).toContain('Bu randevunun hatırlatması kapalı.')
})
it('kaydedilmemiş belirsiz isteği ancak kullanıcı aynı sürümle tekrarlar', async () => {
  post.mockRejectedValueOnce(new TypeError('Synthetic response loss'))
  await render(); await confirm(); expect(container.textContent).toContain('Sonuç doğrulanamadı.')
  expect(post).toHaveBeenCalledTimes(1); await click('Aynı kapatma isteğini yeniden dene')
  expect(post).toHaveBeenCalledTimes(2); expect(post.mock.calls[0][1]).toEqual(post.mock.calls[1][1])
  expect(container.textContent).toContain('Bu randevunun hatırlatması kapalı.')
})
it('204 sonrasında GET de kaybolursa başarı uydurmaz; yeniden okumayla toparlar', async () => {
  await render(); vi.mocked(fetch).mockRejectedValueOnce(new TypeError('Synthetic read loss'))
  await confirm(); expect(container.textContent).toContain('Sonuç doğrulanamadı.')
  expect(container.textContent).not.toContain('Bu randevunun hatırlatması kapalı.')
  await click('Hatırlatma durumunu yeniden yükle'); expect(post).toHaveBeenCalledTimes(1)
  expect(container.textContent).toContain('Bu randevunun hatırlatması kapalı.')
})
it('CSRF alınamamasını belirsiz kaydetme saymaz ve güvenlik mesajını korur', async () => {
  post.mockRejectedValue(new RequestVerificationError('İstek gönderilmedi; güvenlik doğrulamasını yeniden deneyin.'))
  await render(); await confirm(); expect(fetch).toHaveBeenCalledTimes(1)
  expect(container.textContent).toContain('İstek gönderilmedi;'); expect(container.textContent).not.toContain('Sonuç doğrulanamadı')
})
it.each([401, 403, 404, 409])('%s ret cevabında aynı POST tekrarını sunmaz', async status => {
  post.mockResolvedValue(new Response(null, { status }))
  await render(); await confirm()
  expect(container.textContent).not.toContain('Aynı kapatma isteğini yeniden dene')
  expect(button('Hatırlatmayı kapat').disabled).toBe(true)
  if (status === 401) expect(unauthorized).toHaveBeenCalled()
  await click('Güncel randevuyu yükle'); expect(reload).toHaveBeenCalled()
})
it('randevu sürümleri uyuşmazsa kapatmayı kilitler ve birlikte yeniden yüklemeyi ister', async () => {
  data.appointmentVersion = '00000001-0000-0000-0000-000000000003'
  await render(); expect(container.textContent).not.toContain('Bu randevuda hatırlatmayı kapat')
  await click('Güncel randevuyu yükle'); expect(reload).toHaveBeenCalled(); expect(post).not.toHaveBeenCalled()
})
it('POST kaybından sonraki GET farklı sürüm döndürürse eski isteği tekrar sunmaz', async () => {
  post.mockImplementation(async () => { data.appointmentVersion = '00000001-0000-0000-0000-000000000003'; throw new TypeError('loss') })
  await render(); await confirm(); expect(container.textContent).not.toContain('Aynı kapatma isteğini yeniden dene')
  expect(container.textContent).toContain('Güncel randevuyu yükle')
})
it('başlamış gönderimde kapatma kaydı alınsa da SMS geri alındı demez', async () => {
  data.hasStarted = true; await render(); await confirm()
  expect(container.textContent).toContain('başlamış SMS geri alınamayabilir')
  expect(container.textContent).not.toContain('teslim edildi')
})
it('yükleme hatasını açık tercih saymaz', async () => {
  vi.mocked(fetch).mockRejectedValueOnce(new TypeError('Synthetic offline'))
  await render(); expect(container.textContent).toContain('Hatırlatma bilgisi alınamadı.')
  expect(container.textContent).not.toContain('Bu randevuda hatırlatmayı kapat')
  await click('Hatırlatma durumunu yeniden yükle'); expect(container.textContent).toContain('Bu randevuda hatırlatmayı kapat')
})
it('429 yanıtında Retry-After boyunca yazmayı ve tekrar okumayı kilitler', async () => {
  post.mockResolvedValue(new Response(null, { status: 429, headers: { 'Retry-After': '10' } }))
  await render(); await confirm()
  expect(button('Hatırlatmayı kapat').disabled).toBe(true)
  expect(button('Hatırlatma durumunu yeniden yükle').disabled).toBe(true)
  expect(post).toHaveBeenCalledTimes(1); expect(fetch).toHaveBeenCalledTimes(1)
})
it('planlanan zamanı teslim edilmiş SMS olarak sunmaz', async () => {
  data.dispatchEnabled = true; data.scheduledFor = '2026-10-23T06:00:00Z'
  await render(); expect(container.textContent).toContain('Planlanan gönderim:')
  expect(container.textContent).not.toContain('Gönderim bu kurulumda kapalı.')
  expect(container.textContent).not.toContain('teslim edildi')
})
it('bekleyen POST çift tıklamada tekrarlanmaz ve ana ekrana busy bildirir', async () => {
  let finish: ((value: Response) => void) | undefined
  post.mockImplementation(() => new Promise(resolve => { finish = resolve }))
  await render(); await click('Bu randevuda hatırlatmayı kapat')
  await act(async () => { button('Hatırlatmayı kapat').click(); button('Hatırlatmayı kapat').click() })
  expect(post).toHaveBeenCalledTimes(1); expect(busy).toHaveBeenLastCalledWith(true)
  expect(container.textContent).toContain('Hatırlatma bilgisi doğrulanıyor…')
  await act(async () => { data.suppressed = true; finish?.(new Response(null, { status: 204 })) })
  expect(busy).toHaveBeenLastCalledWith(false)
})
it('randevu değişiminde önceki okumayı iptal eder, geç yanıtı yeni kayda uygulamaz', async () => {
  let finish: ((value: Response) => void) | undefined
  vi.mocked(fetch).mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
  await render(); const firstSignal = vi.mocked(fetch).mock.calls[0][1]?.signal
  const next = '00000001-0000-0000-0000-000000000003'; data.appointmentVersion = next
  await render(next); expect(firstSignal?.aborted).toBe(true)
  await act(async () => finish?.(Response.json({ ...data, appointmentVersion: version, suppressed: true })))
  expect(container.textContent).toContain('Bu randevuda hatırlatmayı kapat')
  expect(container.textContent).not.toContain('Bu randevunun hatırlatması kapalı.')
})
