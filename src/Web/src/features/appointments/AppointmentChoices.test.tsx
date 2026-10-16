// @vitest-environment jsdom
import { act, type ComponentProps } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import AppointmentChoices from './AppointmentChoices'

const member = { id: 'member', version: 'version', name: 'Personel A', isActive: true }
const service = { ...member, id: 'service', name: 'Saç kesimi', durationMinutes: 25, price: '450.00', currency: 'TRY' }
const pageOf = (items: unknown[], page = 1, totalCount = items.length) => Response.json({ items, page, pageSize: 50, totalCount, hasMore: page * 50 < totalCount })
let container: HTMLDivElement, root: Root
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('fetch', vi.fn(async () => pageOf([])))
  container = document.createElement('div'); document.body.append(container); root = createRoot(container)
})
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
async function render(props: Partial<ComponentProps<typeof AppointmentChoices>> = {}) {
  const onChange = vi.fn(), onUnauthorized = vi.fn()
  await act(async () => root.render(<MemoryRouter><AppointmentChoices kind="services" id="choice" label="Hizmet" value="" disabled={false} onChange={onChange} onUnauthorized={onUnauthorized} {...props} /></MemoryRouter>))
  return { onChange, onUnauthorized }
}
async function click(text: string) {
  const button = [...container.querySelectorAll('button')].find(item => item.textContent === text)
  if (!button) throw new Error('Düğme yok: ' + text)
  await act(async () => button.click())
}

