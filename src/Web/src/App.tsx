import { useCallback, useEffect, useState } from 'react'
import { createBrowserRouter, createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import Application from './app/Application'
import { readLinkProof } from './features/auth/useLinkProof'
import { NavigationEvents, type SubscribeNavigation } from './app/NavigationEvents'

function createApplicationRouter() {
  // Strip email proofs before the router can copy a URL into navigation state.
  let proof = readLinkProof()
  const consumeProof = () => { const value = proof; proof = null; return value }
  const routes = [{ path: '*', element: <Application initialProof={consumeProof} /> }]
  return typeof window === 'undefined' ? createMemoryRouter(routes) : createBrowserRouter(routes)
}
export default function App() {
  const [router] = useState(createApplicationRouter)
  const subscribe = useCallback<SubscribeNavigation>(listener => {
    let key = router.state.location.key
    return router.subscribe(state => {
      if (state.location.key === key) return
      key = state.location.key; listener(state.location)
    })
  }, [router])
  useEffect(() => () => router.dispose(), [router])
  return <NavigationEvents value={subscribe}><RouterProvider router={router} /></NavigationEvents>
}
