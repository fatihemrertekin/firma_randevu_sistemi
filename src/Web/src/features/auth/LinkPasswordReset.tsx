import { useCallback, useEffect, useRef, type ComponentProps } from 'react'
import { useBlocker } from 'react-router'
import PasswordResetForm from './PasswordResetForm'
import AuthenticationLayout from './AuthenticationLayout'

export default function LinkPasswordReset({ onDone, ...props }: ComponentProps<typeof PasswordResetForm>) {
  const busy = useRef(false), report = useCallback((value: boolean) => { busy.current = value }, [])
  useEffect(() => {
    const heading = document.getElementById('page-title')
    document.title = `${heading?.textContent ?? 'Parola sıfırlama'} · Randevu`
    if (document.activeElement?.getAttribute('role') !== 'alert') heading?.focus()
  }, [])
  const blocker = useBlocker(() => busy.current)
  useEffect(() => { if (blocker.state === 'blocked') blocker.reset() }, [blocker])
  useEffect(() => {
    const warn = (event: BeforeUnloadEvent) => { if (busy.current) { event.preventDefault(); event.returnValue = '' } }
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [])
  return <AuthenticationLayout><PasswordResetForm {...props} onBusyChange={report}
    onDone={() => { report(false); onDone() }} /></AuthenticationLayout>
}
