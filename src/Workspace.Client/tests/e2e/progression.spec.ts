import { expect, test, type Page } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import type { CardProgressionDto, ProgressionSnapshotDto, WorkspaceSnapshotDto } from '../../src/contracts.generated'

async function login(page: Page, nickname: string) {
  await page.goto('/'); await page.getByPlaceholder('輸入暱稱').fill(nickname)
  await page.getByRole('button', { name: '進入工作區', exact: true }).click()
  await expect(page.getByText('即時同步中')).toBeVisible()
}
async function send(page: Page, path: string, body: unknown, method = 'POST') {
  const token = (await (await page.request.get('/api/session/csrf')).json()).token
  const response = await page.request.fetch(path, { method, data: body, headers: { 'X-CSRF-TOKEN': token } })
  expect(response.ok(), await response.text()).toBeTruthy(); return response.json()
}
async function read(page: Page, id: string): Promise<CardProgressionDto> {
  const snapshot: ProgressionSnapshotDto = await (await page.request.get('/api/progression')).json()
  return snapshot.cards.find(x => x.cardId === id)!
}
async function fixture(page: Page, qualify = true) {
  const name = `M4 虛構角色 ${randomUUID().slice(0, 6)}`
  const created: WorkspaceSnapshotDto = await send(page, '/api/accounts', { displayName: name })
  const account = created.accounts.find(x => x.displayName === name)!
  const snapshot: WorkspaceSnapshotDto = await send(page, `/api/accounts/${account.id}/cards`, { displayName: name })
  const id = snapshot.accounts.find(x => x.id === account.id)!.cards[0]!.id
  const profile = randomUUID()
  if (qualify) {
    await send(page, `/api/progression/profiles/${profile}`, { name: '虛構職業門檻', levelTarget: 10, taskItemTarget: 2, meritTarget: 1000, expectedVersion: 0 }, 'PUT')
    await send(page, `/api/cards/${id}/progression`, { requestId: randomUUID(), operation: 'ReportProgress', profileId: profile, level: 10, taskItems: 2, meritBalance: 1000 })
  }
  return { id, name, profile, accountId: account.id }
}
async function open(page: Page, id: string) {
  await page.getByRole('button', { name: '養成與活動', exact: true }).click()
  await page.getByLabel('選擇卡片').selectOption(id)
  await expect(page.getByRole('button', { name: '記錄所得', exact: true })).toBeEnabled()
}
// Test database fixture only: keep production server time authoritative, without a test HTTP backdoor.
function makeEligible(id: string) {
  if (!/^[\da-f-]{36}$/i.test(id)) throw new Error('Invalid fixture ID')
  const pairs = (process.env.ConnectionStrings__Workspace ?? 'Host=localhost;Database=workspace_dev;Username=workspace;Password=workspace_dev_only').split(';')
  const values = Object.fromEntries(pairs.map(pair => { const at = pair.indexOf('='); return [pair.slice(0, at).toLowerCase(), pair.slice(at + 1)] }))
  const boundary = `(date_trunc('day', now() AT TIME ZONE 'Asia/Taipei') - interval '1 day' + interval '4 hours') AT TIME ZONE 'Asia/Taipei'`
  execFileSync('psql', ['-v', 'ON_ERROR_STOP=1', '-c', `UPDATE qualification_cycles SET "QualifiedAt"=(${boundary})-interval '1 second', "EligibleFrom"=(${boundary}) WHERE "CardId"='${id}'::uuid;`], {
    env: { ...process.env, PGHOST: values.host, PGPORT: values.port ?? '5432', PGDATABASE: values.database, PGUSER: values.username, PGPASSWORD: values.password ?? '' }, stdio: 'pipe',
  })
}
async function start(page: Page, id: string) {
  makeEligible(id); await open(page, id)
  await page.getByLabel('分流', { exact: true }).fill('3')
  await page.getByRole('button', { name: '回報已進入活動', exact: true }).click()
  await expect(page.getByRole('button', { name: '回報結束活動', exact: true })).toBeVisible()
}

