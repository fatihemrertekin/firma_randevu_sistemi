import { matchPath } from 'react-router'

export const sectionPaths = {
  business: '/yonetim/isletme', hours: '/yonetim/isletme/saatler',
  personnel: '/yonetim/personel', services: '/yonetim/hizmetler',
  access: '/yonetim/calisan-erisimleri', security: '/yonetim/hesap', audit: '/yonetim/degisiklik-kayitlari',
} as const
export type Section = keyof typeof sectionPaths
export type PersonnelTask = 'information' | 'services' | 'hours'
export const authPaths = {
  login: '/giris', reset: '/parola-yenile', resetCode: '/parola-yenile/kod',
  staffReset: '/calisan/parola-yenile', invitation: '/calisan/davet-kabul', verify: '/eposta-dogrula',
} as const
export type AuthPage = keyof typeof authPaths
export type ManagementRoute = { kind: 'management'; section: Section; create?: boolean; id?: string; task?: PersonnelTask }
export type AppRoute = ManagementRoute | { kind: 'auth'; page: AuthPage } | { kind: 'home' } | { kind: 'missing' }

export function resolveRoute(pathname: string): AppRoute {
  const path = pathname.replace(/\/$/, '') || '/'
  if (path === '/' || path === '/yonetim') return { kind: 'home' }
  for (const [page, value] of Object.entries(authPaths)) {
    if (path === value) return { kind: 'auth', page: page as AuthPage }
  }
  for (const [section, value] of Object.entries(sectionPaths)) {
    if (path === value) return { kind: 'management', section: section as Section }
  }
  if (path === sectionPaths.personnel + '/yeni') return { kind: 'management', section: 'personnel', create: true }
  if (path === sectionPaths.services + '/yeni') return { kind: 'management', section: 'services', create: true }
  const personnel = matchPath('/yonetim/personel/:id/:task?', path)
  if (personnel?.params.id && /^[a-zA-Z0-9-]+$/.test(personnel.params.id)) {
    const task = personnel.params.task
    if (!task || task === 'hizmetler' || task === 'saatler') return {
      kind: 'management', section: 'personnel', id: personnel.params.id,
      task: task === 'hizmetler' ? 'services' : task === 'saatler' ? 'hours' : 'information',
    }
  }
  const service = matchPath('/yonetim/hizmetler/:id/duzenle', path)
  if (service?.params.id && /^[a-zA-Z0-9-]+$/.test(service.params.id)) return { kind: 'management', section: 'services', id: service.params.id }
  return { kind: 'missing' }
}

export function personnelPath(id: string, task: PersonnelTask = 'information') {
  return `${sectionPaths.personnel}/${encodeURIComponent(id)}${task === 'services' ? '/hizmetler' : task === 'hours' ? '/saatler' : ''}`
}
export function readPage(search: string) {
  const value = new URLSearchParams(search).get('sayfa')
  return value && /^[1-9]\d{0,4}$/.test(value) && Number(value) <= 10000 ? Number(value) : 1
}
export function pageSearch(page: number) { return page > 1 ? `?sayfa=${page}` : '' }
export function safeReturnPath(value: string | null) {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.includes('\\')) return null
  const [pathname, search = ''] = value.split('?')
  if (resolveRoute(pathname).kind !== 'management') return null
  return pathname + pageSearch(readPage('?' + search))
}
