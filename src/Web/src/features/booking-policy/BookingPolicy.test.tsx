// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import BookingPolicy from './BookingPolicy'
import { readBookingPolicy, type BookingPolicy as Policy } from './bookingPolicyApi'
import type { postWithCsrf } from '../../app/api'

const original: Policy = { version: 'e662d654-471a-4c80-941f-f8f1073fd00a', leadMinutes: 60, horizonDays: 30, slotMinutes: 15, timeZone: 'Europe/Istanbul' }
const saved: Policy = { ...original, version: 'b3391166-03f1-4f58-8ed3-a7b5a98c52f1', leadMinutes: 90 }
let container: HTMLDivElement, root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('fetch', vi.fn(async () => Response.json(original)))
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.useRealTimers(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
async function render(post = vi.fn<typeof postWithCsrf>(async () => Response.json(saved))) {
  const dirty = vi.fn(), busy = vi.fn()
  await act(async () => root.render(<BookingPolicy post={post} onDirtyChange={dirty} onBusyChange={busy} />))
  return { post, dirty, busy }
}
function field(name = 'leadMinutes') {
  const element = container.querySelector<HTMLInputElement>(`input[name="${name}"]`)
  if (!element) throw new Error('Alan yok: ' + name)
  return element
}
async function fill(name: string, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  if (!setter) throw new Error('Alan ayarlayıcı yok')
  await act(async () => { setter.call(field(name), value); field(name).dispatchEvent(new Event('input', { bubbles: true })) })
}
async function submit() { await act(async () => container.querySelector('form')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))) }
function saveButton() { return container.querySelector<HTMLButtonElement>('button[type=submit]') }
async function reload() { await act(async () => Array.from(container.querySelectorAll('button')).find(button => /Güncel ayarları|Yeniden dene/u.test(button.textContent ?? ''))?.click()) }

