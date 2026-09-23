import { expect, test, type Page } from '@playwright/test'
import { randomUUID } from 'node:crypto'
const cardsId = '40000000-0000-0000-0000-000000000001'
async function login(page: Page, name: string) {
  await page.goto('/'); await page.getByPlaceholder('輸入暱稱').fill(name)
  await page.getByRole('button', { name: '進入工作區', exact: true }).click()
  await expect(page.getByText('即時同步中')).toBeVisible()
  return (await (await page.request.get('/api/session')).json()).participantId as string
}
async function send(page: Page, path: string, body: unknown, method = 'PUT') {
  const token = (await (await page.request.get('/api/session/csrf')).json()).token
  const response = await page.request.fetch(path, { method, data: body, headers: { 'X-CSRF-TOKEN': token } })
  expect(response.ok(), await response.text()).toBeTruthy(); return response.json()
}
async function fixture(page: Page, shared = false) {
  const name = `M3 角色 ${randomUUID().slice(0, 6)}`; const fieldName = `備註 ${randomUUID().slice(0, 6)}`; const field = randomUUID()
  const created = await send(page, '/api/accounts', { displayName: name }, 'POST')
  const account = created.accounts.find((x: { displayName: string }) => x.displayName === name)
  const snapshot = await send(page, `/api/accounts/${account.id}/cards`, { displayName: name }, 'POST')
  const card = snapshot.accounts.find((x: { id: string }) => x.id === account.id).cards[0]
  await send(page, `/api/fields/${field}`, { collectionId: cardsId, name: fieldName, scope: shared ? 'Shared' : 'Common', kind: 'Text', options: [], relationCollectionId: null, color: '#526b84', position: 1, required: false, hidden: false, expectedVersion: 0 })
  return { name, fieldName, field, card, account: snapshot.accounts.find((x: { id: string }) => x.id === account.id), cell: `${name} · ${fieldName}` }
}
const roster = (page: Page) => page.getByRole('region', { name: '在線成員' })
test('AC025–031：多分頁去重、編輯提示及 Admin 隱身與改名撤權', async ({ browser }, info) => {
  const ac = await browser.newContext(); const bc = await browser.newContext(); const adminContext = await browser.newContext()
  const a = await ac.newPage(); const b = await bc.newPage(); const admin = await adminContext.newPage()
  const alice = await login(a, 'M3 同名'); const bob = await login(b, 'M3 同名')
  const tab = await ac.newPage(); await tab.goto('/'); await expect(tab.getByText('即時同步中')).toBeVisible()
  await expect(roster(a).locator('li')).toHaveCount(2)
  await expect(roster(a).locator(`[data-participant="${alice}"]`)).toHaveCount(1)
  await expect(roster(a).locator(`[data-participant="${bob}"]`)).toHaveCount(1)
  const frames: string[] = []; a.on('websocket', socket => socket.on('framereceived', frame => frames.push(String(frame.payload))))
  // Capture the existing socket too by routing a fresh page before its connection starts.
  const observer = await ac.newPage(); observer.on('websocket', socket => socket.on('framereceived', frame => frames.push(String(frame.payload))))
  await observer.goto('/'); await expect(observer.getByText('即時同步中')).toBeVisible()
  const adminId = await login(admin, 'Admin'); const f = await fixture(a, true)
  await a.getByRole('button', { name: f.cell, exact: true }).click()
  await a.getByLabel('你的草稿').fill('我的未保存草稿')
  await b.getByRole('button', { name: f.cell, exact: true }).click()
  await b.getByLabel('你的草稿').fill('另一份未保存草稿')
  await expect(b.getByRole('dialog').locator('.presence-badges')).toContainText('編輯中')
  await admin.getByRole('button', { name: f.cell, exact: true }).click()
  await admin.getByLabel('你的草稿').fill('隱身者的業務更新')
  await admin.getByRole('button', { name: '保存值', exact: true }).click()
  await expect(a.getByTestId('latest-value')).toHaveText('隱身者的業務更新')
  await expect(a.getByLabel('你的草稿')).toHaveValue('我的未保存草稿')
  await expect(a.getByRole('button', { name: '保存值', exact: true })).toBeDisabled()
  await expect(roster(observer).locator('li')).toHaveCount(2)
  expect(await (await a.request.get('/api/presence')).text()).not.toContain(adminId)
  expect(await (await a.request.get('/api/presence')).text()).not.toContain('Admin')
  expect(frames.join('')).not.toContain(adminId); expect(frames.join('')).not.toContain('Admin')
  expect((await a.request.get('/api/audit')).status()).toBe(403)
  await b.screenshot({ path: info.outputPath('M3-多人在線與共用欄位衝突.png'), fullPage: true })
  const adminTab = await adminContext.newPage(); await adminTab.goto('/'); await expect(adminTab.getByRole('heading', { name: '最近操作紀錄' })).toBeVisible()
  await send(admin, '/api/session/nickname', { nickname: 'M3 普通成員' })
  await expect(adminTab.getByRole('heading', { name: '最近操作紀錄' })).toHaveCount(0)
  expect((await adminTab.request.get('/api/audit')).status()).toBe(403)
  await expect(roster(observer).locator(`[data-participant="${adminId}"]`)).toContainText('M3 普通成員')
  await send(admin, '/api/session/nickname', { nickname: 'Admin' })
  await expect(roster(observer).locator(`[data-participant="${adminId}"]`)).toHaveCount(0)
  await tab.close(); await observer.close(); await expect(roster(b).locator(`[data-participant="${alice}"]`)).toHaveCount(1)
  await ac.close(); await bc.close(); await adminContext.close()
})

