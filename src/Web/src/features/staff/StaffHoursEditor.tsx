import { useCallback } from 'react'
import WeeklyHoursEditor, { type HoursSnapshot } from '../../components/WeeklyHoursEditor'
import { HoursError, parseHours, retrySeconds } from '../../app/weeklyHoursApi'
import { isMember, type StaffPost } from './staffMembersApi'

async function readStaffHours(response: Response, memberId: string): Promise<HoursSnapshot> {
  if (!response.ok) throw new HoursError(response.status, response.status === 429 ? retrySeconds(response) : 0)
  const value: unknown = await response.json()
  const hours = parseHours(value)
  if (typeof value !== 'object' || value === null || !('member' in value) || !isMember(value.member) || value.member.id !== memberId || value.member.version !== hours.version) throw new HoursError(500)
  return { ...hours, caption: value.member.name, readOnly: !value.member.isActive }
}
type Props = { memberId: string; post: StaffPost; onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void; onSaved: () => void; onCancel: () => void }
export default function StaffHoursEditor(props: Props) {
  const readSnapshot = useCallback((response: Response) => readStaffHours(response, props.memberId), [props.memberId])
  return <WeeklyHoursEditor {...props} endpoint={`/api/staff-members/${props.memberId}/hours`} heading="Haftalık çalışma saatleri" formLabel="Personelin çalışma saatlerini düzenle"
    subject="Personel saatleri" closedLabel="Çalışmıyor" startLabel="Başlangıç" endLabel="Bitiş" readSnapshot={readSnapshot} />
}
