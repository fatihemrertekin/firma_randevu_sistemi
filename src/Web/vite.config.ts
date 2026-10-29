import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig(({ mode }) => ({
  // Direct navigation to nested screen URLs must load assets from the same origin's root.
  base: '/',
  plugins: [react()],
  build: {
    outDir: mode === 'operator' ? '../Operator/wwwroot' : '../Server/wwwroot',
    emptyOutDir: true,
    ...(mode === 'operator' ? { rollupOptions: { input: 'operator.html' } } : {}),
  },
  test: {
    setupFiles: ['./src/testSetup.ts'],
    include: ['src/**/*.test.tsx'],
  },
}))
