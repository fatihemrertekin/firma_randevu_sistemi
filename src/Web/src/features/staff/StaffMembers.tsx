import { useEffect, useRef, useState } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import StaffMemberEditor from './StaffMemberEditor'
import StaffMemberDetail from './StaffMemberDetail'
import { MemberRequestError, readMemberPage, type StaffMemberPage, type StaffPost } from './staffMembersApi'
import styles from './StaffMembers.module.css'
import { useLocation, useNavigate } from 'react-router'
import { pageSearch, readPage, resolveRoute, sectionPaths, personnelPath } from '../../app/routes'
import NavigationLink from '../../app/NavigationLink'
import { useCompletedNavigation } from '../../app/CompletedNavigation'
import { useNavigationChange } from '../../app/NavigationEvents'

type Props = { post: StaffPost; onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void }
type View = { kind: 'list' } | { kind: 'create' } | { kind: 'detail'; id: string }
export default function StaffMembers({ post, onDirtyChange, onBusyChange }: Props) {
  const location = useLocation(), navigate = useNavigate(), completed = useCompletedNavigation()
  const route = resolveRoute(location.pathname)
  const view: View = route.kind === 'management' && route.create ? { kind: 'create' }
    : route.kind === 'management' && route.id ? { kind: 'detail', id: route.id } : { kind: 'list' }
  const page = readPage(location.search), [revision, setRevision] = useState(0)
  const listPath = sectionPaths.personnel + pageSearch(page)
  const [data, setData] = useState<StaffMemberPage | null>(null)
  const [loading, setLoading] = useState(true), [childBusy, setChildBusy] = useState(false)
  const [error, setError] = useState(''), [notice, setNotice] = useState('')
  const addButton = useRef<HTMLAnchorElement>(null), surface = useRef<HTMLElement>(null)
  const opener = useRef<string | null>(null), restoreFocus = useRef(false)
  useNavigationChange(next => {
    if (next.pathname === sectionPaths.personnel) { setLoading(true); setData(null); setError(''); setRevision(value => value + 1); restoreFocus.current = true }
  })
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
    const button = Array.from(surface.current?.querySelectorAll<HTMLAnchorElement>('[data-member-id]') ?? [])
      .find(item => item.dataset.memberId === opener.current)
    ;(button ?? addButton.current)?.focus()
  }, [view.kind, loading, data])
  function load(next = page) {
    setData(null); setLoading(true); setError(''); setRevision(value => value + 1)
    if (next !== page) void navigate(sectionPaths.personnel + pageSearch(next))
  }
  function back() {
    onDirtyChange(false); setChildBusy(false); restoreFocus.current = true
    setData(null); setLoading(true); setError(''); void navigate(listPath)
  }
  return <section ref={surface} className={styles.surface} aria-label="Personel yönetimi">
    {view.kind === 'detail' && <StaffMemberDetail key={view.id} memberId={view.id} post={post} initialNotice={notice}
      task={route.kind === 'management' ? route.task ?? 'information' : 'information'} listPath={listPath}
      onBack={back} onDeleted={name => { setNotice(`${name} personel listesinden silindi.`); setData(null); setLoading(true); completed(sectionPaths.personnel, { replace: true }) }} onDirtyChange={onDirtyChange} onBusyChange={setChildBusy} />}
    {view.kind === 'create' && <>
      <h2>Yeni personel</h2><p>Bu kayıt sisteme giriş hesabı oluşturmaz.</p>
      <StaffMemberEditor member={null} embedded post={post} onDirtyChange={onDirtyChange} onBusyChange={setChildBusy}
        onCancel={back} onSaved={member => {
          onDirtyChange(false); setNotice('Personel kaydedildi.'); completed(personnelPath(member.id), { replace: true })
        }} />
    </>}
    {view.kind === 'list' && <>
      <div className={styles.toolbar}><p>Personel kayıtları giriş hesabı oluşturmaz.</p>
        <NavigationLink ref={addButton} to={sectionPaths.personnel + '/yeni' + pageSearch(page)} className={styles.primary} disabled={loading || childBusy} onClick={() => {
          opener.current = null; setError(''); setNotice('')
        }}>Yeni personel</NavigationLink></div>
      <ErrorMessage message={error} />
      {notice && <p role="status">{notice}</p>}
      {loading && <p role="status">Personel yükleniyor…</p>}
      {!loading && data?.items.length === 0 && <p>Bu sayfada personel yok. Yeni personel ekleyerek başlayın.</p>}
      {data && <>
        <div className={styles.columns} aria-hidden="true"><span>Ad soyad</span><span>Durum</span><span /></div>
        <ul className={styles.list} aria-label="Personel listesi">{data.items.map(member => <li key={member.id} className={styles.row}>
          <strong className={styles.identity}>{member.name}</strong>
          <span className={styles.status}><span aria-hidden="true">{member.isActive ? '●' : '○'}</span> {member.isActive ? 'Aktif' : 'Pasif'}</span>
          <NavigationLink to={personnelPath(member.id) + pageSearch(page)} data-member-id={member.id} disabled={loading || childBusy} aria-label={`${member.name} için ayrıntılar`} onClick={() => {
            opener.current = member.id; setNotice('')
          }}>Ayrıntılar</NavigationLink>
        </li>)}</ul>
      </>}
      <div className={styles.pagination}>
        {(error || (data?.items.length ?? 0) > 0) && <button type="button" disabled={loading || childBusy} onClick={() => { setNotice(''); load() }}>Listeyi yenile</button>}
        <div><button type="button" disabled={loading || childBusy || page === 1} onClick={() => { setNotice(''); load(page - 1) }}>Önceki sayfa</button>
          <span>Sayfa {page}</span><button type="button" disabled={loading || childBusy || !data?.hasMore} onClick={() => { setNotice(''); load(page + 1) }}>Sonraki sayfa</button></div>
      </div>
    </>}
  </section>
}