it.each([['services', 'hizmet', '/yonetim/hizmetler'], ['staff', 'çalışan', '/yonetim/personel']] as const)('boş %s kaydını açıklar ve mevcut yönetime bağlar', async (kind, subject, href) => {
  await render({ kind })
  expect(container.textContent).toContain(`Henüz ${subject} kaydı yok.`)
  expect(container.textContent).toContain(`Devam etmek için aktif bir ${subject} seçilmelidir.`)
  expect(container.querySelector('a')?.getAttribute('href')).toBe(href)
  expect(container.querySelector('select')?.getAttribute('aria-describedby')).toBe('choice-choices-status')
})
it.each([['services', service, 'hizmet'], ['staff', member, 'çalışan']] as const)('tümü pasif %s kayıtlarını hiç kayıt olmamasından ayırır', async (kind, item, subject) => {
  vi.mocked(fetch).mockResolvedValue(pageOf([{ ...item, isActive: false }]))
  await render({ kind })
  expect(container.textContent).toContain(`Aktif ${subject} bulunmuyor.`)
  expect(container.textContent).not.toContain('Henüz')
  expect(container.querySelector('a')?.textContent).toContain('mevcut kaydı aktifleştirin')
})
it('ilk sayfadaki pasif kayıtları genellemez ve sonraki aktif hizmeti seçilebilir tutar', async () => {
  const inactive = Array.from({ length: 50 }, (_, i) => ({ ...service, id: 'inactive-' + i, isActive: false }))
  vi.mocked(fetch).mockResolvedValueOnce(pageOf(inactive, 1, 51)).mockResolvedValueOnce(pageOf([service], 2, 51))
  const { onChange } = await render()
  expect(container.textContent).toContain('Yüklenen kayıtlarda aktif hizmet bulunmuyor.')
  expect(container.textContent).not.toContain('Aktif hizmet bulunmuyor.')
  expect(container.querySelector('a')).toBeNull()
  await click('Diğer hizmetleri yükle')
  expect(String(vi.mocked(fetch).mock.calls[1][0])).toContain('page=2&pageSize=50')
  expect(container.querySelector('[role=status]')).toBeNull()
  const select = container.querySelector('select')!
  await act(async () => { select.value = service.id; select.dispatchEvent(new Event('change', { bubbles: true })) })
  expect(onChange).toHaveBeenCalledWith(service)
})
it('son pasif sayfadan sonra genel aktif kayıt yokluğunu söyler', async () => {
  vi.mocked(fetch).mockResolvedValueOnce(pageOf([{ ...member, isActive: false }], 1, 51)).mockResolvedValueOnce(pageOf([{ ...member, id: 'last', isActive: false }], 2, 51))
  await render({ kind: 'staff' }); await click('Diğer çalışanları yükle')
  expect(container.textContent).toContain('Aktif çalışan bulunmuyor.')
  expect(container.querySelector('a')?.getAttribute('href')).toBe('/yonetim/personel')
})
it('yükleme ve hata sırasında boş kayıt sonucu vermez, yeniden denemede güncel listeyi gösterir', async () => {
  let release: (value: Response) => void = () => { throw new Error('İstek başlamadı') }
  vi.mocked(fetch).mockImplementationOnce(() => new Promise(resolve => { release = resolve })).mockResolvedValueOnce(pageOf([service]))
  await render({ invalid: true })
  expect(container.textContent).toContain('Hizmetler yükleniyor…')
  expect(container.textContent).not.toContain('kaydı yok')
  expect(container.querySelector('select')?.getAttribute('aria-describedby')).toBe('choice-error choice-choices-status')
  await act(async () => release(new Response(null, { status: 503 })))
  expect(container.querySelector('[role=alert]')).not.toBeNull()
  expect(container.querySelector('[role=status]')).toBeNull()
  await click('Seçimleri yeniden yükle')
  expect(container.querySelector('option[value=service]')).not.toBeNull()
  expect(container.querySelector('[role=alert]')).toBeNull()
})
it('sonraki sayfa hatasını aynı sayfada tekrarlar ve mevcut seçimi değiştirmez', async () => {
  vi.mocked(fetch).mockResolvedValueOnce(pageOf([member], 1, 51)).mockResolvedValueOnce(new Response(null, { status: 503 })).mockResolvedValueOnce(pageOf([{ ...member, id: 'next' }], 2, 51))
  const { onChange } = await render({ kind: 'staff', value: member.id })
  await click('Diğer çalışanları yükle'); expect(container.querySelector('select')?.value).toBe(member.id)
  await click('Seçimleri yeniden yükle')
  expect(vi.mocked(fetch).mock.calls.slice(1).every(call => String(call[0]).includes('page=2&'))).toBe(true)
  expect(container.querySelector('select')?.value).toBe(member.id)
  expect(onChange).not.toHaveBeenCalled()
})
it('günlük filtrede pasif çalışanı ve Tümü seçeneğini korur', async () => {
  vi.mocked(fetch).mockResolvedValue(pageOf([{ ...member, isActive: false }]))
  await render({ kind: 'staff', optional: true, includeInactive: true })
  expect(container.querySelector('option[value=""]')?.textContent).toBe('Tümü')
  expect(container.querySelector('option[value=member]')?.textContent).toContain('(Pasif)')
  expect(container.querySelector('[role=status]')).toBeNull()
})
it('boş günlük filtrede Tümü ile listenin kullanılabildiğini açıklar', async () => {
  await render({ kind: 'staff', optional: true, includeInactive: true })
  expect(container.textContent).toContain('Tümü seçiliyken randevular listelenebilir.')
})
it('henüz yüklenmemiş seçili çalışanı silmez veya boş olarak etiketlemez', async () => {
  const { onChange } = await render({ kind: 'staff', value: member.id })
  expect(container.querySelector('select')?.value).toBe(member.id)
  expect(container.textContent).toContain('Seçili çalışan')
  expect(container.querySelector('[role=status]')).toBeNull()
  expect(onChange).not.toHaveBeenCalled()
})
it('yetkisiz yanıtı boş sonuç olarak göstermez ve oturum çağrısını korur', async () => {
  vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 401 }))
  const { onUnauthorized } = await render()
  expect(onUnauthorized).toHaveBeenCalledOnce()
  expect(container.querySelector('[role=alert]')?.textContent).toContain('Oturumunuz sona erdi')
  expect(container.querySelector('a')).toBeNull()
})
