// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import AppointmentExport from './AppointmentExport'

let container: HTMLDivElement, root: Root
const mime = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
const workbookResponse = () => new Response(new Uint8Array([80, 75, 3, 4, 0]), { headers: { 'Content-Type': mime } })
const oldCreate = Object.getOwnPropertyDescriptor(URL, 'createObjectURL'), oldRevoke = Object.getOwnPropertyDescriptor(URL, 'revokeObjectURL')
const oldHide = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'hidePopover')
function toggle(element: HTMLElement, showing: boolean) {
  const event = new Event('toggle'); Object.defineProperty(event, 'newState', { value: showing ? 'open' : 'closed' }); element.dispatchEvent(event)
}
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); vi.stubGlobal('fetch', vi.fn(async () => workbookResponse()))
  Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: vi.fn(() => 'blob:synthetic-export') })
  Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() })
  Object.defineProperty(HTMLElement.prototype, 'hidePopover', { configurable: true, value: function (this: HTMLElement) { toggle(this, false) } })
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => {
  await act(async () => root.unmount()); container.remove(); vi.useRealTimers(); vi.unstubAllGlobals(); vi.restoreAllMocks()
  for (const [object, name, descriptor] of [[URL, 'createObjectURL', oldCreate], [URL, 'revokeObjectURL', oldRevoke], [HTMLElement.prototype, 'hidePopover', oldHide]] as const) {
    if (descriptor) Object.defineProperty(object, name, descriptor); else Reflect.deleteProperty(object, name)
  }
})
async function render(day = '2026-10-29', staff = '') {
  const unauthorized = vi.fn(), rate = vi.fn(), downloaded = vi.fn()
  await act(async () => root.render(<AppointmentExport key={day + staff} day={day} staff={staff} disabled={false}
    onUnauthorized={unauthorized} onRateLimit={rate} onDownloaded={downloaded} />))
  return { unauthorized, rate, downloaded }
}
async function open() { await act(async () => toggle(container.querySelector<HTMLElement>('[popover]')!, true)) }
async function submit() { await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))) }
async function change(label: string, value: string) {
  const id = [...container.querySelectorAll('label')].find(element => element.textContent === label)!.htmlFor
  const element = document.getElementById(id) as HTMLInputElement
  await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(element, value); element.dispatchEvent(new Event('input', { bubbles: true })) })
}

describe('Excel indirme', () => {
  it('başta yalnız düğme gösterir; doğru aralık/çalışanla indirmeyi başlatır ve geçici URL temizlenir', async () => {
    const callbacks = await render('2026-10-29', '00000045-0000-0000-0000-000000000001')
    expect(container.querySelector('form')).toBeNull(); expect(fetch).not.toHaveBeenCalled()
    await open(); expect(document.activeElement?.getAttribute('type')).toBe('date')
    await change('Bitiş tarihi', '2026-10-31'); vi.useFakeTimers(); await submit()
    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, options] = vi.mocked(fetch).mock.calls[0]
    const query = new URL(String(url), 'https://example.test').searchParams
    expect(query.get('from')).toBe('2026-10-29'); expect(query.get('to')).toBe('2026-10-31')
    expect(query.get('staffMemberId')).toBe('00000045-0000-0000-0000-000000000001'); expect(options?.cache).toBe('no-store')
    expect(options?.method).toBeUndefined(); expect(callbacks.downloaded).toHaveBeenCalledOnce()
    const link = vi.mocked(HTMLAnchorElement.prototype.click).mock.instances[0]
    expect(link).toBeInstanceOf(HTMLAnchorElement)
    if (!(link instanceof HTMLAnchorElement)) throw new Error('İndirme bağlantısı yok')
    expect(link.download).toBe('randevular_2026-10-29_2026-10-31.xlsx'); expect(link.href).toBe('blob:synthetic-export')
    expect(container.querySelector('form')).toBeNull(); expect(document.activeElement?.textContent).toBe("Excel'e aktar")
    vi.advanceTimersByTime(10000); expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:synthetic-export')
  })
  it('geçersiz/ters veya31 günden büyük aralıkta istek göndermez', async () => {
    await render(); await open()
    for (const value of ['2026-10-28', '2026-12-01', '']) {
      await change('Bitiş tarihi', value); await submit(); expect(fetch).not.toHaveBeenCalled(); expect(container.querySelector('[role=alert]')?.textContent).toContain('31 günlük')
    }
  })
  it('çift gönderimi engeller; Vazgeç eski yanıtın dosya indirmesini önler', async () => {
    let finish: (value: Response) => void = () => {}; vi.mocked(fetch).mockImplementation(() => new Promise(resolve => { finish = resolve }))
    const callbacks = await render(); await open(); await submit(); await submit(); expect(fetch).toHaveBeenCalledTimes(1)
    const signal = vi.mocked(fetch).mock.calls[0][1]?.signal
    await act(async () => [...container.querySelectorAll('button')].find(button => button.textContent === 'Vazgeç')!.click())
    expect(signal?.aborted).toBe(true); await act(async () => finish(workbookResponse()))
    expect(HTMLAnchorElement.prototype.click).not.toHaveBeenCalled(); expect(callbacks.downloaded).not.toHaveBeenCalled()
  })
  it('tarih/çalışan bağlamından ayrılma eski isteği keser ve geç dosyayı indirmez', async () => {
    let finish: (value: Response) => void = () => {}; vi.mocked(fetch).mockImplementation(() => new Promise(resolve => { finish = resolve }))
    await render(); await open(); await submit(); const signal = vi.mocked(fetch).mock.calls[0][1]?.signal
    await render('2026-10-30'); expect(signal?.aborted).toBe(true); await act(async () => finish(workbookResponse()))
    expect(HTMLAnchorElement.prototype.click).not.toHaveBeenCalled(); expect(container.querySelector('form')).toBeNull()
  })
  it.each([400, 401, 403, 429])('%s hatasında dosya indirmez, doğru hata/yetki/kota yolunu kullanır', async status => {
    vi.mocked(fetch).mockResolvedValue(Response.json({ errors: { date: ['Aralığı daraltın.'] } }, { status, headers: { 'Retry-After': '30' } }))
    const callbacks = await render(); await open(); await submit()
    expect(container.querySelector('[role=alert]')).not.toBeNull(); expect(HTMLAnchorElement.prototype.click).not.toHaveBeenCalled()
    expect(callbacks.unauthorized).toHaveBeenCalledTimes(status === 401 ? 1 : 0); expect(callbacks.rate).toHaveBeenCalledTimes(status === 429 ? 1 : 0)
    if (status === 400) expect(container.querySelector('[role=alert]')?.textContent).toBe('Aralığı daraltın.')
  })
})
