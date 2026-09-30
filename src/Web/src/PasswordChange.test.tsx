// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'

let container: HTMLDivElement
let root: Root
const owner = { email: 'owner@example.test', mfaEnabled: true, ownerAccess: true }

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  container = document.createElement('div')
  document.body.append(container)
  root = createRoot(container)
})

afterEach(async () => {
  await act(async () => root.unmount())
  container.remove()
  vi.unstubAllGlobals()
})

function mockRequests(change: () => Promise<Response>, account = owner) {
  const requests = vi.fn(async (path: string) => {
    if (path === '/api/auth/me') return Response.json(account)
    if (path === '/api/auth/csrf') return Response.json({ token: 'synthetic-csrf' })
    if (path === '/api/staff-invitations/') return Response.json([])
    if (path === '/api/auth/change-password') return change()
    throw new Error('Beklenmeyen test isteği')
  })
  vi.stubGlobal('fetch', requests)
  return requests
}

async function renderApp() {
  await act(async () => root.render(<App />))
}

async function fillPassword(id: string, value: string) {
  const input = container.querySelector<HTMLInputElement>(`#${id}`)
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  if (!input || !setter) throw new Error('Parola alanı bulunamadı')
  await act(async () => {
    setter.call(input, value)
    input.dispatchEvent(new Event('input', { bubbles: true }))
  })
}

async function fillForm(confirm = 'Synthetic!New456') {
  await fillPassword('current-password', 'Synthetic!Old123')
  await fillPassword('new-password', 'Synthetic!New456')
  await fillPassword('confirm-password', confirm)
}

async function submit() {
  const form = container.querySelector('form[aria-label="Parola değiştirme"]')
  if (!form) throw new Error('Parola formu bulunamadı')
  await act(async () => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))
}

describe('Owner parola değişikliği', () => {
  it('gönderim sürerken alanları kilitler, ikinci isteği engeller ve başarıda girişe döner', async () => {
    let finish: ((response: Response) => void) | undefined
    const response = new Promise<Response>(resolve => { finish = resolve })
    const requests = mockRequests(() => response)
    await renderApp()
    await fillForm()
    await submit()
    expect(container.textContent).toContain('Parola değiştiriliyor…')
    const inputs = Array.from(container.querySelectorAll<HTMLInputElement>('form[aria-label="Parola değiştirme"] input'))
    expect(inputs.every(input => input.disabled)).toBe(true)
    expect(inputs.map(input => input.autocomplete)).toEqual(['current-password', 'new-password', 'new-password'])
    await submit()
    expect(requests.mock.calls.filter(([path]) => path === '/api/auth/change-password')).toHaveLength(1)
    if (!finish) throw new Error('Test yanıtı hazırlanamadı')
    await act(async () => finish?.(new Response(null, { status: 204 })))
    expect(container.textContent).toContain('İşletme girişi')
    expect(container.querySelector('[role="status"]')?.textContent).toContain('bütün oturumlar kapatıldı')
    expect(container.querySelector<HTMLInputElement>('#password')?.value).toBe('')
    expect(container.querySelector('#current-password')).toBeNull()
    expect(container.textContent).not.toContain('Synthetic!New456')
  })

  it('tekrar uyuşmazlığında istek göndermez; sunucu hatasında parola alanlarını temizler', async () => {
    const requests = mockRequests(async () => Response.json({ title: 'Mevcut parola doğrulanamadı.' }, { status: 400 }))
    await renderApp()
    await fillForm('different')
    await submit()
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('aynı olmalı')
    expect(requests.mock.calls.some(([path]) => path === '/api/auth/change-password')).toBe(false)
    await fillPassword('confirm-password', 'Synthetic!New456')
    await submit()
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Mevcut parola doğrulanamadı')
    expect(Array.from(container.querySelectorAll<HTMLInputElement>('form[aria-label="Parola değiştirme"] input')).every(input => input.value === '')).toBe(true)
    expect(container.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBe(false)
  })

  it.each([401, 403, 409])('geçersiz oturum yanıtında (%i) yeniden giriş ister', async status => {
    mockRequests(async () => new Response(null, { status }))
    await renderApp()
    await fillForm()
    await submit()
    expect(container.textContent).toContain('İşletme girişi')
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Yeniden giriş yapın')
  })

  it.each([429, 500])('limit/hata yanıtında (%i) başarı göstermez ve tekrar göndermeyi açar', async status => {
    mockRequests(async () => new Response(null, { status }))
    await renderApp()
    await fillForm()
    await submit()
    expect(container.querySelector('[role="alert"]')).not.toBeNull()
    expect(container.querySelector('[role="status"]')).toBeNull()
    expect(container.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBe(false)
  })

  it('MFA yönetim yetkisi tamamlanmadığında parola formunu göstermez', async () => {
    mockRequests(async () => new Response(null, { status: 204 }), { ...owner, ownerAccess: false })
    await renderApp()
    expect(container.querySelector('form[aria-label="Parola değiştirme"]')).toBeNull()
    expect(container.textContent).toContain('Yeniden giriş yapın')
  })
})
