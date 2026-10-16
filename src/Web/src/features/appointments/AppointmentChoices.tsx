import { useEffect, useState } from 'react'
import { readServicePage, type Service } from '../services/servicesApi'
import { readMemberPage, type StaffMember } from '../staff/staffMembersApi'
import ErrorMessage from '../../components/ErrorMessage'
import NavigationLink from '../../app/NavigationLink'
import { sectionPaths } from '../../app/routes'
import { formatTryPrice } from '../../app/money'
import { signal } from './appointmentsApi'
import styles from './Appointments.module.css'

type Props = { kind: 'services' | 'staff'; id: string; value: string; label: string; disabled: boolean;
  includeInactive?: boolean; optional?: boolean; fieldName?: string; invalid?: boolean; onChange: (item: Service | StaffMember | null) => void; onUnauthorized: () => void }
export default function AppointmentChoices({ kind, id, value, label, disabled, includeInactive = false, optional = false, fieldName, invalid, onChange, onUnauthorized }: Props) {
  const [items, setItems] = useState<(Service | StaffMember)[]>([]), [page, setPage] = useState(1), [more, setMore] = useState(false)
  const [loading, setLoading] = useState(true), [error, setError] = useState(''), [revision, setRevision] = useState(0)
  useEffect(() => {
    const controller = new AbortController()
    void fetch(`/api/${kind === 'staff' ? 'staff-members' : 'services'}/?page=${page}&pageSize=50`, { cache: 'no-store', signal: signal(controller) })
      .then(async response => { if (response.status === 401 && !controller.signal.aborted) onUnauthorized(); return kind === 'staff' ? readMemberPage(response) : readServicePage(response) })
      .then(data => { if (!controller.signal.aborted) { setItems(current => page === 1 ? data.items : [...current.filter(item => !data.items.some(next => next.id === item.id)), ...data.items]); setMore(data.hasMore) } })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Seçimler yüklenemedi.') })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [kind, page, revision, onUnauthorized])
  const choices = items.filter(item => includeInactive || item.isActive)
  const empty = !loading && !error && !value && choices.length === 0
  const subject = kind === 'staff' ? 'çalışan' : 'hizmet'
  const descriptionId = id + '-choices-status'
  return <div className={styles.field}>
    <label htmlFor={id}>{label}</label>
    <select id={id} name={fieldName} value={value} aria-invalid={invalid || undefined} aria-describedby={[invalid ? id + '-error' : '', loading || empty ? descriptionId : ''].filter(Boolean).join(' ') || undefined}
      disabled={disabled || loading || !!error} onChange={event => onChange(items.find(item => item.id === event.target.value) ?? null)}>
      <option value="">{optional ? 'Tümü' : 'Seçin'}</option>
      {value && !items.some(item => item.id === value) && <option value={value}>Seçili çalışan</option>}
      {choices.map(item => <option key={item.id} value={item.id}>
        {item.name}{'durationMinutes' in item ? ` · ${item.durationMinutes} dk · ${formatTryPrice(item.price)}` : ''}{!item.isActive ? ' (Pasif)' : ''}
      </option>)}
    </select>
    {loading && <span id={descriptionId} role="status">{kind === 'staff' ? 'Çalışanlar' : 'Hizmetler'} yükleniyor…</span>}
    {empty && <div id={descriptionId} role="status">
      {more ? <p>Yüklenen kayıtlarda {includeInactive ? '' : 'aktif '}{subject} bulunmuyor. Diğer {kind === 'staff' ? 'çalışanları' : 'hizmetleri'} yükleyerek devam edin.</p> : <>
        <p>{items.length === 0 ? `Henüz ${subject} kaydı yok.` : `Aktif ${subject} bulunmuyor.`} {optional ? 'Tümü seçiliyken randevular listelenebilir.' : `Devam etmek için aktif bir ${subject} seçilmelidir.`}</p>
        <NavigationLink to={kind === 'staff' ? sectionPaths.personnel : sectionPaths.services}>{kind === 'staff' ? 'Personel' : 'Hizmetler'} sayfasında {items.length === 0 ? 'kayıt ekleyin' : 'kayıt ekleyin veya mevcut kaydı aktifleştirin'}</NavigationLink>
      </>}
    </div>}
    <ErrorMessage message={error} />
    {error && <button type="button" disabled={disabled || loading} onClick={() => { setLoading(true); setError(''); setRevision(current => current + 1) }}>Seçimleri yeniden yükle</button>}
    {!error && more && <button type="button" disabled={disabled || loading} onClick={() => { setLoading(true); setPage(current => current + 1) }}>Diğer {kind === 'staff' ? 'çalışanları' : 'hizmetleri'} yükle</button>}
  </div>
}
