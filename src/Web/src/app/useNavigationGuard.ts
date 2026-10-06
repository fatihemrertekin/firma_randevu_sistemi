import { useEffect } from 'react'
import { useBlocker } from 'react-router'
import type { RefObject } from 'react'
import { resolveRoute } from './routes'

type State = { dirty: boolean; profileDirty: boolean; busy: boolean }
export default function useNavigationGuard(state: RefObject<State>, discard: () => void) {
  const blocker = useBlocker(({ currentLocation, nextLocation }) => {
    if (currentLocation.pathname === nextLocation.pathname && currentLocation.search === nextLocation.search) return false
    const route = resolveRoute(currentLocation.pathname)
    if (currentLocation.pathname === nextLocation.pathname && route.kind === 'management' && route.task === 'services') {
      const current = new URLSearchParams(currentLocation.search), next = new URLSearchParams(nextLocation.search)
      for (const key of ['hizmetSayfa', 'hizmetBoyut']) { current.delete(key); next.delete(key) }
      // Aynı seçim formunun sayfalarını gezerken taslak bileşende kalır.
      if (current.toString() === next.toString()) return state.current.busy
    }
    return state.current.busy || state.current.dirty || (state.current.profileDirty && !nextLocation.pathname.startsWith('/yonetim/'))
  })
  useEffect(() => {
    if (blocker.state !== 'blocked') return
    if (state.current.busy || !window.confirm('Kaydedilmemiş değişiklikler silinsin mi?')) blocker.reset()
    else { discard(); blocker.proceed() }
  }, [blocker, state, discard])
  useEffect(() => {
    const warn = (event: BeforeUnloadEvent) => {
      if (state.current.busy || state.current.dirty || state.current.profileDirty) { event.preventDefault(); event.returnValue = '' }
    }
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [state])
}
