import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: '../../tests/e2e',
  fullyParallel: false,
  reporter: [['list'], ['html', { outputFolder: '../../artifacts/playwright-report', open: 'never' }]],
  use: { baseURL: 'http://127.0.0.1:5173', trace: 'retain-on-failure' },
  webServer: [
    { command: 'dotnet run --no-launch-profile --project ../Workspace.Web', url: 'http://127.0.0.1:5080/health', reuseExistingServer: true, env: { ASPNETCORE_URLS: 'http://127.0.0.1:5080' } },
    { command: 'npm run dev -- --host 127.0.0.1', url: 'http://127.0.0.1:5173', reuseExistingServer: true },
  ],
})
