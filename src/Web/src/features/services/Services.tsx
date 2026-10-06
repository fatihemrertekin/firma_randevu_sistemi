import { useEffect, useRef, useState, type RefObject } from 'react'
import { useLocation, useNavigate } from 'react-router'
import ErrorMessage from '../../components/ErrorMessage'
import { formatTryPrice } from '../../app/money'
import { pageSearch, readPage, readPageSize, resolveRoute, sectionPaths } from '../../app/routes'
import Pagination from '../../components/Pagination'
import NavigationLink from '../../app/NavigationLink'
import { useCompletedNavigation } from '../../app/CompletedNavigation'
import { useNavigationChange } from '../../app/NavigationEvents'
import ServiceRouteEditor from './ServiceRouteEditor'
import { ServiceRequestError, readServicePage, type ServicePage, type ServicePost } from './servicesApi'
import styles from './Services.module.css'

type Props = { post: ServicePost; onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void;
  headingRef?: RefObject<HTMLHeadingElement | null> }
export default function Services({ post, onDirtyChange, onBusyChange, headingRef }: Props) {
  const location = useLocation(), navigate = useNavigate(), completed = useCompletedNavigation()
  const route = resolveRoute(location.pathname), page = readPage(location.search), pageSize = readPageSize(location.search)
  const editing = route.kind === 'management' && route.section === 'services' && (route.create || !!route.id)
  const id = route.kind === 'management' ? route.id : undefined
  const [revision, setRevision] = useState(0), [data, setData] = useState<ServicePage | null>(null)
  const [loading, setLoading] = useState(true), [error, setError] = useState(''), [notice, setNotice] = useState('')
  const ownHeading = useRef<HTMLHeadingElement>(null), heading = headingRef ?? ownHeading
  useNavigationChange(next => {
    setError('')
    if (next.pathname === sectionPaths.services) setLoading(true)
    else setNotice('')
  })
  useEffect(() => {
    if (editing) return
    const controller = new AbortController()
    void fetch(`/api/services/?page=${page}${pageSize !== 20 ? `&pageSize=${pageSize}` : ''}`, { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) })
      .then(readServicePage).then(value => { if (!controller.signal.aborted) {
        setData(value)
        if (value.page !== page) completed(sectionPaths.services + pageSearch(value.page, pageSize), { replace: true })
      } })
      .catch((problem: unknown) => {
        if (!controller.signal.aborted) { setData(null); setError(problem instanceof ServiceRequestError ? problem.message : 'Hizmet listesi alınamadı. Yeniden dene.') }
      }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [page, pageSize, revision, editing, completed, location.key])
  function load(next = page, size = pageSize) {
    setLoading(true); setError(''); setNotice('')
    if (next !== page || size !== pageSize) void navigate(sectionPaths.services + pageSearch(next, size))
    else setRevision(value => value + 1)
  }
  function finish(message: string, next = page) {
    setNotice(message); setLoading(true)
    completed(sectionPaths.services + pageSearch(next, pageSize), { replace: true })
  }
  if (editing) return <ServiceRouteEditor key={id ?? 'new'} id={id} post={post} headingRef={heading}
    onDirtyChange={onDirtyChange} onBusyChange={onBusyChange}
    onCancel={() => { void navigate(sectionPaths.services + pageSearch(page, pageSize)) }}
    onSaved={() => finish('Hizmet kaydedildi.', id ? page : 1)} onDeleted={message => finish(message, 1)} />
  return <section className={styles.surface} aria-labelledby="services-title" aria-busy={loading}>
    <div className={styles.titleRow}>
      <div><h1 id="services-title" ref={heading} tabIndex={-1}>Hizmetler</h1><p>Hizmetlerin adını, süresini ve fiyatını yönetin.</p></div>
      <NavigationLink to={sectionPaths.services + '/yeni' + pageSearch(page, pageSize)} className={styles.primary} disabled={loading}>Yeni hizmet</NavigationLink>
    </div>
    <ErrorMessage message={error} />
    {notice && <p role="status">{notice}</p>}
    {loading && <><p role="status">Hizmetler yükleniyor…</p>{!data && <div aria-hidden="true" className={styles.skeleton} />}</>}
    {!loading && data?.items.length === 0 && <p>Bu sayfada hizmet yok. Yeni hizmet ekleyerek başlayabilirsin.</p>}
    {data && data.items.length > 0 && <>
      <div className={styles.columnLabels} aria-hidden="true"><span>Hizmet</span><span>Süre</span><span>Fiyat</span><span>Durum</span></div>
      <ul className={styles.list}>{data.items.map(service => <li key={service.id} className={styles.row}>
        <strong>{service.name}</strong>
        <div className={styles.metrics}><span>{service.durationMinutes} dk</span><span className={styles.price}>{formatTryPrice(service.price)}</span></div>
        <span className={styles.status}>{service.isActive ? 'Aktif' : 'Pasif'}</span>
        <NavigationLink to={`${sectionPaths.services}/${encodeURIComponent(service.id)}/duzenle${pageSearch(page, pageSize)}`}
          disabled={loading} aria-label={`${service.name} hizmetini düzenle`}>Düzenle</NavigationLink>
      </li>)}</ul>
    </>}
    <div className={styles.footer}>
      {(error || (data?.items.length ?? 0) > 0) && <button type="button" disabled={loading} onClick={() => load()}>Listeyi yenile</button>}
      <Pagination label="Hizmet sayfaları" page={page} pageSize={pageSize} totalCount={data?.totalCount} itemCount={data?.items.length ?? 0}
        hasNext={data?.hasMore ?? false} disabled={loading || !!error} onPageChange={next => load(next)} onPageSizeChange={size => load(1, size)} />
    </div>
  </section>
}
