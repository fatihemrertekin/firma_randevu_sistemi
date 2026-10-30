// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import SubscriptionNotice from './SubscriptionNotice'

let container: HTMLDivElement, root: Root
const changed = vi.fn(), unauthorized = vi.fn()
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); vi.stubGlobal('fetch', vi.fn(async () => Response.json({ restrictionsApplied: false })))
  changed.mockReset(); unauthorized.mockReset(); container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); vi.useRealTimers() })
async function render() { await act(async () => root.render(<SubscriptionNotice onChange={changed} onUnauthorized={unauthorized} />)) }
async function refresh() { await act(async () => container.querySelector<HTMLButtonElement>('button')?.click()) }
it('yalnız sunucunun boolean durumunu kullanır; ödeme kaydı sonrası yeniden okuyunca uyarıyı kaldırır', async () => {
  vi.mocked(fetch).mockResolvedValueOnce(Response.json({ restrictionsApplied: true }))
  await render(); expect(container.textContent).toContain('kullanım salt okunur'); expect(changed).toHaveBeenLastCalledWith(true)
  await refresh(); expect(changed).toHaveBeenLastCalledWith(false); expect(container.querySelector('aside')).toBeNull()
  expect(fetch).toHaveBeenCalledWith('/api/subscription/access', expect.objectContaining({ cache: 'no-store', signal: expect.any(AbortSignal) }))
})
it('ortak yazma reddi olayında durumu yeniler; unmount sonrası olay dinleyicisini kaldırır', async () => {
  await render(); vi.mocked(fetch).mockResolvedValueOnce(Response.json({ restrictionsApplied: true }))
  await act(async () => window.dispatchEvent(new Event('subscription-access-changed')))
  expect(changed).toHaveBeenLastCalledWith(true)
  await act(async () => root.render(<p>Başka ekran</p>)); const count = vi.mocked(fetch).mock.calls.length
  window.dispatchEvent(new Event('subscription-access-changed')); expect(fetch).toHaveBeenCalledTimes(count)
})
it.each([new Response(null, { status: 503 }), Response.json({ restrictionsApplied: 'false' })])('hatalı okumada yazmaya izin verilmiş varsaymaz', async response => {
  vi.mocked(fetch).mockResolvedValue(response); await render()
  expect(changed).toHaveBeenLastCalledWith(null); expect(container.querySelector('[role=alert]')?.textContent).toContain('doğrulanamadı')
})
it('401 yanıtında normal oturum dönüşünü kullanır', async () => {
  vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 401 })); await render(); expect(unauthorized).toHaveBeenCalledOnce()
})
it('429 beklemesini uygular ve süre bitince yeniden kontrolü açar', async () => {
  vi.useFakeTimers(); vi.mocked(fetch).mockResolvedValueOnce(new Response(null, { status: 429, headers: { 'Retry-After': '2' } }))
  await render(); expect(container.querySelector<HTMLButtonElement>('button')?.disabled).toBe(true)
  await act(async () => { vi.advanceTimersByTime(1000) }); await act(async () => { vi.advanceTimersByTime(1000) })
  expect(container.querySelector<HTMLButtonElement>('button')?.disabled).toBe(false); await refresh(); expect(changed).toHaveBeenLastCalledWith(false)
})
