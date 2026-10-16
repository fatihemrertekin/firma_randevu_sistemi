import { useEffect, useRef, useState, type RefObject } from 'react'
import { useLocation, useNavigate } from 'react-router'
import type { postWithCsrf } from '../../app/api'
import { readPage, readPageSize, resolveRoute, sectionPaths } from '../../app/routes'
import { useCompletedNavigation } from '../../app/CompletedNavigation'
import { useNavigationChange } from '../../app/NavigationEvents'
import NavigationLink from '../../app/NavigationLink'
import ErrorMessage from '../../components/ErrorMessage'
import Pagination from '../../components/Pagination'
import AppointmentChoices from './AppointmentChoices'
import AppointmentEditor from './AppointmentEditor'
import AppointmentRecord from './AppointmentRecord'
import { appointmentEndpoint, appointmentSearch, AppointmentError, businessDay, formatDay, formatTime, isDay, isId, nextDay, readAppointmentPage, signal, statusLabels, type Appointment, type AppointmentPage } from './appointmentsApi'
import styles from './Appointments.module.css'

type Props = { post: typeof postWithCsrf; onDirtyChange: (value: boolean) => void; onBusyChange: (value: boolean) => void; onUnauthorized: () => void;
  headingRef?: RefObject<HTMLHeadingElement | null> }
