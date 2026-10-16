import { describe, expect, it } from 'vitest'
import { businessDay, isDay, nextDay, readAppointmentPage, readAvailability } from './appointmentsApi'

describe('Randevu veri sınırı', () => {
  it('İstanbul gününü UTC tarihinden ayırır, geçersiz gün taşmaz', () => {
    expect(businessDay(new Date('2026-10-15T21:30:00Z'))).toBe('2026-10-16')
    expect(nextDay('2026-10-31', 1)).toBe('2026-11-01')
    expect(nextDay('2028-02-28', 1)).toBe('2028-02-29')
    for (const value of ['', '2026-99-99', '2026-02-30', '2026-02-29', '0001-01-01', '9999-12-31', '16.10.2026']) expect(isDay(value)).toBe(false)
  })
  it('yanlış liste ve uygunluk sözleşmesini başarı olarak kullanmaz', async () => {
    await expect(readAppointmentPage(Response.json({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false, date: '2026-02-30', timeZone: 'Europe/Istanbul' }))).rejects.toMatchObject({ status: 500 })
    await expect(readAvailability(Response.json({ startsAt: ['2026-10-16T09:00:00'], timeZone: 'Europe/Istanbul', serviceVersion: '00000001-0000-0000-0000-000000000001' }), false)).rejects.toMatchObject({ status: 500 })
    await expect(readAvailability(Response.json({ startsAt: [], timeZone: 'Europe/Istanbul', serviceVersion: '00000001-0000-0000-0000-000000000001' }), true)).rejects.toMatchObject({ status: 500 })
  })
})
