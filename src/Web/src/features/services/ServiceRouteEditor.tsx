import { useEffect, useState } from 'react'
import ServiceEditor from './ServiceEditor'
import ErrorMessage from '../../components/ErrorMessage'
import { readService, ServiceRequestError, serviceFailure, type Service, type ServicePost } from './servicesApi'

type Props = { id?: string; post: ServicePost; onSaved: () => void; onCancel: () => void;
  onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void }
export default function ServiceRouteEditor({ id, onBusyChange, onDirtyChange, ...props }: Props) {
  const [service, setService] = useState<Service | null>(null)
  const [loading, setLoading] = useState(!!id), [revision, setRevision] = useState(0)
  const [error, setError] = useState('')
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
      }).finally(() => { if (!controller.signal.aborted) { setLoading(false); onBusyChange(false) } })
    return () => { controller.abort(); onBusyChange(false) }
  }, [id, revision, onBusyChange])
  if (loading) return <p role="status">Hizmet bilgileri yükleniyor…</p>
  if (id && !service) return <>
    <ErrorMessage message={error} />
    <button type="button" onClick={() => { setLoading(true); setService(null); setError(''); setRevision(value => value + 1) }}>Güncel hizmeti yükle</button>
    <button type="button" onClick={props.onCancel}>Hizmet listesine dön</button>
  </>
  return <ServiceEditor {...props} service={service} onDirtyChange={onDirtyChange} onBusyChange={onBusyChange} />
}
