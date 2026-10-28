import { useEffect, useRef } from 'react'
import NavigationLink from '../../app/NavigationLink'
import { sectionPaths } from '../../app/routes'
import { formatDay, formatTime, statusLabels, weekDates, type AppointmentWeekPage } from './appointmentsApi'
import styles from './WeeklyAppointments.module.css'

type Props = { day: string; data: AppointmentWeekPage | null; disabled: boolean; search: string; onDayChange: (day: string) => void }
const weekday = new Intl.DateTimeFormat('tr-TR', { timeZone: 'Europe/Istanbul', weekday: 'long' })
const shortDays = ['Pzt', 'Sal', 'Çrş', 'Prş', 'Cum', 'Cmt', 'Paz']

export default function WeeklyAppointments({ day, data, disabled, search, onDayChange }: Props) {
  const selected = useRef<HTMLButtonElement>(null), restoreFocus = useRef(false)
  useEffect(() => {
    if (!disabled && data?.date === day && restoreFocus.current) {
      restoreFocus.current = false
      selected.current?.focus({ preventScroll: true })
    }
  }, [disabled, data, day])
  return <div className={styles.week}>
    <div className={styles.days} role="group" aria-label="Haftanın günleri">
      {weekDates(day).map((date, index) => {
        const count = data?.days[index].totalCount, value = new Date(date + 'T12:00:00Z')
        return <button key={date} type="button" ref={date === day ? selected : undefined} aria-pressed={date === day} disabled={disabled}
          aria-label={formatDay(date) + (count === undefined ? '' : `, ${count} randevu`)}
          onClick={() => { restoreFocus.current = true; onDayChange(date) }}>
          <span className={styles.longDay}>{weekday.format(value)}</span><span className={styles.shortDay}>{shortDays[index]}</span>
          <strong>{value.getUTCDate()}</strong><span className={styles.count}>{count === undefined ? '\u00a0' : `${count} kayıt`}</span>
        </button>
      })}
    </div>
    <div className={styles.heading}><h2>{formatDay(day)}</h2>{data && <p>{data.totalCount} randevu · İstanbul saati</p>}</div>
    {data && (data.items.length === 0 ? <p className={styles.empty}>Seçili günde randevu yok. Uygun saatler yeni randevu formunda kontrol edilir.</p>
      : <ol className={styles.list} aria-label="Seçili günün randevuları">{data.items.map(item => <li key={item.id} className={styles.row}>
        <div className={styles.time}><strong><time dateTime={item.startsAt}>{formatTime(item.startsAt)}</time></strong><span><span className={styles.separator}>– </span><time dateTime={item.endsAt}>{formatTime(item.endsAt)}</time></span></div>
        <div className={styles.identity}><strong>{item.customerName}</strong><p>{item.serviceName}</p></div>
        <p className={styles.worker}>{item.staffName}</p><span className={styles.status}>{statusLabels[item.status]}</span>
        <NavigationLink className={styles.details} to={`${sectionPaths.appointments}/${item.id}${search}`}
          aria-label={`${item.customerName}, ${formatTime(item.startsAt)} randevusunun ayrıntıları`}>Ayrıntılar</NavigationLink>
      </li>)}</ol>)}
  </div>
}
