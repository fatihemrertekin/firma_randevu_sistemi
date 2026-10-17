import { useEffect, useRef, useState } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import Pagination from '../../components/Pagination'
import useNavigationGuard from '../../app/useNavigationGuard'
import { RequestVerificationError } from '../../app/api'
import { isDay } from '../appointments/appointmentsApi'
import { BookingError, businessDay, nextDay, formatDay, formatTime, priceLabel, errorMessage, isCatalog, isMembers, isAvailability, isChallenge, isResent, isVerified, isResult, post, type Catalog, type Service, type Member, type Challenge, type Intent, type Result, type Receipt } from './publicBookingApi'
import usePublicRead from './usePublicRead'
import styles from './PublicBooking.module.css'

export function useClock() {
  const [now, setNow] = useState(Date.now)
  useEffect(() => { const timer = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(timer) }, [])
  return now
}
type Props = { onCreated: (value: Result, receipt: Receipt) => void; onBusiness: (value: Catalog['business']) => void }
export default function BookingWizard({ onCreated, onBusiness }: Props) {
  const [step, setStep] = useState(1), [page, setPage] = useState(1), [memberPage, setMemberPage] = useState(1), [size, setSize] = useState(20), [revision, setRevision] = useState(0)
  const [service, setService] = useState<Service | null>(null), [member, setMember] = useState<Member | null>(null), [day, setDay] = useState(businessDay), [start, setStart] = useState('')
  const [name, setName] = useState(''), [phone, setPhone] = useState(''), [code, setCode] = useState(''), [challenge, setChallenge] = useState<Challenge | null>(null), [until, setUntil] = useState('')
  const [busy, setBusy] = useState(false), [failure, setFailure] = useState<unknown>(null), [notice, setNotice] = useState(''), [uncertain, setUncertain] = useState(false), [retryAt, setRetryAt] = useState(0)
  const pending = useRef(false), request = useRef<Intent | null>(null), resendKey = useRef<string | null>(null)
  const guard = useRef({ dirty: false, profileDirty: false, busy: false }), heading = useRef<HTMLHeadingElement>(null)
  const catalog = usePublicRead(`/catalog?page=${page}&pageSize=${size}`, isCatalog, revision)
  const members = usePublicRead(service ? `/staff?serviceId=${service.id}&page=${memberPage}&pageSize=${size}` : null, isMembers, revision)
  const availability = usePublicRead(service && member && isDay(day) ? `/availability?serviceId=${service.id}&staffMemberId=${member.id}&date=${day}` : null, isAvailability, revision)
  const now = useClock(), policy = catalog?.data?.policy
  useEffect(() => { if (catalog?.data) onBusiness(catalog.data.business) }, [catalog, onBusiness])
  useEffect(() => { guard.current = { dirty: !!service || !!name || !!phone || uncertain, profileDirty: false, busy }; }, [service, name, phone, uncertain, busy])
  useNavigationGuard(guard, () => {})
  useEffect(() => { heading.current?.focus() }, [step])
  const fields = failure instanceof BookingError ? failure.fields : {}
  useEffect(() => {
    const field = failure instanceof BookingError ? Object.keys(failure.fields)[0] : undefined
    if (field) document.getElementById(`booking-${field}`)?.focus()
  }, [failure])
  function invalidate() { setChallenge(null); setUntil(''); setCode(''); request.current = null; resendKey.current = null; setFailure(null); setNotice('') }
  function changeStep(value: number) { if (busy || uncertain) return; invalidate(); setStep(value) }
  async function run(action: () => Promise<void>) {
    if (pending.current || Date.now() < retryAt) return
    pending.current = true; setBusy(true); setFailure(null); setNotice('')
    try { await action() }
    catch (e) { setFailure(e); if (e instanceof BookingError && e.status === 429) setRetryAt(e.retryAt) }
    finally { pending.current = false; setBusy(false) }
  }
  function currentIntent(): Intent {
    if (!service || !member || !availability?.data || !policy || !start || availability.data.serviceVersion !== service.version || availability.data.policyVersion !== policy.version) throw new BookingError(409)
    return { requestKey: crypto.randomUUID(), serviceId: service.id, serviceVersion: service.version, policyVersion: policy.version, staffMemberId: member.id, startsAt: start, customerName: name.trim(), customerPhone: phone }
  }
  const requestCode = () => run(async () => {
    request.current ??= currentIntent()
    const value = await post('/verification/request', request.current, isChallenge)
    setChallenge(value); setStep(5); setNotice('Kod isteğiniz alındı. SMS’in ulaşması biraz sürebilir.')
  })
  const send = () => run(async () => {
    if (!request.current || !challenge) return
    // Once a create was sent, only the same receipt/content may resolve an unknown result.
    setUncertain(true)
    try {
      const value = await post('/appointments', { ...request.current, challengeId: challenge.challengeId, challengeSecret: challenge.challengeSecret }, isResult)
      guard.current.dirty = false; guard.current.busy = false
      onCreated(value, { challengeId: challenge.challengeId, challengeSecret: challenge.challengeSecret })
    } catch (e) {
      if (e instanceof RequestVerificationError || e instanceof BookingError && ([400,404,409,429].includes(e.status) || e.status === 403 && !uncertain)) setUncertain(false)
      throw e
    }
  })
  const resend = () => run(async () => {
    if (!challenge) return
    resendKey.current ??= crypto.randomUUID()
    const value = await post('/verification/resend', { challengeId: challenge.challengeId, challengeSecret: challenge.challengeSecret, resendKey: resendKey.current }, isResent)
    if (value.challengeId !== challenge.challengeId) throw new Error('invalid-response')
    setChallenge({ ...challenge, ...value }); resendKey.current = null; setCode(''); setNotice('Yeni kod isteğiniz alındı. Önceki kod artık geçerli değildir.')
  })
  const verify = () => run(async () => {
    if (!challenge) return
    try {
      const value = await post('/verification/verify', { challengeId: challenge.challengeId, challengeSecret: challenge.challengeSecret, code }, isVerified)
      setUntil(value.verifiedUntil); setCode(''); setNotice('Telefon doğrulandı. Talebiniz henüz gönderilmedi.')
    } catch (e) { if (e instanceof BookingError && e.status === 400) throw new BookingError(400, 'verification_code_invalid'); throw e }
  })
  const labels = ['Hizmet', 'Çalışan', 'Zaman', 'İletişim', 'Kontrol']
  const waiting = busy || now < retryAt
  const policyNote = policy && <p className={styles.note}>{policy.requiresBusinessApproval ? 'Talebiniz işletme onayına gönderilir.' : 'Talebiniz kaydedildiğinde randevunuz onaylanır.'}<br/>İptal: randevudan en az {policy.customerCancellationLeadMinutes} dakika önce.</p>
  const summary = <aside className={styles.summary} aria-label="Seçimleriniz"><h2>Seçimleriniz</h2>
    {service && <><div><span>Hizmet</span><button disabled={busy || uncertain} className={styles.link} onClick={() => changeStep(1)} aria-label="Hizmeti değiştir">Değiştir</button><strong>{service.name}</strong><p>{service.durationMinutes} dk · {priceLabel(service.price, service.currency)}</p></div></>}
    {member && <div><span>Çalışan</span><button disabled={busy || uncertain} className={styles.link} onClick={() => changeStep(2)} aria-label="Çalışanı değiştir">Değiştir</button><strong>{member.name}</strong></div>}
    {start && <div><span>Tarih ve saat</span><button disabled={busy || uncertain} className={styles.link} onClick={() => changeStep(3)} aria-label="Zamanı değiştir">Değiştir</button><strong>{formatDay(day)} · {formatTime(start)}</strong><p>İstanbul saati</p></div>}
    {policyNote}
  </aside>
  function paging(data: { page: number; pageSize: number; totalCount: number; hasMore: boolean; items: unknown[] }, staff = false) {
    return <Pagination page={data.page} pageSize={data.pageSize} itemCount={data.items.length} totalCount={data.totalCount} hasNext={data.hasMore} disabled={busy} label={staff ? 'Çalışan sayfaları' : 'Hizmet sayfaları'} onPageChange={staff ? setMemberPage : setPage} onPageSizeChange={value => { setSize(value); setPage(1); setMemberPage(1) }} />
  }
  const readError = step === 1 ? catalog?.error : step === 2 ? members?.error : step === 3 ? availability?.error : null
  return <div className={`${styles.layout} ${styles.wizard}`}><div className={styles.intro}>
    <h1 ref={heading} tabIndex={-1}>Randevu alın</h1><p>Hizmetinizi, çalışanınızı ve saatinizi seçin.</p>
    <ol className={styles.steps} aria-label="Rezervasyon adımları">{labels.map((label, i) => <li key={label} aria-current={step === i + 1 ? 'step' : undefined}><span>{i + 1}</span>{label}</li>)}</ol>
    <p className={styles.mobileStep}>Adım {step} / 5 · {labels[step - 1]}</p>
    </div>{summary}<section className={styles.form} aria-label={labels[step - 1]}>
      <ErrorMessage message={failure ? errorMessage(failure) : readError ? errorMessage(readError) : ''}/>
      {!!readError && <button onClick={() => setRevision(v => v + 1)}>Yeniden dene</button>}
      {failure instanceof BookingError && [404,409].includes(failure.status) && !uncertain && <button onClick={() => { invalidate(); setRevision(v => v + 1); setService(null); setMember(null); setStart(''); setStep(1) }}>Güncel seçenekleri yükle</button>}
      {failure instanceof BookingError && failure.status === 403 && !uncertain && <button onClick={() => { invalidate(); setStep(4) }}>Yeniden doğrula</button>}
      {now < retryAt && <p role="status">Yeniden denemek için yaklaşık {Math.ceil((retryAt - now) / 1000)} saniye bekleyin.</p>}
      {notice && <p role="status">{notice}</p>}
      {step === 1 && <><h2>Hizmet seçin</h2>{!catalog && <p role="status">Hizmetler yükleniyor…</p>}{catalog?.data && <>
        {catalog.data.totalCount === 0 ? <p>Çevrim içi seçilebilecek hizmet bulunmuyor. İşletmeyle iletişime geçin.</p> : <fieldset className={styles.choices}><legend>Hizmetler</legend>{catalog.data.items.map(item => <label key={item.id}><input type="radio" name="service" checked={service?.id === item.id} onChange={() => { invalidate(); setService(item); setMember(null); setMemberPage(1); setStart('') }}/><span>{item.name}<small>{item.durationMinutes} dk · {priceLabel(item.price, item.currency)}</small></span></label>)}</fieldset>}{paging(catalog.data)}
        <button className={styles.primary} disabled={!service} onClick={() => setStep(2)}>Devam et</button></> }</>}
      {step === 2 && <><h2>Çalışan seçin</h2>{!members && <p role="status">Çalışanlar yükleniyor…</p>}{members?.data && <>
        {members.data.totalCount === 0 ? <p>Bu hizmet için çevrim içi seçilebilecek çalışan bulunmuyor. Başka hizmet seçin veya işletmeyle iletişime geçin.</p> : <fieldset className={styles.choices}><legend>Çalışanlar</legend>{members.data.items.map(item => <label key={item.id}><input type="radio" name="member" checked={member?.id === item.id} onChange={() => { invalidate(); setMember(item); setStart('') }}/>{item.name}</label>)}</fieldset>}{paging(members.data, true)}
        <div className={styles.actions}><button onClick={() => changeStep(1)}>Geri</button><button className={styles.primary} disabled={!member} onClick={() => setStep(3)}>Devam et</button></div></>}</>}
      {step === 3 && <><label htmlFor="booking-date">Tarih seçin</label><input id="booking-date" type="date" value={day} min={businessDay()} max={policy ? nextDay(businessDay(), policy.horizonDays) : undefined} onChange={e => { invalidate(); setDay(e.target.value); setStart('') }}/>
        <h2>Uygun saatler</h2><p>İstanbul saati</p>{!availability && isDay(day) && <p role="status">Saatler yükleniyor…</p>}{availability?.data && <>
          {availability.data.startsAt.length === 0 ? <p>Bu çalışan için seçtiğiniz gün uygun saat bulunmuyor. Başka tarih veya çalışan seçin.</p> : <fieldset className={`${styles.choices} ${styles.times}`}><legend>Uygun saatler</legend>{[...availability.data.startsAt].sort().map(value => <label key={value}><input type="radio" name="time" checked={start === value} onChange={() => setStart(value)}/>{formatTime(value)}</label>)}</fieldset>}
        </>}<div className={styles.actions}><button onClick={() => changeStep(2)}>Geri</button><button className={styles.primary} disabled={!start || !availability?.data?.startsAt.includes(start)} onClick={() => setStep(4)}>Devam et</button></div></>}
      {step === 4 && <form onSubmit={e => { e.preventDefault(); void requestCode() }}><label htmlFor="booking-customerName">Adınız ve soyadınız</label><input id="booking-customerName" autoComplete="name" required minLength={2} maxLength={100} value={name} disabled={waiting} aria-invalid={!!fields.customerName} aria-describedby={fields.customerName ? 'name-error' : undefined} onChange={e => { invalidate(); setName(e.target.value) }}/>{fields.customerName && <p id="name-error">{fields.customerName}</p>}
        <label htmlFor="booking-customerPhone">Cep telefonu</label><input id="booking-customerPhone" type="tel" inputMode="tel" autoComplete="tel" required maxLength={32} value={phone} disabled={waiting} aria-invalid={!!fields.customerPhone} aria-describedby="phone-help" onChange={e => { invalidate(); setPhone(e.target.value) }}/><p id="phone-help">Türkiye cep numaranızı bu randevu için doğrulayacağız.{fields.customerPhone && ` ${fields.customerPhone}`}</p>
        <div className={styles.actions}><button disabled={waiting} onClick={() => changeStep(3)} type="button">Geri</button><button type="submit" disabled={waiting}>{busy ? 'İsteniyor…' : 'Doğrulama kodu iste'}</button></div>
        {failure instanceof BookingError && failure.code === 'verification_request_exists' && <button type="button" disabled={waiting} onClick={() => { request.current = null; void requestCode() }}>Yeni kod iste</button>}
      </form>}
      {step === 5 && challenge && <>
        <h2>Telefon doğrulaması ve son kontrol</h2><p>{challenge.maskedPhone}</p>
        {!until && <form onSubmit={e => { e.preventDefault(); void verify() }}><label htmlFor="booking-code">Doğrulama kodu</label><input id="booking-code" inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" maxLength={6} required value={code} disabled={waiting} onChange={e => setCode(e.target.value.replace(/\D/gu, ''))}/><button type="submit" disabled={waiting || now >= Date.parse(challenge.expiresAt)}>Kodu doğrula</button>
          <p>Kod süresi: yaklaşık {Math.max(0, Math.ceil((Date.parse(challenge.expiresAt) - now) / 1000))} saniye.</p>
          <button type="button" disabled={waiting || now < Date.parse(challenge.resendAt) || now >= Date.parse(challenge.expiresAt)} onClick={() => void resend()}>Kodu tekrar iste</button>{now < Date.parse(challenge.resendAt) && <p>Yaklaşık {Math.ceil((Date.parse(challenge.resendAt) - now) / 1000)} saniye sonra tekrar isteyebilirsiniz.</p>}
          {now >= Date.parse(challenge.expiresAt) && <button type="button" disabled={waiting} onClick={() => { invalidate(); setStep(4) }}>Yeni doğrulama başlat</button>}
        </form>}
        {until && now >= Date.parse(until) && !uncertain && <p>Doğrulama süresi doldu. Göndermeden önce yeniden doğrulayın.</p>}
        <p>Kod doğrulaması seçtiğiniz saati tutmaz.</p>
        {policy && start && <p className={styles.note}>{policy.requiresBusinessApproval ? 'Talebiniz işletme onayına gönderilir.' : 'Başarılı kayıt sonrasında randevunuz onaylanır.'}<br/>İptal son anı: {formatDay(day)} · {formatTime(new Date(Date.parse(start) - policy.customerCancellationLeadMinutes * 60000).toISOString())}.{now > Date.parse(start) - policy.customerCancellationLeadMinutes * 60000 && ' Bu randevu bağlantıdan iptal edilemez.'}</p>}
        {uncertain && !busy && <p role="status">Talebin sonucu doğrulanamadı. Yeni randevu başlatmadan aynı talebi yeniden deneyin. Sayfayı kapatırsanız doğrulama bilgileri kaybolur.</p>}
        {uncertain && failure instanceof BookingError && failure.status === 403 && <p>Bu talebin sonucu burada okunamıyor. Varsa sonuç bağlantısını açın; yoksa işletmeyle iletişime geçin. Yeni talep başlatmayın.</p>}
        <div className={styles.actions}><button disabled={waiting || uncertain} onClick={() => { invalidate(); setStep(4) }}>{until && now >= Date.parse(until) ? 'Yeniden doğrula' : 'Geri'}</button><button className={styles.primary} disabled={waiting || (!uncertain && (!until || now >= Date.parse(until)))} onClick={() => void send()}>{busy ? 'Gönderiliyor…' : uncertain ? 'Aynı talebi yeniden dene' : 'Randevu talebi gönder'}</button></div>
      </>}
      {step <= 3 && <div className={styles.mobilePolicy}>{policyNote}</div>}
    </section></div>
}
