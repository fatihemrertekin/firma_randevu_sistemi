// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import AuditLog from './AuditLog'
import { readAuditPage } from './auditLogApi'

const entry = { id: '1:f1111111-1111-4111-8111-111111111111', occurredAt: '2026-10-03T12:30:00Z', module: 'İşletme bilgileri', action: 'Güncellendi', actor: 'owner@example.test', target: '<script>İşletme</script>' }
const initial = { items: [entry], category: 'all', timeZone: 'Europe/Istanbul', cursor: 'first', nextCursor: null }
let container: HTMLDivElement, root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); vi.stubGlobal('fetch', vi.fn(async () => Response.json(initial)))
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.useRealTimers(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
async function render() { await act(async () => root.render(<AuditLog />)) }
function button(name: string) { const found = Array.from(container.querySelectorAll('button')).find(value => value.textContent === name); if (!found) throw new Error(name); return found }
async function click(name: string) { await act(async () => button(name).click()) }
async function filter(value: string) { await act(async () => { const select = container.querySelector('select'); if (!select) throw new Error('Kategori yok'); select.value = value; select.dispatchEvent(new Event('change', { bubbles: true })) }) }

describe('Değişiklik kayıtları', () => {
  it('İstanbul saatini gösterir, metni HTML olarak çalıştırmaz ve yalnız GET yapar', async () => {
    await render(); expect(container.textContent).toContain('15:30:00'); expect(container.textContent).toContain(entry.target)
    expect(container.querySelector('script')).toBeNull(); expect(fetch).toHaveBeenCalledWith('/api/audit-log/?category=all', expect.objectContaining({ cache: 'no-store' }))
  })
  it('boş kategoriyi yükleme durumundan ayırır', async () => {
    let finish: ((response: Response) => void) | undefined
    vi.mocked(fetch).mockImplementationOnce(() => new Promise(resolve => { finish = resolve })); await render()
    expect(container.textContent).toContain('Kayıtlar yükleniyor…'); expect(container.textContent).not.toContain('işlem kaydı yok')
    await act(async () => finish?.(Response.json({ ...initial, items: [] }))); expect(container.textContent).toContain('Bu kategoride işlem kaydı yok.')
  })
  it('sonraki/önceki sayfa özgün cursoru kullanır, çift geçişi engeller ve başlığa odaklanır', async () => {
    const full = Array.from({ length: 20 }, (_, index) => ({ ...entry, id: `1:${index.toString(16).padStart(8, '0')}-1111-4111-8111-111111111111` }))
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ ...initial, items: full, nextCursor: 'second' }))
    await render(); let finish: ((response: Response) => void) | undefined
    vi.mocked(fetch).mockImplementationOnce(() => new Promise(resolve => { finish = resolve })); await click('Sonraki sayfa'); await click('Sonraki sayfa'); expect(fetch).toHaveBeenCalledTimes(2)
    await act(async () => finish?.(Response.json({ ...initial, cursor: 'second' }))); expect(document.activeElement).toBe(container.querySelector('h2'))
    await click('Önceki sayfa'); expect(fetch).toHaveBeenLastCalledWith('/api/audit-log/?category=all&cursor=first', expect.any(Object))
  })
  it('kategori değişiminde eski yavaş yanıtı göstermez', async () => {
    let finish: ((response: Response) => void) | undefined
    vi.mocked(fetch).mockImplementationOnce(() => new Promise(resolve => { finish = resolve })); await render()
    vi.mocked(fetch).mockResolvedValueOnce(Response.json({ ...initial, category: 'security', items: [] })); await filter('security')
    await act(async () => finish?.(Response.json(initial))); expect(container.textContent).not.toContain(entry.actor); expect(container.textContent).toContain('işlem kaydı yok')
  })
  it.each([400, 401, 403, 500])('%i sonrası önceki sonucu açıkça belirtir, sayfalama kilitlenir ve yenileme düzelir', async status => {
    await render(); vi.mocked(fetch).mockResolvedValueOnce(new Response(null, { status })); await click('Listeyi yenile')
    expect(container.querySelector('[role=alert]')).not.toBeNull(); expect(container.textContent).toContain('Önceki sonuçlar gösteriliyor')
    expect(button('Sonraki sayfa').disabled).toBe(true); expect(container.querySelector('select')?.disabled).toBe(true)
    await click('Listeyi yenile'); expect(container.querySelector('[role=alert]')).toBeNull(); expect(container.querySelector('select')?.disabled).toBe(false)
  })
  it('429 bekler ve süre dolmadan tekrar istek göndermez', async () => {
    await render(); vi.useFakeTimers(); vi.mocked(fetch).mockResolvedValueOnce(new Response(null, { status: 429, headers: { 'Retry-After': '2' } }))
    await click('Listeyi yenile'); await click('Listeyi yenile'); expect(fetch).toHaveBeenCalledTimes(2)
    expect(container.textContent).toContain('2 saniye sonra'); await act(async () => vi.advanceTimersByTimeAsync(1000)); await act(async () => vi.advanceTimersByTimeAsync(1000)); vi.useRealTimers()
    await click('Listeyi yenile'); expect(fetch).toHaveBeenCalledTimes(3)
  })
  it('yanlış kategori, saat bölgesi, yinelenen kimlik ve geçersiz devam bilgisini reddeder', async () => {
    for (const body of [{ ...initial, category: 'security' }, { ...initial, timeZone: 'UTC' }, { ...initial, items: [entry, entry] }, { ...initial, cursor: '' }, { ...initial, nextCursor: 'first' }, { ...initial, items: [{ ...entry, occurredAt: 'invalid' }] }, { ...initial, items: [{ ...entry, id: '13:invalid' }] }])
      await expect(readAuditPage(Response.json(body), 'all')).rejects.toThrow()
  })
})
