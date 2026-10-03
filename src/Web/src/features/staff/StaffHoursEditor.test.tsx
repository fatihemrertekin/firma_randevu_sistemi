// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import StaffHoursEditor from './StaffHoursEditor'
import { hoursDraft } from '../../app/weeklyHoursApi'
import type { StaffPost } from './staffMembersApi'

const member = { id: '932e8ca1-4887-4a64-94e2-380507e83d41', name: 'Sentetik personel', isActive: true, version: '0f501006-1b39-4594-92e3-a3529f35649f' }
const initial = { member, version: member.version, isConfigured: false, timeZone: 'Europe/Istanbul' as const, days: [] }
const saved = { ...initial, isConfigured: true, days: hoursDraft(initial) }
let container: HTMLDivElement, root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); vi.stubGlobal('fetch', vi.fn(async () => Response.json(initial)))
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
async function render(post = vi.fn<StaffPost>(async () => Response.json(saved))) {
  const dirty = vi.fn(), busy = vi.fn(), onSaved = vi.fn(), onCancel = vi.fn()
  await act(async () => root.render(<StaffHoursEditor memberId={member.id} post={post} onDirtyChange={dirty} onBusyChange={busy} onSaved={onSaved} onCancel={onCancel} />))
  return { post, dirty, busy, onSaved, onCancel }
}
function checkbox() { const input = container.querySelector<HTMLInputElement>('input[data-field=day0Closed]'); if (!input) throw new Error('Gün bulunamadı'); return input }
async function submit() { await act(async () => container.querySelector('form')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))) }
async function back() { await act(async () => Array.from(container.querySelectorAll('button')).find(item => item.textContent === 'Listeye dön')?.click()) }
it('personeli ve yedi günü yükler, odağı ilk güne taşır ve doğru uçta kaydeder', async () => {
  const { post, onSaved, dirty } = await render(); expect(container.textContent).toContain(member.name)
  expect(container.querySelectorAll('input[type=checkbox]')).toHaveLength(7); expect(document.activeElement).toBe(checkbox())
  expect(fetch).toHaveBeenCalledWith(`/api/staff-members/${member.id}/hours`, expect.any(Object))
  await submit(); expect(post).toHaveBeenCalledWith(`/api/staff-members/${member.id}/hours`, { version: member.version, days: saved.days }, expect.any(AbortSignal))
  expect(onSaved).toHaveBeenCalledOnce(); expect(dirty).toHaveBeenLastCalledWith(false)
})
it('pasif personelin saatlerini gösterir ve düzenleme/gönderimi engeller', async () => {
  vi.mocked(fetch).mockResolvedValueOnce(Response.json({ ...saved, member: { ...member, isActive: false } }))
  const { post } = await render(); expect(container.textContent).toContain('Personel pasif.')
  expect(container.querySelector('button[type=submit]')).toBeNull(); expect(checkbox().closest('fieldset')?.disabled).toBe(true)
  await submit(); expect(post).not.toHaveBeenCalled()
})
it('taslağı onaysız silmez, onayla listeye döner ve temizler', async () => {
  const { dirty, onCancel } = await render(); await act(async () => checkbox().click())
  const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false); await back(); expect(onCancel).not.toHaveBeenCalled()
  confirm.mockReturnValue(true); await back(); expect(onCancel).toHaveBeenCalledOnce(); expect(dirty).toHaveBeenLastCalledWith(false)
})
it.each([401, 403, 404, 409, 500])('%i sonucu başarı saymaz ve ikinci gönderimi kilitler', async status => {
  const { post, onSaved } = await render(vi.fn<StaffPost>(async () => new Response(null, { status })))
  await submit(); await submit(); expect(post).toHaveBeenCalledOnce(); expect(onSaved).not.toHaveBeenCalled(); expect(container.querySelector('[role=alert]')).not.toBeNull()
})
it('başka personel veya uyuşmayan sürüm yanıtını kabul etmez', async () => {
  vi.mocked(fetch).mockResolvedValueOnce(Response.json({ ...initial, member: { ...member, id: 'başka-personel' } }))
  const { post, onSaved } = await render(); expect(container.querySelector('form')).toBeNull(); expect(container.textContent).toContain('Saatler yüklenemedi.')
  await submit(); expect(post).not.toHaveBeenCalled(); expect(onSaved).not.toHaveBeenCalled()
})
