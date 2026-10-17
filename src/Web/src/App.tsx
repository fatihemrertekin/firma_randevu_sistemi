import { useCallback, useEffect, useState } from 'react'
import { createBrowserRouter, createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import Application from './app/Application'
import { readLinkProof } from './features/auth/useLinkProof'
import { NavigationEvents, type SubscribeNavigation } from './app/NavigationEvents'
import PublicBooking from './features/public-booking/PublicBooking'
import { readAccess } from './features/public-booking/publicBookingApi'

function createApplicationRouter() {
  let access = readAccess()
  const captureAccess = () => { const next = readAccess(); if (next) access = next }
  if (typeof window !== 'undefined') window.addEventListener('hashchange', captureAccess)
  const consumeAccess = () => { const value = access; access = null; return value }
  // Strip email proofs before the router can copy a URL into navigation state.
  let proof = readLinkProof()
  const consumeProof = () => { const value = proof; proof = null; return value }
  const routes = [{ path: '/randevu/*', element: <PublicBooking initialAccess={consumeAccess} /> }, { path: '*', element: <Application initialProof={consumeProof} /> }]
  const router = typeof window === 'undefined' ? createMemoryRouter(routes) : createBrowserRouter(routes)
  return { router, cleanup: () => { if (typeof window !== 'undefined') window.removeEventListener('hashchange', captureAccess); router.dispose() } }
}
export default function App() {
  const [{ router, cleanup }] = useState(createApplicationRouter)
  const subscribe = useCallback<SubscribeNavigation>(listener => {
    let key = router.state.location.key
    return router.subscribe(state => {
      if (state.location.key === key) return
      key = state.location.key; listener(state.location)
    })
  }, [router])
  useEffect(() => cleanup, [cleanup])
  return <NavigationEvents value={subscribe}><RouterProvider router={router} /></NavigationEvents>
}