function Arrow({ right = false }: { right?: boolean }) {
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={right ? 'm9 6 6 6-6 6' : 'm15 6-6 6 6 6'} /></svg>
}
export default function Appointments({ post, onDirtyChange, onBusyChange, onUnauthorized, headingRef }: Props) {
  const location = useLocation(), navigate = useNavigate(), complete = useCompletedNavigation(), route = resolveRoute(location.pathname)
  const query = new URLSearchParams(location.search), requestedDay = query.get('tarih') ?? '', day = isDay(requestedDay) ? requestedDay : businessDay()
  const requestedStaff = query.get('calisan'), staff = isId(requestedStaff) ? requestedStaff : ''
  const page = readPage(location.search), pageSize = readPageSize(location.search)
  const search = appointmentSearch(day, staff, page, pageSize), id = route.kind === 'management' ? route.id : undefined
  const creating = route.kind === 'management' && route.create, moving = route.kind === 'management' && route.move
  const [data, setData] = useState<AppointmentPage | null>(null), [loading, setLoading] = useState(true), [error, setError] = useState('')
  const [revision, setRevision] = useState(0), [notice, setNotice] = useState(''), [retryUntil, setRetryUntil] = useState(0)
  const ownHeading = useRef<HTMLHeadingElement>(null), heading = headingRef ?? ownHeading
  useNavigationChange(next => {
    setError('')
    if (next.pathname === sectionPaths.appointments) { setLoading(true); setData(null) }
    else setNotice('')
  })
  useEffect(() => () => { onDirtyChange(false); onBusyChange(false) }, [onDirtyChange, onBusyChange])
  useEffect(() => { if (!retryUntil) return; const timer = setTimeout(() => setRetryUntil(0), Math.max(0, retryUntil - Date.now())); return () => clearTimeout(timer) }, [retryUntil])
  useEffect(() => {
    if (creating || id) return
    const controller = new AbortController(), parameters = new URLSearchParams({ date: day, page: String(page), pageSize: String(pageSize) })
    if (staff) parameters.set('staffMemberId', staff)
    void fetch(appointmentEndpoint + '?' + parameters, { cache: 'no-store', signal: signal(controller) }).then(readAppointmentPage).then(value => {
      if (controller.signal.aborted) return
      if (value.date !== day || value.pageSize !== pageSize) throw new AppointmentError(500)
      setData(value); setError('')
      if (value.page !== page) complete(sectionPaths.appointments + appointmentSearch(day, staff, value.page, pageSize), { replace: true })
    }).catch((problem: unknown) => {
      if (controller.signal.aborted) return
      const failure = problem instanceof AppointmentError ? problem : new AppointmentError(500)
      setError(failure.message); setData(null); setRetryUntil(failure.retryAfter)
      if (failure.status === 401) onUnauthorized()
    }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [creating, id, day, staff, page, pageSize, revision, location.key, complete, onUnauthorized])
  function filter(next = day, worker = staff, nextPage = 1, size = pageSize) {
    setLoading(true); setData(null); setError(''); setNotice('')
    const nextSearch = appointmentSearch(next, worker, nextPage, size)
    if (location.pathname !== sectionPaths.appointments || location.search !== nextSearch) void navigate(sectionPaths.appointments + nextSearch)
    else setRevision(value => value + 1)
  }
  function saved(value: Appointment) {
    setNotice('Randevu kaydedildi.'); setLoading(true); setData(null)
    complete(sectionPaths.appointments + appointmentSearch(businessDay(new Date(value.startsAt)), staff && staff === value.staffMemberId ? staff : '', 1, pageSize), { replace: true })
  }
  const cancel = () => { void navigate(sectionPaths.appointments + search) }
  const groups = new Map<string, NonNullable<AppointmentPage>['items']>()
  for (const item of data?.items ?? []) groups.set(item.staffMemberId, [...(groups.get(item.staffMemberId) ?? []), item])
  if (creating || id) return <section className={styles.surface}>
    <NavigationLink to={sectionPaths.appointments + search} className={styles.back}>Randevulara dön</NavigationLink>
    {creating ? <><h1 ref={heading} tabIndex={-1}>Yeni randevu</h1><AppointmentEditor initialDay={day} post={post} onSaved={saved}
      onDirtyChange={onDirtyChange} onBusyChange={onBusyChange} onUnauthorized={onUnauthorized} onReload={() => {}} onCancel={cancel} /></>
      : id && <AppointmentRecord key={id} id={id} moving={!!moving} initialDay={day} search={search} post={post} onSaved={saved} headingRef={heading}
        onDirtyChange={onDirtyChange} onBusyChange={onBusyChange} onUnauthorized={onUnauthorized} onCancel={cancel} />}
  </section>
  return <section className={styles.surface} aria-labelledby="appointments-title" aria-busy={loading}>
    <div className={styles.titleRow}><div><h1 id="appointments-title" ref={heading} tabIndex={-1}>Randevular</h1><p>Günlük randevuları çalışanlara göre görüntüleyin ve yönetin.</p></div>
      <NavigationLink className={styles.primary} to={sectionPaths.appointments + '/yeni' + search} disabled={loading}>Yeni randevu</NavigationLink></div>
    <div className={styles.toolbar}><div className={styles.dayActions}>
      <button type="button" aria-label="Önceki gün" disabled={loading || !!retryUntil || nextDay(day, -1) === day} onClick={() => filter(nextDay(day, -1))}><Arrow /></button>
      <button type="button" aria-label="Sonraki gün" disabled={loading || !!retryUntil || nextDay(day, 1) === day} onClick={() => filter(nextDay(day, 1))}><Arrow right /></button>
    </div><div className={styles.dateField}><label htmlFor="appointment-day">Tarih</label><input id="appointment-day" type="date" value={day} min="0001-01-02" max="9999-12-30" disabled={loading || !!retryUntil}
      onChange={event => { if (isDay(event.target.value)) filter(event.target.value) }} /></div>
      <button type="button" disabled={loading || !!retryUntil} onClick={() => filter(businessDay())}>Bugün</button>
      <AppointmentChoices kind="staff" id="appointment-filter" label="Çalışan" value={staff} includeInactive optional disabled={loading || !!retryUntil}
        onUnauthorized={onUnauthorized} onChange={item => filter(day, item?.id ?? '')} />
    </div>
    <h2 className={styles.dayTitle}>{formatDay(day)}</h2>
    {notice && <p role="status">{notice}</p>}
    <ErrorMessage message={error} />
    {loading && <p role="status">Randevular yükleniyor…</p>}
    {!loading && data && <>
      {data.items.length === 0 ? <p>{staff ? 'Bu çalışan için seçili günde randevu yok.' : 'Seçili günde randevu yok.'}</p> : <>
        {(data.hasMore || page > 1) && <p>Çalışan bölümleri yalnız bu sayfadaki kayıtları gösterir. Günün devamı için sayfalar arasında ilerleyin veya çalışan seçin.</p>}
        <div className={styles.groups}>{[...groups].sort(([leftId, left], [rightId, right]) => left[0].staffName.localeCompare(right[0].staffName, 'tr') || leftId.localeCompare(rightId)).map(([memberId, rows]) => <section className={styles.staff} key={memberId} aria-labelledby={`staff-${memberId}`}>
          <header className={styles.staffHeader}><h3 id={`staff-${memberId}`}>{rows[0].staffName}</h3><p>{data.hasMore || page > 1 ? 'Bu sayfadaki randevular' : 'Günlük randevular'}</p></header>
          <ol className={styles.appointments}>{rows.map(item => <li key={item.id} className={styles.appointment}>
            <div className={styles.time}><strong><time dateTime={item.startsAt}>{formatTime(item.startsAt)}</time></strong><time dateTime={item.endsAt}>{formatTime(item.endsAt)}</time></div>
            <div className={styles.appointmentContent}><strong className={styles.customer}>{item.customerName}</strong><span className={styles.service}>{item.serviceName}</span>
              <span className={styles.status} data-status={item.status}>{statusLabels[item.status]}</span>
              <NavigationLink to={`${sectionPaths.appointments}/${item.id}${search}`} className={styles.details} aria-label={`${item.customerName}, ${formatTime(item.startsAt)} randevusunun ayrıntıları`}>Ayrıntılar <Arrow right /></NavigationLink>
            </div>
          </li>)}</ol>
        </section>)}</div>
      </>}
    </>}
    <div className={styles.footer}>{(error || (data?.items.length ?? 0) > 0) && <button type="button" disabled={loading || !!retryUntil} onClick={() => filter(day, staff, page)}>Listeyi yenile</button>}
      {data && <Pagination label="Randevu sayfaları" page={data.page} pageSize={pageSize} totalCount={data.totalCount} itemCount={data.items.length}
        hasNext={data.hasMore} disabled={loading || !!error || !!retryUntil} onPageChange={next => filter(day, staff, next)} onPageSizeChange={size => filter(day, staff, 1, size)} />}
    </div>
  </section>
}
