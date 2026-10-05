import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  // Direct navigation to nested screen URLs must load assets from the same origin's root.
  base: '/',
  plugins: [react()],
  build: {
    outDir: '../Server/wwwroot',
    emptyOutDir: true,
  },
  test: {
    setupFiles: ['./src/testSetup.ts'],
    include: ['src/**/*.test.tsx'],
  },
})
