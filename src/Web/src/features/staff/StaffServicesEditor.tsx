import { useEffect, useRef, useState, type FormEvent } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import useUnsavedChanges from '../../app/useUnsavedChanges'
import { formatTryPrice } from '../../app/money'
import type { Service } from '../services/servicesApi'
import type { StaffMember, StaffPost } from './staffMembersApi'
import { SelectionError, readSelection, readSelectionPage, type Selection, type SelectionPage, type ServiceReference } from './staffServicesApi'
import styles from '../../components/DefinitionManagement.module.css'
import choices from './StaffServicesEditor.module.css'
import personnel from './StaffMembers.module.css'
import { useLocation, useNavigate } from 'react-router'
import { readPage, readPageSize } from '../../app/routes'
import { useCompletedNavigation } from '../../app/CompletedNavigation'
import Pagination from '../../components/Pagination'

type Props = { memberId: string; post: StaffPost; onSaved: () => void; onCancel: () => void; onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void;
  embedded?: boolean; onMemberRead?: (member: StaffMember) => void }
function sameReferences(left: ServiceReference[], right: ServiceReference[]) {
  return left.length === right.length && left.every(item => right.some(other => item.id === other.id && item.version === other.version))
}
export default function StaffServicesEditor({ memberId, post, onSaved, onCancel, onDirtyChange, onBusyChange, embedded, onMemberRead }: Props) {
  const [snapshot, setSnapshot] = useState<Selection | null>(null)
  const [selected, setSelected] = useState(new Map<string, ServiceReference>())
  const [data, setData] = useState<SelectionPage | null>(null)
  const location = useLocation(), navigate = useNavigate(), completed = useCompletedNavigation()
  const page = readPage(location.search, 'hizmetSayfa'), pageSize = readPageSize(location.search, 10, 'hizmetBoyut')
  const [revision, setRevision] = useState(0)
  const [loading, setLoading] = useState(true), [busy, setBusy] = useState(false), [locked, setLocked] = useState(false)
  const [error, setError] = useState(''), [fieldError, setFieldError] = useState('')
  const baseline = useRef<Selection | null>(null), chosen = useRef(selected), reset = useRef(false), sending = useRef(false)
  const fields = useRef<HTMLFieldSetElement>(null)
  const dirty = snapshot !== null && (selected.size !== snapshot.selected.length || snapshot.selected.some(item => !selected.has(item.id)))
  useUnsavedChanges(dirty, onDirtyChange)
  useEffect(() => { onBusyChange(loading || busy); return () => onBusyChange(false) }, [loading, busy, onBusyChange])
  useEffect(() => {
    const controller = new AbortController()
    void fetch(`/api/staff-members/${memberId}/services?page=${page}&pageSize=${pageSize}`, { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) })
      .then(readSelectionPage).then(current => {
        if (controller.signal.aborted) return
        if (!baseline.current || reset.current) {
          baseline.current = current; reset.current = false; setSnapshot(current)
          onMemberRead?.(current.member)
          const next = new Map(current.selected.map(item => [item.id, item])); chosen.current = next; setSelected(next)
        } else if (baseline.current.member.version !== current.member.version || !sameReferences(baseline.current.selected, current.selected) ||
          current.items.some(item => chosen.current.has(item.id) && chosen.current.get(item.id)?.version !== item.version)) {
          setLocked(true); setError(new SelectionError(409).message); return
        }
        setData(current); setLocked(false)
        if (current.page !== page) {
          const query = new URLSearchParams(location.search)
          if (current.page > 1) query.set('hizmetSayfa', String(current.page)); else query.delete('hizmetSayfa')
          completed(location.pathname + (query.size ? `?${query}` : ''), { replace: true }, true)
        }
      }).catch((problem: unknown) => {
        if (!controller.signal.aborted) { setError(problem instanceof SelectionError ? problem.message : new SelectionError(500).message); setLocked(true) }
      }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [memberId, page, pageSize, revision, onMemberRead, completed, location.pathname, location.search])
  useEffect(() => {
    if (loading || busy) return
    if (fieldError) {
      const first = fields.current?.querySelector<HTMLInputElement>('input:not(:disabled)')
      if (first) first.focus(); else fields.current?.focus()
    }
  }, [loading, busy, fieldError])
  function choose(service: Service, checked: boolean) {
    const next = new Map(selected)
    if (checked) {
      if (next.size >= 500) { setFieldError('En fazla 500 hizmet seçebilirsin.'); return }
      next.set(service.id, { id: service.id, version: service.version })
    } else next.delete(service.id)
    chosen.current = next; setSelected(next); setFieldError('')
  }
  function turnPage(next: number, size = pageSize, replace = false) {
    const query = new URLSearchParams(location.search)
    if (next > 1) query.set('hizmetSayfa', String(next)); else query.delete('hizmetSayfa')
    if (size !== 10) query.set('hizmetBoyut', String(size)); else query.delete('hizmetBoyut')
    setLoading(true); setError('')
    void navigate(location.pathname + (query.size ? `?${query}` : ''), { replace })
  }
  function reload() {
    if (sending.current || (dirty && !window.confirm('Güncel seçimler yüklensin ve kaydedilmemiş değişiklikler silinsin mi?'))) return
    reset.current = true; setLoading(true); setError(''); setFieldError(''); setRevision(value => value + 1)
  }
  function cancel() {
    if (sending.current || loading) return
    if (dirty && !window.confirm('Kaydedilmemiş hizmet seçimleri silinsin mi?')) return
    onDirtyChange(false); onCancel()
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!snapshot || sending.current || loading || locked || !dirty) return
    sending.current = true; setBusy(true); setError(''); setFieldError('')
    try {
      const response = await post(`/api/staff-members/${memberId}/services`,
        { version: snapshot.member.version, services: Array.from(selected.values()) }, AbortSignal.timeout(15000))
      if (response.status === 400) {
        setFieldError('Hizmet seçimleri doğrulanamadı. Güncel listeyi yükleyip yeniden seç.'); return
      }
      await readSelection(response); onDirtyChange(false); onSaved()
    } catch (problem: unknown) {
      setError(problem instanceof SelectionError ? problem.message : new SelectionError(500).message); setLocked(true)
    } finally { sending.current = false; setBusy(false) }
  }
  const blocked = loading || busy || locked
  return <form className={`${styles.editor} ${choices.editor} ${personnel.taskForm}`} aria-label="Personelin hizmet seçimleri" aria-busy={loading || busy} onSubmit={event => { void save(event) }}>
    {!embedded && <h3 className={styles.identity}>{snapshot?.member.name ?? 'Personel'} — Hizmet seçimi</h3>}
    <p id="assignment-info">Seçili: {selected.size} hizmet (tüm sayfalarda). Seçimleri kaydettiğinde personelin verebildiği hizmetler güncellenir.</p>
    {snapshot && !snapshot.member.isActive && <p>Personel pasif. Mevcut eşleşmeleri koruyabilir veya kaldırabilirsin; yeni eşleşme için personeli aktifleştir.</p>}
    <p>Pasif hizmetlere yeni eşleşme eklenmez. Mevcut eşleşmeyi kaldırmak personeli veya hizmeti silmez.</p>
    <ErrorMessage message={error} />
    {loading && <><p role="status">Hizmet seçimleri yükleniyor…</p>{!data && <div className={styles.skeleton} aria-hidden="true" />}</>}
    <fieldset ref={fields} className={`${styles.fields} ${styles.heading}`} tabIndex={-1} disabled={blocked}
      aria-invalid={!!fieldError || undefined} aria-describedby={fieldError ? 'assignment-info assignment-error' : 'assignment-info'}>
      <legend>Hizmet seçimleri</legend>
      {!loading && data?.items.length === 0 && <p>Bu sayfada hizmet yok. Hizmetler bölümünden hizmet ekleyebilirsin.</p>}
      {data?.items.map(service => <label key={service.id} className={choices.choice}>
        <input type="checkbox" aria-label={`${service.name} hizmetini seç`} checked={selected.has(service.id)}
          disabled={!selected.has(service.id) && (!snapshot?.member.isActive || !service.isActive) && !snapshot?.selected.some(item => item.id === service.id)}
          onChange={event => choose(service, event.target.checked)} />
        <span className={choices.text}><strong>{service.name}</strong><span className={choices.details}>
          {service.durationMinutes} dk · {formatTryPrice(service.price)} · {service.isActive ? '● Aktif' : '○ Pasif'}</span></span>
      </label>)}
    </fieldset>
    {fieldError && <p id="assignment-error" className={styles.fieldError}>{fieldError}</p>}
    {dirty && <p className={personnel.draft} role="status">Değişiklikler henüz kaydedilmedi.</p>}
    <div className={personnel.actions}>
      <button type="submit" className={styles.primary} disabled={blocked || !dirty}>{busy ? 'İşlem sürüyor…' : 'Seçimleri kaydet'}</button>
      <button type="button" disabled={busy || loading} onClick={cancel}>Vazgeç</button>
      {(error || fieldError || locked) && <button type="button" disabled={busy || loading} onClick={reload}>Güncel seçimleri yükle</button>}
    </div>
    <Pagination label="Personel hizmet sayfaları" page={data?.page ?? page} pageSize={pageSize} itemCount={data?.items.length ?? 0} totalCount={data?.totalCount}
      hasNext={data?.hasMore ?? false} disabled={blocked} onPageChange={next => turnPage(next)} onPageSizeChange={size => turnPage(1, size)} />
  </form>
}
