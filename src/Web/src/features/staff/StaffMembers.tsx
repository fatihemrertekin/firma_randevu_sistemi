import { useEffect, useRef, useState } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import StaffMemberEditor from './StaffMemberEditor'
import StaffMemberDetail from './StaffMemberDetail'
import { MemberRequestError, readMemberPage, type StaffMemberPage, type StaffPost } from './staffMembersApi'
import styles from './StaffMembers.module.css'

type Props = { post: StaffPost; onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void }
type View = { kind: 'list' } | { kind: 'create' } | { kind: 'detail'; id: string }
export default function StaffMembers({ post, onDirtyChange, onBusyChange }: Props) {
  const [view, setView] = useState<View>({ kind: 'list' })
  const [page, setPage] = useState(1), [revision, setRevision] = useState(0)
  const [data, setData] = useState<StaffMemberPage | null>(null)
  const [loading, setLoading] = useState(true), [childBusy, setChildBusy] = useState(false)
  const [error, setError] = useState(''), [notice, setNotice] = useState('')
  const addButton = useRef<HTMLButtonElement>(null), surface = useRef<HTMLElement>(null)
  const opener = useRef<string | null>(null), restoreFocus = useRef(false)
  useEffect(() => {
    onBusyChange((view.kind === 'list' && loading) || childBusy)
    return () => onBusyChange(false)
  }, [view.kind, loading, childBusy, onBusyChange])
  useEffect(() => () => onDirtyChange(false), [onDirtyChange])
  useEffect(() => {
    if (view.kind !== 'list') return
    const controller = new AbortController()
    void fetch(`/api/staff-members/?page=${page}`, { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) })
      .then(readMemberPage).then(value => { if (!controller.signal.aborted) setData(value) })
      .catch((problem: unknown) => {
        if (!controller.signal.aborted) setError(problem instanceof MemberRequestError ? problem.message : 'Personel listesi alınamadı. Yeniden deneyin.')
      }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [page, revision, view.kind])
  useEffect(() => {
    if (view.kind !== 'list' || loading || !restoreFocus.current) return
    restoreFocus.current = false
    const button = Array.from(surface.current?.querySelectorAll<HTMLButtonElement>('[data-member-id]') ?? [])
      .find(item => item.dataset.memberId === opener.current)
    ;(button ?? addButton.current)?.focus()
  }, [view.kind, loading, data])
  function load(next = page) { setData(null); setLoading(true); setError(''); setPage(next); setRevision(value => value + 1) }
  function back() {
    onDirtyChange(false); setChildBusy(false); restoreFocus.current = true
    setData(null); setLoading(true); setError(''); setView({ kind: 'list' })
  }
  return <section ref={surface} className={styles.surface} aria-label="Personel yönetimi">
    {view.kind === 'detail' && <StaffMemberDetail memberId={view.id} post={post} initialNotice={notice}
      onBack={back} onDeleted={name => { setNotice(`${name} personel listesinden silindi.`); setPage(1); back() }} onDirtyChange={onDirtyChange} onBusyChange={setChildBusy} />}
    {view.kind === 'create' && <>
      <h2>Yeni personel</h2><p>Bu kayıt sisteme giriş hesabı oluşturmaz.</p>
      <StaffMemberEditor member={null} embedded post={post} onDirtyChange={onDirtyChange} onBusyChange={setChildBusy}
        onCancel={back} onSaved={member => {
          onDirtyChange(false); setPage(1); setNotice('Personel kaydedildi.'); setView({ kind: 'detail', id: member.id })
        }} />
    </>}
    {view.kind === 'list' && <>
      <div className={styles.toolbar}><p>Personel kayıtları giriş hesabı oluşturmaz.</p>
        <button ref={addButton} className={styles.primary} type="button" disabled={loading || childBusy} onClick={() => {
          opener.current = null; setError(''); setNotice(''); setView({ kind: 'create' })
        }}>Yeni personel</button></div>
      <ErrorMessage message={error} />
      {notice && <p role="status">{notice}</p>}
      {loading && <p role="status">Personel yükleniyor…</p>}
      {!loading && data?.items.length === 0 && <p>Bu sayfada personel yok. Yeni personel ekleyerek başlayın.</p>}
      {data && <>
        <div className={styles.columns} aria-hidden="true"><span>Ad soyad</span><span>Durum</span><span /></div>
        <ul className={styles.list} aria-label="Personel listesi">{data.items.map(member => <li key={member.id} className={styles.row}>
          <strong className={styles.identity}>{member.name}</strong>
          <span className={styles.status}><span aria-hidden="true">{member.isActive ? '●' : '○'}</span> {member.isActive ? 'Aktif' : 'Pasif'}</span>
          <button type="button" data-member-id={member.id} disabled={loading || childBusy} aria-label={`${member.name} için ayrıntılar`} onClick={() => {
            opener.current = member.id; setNotice(''); setView({ kind: 'detail', id: member.id })
          }}>Ayrıntılar</button>
        </li>)}</ul>
      </>}
      <div className={styles.pagination}>
        <button type="button" disabled={loading || childBusy} onClick={() => { setNotice(''); load() }}>Listeyi yenile</button>
        <div><button type="button" disabled={loading || childBusy || page === 1} onClick={() => { setNotice(''); load(page - 1) }}>Önceki sayfa</button>
          <span>Sayfa {page}</span><button type="button" disabled={loading || childBusy || !data?.hasMore} onClick={() => { setNotice(''); load(page + 1) }}>Sonraki sayfa</button></div>
      </div>
    </>}
  </section>
}
