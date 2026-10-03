import { useEffect, useRef, useState } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import { formatBusinessDateTime } from '../../app/format'
import { auditEndpoint, AuditError, auditFailure, readAuditPage, type AuditPage, type Category } from './auditLogApi'
import styles from './AuditLog.module.css'

export default function AuditLog() {
  const [category, setCategory] = useState<Category>('all'), [history, setHistory] = useState<(string | null)[]>([null])
  const [revision, setRevision] = useState(0), [data, setData] = useState<AuditPage | null>(null)
  const [loading, setLoading] = useState(true), [error, setError] = useState(''), [retry, setRetry] = useState(0)
  const [locked, setLocked] = useState(false)
  const heading = useRef<HTMLHeadingElement>(null), focusAfterPage = useRef(false)
  const cursor = history[history.length - 1]
  useEffect(() => {
    if (!retry) return
    const timer = setTimeout(() => setRetry(value => Math.max(0, value - 1)), 1000)
    return () => clearTimeout(timer)
  }, [retry])
  useEffect(() => {
    const controller = new AbortController()
    const query = new URLSearchParams({ category })
    if (cursor) query.set('cursor', cursor)
    void fetch(`${auditEndpoint}?${query}`, { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) })
      .then(response => readAuditPage(response, category)).then(value => {
        if (!controller.signal.aborted) { setData(value); setError(''); setLocked(false) }
      }).catch((problem: unknown) => {
        if (controller.signal.aborted) return
        const status = problem instanceof AuditError ? problem.status : 500
        setError(auditFailure(status)); setLocked(true)
        if (problem instanceof AuditError) setRetry(problem.wait)
      }).finally(() => {
        if (controller.signal.aborted) return
        setLoading(false)
        if (focusAfterPage.current) { focusAfterPage.current = false; heading.current?.focus() }
      })
    return () => controller.abort()
  }, [category, cursor, revision])
  function reload() {
    if (loading || retry) return
    setLoading(true); setError(''); setHistory([null]); setRevision(value => value + 1)
  }
  function filter(next: string) {
    if (next !== 'all' && next !== 'definitions' && next !== 'security') return
    setCategory(next); setHistory([null]); setData(null); setError(''); setLocked(false); setLoading(true)
    setRevision(value => value + 1)
  }
  function page(next: boolean) {
    if (loading || locked || retry || (next ? !data?.nextCursor : history.length === 1)) return
    focusAfterPage.current = true; setLoading(true); setData(null); setError('')
    setHistory(value => next ? [...value.slice(0, -1), data?.cursor ?? null, data?.nextCursor ?? null] : value.slice(0, -1))
  }
  return <section aria-labelledby="audit-title" aria-busy={loading}>
    <h2 id="audit-title" ref={heading} className={styles.heading} tabIndex={-1}>İşlem geçmişi</h2>
    <p>Tanım, çalışan erişimi ve kurtarma kayıtları. Adlar ve hesap adresleri güncel kayıtlardan gösterilir.</p>
    <div className={styles.filters}>
      <div><label htmlFor="audit-category">Kayıt kategorisi</label>
        <select id="audit-category" className={styles.select} disabled={retry > 0 || locked} value={category} onChange={event => filter(event.target.value)}>
          <option value="all">Tümü</option><option value="definitions">Tanımlar</option><option value="security">Hesap ve güvenlik</option>
        </select>
      </div>
      <button type="button" className={styles.primary} disabled={loading || retry > 0} onClick={reload}>Listeyi yenile</button>
    </div>
    {loading && <p role="status">Kayıtlar yükleniyor…</p>}
    <ErrorMessage message={error} />
    {error && data && <p>Önceki sonuçlar gösteriliyor; liste yenilenemedi.</p>}
    {retry > 0 && <p role="status">{retry} saniye sonra tekrar deneyebilirsin.</p>}
    {!loading && !error && data?.items.length === 0 && <p>Bu kategoride işlem kaydı yok.</p>}
    {data && data.items.length > 0 && <ol className={styles.list} aria-label="Değişiklik kayıtları">
      {data.items.map(item => <li key={item.id} className={styles.row}>
        <time dateTime={item.occurredAt}>{formatBusinessDateTime(item.occurredAt)}</time>
        <div><strong>{item.module} · {item.action}</strong><dl>
          <div><dt>Yapan hesap</dt><dd>{item.actor}</dd></div>
          <div><dt>İlgili kayıt</dt><dd>{item.target}</dd></div>
        </dl></div>
      </li>)}
    </ol>}
    <div className={styles.actions} aria-label="Kayıt sayfaları">
      <button type="button" disabled={loading || locked || retry > 0 || history.length === 1} onClick={() => page(false)}>Önceki sayfa</button>
      <span>Sayfa {history.length} · İstanbul saati</span>
      <button type="button" disabled={loading || locked || retry > 0 || !data?.nextCursor} onClick={() => page(true)}>Sonraki sayfa</button>
    </div>
  </section>
}
