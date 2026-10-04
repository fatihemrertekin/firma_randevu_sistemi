import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  // Keep the production asset bundle portable for local design review.
  base: './',
  plugins: [react()],
  build: {
    outDir: '../Server/wwwroot',
    emptyOutDir: true,
  },
  test: {
    include: ['src/**/*.test.tsx'],
  },
})