describe('Rezervasyon ayarları', () => {
  it('yükleme sırasında varsayılan form sunmaz; doğrulanan yanıtla temiz form açar', async () => {
    let finish: (response: Response) => void = () => { throw new Error('Bekleyen GET yok') }
    vi.mocked(fetch).mockImplementation(() => new Promise(resolve => { finish = resolve }))
    const { busy } = await render()
    expect(container.textContent).toContain('yükleniyor')
    expect(container.querySelector('input')).toBeNull(); expect(busy).toHaveBeenLastCalledWith(true)
    await act(async () => finish(Response.json(original)))
    expect(field().value).toBe('60'); expect(saveButton()?.disabled).toBe(true); expect(busy).toHaveBeenLastCalledWith(false)
    expect(fetch).toHaveBeenCalledWith('/api/booking-policy/', expect.objectContaining({ cache: 'no-store', signal: expect.any(AbortSignal) }))
  })
  it('üç ayarı ve güncel sürümü gönderir; sunucu onayından sonra yeni temel değerleri kullanır', async () => {
    const { post, dirty } = await render(); await fill('leadMinutes', '90'); await submit()
    expect(post).toHaveBeenCalledWith('/api/booking-policy/', { version: original.version, leadMinutes: 90, horizonDays: 30, slotMinutes: 15 }, expect.any(AbortSignal))
    expect(container.textContent).toContain('Rezervasyon ayarları kaydedildi.')
    expect(dirty).toHaveBeenLastCalledWith(false); expect(saveButton()?.disabled).toBe(true)
    await fill('horizonDays', '31'); await submit()
    expect(post.mock.calls[1]?.[1]).toEqual({ version: saved.version, leadMinutes: 90, horizonDays: 31, slotMinutes: 15 })
  })
  it.each(['7', '007'])('%s adımını kabul eder ve saat örneğini gece yarısına hizalar', async value => {
    const { post } = await render(); await fill('slotMinutes', value)
    expect(container.textContent).toContain('09.06 · 09.13 · 09.20')
    await submit(); expect(post.mock.calls[0]?.[1]).toMatchObject({ slotMinutes: 7 })
  })
  it.each([
    ['leadMinutes', ''], ['leadMinutes', '-1'], ['leadMinutes', '43201'], ['leadMinutes', '1.5'],
    ['horizonDays', '0'], ['horizonDays', '366'], ['slotMinutes', '0'], ['slotMinutes', '61'],
  ])('geçersiz %s=%s değerini göndermeden alanı odaklar', async (name, value) => {
    const { post } = await render(); await fill(name, value); await submit()
    expect(post).not.toHaveBeenCalled(); expect(field(name).getAttribute('aria-invalid')).toBe('true'); expect(document.activeElement).toBe(field(name))
  })
  it('ön süre ufka eşitse kaydetmez', async () => {
    const { post } = await render(); await fill('leadMinutes', '1440'); await fill('horizonDays', '1'); await submit()
    expect(post).not.toHaveBeenCalled(); expect(container.textContent).toContain('Ön süre ileri rezervasyon süresinden kısa olmalı.')
  })
  it('sunucu alan hatasını ilgili alana bağlar ve düzeltmeye izin verir', async () => {
    const { post } = await render(vi.fn(async () => Response.json({ errors: { slotMinutes: ['Sunucu aralık hatası.'] } }, { status: 400 })))
    await fill('leadMinutes', '90'); await submit()
    expect(document.activeElement).toBe(field('slotMinutes')); expect(field('slotMinutes').getAttribute('aria-describedby')).toContain('policy-slotMinutes-error')
    await fill('slotMinutes', '20'); expect(field('slotMinutes').hasAttribute('aria-invalid')).toBe(false)
    await submit(); expect(post).toHaveBeenCalledTimes(2)
  })
  it.each([400, 409, 429, 500])('%s yanıtında taslağı korur ve GET olmadan tekrar POST etmez', async status => {
    const { post } = await render(vi.fn(async () => new Response(null, { status })))
    await fill('leadMinutes', '90'); await submit(); await submit()
    expect(post).toHaveBeenCalledTimes(1); expect(field().value).toBe('90'); expect(saveButton()?.disabled).toBe(true)
    expect(container.textContent).not.toContain('Rezervasyon ayarları kaydedildi.')
  })
  it('çatışmada yeniden yükleme onayını ve başarısız GET sonrası kilidi korur', async () => {
    const { post } = await render(vi.fn(async () => new Response(null, { status: 409 })))
    await fill('leadMinutes', '90'); await submit()
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    await reload(); expect(fetch).toHaveBeenCalledTimes(1); expect(field().value).toBe('90')
    confirm.mockReturnValue(true); vi.mocked(fetch).mockResolvedValueOnce(new Response(null, { status: 503 }))
    await reload(); await submit(); expect(post).toHaveBeenCalledTimes(1); expect(field().value).toBe('90')
    vi.mocked(fetch).mockResolvedValueOnce(Response.json(saved)); await reload()
    expect(saveButton()?.disabled).toBe(true); await fill('leadMinutes', '100'); await submit()
    expect(post.mock.calls[1]?.[1]).toMatchObject({ version: saved.version, leadMinutes: 100 })
  })
  it.each(['network', 'json', 'shape'])('%s belirsizliğinde başarı sunmaz ve kör tekrar yapmaz', async kind => {
    const post = vi.fn<typeof postWithCsrf>(async () => {
      if (kind === 'network') throw new Error('Synthetic connection failure')
      return kind === 'json' ? new Response('bad json') : Response.json({ ...saved, timeZone: 'UTC' })
    })
    await render(post); await fill('leadMinutes', '90'); await submit(); await submit()
    expect(container.textContent).toContain('Kayıt sonucu doğrulanamadı.'); expect(field().value).toBe('90'); expect(post).toHaveBeenCalledTimes(1)
  })
  it.each([401, 403])('%s durumunda değerleri düzenlenebilir form olarak göstermez', async status => {
    await render(vi.fn(async () => new Response(null, { status }))); await fill('leadMinutes', '90'); await submit()
    expect(container.querySelector('form')).toBeNull(); expect(container.querySelector('[role=alert]')).not.toBeNull()
  })
  it.each([401, 403, 404])('ilk GET %s döndüğünde form veya örnek varsayılan göstermez', async status => {
    vi.mocked(fetch).mockResolvedValueOnce(new Response(null, { status })); await render()
    expect(container.querySelector('input')).toBeNull(); expect(container.querySelector('[role=alert]')).not.toBeNull()
  })
  it('Retry-After süresinde yeniden GET/POST başlatmaz; süre dolunca yalnız elle yükler', async () => {
    vi.useFakeTimers()
    const { post } = await render(vi.fn(async () => new Response(null, { status: 429, headers: { 'Retry-After': '2' } })))
    await fill('leadMinutes', '90'); await submit(); await reload()
    expect(fetch).toHaveBeenCalledTimes(1); await act(async () => vi.advanceTimersByTimeAsync(2000))
    expect(fetch).toHaveBeenCalledTimes(1); expect(post).toHaveBeenCalledTimes(1)
    vi.spyOn(window, 'confirm').mockReturnValue(true); await reload(); expect(fetch).toHaveBeenCalledTimes(2)
  })
  it('yanıt gövdesi tamamlanana kadar çift göndermeyi ve alanları kilitler', async () => {
    let finish: (value: Policy) => void = () => { throw new Error('Bekleyen gövde yok') }
    const response = Response.json(saved); vi.spyOn(response, 'json').mockImplementation(() => new Promise(resolve => { finish = resolve }))
    const { post, busy } = await render(vi.fn(async () => response)); await fill('leadMinutes', '90'); await submit(); await submit()
    expect(post).toHaveBeenCalledTimes(1); expect(field().disabled).toBe(true); expect(busy).toHaveBeenLastCalledWith(true)
    expect(container.textContent).not.toContain('Rezervasyon ayarları kaydedildi.')
    await act(async () => finish(saved)); expect(field().disabled).toBe(false); expect(busy).toHaveBeenLastCalledWith(false)
  })
  it('yükleme hatasında varsayılan değer üretmez ve bileşenden çıkınca GET iptal edilir', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(Response.json(null)); await render()
    expect(container.querySelector('input')).toBeNull(); expect(container.textContent).toContain('Ayarlar yüklenemedi.')
    vi.mocked(fetch).mockImplementation(() => new Promise(() => {})); await reload()
    const signal = vi.mocked(fetch).mock.calls[1]?.[1]?.signal
    await act(async () => root.unmount()); expect(signal?.aborted).toBe(true)
    root = createRoot(container)
  })
  it.each([
    null, [], { ...original, version: '' }, { ...original, version: '00000000-0000-0000-0000-000000000000' },
    { ...original, leadMinutes: '60' }, { ...original, horizonDays: 0 }, { ...original, slotMinutes: 61 },
    { ...original, leadMinutes: 1440, horizonDays: 1 }, { ...original, timeZone: 'UTC' },
  ])('bozuk API sözleşmesini form değeri kabul etmez: %j', async value => {
    await expect(readBookingPolicy(Response.json(value))).rejects.toThrow()
  })
})
