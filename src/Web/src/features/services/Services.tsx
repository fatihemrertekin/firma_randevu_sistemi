import { useEffect, useRef, useState, type RefObject } from 'react'
import { useLocation, useNavigate } from 'react-router'
import ErrorMessage from '../../components/ErrorMessage'
import { formatTryPrice } from '../../app/money'
import { pageSearch, readPage, resolveRoute, sectionPaths } from '../../app/routes'
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
  const route = resolveRoute(location.pathname), page = readPage(location.search)
  const editing = route.kind === 'management' && route.section === 'services' && (route.create || !!route.id)
  const id = route.kind === 'management' ? route.id : undefined
  const [revision, setRevision] = useState(0), [data, setData] = useState<ServicePage | null>(null)
  const [loading, setLoading] = useState(true), [error, setError] = useState(''), [notice, setNotice] = useState('')
  const ownHeading = useRef<HTMLHeadingElement>(null), heading = headingRef ?? ownHeading
  useNavigationChange(next => {
    setError('')
    if (next.pathname === sectionPaths.services) { setLoading(true); setRevision(value => value + 1) }
    else setNotice('')
  })
  useEffect(() => {
    if (editing) return
    const controller = new AbortController()
    void fetch(`/api/services/?page=${page}`, { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) })
      .then(readServicePage).then(value => { if (!controller.signal.aborted) setData(value) })
      .catch((problem: unknown) => {
        if (!controller.signal.aborted) { setData(null); setError(problem instanceof ServiceRequestError ? problem.message : 'Hizmet listesi alınamadı. Yeniden dene.') }
      }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [page, revision, editing])
  function load(next = page) {
    setLoading(true); setError(''); setNotice(''); setRevision(value => value + 1)
    if (next !== page) void navigate(sectionPaths.services + pageSearch(next))
  }
  function finish(message: string, next = page) {
    setNotice(message); setLoading(true)
    completed(sectionPaths.services + pageSearch(next), { replace: true })
  }
  if (editing) return <ServiceRouteEditor key={id ?? 'new'} id={id} post={post} headingRef={heading}
    onDirtyChange={onDirtyChange} onBusyChange={onBusyChange}
    onCancel={() => { void navigate(sectionPaths.services + pageSearch(page)) }}
    onSaved={() => finish('Hizmet kaydedildi.', id ? page : 1)} onDeleted={message => finish(message, 1)} />
  return <section className={styles.surface} aria-labelledby="services-title" aria-busy={loading}>
    <div className={styles.titleRow}>
      <div><h1 id="services-title" ref={heading} tabIndex={-1}>Hizmetler</h1><p>Hizmetlerin adını, süresini ve fiyatını yönetin.</p></div>
      <NavigationLink to={sectionPaths.services + '/yeni' + pageSearch(page)} className={styles.primary} disabled={loading}>Yeni hizmet</NavigationLink>
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
        <NavigationLink to={`${sectionPaths.services}/${encodeURIComponent(service.id)}/duzenle${pageSearch(page)}`}
          disabled={loading} aria-label={`${service.name} hizmetini düzenle`}>Düzenle</NavigationLink>
      </li>)}</ul>
    </>}
    <div className={styles.footer}>
      <button type="button" disabled={loading} onClick={() => load()}>Listeyi yenile</button>
      <div className={styles.pagination}>
        <button type="button" disabled={loading || page === 1} onClick={() => load(page - 1)} aria-label="Önceki sayfa">Önceki</button>
        <span>Sayfa {data?.page ?? page}</span>
        <button type="button" disabled={loading || !data?.hasMore} onClick={() => load(page + 1)} aria-label="Sonraki sayfa">Sonraki</button>
      </div>
    </div>
  </section>
}
