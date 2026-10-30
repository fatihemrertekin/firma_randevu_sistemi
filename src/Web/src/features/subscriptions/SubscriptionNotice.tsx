import { useEffect, useState } from 'react'
import { retrySeconds } from '../../app/weeklyHoursApi'
import styles from './Subscription.module.css'

export default function SubscriptionNotice({ onChange, onUnauthorized }: { onChange: (value: boolean | null) => void; onUnauthorized: () => void }) {
  const [restricted, setRestricted] = useState<boolean | null>(null), [error, setError] = useState(''), [loading, setLoading] = useState(true)
  const [revision, setRevision] = useState(0), [retry, setRetry] = useState(0)
  useEffect(() => {
    const refresh = () => { setLoading(true); setRevision(value => value + 1) }
    window.addEventListener('subscription-access-changed', refresh)
    return () => window.removeEventListener('subscription-access-changed', refresh)
  }, [])
  useEffect(() => {
    if (!retry) return
    const timer = setTimeout(() => setRetry(value => Math.max(0, value - 1)), 1000)
    return () => clearTimeout(timer)
  }, [retry])
  useEffect(() => {
    const controller = new AbortController()
    fetch('/api/subscription/access', { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(7000)]) })
      .then(async response => {
        if (response.status === 401) { if (!controller.signal.aborted) onUnauthorized(); return }
        if (response.status === 429 && !controller.signal.aborted) setRetry(retrySeconds(response))
        if (!response.ok) throw new Error('access-unavailable')
        const value: unknown = await response.json()
        if (typeof value !== 'object' || value === null || !('restrictionsApplied' in value) || typeof value.restrictionsApplied !== 'boolean') throw new Error('access-invalid')
        if (!controller.signal.aborted) { setRestricted(value.restrictionsApplied); onChange(value.restrictionsApplied); setError('') }
      }).catch(() => {
        if (!controller.signal.aborted) { setRestricted(null); onChange(null); setError('Abonelik durumu doğrulanamadı. Kayıt işlemleri için yeniden kontrol edin; mevcut kayıtları görüntüleyebilir ve Excel alabilirsiniz.') }
      }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [revision, onChange, onUnauthorized])
  if (loading && restricted === null && !error) return <p role="status">Abonelik erişimi kontrol ediliyor…</p>
  if (!restricted && !error) return null
  return <aside className={styles.note} aria-label="Abonelik erişimi" aria-busy={loading}>
    <p role={error ? 'alert' : 'status'}>{error || 'Vadesi geçmiş abonelik borcu nedeniyle kullanım salt okunur. Yeni randevu ve değişiklik yapılamaz; mevcut randevuları görüntüleyebilir ve Excel alabilirsiniz.'}</p>
    <button type="button" disabled={loading || retry > 0} onClick={() => { setLoading(true); setRevision(value => value + 1) }}>Erişimi yeniden kontrol et</button>
    {retry > 0 && <p role="status">{retry} saniye sonra yeniden deneyebilirsiniz.</p>}
  </aside>
}