test('AC036–037：職業設定、養成進度與伺服器換日，不採信客戶端快轉時間', async ({ page }, info) => {
  await login(page, 'M4 養成測試'); const f = await fixture(page, false); await open(page, f.id)
  await page.getByRole('button', { name: '職業與流程設定', exact: true }).click()
  await page.getByRole('button', { name: '新增職業門檻', exact: true }).click()
  await page.getByLabel('職業名稱', { exact: true }).fill(f.name + ' 職業')
  await page.getByLabel('目標等級', { exact: true }).fill('10')
  await page.getByLabel('目標道具數', { exact: true }).fill('2')
  await page.getByLabel('目標功勳', { exact: true }).fill('1000')
  await page.getByRole('button', { name: '保存職業門檻', exact: true }).click()
  await page.getByRole('combobox', { name: '職業門檻', exact: true }).selectOption({ label: f.name + ' 職業' })
  await page.getByLabel('目前等級').fill('10'); await page.getByLabel('已取得道具').fill('2'); await page.getByLabel('最後確認功勳').fill('1000')
  await page.getByRole('button', { name: '保存養成進度', exact: true }).click()
  await expect(page.locator('.metrics')).toContainText('等待換日')
  const cycle = (await read(page, f.id)).cycles[0]!
  await page.getByRole('button', { name: '載入最新進度', exact: true }).click()
  await page.getByRole('button', { name: '保存養成進度', exact: true }).click()
  await expect.poll(async () => (await read(page, f.id)).version).toBe(2)
  expect((await read(page, f.id)).cycles[0]).toEqual(cycle)
  await page.clock.setSystemTime(new Date('2040-01-01T00:00:00Z'))
  const token = (await (await page.request.get('/api/session/csrf')).json()).token
  const response = await page.request.post(`/api/cards/${f.id}/progression`, { data: { requestId: randomUUID(), operation: 'StartActivity', expectedVersion: 2, channel: '3', occurredAt: '2040-01-01T00:00:00Z' }, headers: { 'X-CSRF-TOKEN': token } })
  expect(response.status()).toBe(409); expect((await read(page, f.id)).activities).toHaveLength(0)
  await expect(page.getByRole('button', { name: '回報已進入活動', exact: true })).toBeDisabled()
  await page.getByRole('button', { name: '職業與流程設定', exact: true }).click()
  await page.screenshot({ path: info.outputPath('M4-養成與換日資格.png'), fullPage: true })
})

test('AC038、041、047–049：活動同步、回應遺失後重載重試及同日新活動', async ({ browser }, info) => {
  const ac = await browser.newContext(); const bc = await browser.newContext(); const a = await ac.newPage(); const b = await bc.newPage()
  await login(a, 'M4 活動操作者'); await login(b, 'M4 觀察者'); const f = await fixture(a); await start(a, f.id); await open(b, f.id)
  await expect(b.getByRole('button', { name: '回報結束活動', exact: true })).toBeVisible()
  expect((await read(b, f.id)).activities[0]!.channel).toBe('3')
  await a.getByLabel('本次活動所得').fill('200'); await a.getByRole('button', { name: '記錄所得', exact: true }).click()
  await expect(a.getByTestId('cumulative-credits')).toHaveText('200')
  let originalRequest = ''
  await a.route(`**/api/cards/${f.id}/progression`, async route => {
    originalRequest = route.request().postDataJSON().requestId
    const committed = await route.fetch(); expect(committed.ok()).toBeTruthy(); await route.abort()
  }, { times: 1 })
  await a.getByLabel('已完成兌換量（1:1）').fill('300'); await a.getByRole('button', { name: '記錄已完成兌換', exact: true }).click()
  await expect(a.getByRole('button', { name: '使用原請求確認結果', exact: true })).toBeEnabled()
  await a.reload(); await expect(a.getByRole('button', { name: '使用原請求確認結果', exact: true })).toBeEnabled()
  const retry = a.waitForRequest(request => request.url().endsWith(`/cards/${f.id}/progression`) && request.method() === 'POST')
  await a.getByRole('button', { name: '使用原請求確認結果', exact: true }).click()
  expect((await retry).postDataJSON().requestId).toBe(originalRequest)
  await expect(a.getByRole('button', { name: '使用原請求確認結果', exact: true })).toHaveCount(0)
  await expect(b.getByTestId('merit-balance')).toHaveText('700'); await expect(b.getByTestId('cumulative-credits')).toHaveText('500')
  expect((await read(a, f.id)).entries.filter(x => x.kind === 'Conversion')).toHaveLength(1)
  await a.getByLabel('已完成兌換量（1:1）').fill('700'); await a.getByRole('button', { name: '記錄已完成兌換', exact: true }).click()
  await expect(a.getByTestId('merit-balance')).toHaveText('0'); await expect(a.locator('.metrics')).toContainText('已啟用資格')
  const first = (await read(a, f.id)).activities[0]!
  await a.getByText('活動與所得紀錄', { exact: true }).click()
  await a.getByRole('button', { name: '更正入場紀錄', exact: true }).click()
  await a.getByLabel('更正分流').fill('8'); await a.getByLabel('實際進入時間（含時區）').fill(new Date(Date.parse(first.occurredAt) - 1000).toISOString())
  await a.getByLabel('補填原因').fill('虛構實際入場補填'); await a.getByRole('button', { name: '保存入場更正', exact: true }).click()
  await expect.poll(async () => (await read(b, f.id)).activities[0]!.channel).toBe('8')
  expect((await read(b, f.id)).activities[0]!.recordedAt).toBe(first.recordedAt)
  await a.getByRole('button', { name: '回報結束活動', exact: true }).click()
  await a.getByLabel('分流', { exact: true }).fill('8'); await a.getByRole('button', { name: '回報已進入活動', exact: true }).click()
  await expect.poll(async () => (await read(b, f.id)).activities.length).toBe(2)
  const next = (await read(b, f.id)).activities.find(x => !x.endedAt)!; expect(next.id).not.toBe(first.id); expect(next.channel).toBe('8')
  await a.screenshot({ path: info.outputPath('M4-活動與兌換紀錄.png'), fullPage: true })
  await ac.close(); await bc.close()
})

