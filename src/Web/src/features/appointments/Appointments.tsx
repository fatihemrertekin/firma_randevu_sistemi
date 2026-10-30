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
import WeeklyAppointments from './WeeklyAppointments'
import AppointmentExport from './AppointmentExport'
import SubscriptionNotice from '../subscriptions/SubscriptionNotice'
import { appointmentEndpoint, appointmentSearch, AppointmentError, businessDay, formatDay, formatTime, formatWeek, isDay, isId, nextDay, readAppointmentPage, readAppointmentWeekPage, signal, statusLabels, weekDates, type Appointment, type AppointmentPage, type AppointmentWeekPage } from './appointmentsApi'
import styles from './Appointments.module.css'

type Props = { post: typeof postWithCsrf; onDirtyChange: (value: boolean) => void; onBusyChange: (value: boolean) => void; onUnauthorized: () => void;
  headingRef?: RefObject<HTMLHeadingElement | null> }
type ListResult = { view: 'day'; page: AppointmentPage } | { view: 'week'; page: AppointmentWeekPage }
function Arrow({ right = false }: { right?: boolean }) {
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={right ? 'm9 6 6 6-6 6' : 'm15 6-6 6 6 6'} /></svg>
}
export default function Appointments({ post, onDirtyChange, onBusyChange, onUnauthorized, headingRef }: Props) {
  const location = useLocation(), navigate = useNavigate(), complete = useCompletedNavigation(), route = resolveRoute(location.pathname)
  const query = new URLSearchParams(location.search), requestedDay = query.get('tarih') ?? '', day = isDay(requestedDay) ? requestedDay : businessDay()
  const requestedStaff = query.get('calisan'), staff = isId(requestedStaff) ? requestedStaff : ''
  const weekly = query.get('gorunum') === 'hafta'
  const page = readPage(location.search), pageSize = readPageSize(location.search, weekly ? 10 : 20)
  const search = appointmentSearch(day, staff, page, pageSize, weekly), id = route.kind === 'management' ? route.id : undefined
  const creating = route.kind === 'management' && route.create, moving = route.kind === 'management' && route.move
  const [subscriptionReadOnly, setSubscriptionReadOnly] = useState<boolean | null>(null)
  const [result, setResult] = useState<ListResult | null>(null), [loading, setLoading] = useState(true), [error, setError] = useState('')
  const data = result?.view === (weekly ? 'week' : 'day') && result.page.date === day ? result.page : null
  const weekData = result?.view === 'week' && result.page.date === day ? result.page : null
  const [revision, setRevision] = useState(0), [notice, setNotice] = useState(''), [retryUntil, setRetryUntil] = useState(0)
  const ownHeading = useRef<HTMLHeadingElement>(null), heading = headingRef ?? ownHeading
  useNavigationChange(next => {
    setError('')
    if (next.pathname === sectionPaths.appointments) { setLoading(true); setResult(null) }
    else setNotice('')
  })
  useEffect(() => () => { onDirtyChange(false); onBusyChange(false) }, [onDirtyChange, onBusyChange])
  useEffect(() => { if (!retryUntil) return; const timer = setTimeout(() => setRetryUntil(0), Math.max(0, retryUntil - Date.now())); return () => clearTimeout(timer) }, [retryUntil])
  useEffect(() => {
    if (creating || id) return
    const controller = new AbortController(), parameters = new URLSearchParams({ date: day, page: String(page), pageSize: String(pageSize) })
    if (staff) parameters.set('staffMemberId', staff)
    void fetch(appointmentEndpoint + (weekly ? 'week' : '') + '?' + parameters, { cache: 'no-store', signal: signal(controller) })
      .then(async response => weekly ? { view: 'week' as const, page: await readAppointmentWeekPage(response) }
        : { view: 'day' as const, page: await readAppointmentPage(response) }).then(value => {
      if (controller.signal.aborted) return
      if (value.page.date !== day || value.page.pageSize !== pageSize) throw new AppointmentError(500)
      setResult(value); setError('')
      if (value.page.page !== page) complete(sectionPaths.appointments + appointmentSearch(day, staff, value.page.page, pageSize, weekly), { replace: true })
    }).catch((problem: unknown) => {
      if (controller.signal.aborted) return
      const failure = problem instanceof AppointmentError ? problem : new AppointmentError(500)
      setError(failure.message); setResult(null); setRetryUntil(failure.retryAfter)
      if (failure.status === 401) onUnauthorized()
    }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [creating, id, day, staff, page, pageSize, weekly, revision, location.key, complete, onUnauthorized])
  function filter(next = day, worker = staff, nextPage = 1, size = pageSize) {
    setLoading(true); setResult(null); setError(''); setNotice('')
    const nextSearch = appointmentSearch(next, worker, nextPage, size, weekly)
    if (location.pathname !== sectionPaths.appointments || location.search !== nextSearch) void navigate(sectionPaths.appointments + nextSearch)
    else setRevision(value => value + 1)
  }
  function saved(value: Appointment) {
    setNotice('Randevu kaydedildi.'); setLoading(true); setResult(null)
    complete(sectionPaths.appointments + appointmentSearch(businessDay(new Date(value.startsAt)), staff && staff === value.staffMemberId ? staff : '', 1, pageSize, weekly), { replace: true })
  }
  const cancel = () => { void navigate(sectionPaths.appointments + search) }
  const groups = new Map<string, NonNullable<AppointmentPage>['items']>()
  for (const item of data?.items ?? []) groups.set(item.staffMemberId, [...(groups.get(item.staffMemberId) ?? []), item])
  if (creating || id) return <section className={styles.surface}>
    <SubscriptionNotice onChange={setSubscriptionReadOnly} onUnauthorized={onUnauthorized} />
    <NavigationLink to={sectionPaths.appointments + search} className={styles.back}>Randevulara dön</NavigationLink>
    {creating ? <><h1 ref={heading} tabIndex={-1}>Yeni randevu</h1><AppointmentEditor readOnly={subscriptionReadOnly !== false} initialDay={day} post={post} onSaved={saved}
      onDirtyChange={onDirtyChange} onBusyChange={onBusyChange} onUnauthorized={onUnauthorized} onReload={() => {}} onCancel={cancel} /></>
      : id && <AppointmentRecord readOnly={subscriptionReadOnly !== false} key={id} id={id} moving={!!moving} initialDay={day} search={search} post={post} onSaved={saved} headingRef={heading}
        onDirtyChange={onDirtyChange} onBusyChange={onBusyChange} onUnauthorized={onUnauthorized} onCancel={cancel} />}
  </section>
  return <section className={styles.surface} aria-labelledby="appointments-title" aria-busy={loading}>
    <div className={styles.titleRow}><div><h1 id="appointments-title" ref={heading} tabIndex={-1}>Randevular</h1><p>{weekly ? 'Haftayı seçin; yalnız seçili günün randevularını görün.' : 'Günlük randevuları çalışanlara göre görüntüleyin ve yönetin.'}</p></div>
      <div className={styles.titleActions}><AppointmentExport key={day + staff} day={day} staff={staff} disabled={loading || !!retryUntil}
        onUnauthorized={onUnauthorized} onRateLimit={setRetryUntil} onDownloaded={() => setNotice('Excel dosyası indirmeye gönderildi.')} />
        <NavigationLink className={styles.primary} to={sectionPaths.appointments + '/yeni' + search} disabled={loading || subscriptionReadOnly !== false}>Yeni randevu</NavigationLink></div></div>
    <SubscriptionNotice onChange={setSubscriptionReadOnly} onUnauthorized={onUnauthorized} />
    <nav className={styles.views} aria-label="Randevu görünümü">
      <NavigationLink to={sectionPaths.appointments + appointmentSearch(day, staff)} aria-current={!weekly ? 'page' : undefined} disabled={loading || !!retryUntil}>Günlük</NavigationLink>
      <NavigationLink to={sectionPaths.appointments + appointmentSearch(day, staff, 1, 10, true)} aria-current={weekly ? 'page' : undefined} disabled={loading || !!retryUntil}>Haftalık</NavigationLink>
    </nav>
    <div className={styles.toolbar}>{weekly ? <>
      <div className={styles.weekActions}>
        <button type="button" aria-label="Önceki hafta" disabled={loading || !!retryUntil || nextDay(day, -7) === day || weekDates(nextDay(day, -7)).length !== 7} onClick={() => filter(nextDay(day, -7))}><Arrow /></button>
        <h2>{formatWeek(day)}</h2>
        <button type="button" aria-label="Sonraki hafta" disabled={loading || !!retryUntil || nextDay(day, 7) === day || weekDates(nextDay(day, 7)).length !== 7} onClick={() => filter(nextDay(day, 7))}><Arrow right /></button>
      </div><button type="button" disabled={loading || !!retryUntil} onClick={() => filter(businessDay())}>Bu hafta</button>
    </> : <><div className={styles.dayActions}>
      <button type="button" aria-label="Önceki gün" disabled={loading || !!retryUntil || nextDay(day, -1) === day} onClick={() => filter(nextDay(day, -1))}><Arrow /></button>
      <button type="button" aria-label="Sonraki gün" disabled={loading || !!retryUntil || nextDay(day, 1) === day} onClick={() => filter(nextDay(day, 1))}><Arrow right /></button>
    </div><div className={styles.dateField}><label htmlFor="appointment-day">Tarih</label><input id="appointment-day" type="date" value={day} min="0001-01-02" max="9999-12-30" disabled={loading || !!retryUntil}
      onChange={event => { if (isDay(event.target.value)) filter(event.target.value) }} /></div>
      <button type="button" disabled={loading || !!retryUntil} onClick={() => filter(businessDay())}>Bugün</button></>}
      <AppointmentChoices kind="staff" id="appointment-filter" label="Çalışan" value={staff} includeInactive optional disabled={loading || !!retryUntil}
        onUnauthorized={onUnauthorized} onChange={item => filter(day, item?.id ?? '')} />
    </div>
    {!weekly && <h2 className={styles.dayTitle}>{formatDay(day)}</h2>}
    {notice && <p role="status">{notice}</p>}
    <ErrorMessage message={error} />
    {loading && <p role="status">Randevular yükleniyor…</p>}
    {weekly && <WeeklyAppointments day={day} data={!loading ? weekData : null} disabled={loading || !!retryUntil} search={search} onDayChange={next => filter(next)} />}
    {!weekly && !loading && data && <>
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
