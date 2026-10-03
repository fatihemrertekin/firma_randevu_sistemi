import { useEffect, useRef, useState } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import StaffMemberEditor from './StaffMemberEditor'
import StaffServicesEditor from './StaffServicesEditor'
import { MemberRequestError, memberFailure, readMember, readMemberPage, type StaffMember, type StaffMemberPage, type StaffPost } from './staffMembersApi'
import styles from '../../components/DefinitionManagement.module.css'

type Props = { post: StaffPost; onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void }
export default function StaffMembers({ post, onDirtyChange, onBusyChange }: Props) {
  const [page, setPage] = useState(1)
  const [revision, setRevision] = useState(0)
  const [data, setData] = useState<StaffMemberPage | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [editing, setEditing] = useState<{ member: StaffMember | null } | null>(null)
  const [target, setTarget] = useState<StaffMember | null>(null)
  const [assignment, setAssignment] = useState<StaffMember | null>(null)
  const [busy, setBusy] = useState(false)
  const [stale, setStale] = useState(false)
  const sending = useRef(false)
  const heading = useRef<HTMLHeadingElement>(null)
  const confirm = useRef<HTMLButtonElement>(null)
  const addButton = useRef<HTMLButtonElement>(null)
  const opener = useRef<HTMLButtonElement | null>(null)
  const restoreFocus = useRef(false)
  useEffect(() => {
    if (target) confirm.current?.focus()
    else if (!editing && !assignment && restoreFocus.current) {
      restoreFocus.current = false
      if (opener.current?.isConnected) opener.current.focus()
      else addButton.current?.focus()
    }
  }, [target, editing, assignment])
  useEffect(() => {
    const controller = new AbortController()
    void fetch(`/api/staff-members/?page=${page}`, { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) })
      .then(readMemberPage).then(value => { if (!controller.signal.aborted) setData(value) })
      .catch((problem: unknown) => {
        if (!controller.signal.aborted) setError(problem instanceof MemberRequestError ? problem.message : 'Personel listesi alınamadı. Yeniden deneyin.')
      }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [page, revision])
  function load(next = page) {
    setData(null); setLoading(true); setError(''); setTarget(null); setStale(false)
    setPage(next); setRevision(value => value + 1)
  }
  function closeEditor() { restoreFocus.current = true; setEditing(null) }
  async function changeStatus() {
    if (!target || sending.current || stale) return
    sending.current = true; setBusy(true); onBusyChange(true); setError(''); setNotice('')
    try {
      const updated = await readMember(await post(`/api/staff-members/${target.id}/status`,
        { isActive: !target.isActive, version: target.version }, AbortSignal.timeout(15000)))
      setNotice(`${updated.name} ${updated.isActive ? 'aktifleştirildi' : 'pasifleştirildi'}. Giriş hesapları değişmedi.`)
      load(); heading.current?.focus()
    } catch (problem: unknown) {
      setError(problem instanceof MemberRequestError ? problem.message : memberFailure(500))
      setStale(true)
    } finally { sending.current = false; setBusy(false); onBusyChange(false) }
  }
  const blocked = busy || loading || editing !== null || assignment !== null || target !== null
  return <section aria-labelledby="staff-members-title">
    <h2 id="staff-members-title" ref={heading} className={styles.heading} tabIndex={-1}>Personel listesi</h2>
    <p>İşletmede hizmet veren kişileri tanımlayın. Kendinizi de ekleyebilirsiniz. Bu kayıtlar sisteme giriş hesabı oluşturmaz.</p>
    <ErrorMessage message={error} />
    {notice && <p role="status">{notice}</p>}
    {loading && <p role="status">Personel yükleniyor…</p>}
    {!loading && data?.items.length === 0 && <p>Bu sayfada personel yok. Yeni personel ekleyerek başlayın.</p>}
    {!editing && !assignment && <button type="button" ref={addButton} className={styles.primary} disabled={busy || target !== null} onClick={event => {
      opener.current = event.currentTarget; setEditing({ member: null }); setError(''); setNotice('')
    }}>Yeni personel</button>}
    {editing && <StaffMemberEditor member={editing.member} post={post} onDirtyChange={onDirtyChange} onBusyChange={onBusyChange}
      onCancel={closeEditor} onSaved={() => { setEditing(null); setNotice('Personel kaydedildi.'); load(1); heading.current?.focus() }} />}
    {assignment && <StaffServicesEditor memberId={assignment.id} post={post} onDirtyChange={onDirtyChange} onBusyChange={onBusyChange}
      onCancel={() => { restoreFocus.current = true; setAssignment(null) }}
      onSaved={() => { setAssignment(null); setNotice('Personelin hizmet seçimleri kaydedildi.'); load(); heading.current?.focus() }} />}
    {data && <ul className={styles.list}>{data.items.map(member => <li key={member.id} className={styles.row}>
      <div className={styles.identity}><strong>{member.name}</strong><p><span aria-hidden="true">{member.isActive ? '● ' : '○ '}</span>{member.isActive ? 'Aktif' : 'Pasif'}</p></div>
      <div className={styles.actions}>
        <button type="button" disabled={blocked} aria-label={`${member.name} için hizmetleri seç`} onClick={event => {
          opener.current = event.currentTarget; setAssignment(member); setError(''); setNotice('')
        }}>Hizmetleri seç</button>
        <button type="button" disabled={blocked} aria-label={`${member.name} adını düzenle`} onClick={event => {
          opener.current = event.currentTarget; setEditing({ member }); setError(''); setNotice('')
        }}>Adı düzenle</button>
        <button type="button" disabled={blocked} aria-label={`${member.name} personelini ${member.isActive ? 'pasifleştir' : 'aktifleştir'}`} onClick={event => {
          opener.current = event.currentTarget; setTarget(member); setStale(false); setError(''); setNotice('')
        }}>{member.isActive ? 'Pasifleştir' : 'Aktifleştir'}</button>
      </div>
    </li>)}</ul>}
    {target && <fieldset className={styles.confirmation} disabled={busy} aria-label="Personel durum değişikliği onayı">
      <legend>Personeli {target.isActive ? 'pasifleştir' : 'aktifleştir'}</legend>
      <p className={styles.identity}><strong>{target.name}</strong> {target.isActive ? 'pasif' : 'aktif'} olarak işaretlenecek. Kayıt silinmez. Giriş hesabı ve açık oturumlar etkilenmez.</p>
      <button type="button" ref={confirm} disabled={stale} onClick={() => { void changeStatus() }}>{busy ? 'İşlem sürüyor…' : 'Durumu değiştir'}</button>
      <button type="button" onClick={() => { restoreFocus.current = true; setTarget(null); setError('') }}>Vazgeç</button>
    </fieldset>}
    <div className={styles.actions}>
      <button type="button" disabled={busy || loading || editing !== null || assignment !== null} onClick={() => { setNotice(''); load(); heading.current?.focus() }}>Listeyi yenile</button>
      <button type="button" disabled={blocked || page === 1} onClick={() => { setNotice(''); load(page - 1) }}>Önceki sayfa</button>
      <span>Sayfa {page}</span>
      <button type="button" disabled={blocked || !data?.hasMore} onClick={() => { setNotice(''); load(page + 1) }}>Sonraki sayfa</button>
    </div>
  </section>
}
