import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { postWithCsrf, requestErrorMessage } from '../../app/api'
import { parseTryPrice, formatTryPrice } from '../../app/money'
import { formatBusinessDateTime } from '../../app/format'
import styles from './SubscriptionOperator.module.css'

type Period = { id: string; startsOn: string; endsOn: string; dueOn: string; version: string; amountTry: string; paidTry: string; outstandingTry: string; overdue: boolean }
type Payment = { id: string; paidOn: string; recordedAt: string; amountTry: string; reference: string | null; reason: string | null; reversed: boolean }
type Data = { targetId: string; targetName: string; asOf: string; page: number; total: number; selectedPeriodId: string | null; periods: Period[]; payments: Payment[]; paymentsPage: number; paymentCount: number }
type Editor = { kind: 'period' | 'payment' | 'reverse' | 'correct'; payment?: Payment; key: string }
const money = formatTryPrice
const date = (value: string) => new Intl.DateTimeFormat('tr-TR', { dateStyle: 'long', timeZone: 'Europe/Istanbul' }).format(new Date(value + 'T12:00:00Z'))
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null
const validDate = (v: unknown) => typeof v === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(v) && Number.isFinite(Date.parse(v + 'T12:00:00Z'))
const validMoney = (v: unknown) => typeof v === 'string' && /^-?\d{1,10}\.\d{2}$/.test(v)
function dataResponse(value: unknown): Data {
  if (!record(value) || typeof value.targetId !== 'string' || typeof value.targetName !== 'string' || typeof value.asOf !== 'string' ||
    typeof value.page !== 'number' || typeof value.total !== 'number' || typeof value.paymentsPage !== 'number' || typeof value.paymentCount !== 'number' ||
    !(value.selectedPeriodId === null || typeof value.selectedPeriodId === 'string') || !Array.isArray(value.periods) || !Array.isArray(value.payments) ||
    value.periods.some(p => !record(p) || ['id','version'].some(k => typeof p[k] !== 'string') || ['startsOn','endsOn','dueOn'].some(k => !validDate(p[k])) || ['amountTry','paidTry','outstandingTry'].some(k => !validMoney(p[k])) || typeof p.overdue !== 'boolean') ||
    value.payments.some(p => !record(p) || typeof p.id !== 'string' || !validDate(p.paidOn) || typeof p.recordedAt !== 'string' || !Number.isFinite(Date.parse(p.recordedAt)) || !validMoney(p.amountTry) || typeof p.reversed !== 'boolean' || !(p.reference === null || typeof p.reference === 'string') || !(p.reason === null || typeof p.reason === 'string'))) throw new Error('Abonelik yanıtı doğrulanamadı. Yeniden yükleyin.')
  return value as Data
}
class SessionExpired extends Error {}
async function readData(page = 1, periodId?: string, paymentsPage = 1): Promise<Data> {
  const query = new URLSearchParams({ page: String(page), paymentsPage: String(paymentsPage), ...(periodId ? { periodId } : {}) })
  const response = await fetch('/api/subscription?' + query, { cache: 'no-store', signal: AbortSignal.timeout(15000) })
  if (response.status === 401) throw new SessionExpired()
  if (!response.ok) throw new Error(response.status === 403 ? 'Bu işlem için operatör yetkiniz yok.' : response.status === 429 ? 'Çok fazla istek. Bekleyip yeniden deneyin.' : 'Firma kayıtları okunamadı. Hedefi ve bağlantıyı kontrol edip yeniden deneyin.')
  return dataResponse(await response.json())
}