test('AC030–032：斷線清除編輯提示、保留占用及草稿，重連核對最新值', async ({ browser }, info) => {
  const ac = await browser.newContext(); const bc = await browser.newContext()
  const a = await ac.newPage(); const b = await bc.newPage()
  const alice = await login(a, 'M3 遊戲操作者'); const bob = await login(b, 'M3 欄位編輯者')
  const f = await fixture(a)
  await send(a, `/api/cards/${f.card.id}/reserve`, { regionId: '20000000-0000-0000-0000-000000000001', expectedAccountVersion: f.account.coordinationVersion }, 'POST')
  await send(a, `/api/cards/${f.card.id}/usage`, { status: 'InUse', assignMeAsPrimaryOperator: true })
  await b.getByRole('button', { name: f.cell, exact: true }).click(); await b.getByLabel('你的草稿').fill('離線也保留')
  const card = a.locator('article.card').filter({ has: a.getByRole('heading', { name: f.name, exact: true }) })
  await expect(card.locator('.presence-badges').first()).toContainText('M3 欄位編輯者')
  await expect(card).toContainText('主要操作者：M3 遊戲操作者')
  await bc.setOffline(true)
  await expect(b.getByRole('button', { name: '保存值', exact: true })).toBeDisabled()
  await expect(roster(a).locator(`[data-participant="${bob}"]`)).toHaveCount(0, { timeout: 15000 })
  await expect(card.locator('.presence-badges')).toHaveCount(0)
  await expect(card).toContainText('已預約'); await expect(card).toContainText('主要操作者：M3 遊戲操作者')
  await send(a, `/api/fields/${f.field}/records/${f.card.id}`, { value: '離線期間的新值', expectedVersion: 0, expectedDefinitionVersion: 1, attached: true })
  await bc.setOffline(false)
  await expect(b.getByText('即時同步中')).toBeVisible({ timeout: 15000 })
  await expect(b.getByTestId('latest-value')).toHaveText('離線期間的新值')
  await expect(b.getByLabel('你的草稿')).toHaveValue('離線也保留')
  await expect(b.getByText('無法連接伺服器，請等待重新連線。')).toHaveCount(0)
  await expect(roster(a).locator(`[data-participant="${bob}"]`)).toHaveCount(1)
  expect((await (await b.request.get('/api/session')).json()).participantId).toBe(bob)
  await expect(roster(b).locator(`[data-participant="${alice}"]`)).toHaveCount(1)
  await b.screenshot({ path: info.outputPath('M3-重連後保留草稿.png'), fullPage: true })
  await ac.close(); await bc.close()
})

test('AC032、056：漏掉 SignalR 通知仍由定期版本核對收斂', async ({ browser }) => {
  const ac = await browser.newContext(); const bc = await browser.newContext()
  const a = await ac.newPage(); const b = await bc.newPage(); let dropped = 0
  await b.routeWebSocket('**/hubs/workspace**', ws => {
    const server = ws.connectToServer()
    server.onMessage(message => {
      // SignalR JSON messages use a record separator; preserve handshake, heartbeat and presence.
      if (typeof message !== 'string') { ws.send(message); return }
      const frames = message.split('\u001e').filter(Boolean).filter(frame => {
        if (JSON.parse(frame).target === 'snapshotChanged') { dropped++; return false } return true
      })
      if (frames.length) ws.send(frames.join('\u001e') + '\u001e')
    })
  })
  await login(a, 'M3 發送者'); await login(b, 'M3 漏收通知者')
  const f = await fixture(a)
  await expect(b.getByRole('button', { name: f.cell, exact: true })).toBeVisible({ timeout: 10000 })
  await send(a, `/api/fields/${f.field}/records/${f.card.id}`, { value: '校對找回已提交值', expectedVersion: 0, expectedDefinitionVersion: 1, attached: true })
  await expect(b.getByRole('button', { name: f.cell, exact: true })).toContainText('校對找回已提交值', { timeout: 10000 })
  expect(dropped).toBeGreaterThan(0)
  await ac.close(); await bc.close()
})

test('AC032：首次快照傳送途中收到更新，完成核對後才恢復操作', async ({ browser }) => {
  const ac = await browser.newContext(); const bc = await browser.newContext()
  const a = await ac.newPage(); const b = await bc.newPage()
  await login(a, 'M3 快照競態寫入者'); const f = await fixture(a)
  let fetched!: () => void; const captured = new Promise<void>(resolve => { fetched = resolve })
  let release!: () => void; const released = new Promise<void>(resolve => { release = resolve })
  await b.route('**/api/snapshot', async route => {
    const response = await route.fetch(); fetched(); await released; await route.fulfill({ response })
  }, { times: 1 })
  await b.goto('/'); await b.getByPlaceholder('輸入暱稱').fill('M3 快照接收者')
  await b.getByRole('button', { name: '進入工作區', exact: true }).click(); await captured
  await expect(b.getByText('即時同步中')).toHaveCount(0)
  await send(a, `/api/fields/${f.field}/records/${f.card.id}`, { value: '取快照途中已更新', expectedVersion: 0, expectedDefinitionVersion: 1, attached: true })
  release()
  await expect(b.getByText('即時同步中')).toBeVisible()
  await expect(b.getByRole('button', { name: f.cell, exact: true })).toContainText('取快照途中已更新')
  await ac.close(); await bc.close()
})
