import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
      '/health': 'http://localhost:5080',
      '/hubs': { target: 'http://localhost:5080', ws: true },
    },
  },
  test: {
    include: ['src/**/*.{test,spec}.{js,ts,jsx,tsx}'],
    environment: 'jsdom',
    environmentOptions: {
      jsdom: { url: 'http://localhost/' },
    },
  },
})
