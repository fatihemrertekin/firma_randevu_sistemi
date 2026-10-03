import { isService, type Service } from '../services/servicesApi'
import { isMember, memberFailure, type StaffMember } from './staffMembersApi'

export type ServiceReference = { id: string; version: string }
export type Selection = { member: StaffMember; selected: ServiceReference[] }
export type SelectionPage = Selection & { items: Service[]; page: number; hasMore: boolean }
export class SelectionError extends Error {
  constructor(public status: number) {
    super(status === 400 ? 'Hizmet seçimlerini kontrol edip yeniden dene.'
      : status === 404 ? 'Personel veya hizmet bulunamadı. Güncel seçimleri yükle.'
        : status === 409 ? 'Personel veya hizmet değişti. Taslağın korundu; güncel seçimleri yükleyip yeniden düzenle.' : memberFailure(status))
  }
}
function isSelection(value: unknown): value is Selection {
  if (typeof value !== 'object' || value === null || !('member' in value) || !isMember(value.member) ||
    !('selected' in value) || !Array.isArray(value.selected) || value.selected.length > 500) return false
  return value.selected.every((item: unknown) => typeof item === 'object' && item !== null && 'id' in item && typeof item.id === 'string' &&
    'version' in item && typeof item.version === 'string') && new Set(value.selected.map((item: ServiceReference) => item.id)).size === value.selected.length
}
export async function readSelection(response: Response): Promise<Selection> {
  if (!response.ok) throw new SelectionError(response.status)
  const value: unknown = await response.json()
  if (!isSelection(value)) throw new SelectionError(500)
  return value
}
export async function readSelectionPage(response: Response): Promise<SelectionPage> {
  if (!response.ok) throw new SelectionError(response.status)
  const value: unknown = await response.json()
  if (!isSelection(value) || !('items' in value) || !Array.isArray(value.items) || !value.items.every(isService) ||
    !('page' in value) || typeof value.page !== 'number' || !Number.isInteger(value.page) || value.page < 1 ||
    !('hasMore' in value) || typeof value.hasMore !== 'boolean') throw new SelectionError(500)
  return { ...value, items: value.items, page: value.page, hasMore: value.hasMore }
}
