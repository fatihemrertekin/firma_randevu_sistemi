const businessDateTime = new Intl.DateTimeFormat('tr-TR', { timeZone: 'Europe/Istanbul', dateStyle: 'short', timeStyle: 'medium' })
export function formatBusinessDateTime(value: string) { return businessDateTime.format(new Date(value)) }
