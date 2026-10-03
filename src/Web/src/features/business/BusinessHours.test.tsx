// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import BusinessHours from './BusinessHours'
import { hoursDraft, readHours, type HoursPost } from './businessHoursApi'

const initial = { isConfigured: false, timeZone: 'Europe/Istanbul' as const, version: 'd317d899-8208-41f1-9b8e-c6fbde437cde', days: [] }
const saved = { ...initial, isConfigured: true, version: 'b112d899-8208-41f1-9b8e-c6fbde437cde', days: hoursDraft(initial) }
let container: HTMLDivElement, root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); vi.stubGlobal('fetch', vi.fn(async () => Response.json(initial)))
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.useRealTimers(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
async function render(post = vi.fn<HoursPost>(async () => Response.json(saved))) {
  const dirty = vi.fn(), busy = vi.fn()
  await act(async () => root.render(<BusinessHours post={post} onDirtyChange={dirty} onBusyChange={busy} />))
  return { post, dirty, busy }
}
function field(key: string) {
  const element = container.querySelector<HTMLInputElement>(`input[data-field="${key}"]`)
  if (!element) throw new Error('Alan yok: ' + key)
  return element
}
async function toggle(day = 0) { await act(async () => field(`day${day}Closed`).click()) }
async function fill(key: string, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  if (!setter) throw new Error('Alan ayarlayıcı yok')
  await act(async () => { setter.call(field(key), value); field(key).dispatchEvent(new Event('input', { bubbles: true })) })
}
async function submit() { await act(async () => container.querySelector('form')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))) }
async function reload() { await act(async () => Array.from(container.querySelectorAll('button')).find(item => item.textContent === 'Güncel saatleri yükle')?.click()) }
async function monday() { await toggle(); await fill('day0OpensAt', '09:00'); await fill('day0ClosesAt', '19:00') }

describe('Haftalık işletme saatleri', () => {
  it('belirlenmemiş durumu kaydedilmiş kapalı haftadan ayırır', async () => {
    await render(); expect(container.textContent).toContain('Saatler henüz belirlenmedi.'); expect(container.querySelectorAll('input[type=checkbox]')).toHaveLength(7)
    await submit(); expect(container.textContent).toContain('İşletme saatleri kaydedildi.'); expect(container.textContent).not.toContain('Saatler henüz belirlenmedi.')
  })
  it('tam haftayı kaydeder; kapalı güne dönerken saatleri temizler', async () => {
    const { post, dirty } = await render(); await monday(); await toggle(); expect(dirty).toHaveBeenLastCalledWith(false)
    await submit(); expect(post).toHaveBeenCalledWith('/api/business-hours/', { version: initial.version, days: saved.days }, expect.any(AbortSignal))
  })
  it('eksik/ters saatleri göndermeden ilk hatalı alana odaklanır', async () => {
    const { post } = await render(); await toggle(); await submit(); expect(post).not.toHaveBeenCalled(); expect(document.activeElement).toBe(field('day0OpensAt'))
    await fill('day0OpensAt', '19:00'); await fill('day0ClosesAt', '09:00'); await submit(); expect(post).not.toHaveBeenCalled()
    expect(document.activeElement).toBe(field('day0ClosesAt')); expect(container.textContent).toContain('Kapanış aynı gün içinde açılıştan sonra olmalı.')
  })
  it('iki gönderimi engeller ve sunucu doğrulamasını bekler', async () => {
    let finish: ((response: Response) => void) | undefined
    const post = vi.fn<HoursPost>(() => new Promise<Response>(resolve => { finish = resolve }))
    const { busy } = await render(post); await monday(); await submit(); await submit(); expect(post).toHaveBeenCalledOnce(); expect(busy).toHaveBeenLastCalledWith(true)
    expect(container.textContent).not.toContain('İşletme saatleri kaydedildi.')
    await act(async () => finish?.(Response.json({ ...saved, days: saved.days.map(day => day.day === 0 ? { ...day, isClosed: false, opensAt: '09:00', closesAt: '19:00' } : day) })))
    expect(busy).toHaveBeenLastCalledWith(false); expect(container.textContent).toContain('İşletme saatleri kaydedildi.')
  })
  it.each([401, 403, 409, 500])('%i hatasında taslağı korur ve güncel sürümü yüklemeden yeniden göndermez', async status => {
    const { post } = await render(vi.fn<HoursPost>(async () => new Response(null, { status }))); await monday(); await submit(); await submit()
    expect(post).toHaveBeenCalledOnce(); expect(field('day0OpensAt').value).toBe('09:00'); expect(container.querySelector('[role=alert]')).not.toBeNull()
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false); await reload(); expect(field('day0OpensAt').value).toBe('09:00')
    confirm.mockReturnValue(true); await reload(); expect(field('day0Closed').checked).toBe(true); expect(document.activeElement).toBe(field('day0Closed'))
  })
  it('400 sunucu hatasını alan yanında gösterir ve taslağı/odağı korur', async () => {
    await render(vi.fn<HoursPost>(async () => Response.json({ errors: { day0ClosesAt: ['Geçersiz kapanış'] } }, { status: 400 })))
    await monday(); await submit(); expect(field('day0ClosesAt').getAttribute('aria-invalid')).toBe('true')
    expect(document.activeElement).toBe(field('day0ClosesAt')); expect(field('day0OpensAt').value).toBe('09:00')
  })
  it('429 bekleme süresinde göndermeyi engeller, sonra yeniden denenebilir', async () => {
    const { post } = await render(vi.fn<HoursPost>(async () => new Response(null, { status: 429, headers: { 'Retry-After': '2' } })))
    await monday(); vi.useFakeTimers(); await submit(); await submit(); expect(post).toHaveBeenCalledOnce(); expect(container.textContent).toContain('2 saniye sonra')
    await act(async () => { await vi.advanceTimersByTimeAsync(1000) }); await act(async () => { await vi.advanceTimersByTimeAsync(1000) }); vi.useRealTimers()
    await submit(); expect(post).toHaveBeenCalledTimes(2)
  })
  it('başlangıç yükleme/ağ hatasını ayırır ve yeniden yükleyebilir', async () => {
    vi.mocked(fetch).mockRejectedValueOnce(new Error('ağ')); await render(); expect(container.textContent).toContain('Saatler yüklenemedi.')
    await reload(); expect(container.textContent).toContain('Saatler henüz belirlenmedi.')
  })
  it('taslak varken sekme kapanışını uyarır; başarısız yeniden yüklemede taslağı ezmez', async () => {
    await render(); await monday(); const event = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(event); expect(event.defaultPrevented).toBe(true)
    vi.spyOn(window, 'confirm').mockReturnValue(true); vi.mocked(fetch).mockRejectedValueOnce(new Error('ağ')); await reload(); expect(field('day0OpensAt').value).toBe('09:00')
  })
  it('eksik/çift gün ve bozuk saat yanıtlarını başarı saymaz', async () => {
    for (const value of [{ ...saved, days: saved.days.slice(1) }, { ...saved, days: saved.days.map(() => saved.days[0]) }, { ...saved, days: saved.days.map(day => ({ ...day, isClosed: false, opensAt: '19:00', closesAt: '09:00' })) }, { ...saved, timeZone: 'UTC' }])
      await expect(readHours(Response.json(value))).rejects.toThrow('Sonuç doğrulanamadı.')
    await render(vi.fn<HoursPost>(async () => Response.json({ ...saved, days: [] }))); await monday(); await submit()
    expect(container.textContent).not.toContain('İşletme saatleri kaydedildi.'); expect(field('day0OpensAt').value).toBe('09:00')
  })
})