test('AC044–046：重刷、永久失格、先結束占用再封存並建立獨立接替卡片', async ({ page }, info) => {
  await login(page, 'M4 卡片處理'); const f = await fixture(page); await start(page, f.id)
  await page.getByLabel('本次活動所得').fill('25'); await page.getByRole('button', { name: '記錄所得', exact: true }).click()
  await expect(page.getByTestId('cumulative-credits')).toHaveText('25')
  const core: WorkspaceSnapshotDto = await (await page.request.get('/api/snapshot')).json(); const account = core.accounts.find(x => x.id === f.accountId)!
  await send(page, `/api/cards/${f.id}/reserve`, { regionId: '20000000-0000-0000-0000-000000000001', expectedAccountVersion: account.coordinationVersion })
  page.on('dialog', dialog => dialog.accept())
  await page.getByRole('button', { name: '封存卡片', exact: true }).click(); await expect(page.getByRole('alert')).toContainText('回村')
  await page.getByRole('button', { name: '回報結束活動', exact: true }).click()
  await page.getByLabel('處理原因').fill('死亡過多，重新累積'); await page.getByRole('button', { name: '回報需要重刷', exact: true }).click()
  await expect(page.locator('.metrics')).toContainText('需要重刷'); await expect(page.getByTestId('cumulative-credits')).toHaveText('25')
  await page.getByRole('button', { name: '載入最新進度', exact: true }).click(); await page.getByLabel('最後確認功勳').fill('1000')
  await page.getByRole('button', { name: '保存養成進度', exact: true }).click(); await expect(page.locator('.metrics')).toContainText('等待換日')
  expect((await read(page, f.id)).cycles).toHaveLength(2)
  await page.getByLabel('處理原因').fill('TK 永久失格'); await page.getByRole('button', { name: '回報永久失格', exact: true }).click()
  await expect(page.locator('.metrics')).toContainText('永久失格')
  await page.getByRole('button', { name: '封存卡片', exact: true }).click(); await expect(page.getByRole('alert')).toContainText('回村')
  const latest: WorkspaceSnapshotDto = await (await page.request.get('/api/snapshot')).json(); const current = latest.accounts.find(x => x.id === f.accountId)!
  expect(current.cards[0]!.reservation).not.toBeNull()
  await send(page, `/api/cards/${f.id}/cancel`, { expectedAccountVersion: current.coordinationVersion, expectedReservationVersion: current.cards[0]!.reservation!.version })
  await page.getByRole('button', { name: '封存卡片', exact: true }).click(); await expect(page.getByRole('button', { name: '建立接替卡片', exact: true })).toBeVisible()
  await page.getByLabel('接替卡片名稱').fill(f.name + ' 接替'); await page.getByRole('button', { name: '建立接替卡片', exact: true }).click()
  await page.getByLabel('選擇卡片').selectOption({ label: f.name + ' 接替' })
  await expect(page.getByTestId('cumulative-credits')).toHaveText('0'); await expect(page.locator('.metrics')).toContainText('尚未設定職業')
  expect((await read(page, f.id)).cumulativeCredits).toBe(25)
  await page.screenshot({ path: info.outputPath('M4-封存後獨立接替.png'), fullPage: true })
})

test('AC057：兩人收入並存，累積更正保留草稿並拒絕覆蓋新收入', async ({ browser }) => {
  const ac = await browser.newContext(); const bc = await browser.newContext(); const a = await ac.newPage(); const b = await bc.newPage()
  await login(a, 'M4 更正者'); await login(b, 'M4 所得回報者'); const f = await fixture(a, false); await open(a, f.id); await open(b, f.id)
  await a.getByText('更正累積取得量', { exact: true }).click(); await a.getByLabel('正確累積值').fill('25'); await a.getByLabel('更正原因').fill('虛構更正')
  await Promise.all([send(a, `/api/cards/${f.id}/progression`, { requestId: randomUUID(), operation: 'RecordIncome', amount: 10 }), send(b, `/api/cards/${f.id}/progression`, { requestId: randomUUID(), operation: 'RecordIncome', amount: 20 })])
  await expect(a.getByTestId('cumulative-credits')).toHaveText('30'); await expect(a.getByLabel('正確累積值')).toHaveValue('25')
  await a.getByRole('button', { name: '保存累積更正', exact: true }).click(); await expect(a.getByRole('alert')).toBeVisible()
  expect((await read(a, f.id)).cumulativeCredits).toBe(30)
  await a.getByRole('button', { name: '載入最新累積值', exact: true }).click(); await a.getByLabel('正確累積值').fill('25')
  await a.getByRole('button', { name: '保存累積更正', exact: true }).click(); await expect(b.getByTestId('cumulative-credits')).toHaveText('25')
  await ac.close(); await bc.close()
})
