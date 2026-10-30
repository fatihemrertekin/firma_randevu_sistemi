import { useEffect, useState } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { readPage, sectionPaths } from '../../app/routes'
import { formatTryPrice } from '../../app/money'
import ErrorMessage from '../../components/ErrorMessage'
import { readSubscription, SubscriptionError, subscriptionFailure, type Period, type Subscription as SubscriptionData } from './subscriptionApi'
import styles from './Subscription.module.css'

const date = (value: string) => new Intl.DateTimeFormat('tr-TR', { dateStyle: 'long', timeZone: 'Europe/Istanbul' }).format(new Date(value + 'T12:00:00Z'))
function endDate(value: string) { const end = new Date(value + 'T12:00:00Z'); end.setUTCDate(end.getUTCDate() - 1); return date(end.toISOString().slice(0, 10)) }
const status = (period: Period) => `${period.paymentState === 'Paid' ? 'Ödendi' : period.paymentState === 'Partial' ? 'Kısmen ödendi' : 'Ödeme bekleniyor'}${period.overdue ? ' · Vadesi geçti' : ''}`
function Facts({ period }: { period: Period }) {
  return <dl className={styles.facts}>
    <div><dt>Dönem bedeli</dt><dd>{formatTryPrice(period.amountTry)}</dd></div>
    <div><dt>Son ödeme tarihi</dt><dd>{date(period.dueOn)}</dd></div>
    <div><dt>Doğrulanan ödeme</dt><dd>{formatTryPrice(period.paidTry)}</dd></div>
    <div><dt>Kalan borç</dt><dd>{formatTryPrice(period.outstandingTry)}</dd></div>
  </dl>
}
export default function Subscription({ onUnauthorized }: { onUnauthorized: () => void }) {
  const location = useLocation(), navigate = useNavigate(), page = readPage(location.search)
  const [data, setData] = useState<SubscriptionData | null>(null), [loading, setLoading] = useState(true)
  const [error, setError] = useState(''), [retry, setRetry] = useState(0), [revision, setRevision] = useState(0), [forbidden, setForbidden] = useState(false)
  useEffect(() => {
    if (!retry) return
    const timer = setTimeout(() => setRetry(value => Math.max(0, value - 1)), 1000)
    return () => clearTimeout(timer)
  }, [retry])
  useEffect(() => {
    const controller = new AbortController()
    fetch(`/api/subscription?page=${page}&pageSize=12`, { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) })
      .then(readSubscription).then(value => { if (!controller.signal.aborted) setData(value) })
      .catch((problem: unknown) => {
        if (controller.signal.aborted) return
        const status = problem instanceof SubscriptionError ? problem.status : 500
        setData(null); setError(subscriptionFailure(status)); setForbidden(status === 403)
        if (problem instanceof SubscriptionError) setRetry(problem.wait)
        if (status === 401) onUnauthorized()
      }).finally(() => {
        if (controller.signal.aborted) return
        setLoading(false)
      })
    return () => controller.abort()
  }, [page, revision, onUnauthorized])
  function load(next = page) {
    if (loading || retry) return
    setData(null); setError(''); setLoading(true); setForbidden(false)
    if (next !== page) { void navigate(sectionPaths.subscription + (next > 1 ? `?sayfa=${next}` : '')) }
    else setRevision(value => value + 1)
  }
  const current = data?.currentPeriod
  return <section className={styles.surface} aria-label="Abonelik bilgileri" aria-busy={loading}>
    <div className={styles.heading}><p>Bu bilgiler yalnız görüntülenebilir. Ödeme kaydını hizmet sağlayıcınız doğrular.</p>
      {!forbidden && <button type="button" disabled={loading || retry > 0} onClick={() => load(error ? 1 : page)}>{error ? 'Yeniden dene' : 'Bilgileri yenile'}</button>}
    </div>
    {loading && <p role="status">Abonelik bilgileri yükleniyor…</p>}
    <ErrorMessage message={error} />
    {retry > 0 && <p role="status">{retry} saniye sonra yeniden deneyebilirsiniz.</p>}
    {!loading && !error && data && <>
      {!data.recorded ? <><h2>Henüz abonelik kaydı yok</h2><p>Abonelik bilgileriniz hizmet sağlayıcınız tarafından hazırlanıyor.</p></> :
        current ? <><h2>Mevcut dönem</h2><p>{date(current.startsOn)} – {endDate(current.endsOn)}</p><p className={styles.state}>{status(current)}</p><Facts period={current} /></> :
          <><h2>Bugün için tanımlanmış dönem yok</h2><p>Dönem kayıtlarını aşağıdan inceleyebilirsiniz. Güncel dönem için hizmet sağlayıcınızla iletişime geçin.</p></>}
      <p className={styles.note}>{data.restrictionsApplied ? 'Abonelik nedeniyle kullanım salt okunur. Ödeme durumunuz için hizmet sağlayıcınızla iletişime geçin.' : 'Şu anda abonelik nedeniyle erişim kısıtlaması uygulanmıyor.'}</p>
      {data.recorded && <details className={styles.history} open={page > 1}>
        <summary>Dönem kayıtları</summary>
        <h2>Kayıtlı dönemler</h2>
        <ul>{data.periods.map(period => <li key={period.id}>
          <h3>{date(period.startsOn)} – {endDate(period.endsOn)}</h3><p className={styles.state}>{status(period)}</p><Facts period={period} />
        </li>)}</ul>
        {data.totalCount > 12 && <nav className={styles.paging} aria-label="Abonelik dönem sayfaları">
          <button disabled={loading || retry > 0 || data.page === 1} onClick={() => load(data.page - 1)}>Önceki sayfa</button>
          <span>Sayfa {data.page} / {Math.ceil(data.totalCount / 12)}</span>
          <button disabled={loading || retry > 0 || !data.hasMore} onClick={() => load(data.page + 1)}>Sonraki sayfa</button>
        </nav>}
      </details>}
      <p className={styles.muted}>Bilgilerin tarihi: {date(data.asOf)} · Türkiye saati</p>
    </>}
  </section>
}
