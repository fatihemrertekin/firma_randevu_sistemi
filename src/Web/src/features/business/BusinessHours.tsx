import WeeklyHoursEditor from '../../components/WeeklyHoursEditor'
import type { HoursPost } from './businessHoursApi'

type Props = { post: HoursPost; onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void }
export default function BusinessHours(props: Props) {
  return <WeeklyHoursEditor {...props} endpoint="/api/business-hours/" heading="Haftalık açılış ve kapanış" formLabel="İşletme saatlerini düzenle"
    subject="İşletme saatleri" closedLabel="Kapalı" startLabel="Açılış" endLabel="Kapanış" />
}
