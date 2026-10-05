import { beforeEach } from 'vitest'

beforeEach(() => { if (typeof window !== 'undefined') window.history.replaceState(null, '', '/') })
