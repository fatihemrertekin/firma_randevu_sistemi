import { afterEach, expect, it, vi } from 'vitest'
import { postWithCsrf, RequestVerificationError } from './api'
afterEach(() => vi.unstubAllGlobals())
it.each([null, {}, { token: null }, { token: 7 }, { token: '' }])('güvenlik yanıtı %j ise durum değiştiren isteği göndermez', async value => {
  const fetcher = vi.fn(async () => Response.json(value)); vi.stubGlobal('fetch', fetcher)
  await expect(postWithCsrf('/api/services/', { name: 'Taslak' })).rejects.toBeInstanceOf(RequestVerificationError)
  expect(fetcher).toHaveBeenCalledTimes(1)
})
it.each([400, 401, 403, 503])('güvenlik isteği %i ise alan hatası veya belirsiz sonuç üretmez', async status => {
  const fetcher = vi.fn(async () => new Response(null, { status })); vi.stubGlobal('fetch', fetcher)
  await expect(postWithCsrf('/api/services/', {})).rejects.toThrow('işlem gönderilmedi')
  expect(fetcher).toHaveBeenCalledTimes(1)
})
it('güvenlik isteğinin bağlantı hatasında POST göndermeden açıklama verir', async () => {
  const fetcher = vi.fn().mockRejectedValue(new TypeError('network')); vi.stubGlobal('fetch', fetcher)
  await expect(postWithCsrf('/api/services/', {})).rejects.toBeInstanceOf(RequestVerificationError)
  expect(fetcher).toHaveBeenCalledTimes(1)
})
it('tanınan CSRF reddini ayırır; yeniden denemede taze token ve aynı gövdeyi kullanır', async () => {
  const fetcher = vi.fn().mockResolvedValueOnce(Response.json({ token: 'one' })).mockResolvedValueOnce(Response.json({ code: 'csrf_invalid' }, { status: 400 }))
    .mockResolvedValueOnce(Response.json({ token: 'two' })).mockResolvedValueOnce(new Response(null, { status: 204 }))
  vi.stubGlobal('fetch', fetcher)
  const body = { id: 'stable', name: 'Taslak' }
  await expect(postWithCsrf('/api/services/', body)).rejects.toThrow('işlem uygulanmadı')
  expect((await postWithCsrf('/api/services/', body)).status).toBe(204)
  expect(fetcher.mock.calls[1][1].body).toBe(fetcher.mock.calls[3][1].body)
  expect(fetcher.mock.calls[1][1].headers['X-CSRF-TOKEN']).toBe('one')
  expect(fetcher.mock.calls[3][1].headers['X-CSRF-TOKEN']).toBe('two')
})
it.each([{ errors: { name: ['Ad gerekli.'] } }, { code: 'unknown', title: 'uydurma' }])('diğer 400 gövdelerini tüketmeden mevcut alan okuyucusuna bırakır', async body => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(Response.json({ token: 'token' })).mockResolvedValueOnce(Response.json(body, { status: 400 })))
  expect(await (await postWithCsrf('/api/services/', {})).json()).toEqual(body)
})
it('POST sonrası bağlantı hatasını güvenlik reddi saymaz; belirsiz sonucu korur', async () => {
  const problem = new TypeError('response lost')
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(Response.json({ token: 'token' })).mockRejectedValueOnce(problem))
  await expect(postWithCsrf('/api/services/', {})).rejects.toBe(problem)
})

it.each([[403, 'subscription_read_only'], [503, 'subscription_unavailable']])('abonelik %i reddini yazılmamış işlem olarak açıklar ve sunucu metnini basmaz', async (status, code) => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(Response.json({ token: 'token' })).mockResolvedValueOnce(Response.json({ code, title: 'UNTRUSTED TITLE' }, { status: Number(status) })))
  await expect(postWithCsrf('/api/services/', {})).rejects.toBeInstanceOf(RequestVerificationError)
})
it('public abonelik reddinde işletmenin ödeme/borç bilgisini açıklamaz', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(Response.json({ token: 'token' })).mockResolvedValueOnce(Response.json({ code: 'subscription_read_only' }, { status: 403 })))
  await expect(postWithCsrf('/api/public-booking/appointments', {})).rejects.toThrow('İşletme şu anda yeni çevrim içi randevu alamıyor.')
})
it('diğer403 yanıtlarını mevcut nesne-yetki hata okuyucusuna bırakır', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(Response.json({ token: 'token' })).mockResolvedValueOnce(Response.json({ code: 'other' }, { status: 403 })))
  expect((await postWithCsrf('/api/services/', {})).status).toBe(403)
})
