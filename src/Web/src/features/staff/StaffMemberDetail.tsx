import { useCallback, useEffect, useRef, useState } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import StaffMemberEditor from './StaffMemberEditor'
import StaffServicesEditor from './StaffServicesEditor'
import StaffHoursEditor from './StaffHoursEditor'
import { MemberRequestError, memberFailure, readMember, type StaffMember, type StaffPost } from './staffMembersApi'
import styles from './StaffMembers.module.css'

type Task = 'information' | 'services' | 'hours'
const tasks: { id: Task; label: string }[] = [{ id: 'information', label: 'Bilgiler' }, { id: 'services', label: 'Hizmetler' }, { id: 'hours', label: 'Saatler' }]
type Props = { memberId: string; post: StaffPost; initialNotice: string; onBack: () => void;
  onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void }
export default function StaffMemberDetail({ memberId, post, initialNotice, onBack, onDirtyChange, onBusyChange }: Props) {
  const [member, setMember] = useState<StaffMember | null>(null), [task, setTask] = useState<Task>('information')
  const [revision, setRevision] = useState(0), [loading, setLoading] = useState(true)
  const [paneDirty, setPaneDirty] = useState(false), [paneBusy, setPaneBusy] = useState(false)
  const [confirmStatus, setConfirmStatus] = useState(false), [statusBusy, setStatusBusy] = useState(false), [stale, setStale] = useState(false)
  const [error, setError] = useState(''), [notice, setNotice] = useState(initialNotice)
  const sending = useRef(false), confirm = useRef<HTMLButtonElement>(null), statusButton = useRef<HTMLButtonElement>(null), restoreStatus = useRef(false)
  const reportDirty = useCallback((value: boolean) => { setPaneDirty(value); onDirtyChange(value) }, [onDirtyChange])
  const blocked = loading || paneBusy || statusBusy
  useEffect(() => { onBusyChange(blocked); return () => onBusyChange(false) }, [blocked, onBusyChange])
  useEffect(() => () => onDirtyChange(false), [onDirtyChange])
  useEffect(() => {
    const controller = new AbortController()
    void fetch(`/api/staff-members/${memberId}`, { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) })
      .then(readMember).then(value => {
        if (value.id !== memberId) throw new MemberRequestError(500)
        if (!controller.signal.aborted) { setMember(value); setStale(false) }
      }).catch((problem: unknown) => {
        if (!controller.signal.aborted) { setMember(null); setError(problem instanceof MemberRequestError ? problem.message : memberFailure(500)) }
      }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [memberId, revision])
  useEffect(() => {
    if (loading || statusBusy) return
    if (confirmStatus && member) confirm.current?.focus()
    else if (restoreStatus.current) { restoreStatus.current = false; statusButton.current?.focus() }
  }, [confirmStatus, member, loading, statusBusy])
  function discard() {
    if (sending.current || blocked) return false
    if (paneDirty && !window.confirm('Kaydedilmemiş personel değişiklikleri silinsin mi?')) return false
    reportDirty(false); return true
  }
  function refresh() { setLoading(true); setError(''); setStale(false); setPaneBusy(false); setRevision(value => value + 1) }
  function reload() { if (discard()) { setConfirmStatus(false); setNotice(''); refresh() } }
  function changeTask(next: Task) {
    if (task === next || confirmStatus || !discard()) return
    setTask(next); setNotice(''); refresh()
  }
  function saved(message: string) { reportDirty(false); setNotice(message); refresh() }
  function resetTask() { reportDirty(false); setNotice(''); refresh() }
  function back() { if (!confirmStatus && discard()) onBack() }
  async function changeStatus() {
    if (!member || sending.current || blocked || stale) return
    sending.current = true; setStatusBusy(true); setError(''); setNotice('')
    try {
      const updated = await readMember(await post(`/api/staff-members/${memberId}/status`,
        { isActive: !member.isActive, version: member.version }, AbortSignal.timeout(15000)))
      if (updated.id !== memberId) throw new MemberRequestError(500)
      setConfirmStatus(false); saved(`${updated.name} ${updated.isActive ? 'aktifleştirildi' : 'pasifleştirildi'}. Giriş hesapları değişmedi.`)
    } catch (problem: unknown) { setError(problem instanceof MemberRequestError ? problem.message : memberFailure(500)); setStale(true) }
    finally { sending.current = false; setStatusBusy(false) }
  }
  return <>
    <button className={styles.back} type="button" disabled={blocked || confirmStatus} onClick={back}>Personel listesine dön</button>
    {member && <div className={styles.detailIdentity}><h2 className={styles.identity}>{member.name}</h2>
      <span className={styles.status}><span aria-hidden="true">{member.isActive ? '●' : '○'}</span> {member.isActive ? 'Aktif' : 'Pasif'}</span></div>}
    {loading && <p role="status">Personel bilgileri yükleniyor…</p>}
    <ErrorMessage message={error} />
    {notice && <p className={styles.notice} role="status">{notice}</p>}
    {!loading && !member && <button type="button" onClick={reload}>Güncel kaydı yükle</button>}
    {member && <>
      <nav className={styles.tasks} aria-label="Personel görevleri">{tasks.map(item => <button key={item.id} type="button"
        aria-pressed={task === item.id} aria-controls="personnel-task" disabled={blocked || confirmStatus} onClick={() => changeTask(item.id)}>{item.label}</button>)}</nav>
      <div id="personnel-task" className={styles.task} aria-busy={blocked}>
        {!loading && !confirmStatus && <>
          {task === 'information' && <StaffMemberEditor key={revision} member={member} embedded post={post}
            onDirtyChange={reportDirty} onBusyChange={setPaneBusy} onMemberRead={setMember}
            onCancel={resetTask} onSaved={() => saved('Personel kaydedildi.')} />}
          {task === 'services' && <StaffServicesEditor key={revision} memberId={memberId} embedded post={post}
            onDirtyChange={reportDirty} onBusyChange={setPaneBusy} onMemberRead={setMember}
            onCancel={resetTask} onSaved={() => saved('Personelin hizmet seçimleri kaydedildi.')} />}
          {task === 'hours' && <StaffHoursEditor key={revision} memberId={memberId} embedded post={post}
            onDirtyChange={reportDirty} onBusyChange={setPaneBusy} onMemberRead={setMember}
            onCancel={() => { reportDirty(false); onBack() }} onSaved={() => saved('Personelin çalışma saatleri kaydedildi.')} />}
        </>}
        {!loading && task === 'information' && <section className={styles.stateSection} aria-labelledby="member-state-title">
          <h3 id="member-state-title">Personel durumu</h3>
          <p>{member.isActive ? 'Pasifleştirme kaydı silmez.' : 'Aktifleştirme personel kaydını yeniden kullanılabilir yapar.'} Giriş hesapları etkilenmez.</p>
          {!confirmStatus && <button ref={statusButton} type="button" disabled={blocked} onClick={() => {
            if (discard()) { setConfirmStatus(true); setNotice(''); refresh() }
          }}>{member.isActive ? 'Pasifleştir' : 'Aktifleştir'}</button>}
          {confirmStatus && <fieldset className={styles.confirmation} disabled={statusBusy} aria-label="Personel durum değişikliği onayı">
            <legend>Personeli {member.isActive ? 'pasifleştir' : 'aktifleştir'}</legend>
            <p><strong>{member.name}</strong> {member.isActive ? 'pasif' : 'aktif'} olarak işaretlenecek. Kayıt silinmez. Giriş hesabı ve açık oturumlar etkilenmez.</p>
            <div className={styles.actions}><button ref={confirm} type="button" disabled={stale} onClick={() => { void changeStatus() }}>{statusBusy ? 'İşlem sürüyor…' : 'Durumu değiştir'}</button>
              <button type="button" onClick={() => { restoreStatus.current = true; setConfirmStatus(false); setError(''); refresh() }}>Vazgeç</button>
              {stale && <button type="button" onClick={reload}>Güncel kaydı yükle</button>}</div>
          </fieldset>}
        </section>}
      </div>
    </>}
  </>
}