export default function SubscriptionOperator({ onExpired, onUnsaved, onBusy }: { onExpired: () => void; onUnsaved?: (value: boolean) => void; onBusy?: (value: boolean) => void }) {
  const [data, setData] = useState<Data | null>(null), [loading, setLoading] = useState(true), [error, setError] = useState(''), [notice, setNotice] = useState('')
  const [editor, setEditor] = useState<Editor | null>(null), [busy, setBusy] = useState(false), [formError, setFormError] = useState('')
  const [start, setStart] = useState(''), [end, setEnd] = useState(''), [due, setDue] = useState(''), [paid, setPaid] = useState(''), [amount, setAmount] = useState(''), [reference, setReference] = useState(''), [reason, setReason] = useState('')
  const [destination, setDestination] = useState(''), [dirty, setDirty] = useState(false), [uncertain, setUncertain] = useState(false)
  const [mustReload, setMustReload] = useState(false)
  const [formPeriods, setFormPeriods] = useState<Period[]>([]), [formPage, setFormPage] = useState(1)
  const pending = useRef(false), popup = useRef<HTMLDivElement>(null), opener = useRef<HTMLElement | null>(null), generation = useRef(0), savedRequest = useRef<{ path: string; body: object } | null>(null)
  useEffect(() => { onUnsaved?.(dirty || busy); return () => onUnsaved?.(false) }, [dirty, busy, onUnsaved])
  useEffect(() => { onBusy?.(busy); return () => onBusy?.(false) }, [busy, onBusy])
  const load = useCallback(async (page = 1, periodId?: string, paymentsPage = 1) => {
    const version = ++generation.current; setLoading(true); setError('')
    try {
      const result = await readData(page, periodId, paymentsPage); if (version === generation.current) { setData(result); return result }
    } catch (problem) { if (version === generation.current) { if (problem instanceof SessionExpired) onExpired(); setData(null); setError(problem instanceof Error ? problem.message : 'Kayıtlar okunamadı.') } }
    finally { if (version === generation.current) setLoading(false) }
  }, [onExpired])
  useEffect(() => {
    let active = true
    void readData().then(value => { if (active) setData(value) }).catch(problem => { if (active) { if (problem instanceof SessionExpired) onExpired(); setError(problem instanceof Error ? problem.message : 'Kayıtlar okunamadı.') } }).finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [onExpired])
  const selected = data?.periods.find(p => p.id === data.selectedPeriodId)
  const close = useCallback((force = false) => {
    if (pending.current) return
    if (!force && dirty && !window.confirm(uncertain ? 'İşlem sonucu belirsiz. Kapatmadan önce aynı işlemle yeniden deneyin veya kayıtlardan sonucu kontrol edin. Yine de kapatılsın mı?' : 'Kaydedilmemiş bilgiler silinsin mi?')) return
    popup.current?.hidePopover(); setEditor(null); setDirty(false); setUncertain(false); savedRequest.current = null; opener.current?.focus()
  }, [dirty, uncertain])
  useEffect(() => {
    if (!editor) return
    function unload(event: BeforeUnloadEvent) { if (dirty) event.preventDefault() }
    function escape(event: KeyboardEvent) { if (event.key === 'Escape') { event.preventDefault(); close() } }
    window.addEventListener('beforeunload', unload); window.addEventListener('keydown', escape)
    return () => { window.removeEventListener('beforeunload', unload); window.removeEventListener('keydown', escape) }
  }, [editor, dirty, close])
  useEffect(() => { if (editor) { popup.current?.showPopover(); popup.current?.querySelector<HTMLInputElement>('input')?.focus() } }, [editor, data?.targetId])
  function open(kind: Editor['kind'], payment?: Payment) {
    if (!data || loading) return
    opener.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const first = new Date(data.asOf + 'T12:00:00Z'); first.setUTCMonth(first.getUTCMonth() + 1, 1)
    const next = first.toISOString().slice(0, 10), last = new Date(first); last.setUTCMonth(last.getUTCMonth() + 1); last.setUTCDate(0)
    setStart(next); setEnd(last.toISOString().slice(0,10)); setDue(next); setPaid(payment?.paidOn ?? data.asOf)
    setAmount((payment?.amountTry ?? selected?.outstandingTry ?? '').replace('.', ','))
    setReference(''); setReason(''); setDestination(selected?.id ?? ''); setFormError(''); setDirty(false); setUncertain(false); setMustReload(false); savedRequest.current = null
    setFormPeriods(data.periods); setFormPage(data.page)
    setEditor({ kind, payment, key: crypto.randomUUID() })
  }
  async function submit(event: FormEvent) {
    event.preventDefault(); if (!editor || !data || pending.current || mustReload) return
    setFormError('')
    if (!savedRequest.current) {
      const parsed = parseTryPrice(amount, 10)
      if (editor.kind !== 'reverse' && (!parsed || parsed === '0.00')) { setFormError('Pozitif ve en fazla iki ondalıklı bir TL tutarı girin.'); return }
      const period = formPeriods.find(p => p.id === destination)
      let body: object, path: string
      const common = { targetId: data.targetId, requestKey: editor.key }
      if (editor.kind === 'period') {
        if (![start, end, due].every(validDate)) { setFormError('Geçerli dönem ve son ödeme tarihlerini girin.'); return }
        const endDate = new Date(end + 'T12:00:00Z'); endDate.setUTCDate(endDate.getUTCDate() + 1)
        if (endDate.getUTCFullYear() > 9999) { setFormError('Dönem tarihi desteklenen takvim aralığının dışında.'); return }
        body = { ...common, startsOn: start, endsOn: endDate.toISOString().slice(0,10), dueOn: due, amountTry: parsed }; path = '/api/subscription/periods'
      } else {
        if (!period) { setFormError('Güncel dönemi seçin.'); return }
        const target = { ...common, periodId: period.id, version: period.version }
        body = editor.kind === 'reverse' ? { ...target, reason } : editor.kind === 'correct' ? { ...target, paidOn: paid, amountTry: parsed, reason } : { ...target, paidOn: paid, amountTry: parsed, reference }
        path = editor.kind === 'payment' ? '/api/subscription/payments' : `/api/subscription/payments/${editor.payment?.id}/${editor.kind}`
      }
      savedRequest.current = { path, body }
    }
    pending.current = true; setBusy(true)
    try {
      const response = await postWithCsrf(savedRequest.current.path, savedRequest.current.body)
      if (response.status === 401) { onExpired(); return }
      if (!response.ok) {
        if (response.status === 409) setMustReload(true)
        if (response.status < 500) savedRequest.current = null
        const problem: unknown = await response.json().catch(() => null)
        if (response.status >= 500) setUncertain(true)
        throw new Error(record(problem) && typeof problem.title === 'string' ? problem.title : response.status === 403 ? 'Operatör yetkisi doğrulanamadı.' : response.status === 429 ? 'Çok fazla işlem. Bekleyip aynı işlemle yeniden deneyin.' : 'Kayıt tamamlanamadı. Bilgileri kontrol edin.')
      }
      pending.current = false; setNotice(editor.kind === 'period' ? 'Abonelik dönemi kaydedildi.' : editor.kind === 'payment' ? 'Doğruladığınız ödeme kaydedildi.' : 'Düzeltme kaydedildi. Önceki hareket geçmişte korundu.'); close(true)
      await load(data.page, selected?.id)
    } catch (problem) {
      if (savedRequest.current) setUncertain(true)
      setFormError(requestErrorMessage(problem, problem instanceof Error ? problem.message : 'Sonuç doğrulanamadı. Aynı işlemle yeniden deneyin.'))
    } finally { pending.current = false; setBusy(false) }
  }
  const edit = (setter: (s: string) => void) => (value: string) => { setter(value); setDirty(true); savedRequest.current = null }
  return <main className={styles.main}>
    <div className={styles.heading}><div><h1>Abonelik kaydı</h1>{data && <p>{data.targetName} · {data.targetId}</p>}</div><div className={styles.actions}>
      <button disabled={!data || loading || !!editor} onClick={() => open('period')}>Dönem ekle</button>
      {selected && <button disabled={loading || !!editor || selected.outstandingTry === '0.00'} onClick={() => open('payment')}>Ödeme kaydet</button>}
    </div></div>
    {notice && <p role="status">{notice}</p>}
    {loading ? <p role="status">Abonelik kayıtları yükleniyor…</p> : error ? <><p role="alert">{error}</p><button onClick={() => void load()}>Yeniden dene</button></> : data && !data.total ? <p>Bu firma için henüz abonelik dönemi kaydedilmedi. İlk dönemi ekleyin.</p> : data && <>
      <label htmlFor="subscription-period">Dönem</label><select id="subscription-period" value={data.selectedPeriodId ?? ''} disabled={!!editor} onChange={e => void load(data.page, e.target.value)}>
        {data.periods.map(p => <option key={p.id} value={p.id}>{date(p.startsOn)} — {p.outstandingTry === '0.00' ? 'Ödendi' : p.overdue ? 'Vadesi geçti' : 'Ödeme bekleniyor'}</option>)}
      </select>
      {selected && <><h2>{date(selected.startsOn)}</h2><dl className={styles.facts}>
        <div><dt>Dönem bedeli</dt><dd>{money(selected.amountTry)}</dd></div><div><dt>Son ödeme tarihi</dt><dd>{date(selected.dueOn)}</dd></div>
        <div><dt>Doğrulanan ödeme</dt><dd>{money(selected.paidTry)}</dd></div><div><dt>Kalan borç</dt><dd>{money(selected.outstandingTry)}</dd></div>
      </dl>{selected.overdue && <p>Bu dönemin vadesi geçti; {money(selected.outstandingTry)} ödeme bekleniyor.</p>}</>}
      <div className={styles.actions}><button disabled={data.page === 1 || !!editor} onClick={() => void load(data.page - 1)}>Daha yeni dönemler</button><span>{data.page} / {Math.max(1, Math.ceil(data.total / 12))}</span><button disabled={data.page * 12 >= data.total || !!editor} onClick={() => void load(data.page + 1)}>Önceki dönemler</button></div>
      <details><summary>Ödeme ve düzeltme geçmişi ({data.paymentCount})</summary>{!data.payments.length ? <p>Bu dönem için ödeme hareketi yok.</p> : <ul className={styles.history}>{data.payments.map(p => <li key={p.id}>
        <p><strong>{money(p.amountTry)}</strong> · {date(p.paidOn)}{p.reversed ? ' · Ters kaydı var' : ''}</p>
        <p>Kayıt zamanı: {formatBusinessDateTime(p.recordedAt)}</p>
        {p.reference && <p>İşlem referansı: {p.reference}</p>}{p.reason && <p>Gerekçe: {p.reason}</p>}
        {!p.amountTry.startsWith('-') && !p.reversed && <div className={styles.actions}><button disabled={!!editor} onClick={() => open('correct', p)}>Kaydı düzelt</button><button disabled={!!editor} onClick={() => open('reverse', p)}>Kaydı geri al</button></div>}
      </li>)}</ul>}
        {data.paymentCount > 20 && <div className={styles.actions}><button disabled={data.paymentsPage === 1 || !!editor} onClick={() => void load(data.page, data.selectedPeriodId ?? undefined, data.paymentsPage - 1)}>Yeni hareketler</button><button disabled={data.paymentsPage * 20 >= data.paymentCount || !!editor} onClick={() => void load(data.page, data.selectedPeriodId ?? undefined, data.paymentsPage + 1)}>Eski hareketler</button></div>}
      </details>
    </>}
    {editor && data && <div popover="manual" role="dialog" aria-labelledby="subscription-form-title" className={styles.popup} ref={popup}>
      <h2 id="subscription-form-title">{editor.kind === 'period' ? 'Dönem ekle' : editor.kind === 'payment' ? 'Ödeme kaydet' : editor.kind === 'correct' ? 'Ödeme kaydını düzelt' : 'Ödeme kaydını geri al'}</h2>
      <p><strong>{data.targetName} · {data.targetId}</strong></p><p>{editor.kind === 'payment' ? 'Yalnız banka hareketinden doğruladığınız ödemeyi girin. Bu işlem para tahsil etmez.' : editor.kind === 'period' ? 'Anlaştığınız dönem bedelini ve son ödeme tarihini girin.' : 'Önceki kayıt silinmez; gerekçeli ters hareket oluşturulur. Bu işlem bankadan para iadesi yapmaz.'}</p>
      <form onSubmit={e => void submit(e)}><fieldset disabled={busy || uncertain} className={styles.fields}>
        {editor.kind === 'period' ? <>
          <div><label htmlFor="period-start">Başlangıç tarihi</label><input id="period-start" type="date" required value={start} onChange={e => edit(setStart)(e.target.value)} /></div>
          <div><label htmlFor="period-end">Bitiş tarihi</label><input id="period-end" type="date" required value={end} onChange={e => edit(setEnd)(e.target.value)} /></div>
          <div><label htmlFor="period-due">Son ödeme tarihi</label><input id="period-due" type="date" required value={due} onChange={e => edit(setDue)(e.target.value)} /></div>
        </> : <div className={styles.wide}><label htmlFor="payment-period">Dönem</label><select id="payment-period" required value={destination} disabled={editor.kind !== 'correct'} onChange={e => edit(setDestination)(e.target.value)}>{formPeriods.map(p => <option key={p.id} value={p.id}>{date(p.startsOn)}</option>)}</select>
          {editor.kind === 'correct' && data.total > 12 && <div className={styles.actions}>{[-1, 1].map(delta => <button key={delta} type="button" disabled={busy || (delta < 0 ? formPage === 1 : formPage * 12 >= data.total)} onClick={() => {
            setBusy(true); void readData(formPage + delta).then(next => { setFormPage(next.page); setFormPeriods(next.periods); setDestination(next.periods[0]?.id ?? ''); setDirty(true); savedRequest.current = null }).catch(() => setFormError('Dönemler yüklenemedi. Yeniden deneyin.')).finally(() => setBusy(false))
          }}>{delta < 0 ? 'Daha yeni dönemler' : 'Önceki dönemler'}</button>)}</div>}
        </div>}
        {editor.kind !== 'period' && editor.kind !== 'reverse' && <div><label htmlFor="payment-date">Ödeme tarihi</label><input id="payment-date" type="date" required max={data.asOf} value={paid} onChange={e => edit(setPaid)(e.target.value)} /></div>}
        {editor.kind !== 'reverse' && <div><label htmlFor="subscription-amount">{editor.kind === 'period' ? 'Dönem bedeli (TL)' : 'Tutar (TL)'}</label><input id="subscription-amount" inputMode="decimal" required value={amount} onChange={e => edit(setAmount)(e.target.value)} /></div>}
        {editor.kind === 'payment' && <div className={styles.wide}><label htmlFor="payment-reference">Doğrulama referansı</label><input id="payment-reference" required autoComplete="off" maxLength={80} pattern="[A-Za-z0-9][A-Za-z0-9._:/-]{0,79}" value={reference} onChange={e => edit(setReference)(e.target.value)} /></div>}
        {(editor.kind === 'correct' || editor.kind === 'reverse') && <div className={styles.wide}><label htmlFor="payment-reason">Düzeltme gerekçesi</label><textarea id="payment-reason" required maxLength={300} value={reason} onChange={e => edit(setReason)(e.target.value)} /></div>}
      </fieldset>{formError && <p role="alert">{formError}</p>}{uncertain && <p>Sonuç belirsiz; bilgiler sabitlendi. Aynı işlemle yeniden deneyin veya kayıtları kontrol edin.</p>}
        {mustReload && <button type="button" disabled={busy} onClick={() => { void load(formPage, destination).then(latest => { if (!latest) return; setFormPeriods(latest.periods); setFormPage(latest.page); setMustReload(false); setEditor({ ...editor, key: crypto.randomUUID() }); setFormError('Güncel kayıtları yükledik. Bilgileri kontrol edip yeniden kaydedin.') }) }}>Güncel kayıtları yükle</button>}
        <div className={styles.formActions}><button type="submit" disabled={busy || mustReload}>{busy ? 'Kaydediliyor…' : uncertain ? 'Aynı işlemle yeniden dene' : 'Kaydet'}</button><button type="button" disabled={busy} onClick={() => close()}>Vazgeç</button></div>
      </form>
    </div>}
  </main>
}
