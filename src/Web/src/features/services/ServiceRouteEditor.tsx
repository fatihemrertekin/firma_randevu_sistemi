import { useCallback, useEffect, useRef, useState, type RefObject } from 'react'
import ServiceEditor from './ServiceEditor'
import ServiceActions from './ServiceActions'
import NavigationLink from '../../app/NavigationLink'
import { pageSearch, readPage, sectionPaths } from '../../app/routes'
import { useLocation } from 'react-router'
import ErrorMessage from '../../components/ErrorMessage'
import { readService, ServiceRequestError, serviceFailure, type Service, type ServicePost } from './servicesApi'
import styles from './Services.module.css'

type Props = { id?: string; post: ServicePost; onSaved: () => void; onCancel: () => void; onDeleted: (message: string) => void;
  onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void; headingRef?: RefObject<HTMLHeadingElement | null> }
export default function ServiceRouteEditor({ id, post, onSaved, onCancel, onDeleted, onBusyChange, onDirtyChange, headingRef }: Props) {
  const location = useLocation(), ownHeading = useRef<HTMLHeadingElement>(null), heading = headingRef ?? ownHeading
  const [service, setService] = useState<Service | null>(null)
  const [loading, setLoading] = useState(!!id), [revision, setRevision] = useState(0), [editorRevision, setEditorRevision] = useState(0)
  const [error, setError] = useState(''), [notice, setNotice] = useState('')
  const [dirty, setDirty] = useState(false), [busy, setBusy] = useState(!!id)
  const [actionBusy, setActionBusy] = useState(false), [actionOpen, setActionOpen] = useState(false), [requiresReload, setRequiresReload] = useState(false)
  const reportBusy = useCallback((value: boolean) => { setBusy(value); onBusyChange(value) }, [onBusyChange])
  const reportActionBusy = useCallback((value: boolean) => { setActionBusy(value); reportBusy(value) }, [reportBusy])
  const reportDirty = useCallback((value: boolean) => { setDirty(value); onDirtyChange(value) }, [onDirtyChange])
  useEffect(() => () => onDirtyChange(false), [onDirtyChange])
  useEffect(() => {
    if (!id) return
    const controller = new AbortController()
    onBusyChange(true)
    void fetch(`/api/services/${id}`, { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) })
      .then(readService).then(value => {
        if (value.id !== id) throw new ServiceRequestError(500)
        if (!controller.signal.aborted) setService(value)
      }).catch((problem: unknown) => {
        if (!controller.signal.aborted) setError(problem instanceof ServiceRequestError ? problem.message : serviceFailure(500))
      }).finally(() => { if (!controller.signal.aborted) { setLoading(false); reportBusy(false) } })
    return () => { controller.abort(); reportBusy(false) }
  }, [id, revision, reportBusy, onBusyChange])
  function reloaded(current: Service, message = '') {
    setService(current); setEditorRevision(value => value + 1); setRequiresReload(false); setActionOpen(false); setNotice(message)
  }
  return <section className={styles.surface} aria-labelledby="service-title">
    <NavigationLink to={sectionPaths.services + pageSearch(readPage(location.search))} className={styles.back} disabled={busy}>Listeye dön</NavigationLink>
    <h1 id="service-title" ref={heading} tabIndex={-1}>{id ? service?.name ?? 'Hizmeti düzenle' : 'Yeni hizmet'}</h1>
    {id && service && <p>{service.isActive ? 'Aktif' : 'Pasif'}</p>}
    {loading ? <p role="status">Hizmet bilgileri yükleniyor…</p> : id && !service ? <>
      <ErrorMessage message={error} />
      <button type="button" onClick={() => { setLoading(true); setService(null); setError(''); setRevision(value => value + 1) }}>Güncel hizmeti yükle</button>
    </> : <>
      {notice && <p role="status">{notice}</p>}
      <ServiceEditor key={`editor-${editorRevision}`} service={service} post={post} onSaved={onSaved} onCancel={onCancel}
        onDirtyChange={reportDirty} onBusyChange={reportBusy} disabled={actionBusy || actionOpen} requiresReload={requiresReload} onReloaded={reloaded} />
      {id && service && <ServiceActions key={`actions-${editorRevision}`} service={service} post={post}
        blocked={dirty || busy || requiresReload} dirty={dirty} onBusyChange={reportActionBusy}
        onConfirmChange={setActionOpen} onRequiresReload={() => setRequiresReload(true)}
        onChanged={reloaded} onDeleted={onDeleted} />}
    </>}
  </section>
}
