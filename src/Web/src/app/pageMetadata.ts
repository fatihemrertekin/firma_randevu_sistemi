export type PageMetadata = { page: number; pageSize: number; totalCount: number; hasMore: boolean }
export function isPageMetadata(value: unknown): value is PageMetadata {
  if (typeof value !== 'object' || value === null || !('page' in value) || typeof value.page !== 'number' ||
    !Number.isInteger(value.page) || value.page < 1 || value.page > 10000 ||
    !('pageSize' in value) || typeof value.pageSize !== 'number' || !Number.isInteger(value.pageSize) || value.pageSize < 1 || value.pageSize > 50 ||
    !('totalCount' in value) || typeof value.totalCount !== 'number' || !Number.isSafeInteger(value.totalCount) || value.totalCount < 0 ||
    !('hasMore' in value) || typeof value.hasMore !== 'boolean') return false
  const last = Math.max(1, Math.ceil(value.totalCount / value.pageSize))
  return value.page <= last && value.hasMore === (value.page < last)
}
