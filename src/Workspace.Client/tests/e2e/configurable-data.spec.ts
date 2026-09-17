import { expect, test, type Page } from '@playwright/test'
import { randomUUID } from 'node:crypto'
const cardsId = '40000000-0000-0000-0000-000000000001'
const accountsId = '40000000-0000-0000-0000-000000000002'
async function login(page: Page, name = '小明') {
  await page.goto('/'); await page.getByRole('button', { name, exact: true }).click()
  await expect(page.getByText('即時同步中')).toBeVisible()
  await expect(page.getByRole('navigation')).toBeVisible()
}
async function send(page: Page, path: string, body: unknown, method = 'PUT') {
  const token = (await (await page.request.get('/api/session/csrf')).json()).token
  const response = await page.request.fetch(path, { method, data: body, headers: { 'X-CSRF-TOKEN': token } })
  expect(response.ok(), await response.text()).toBeTruthy(); return response.json()
}
function definition(collectionId: string, name: string, scope = 'Common', kind = 'Text') { return { collectionId, name, scope, kind, options: [], relationCollectionId: null, color: '#526b84', position: 5, required: false, hidden: false, expectedVersion: 0 } }
async function acceptPrompt(page: Page, text: string, action: () => Promise<unknown>) { page.once('dialog', dialog => dialog.accept(text)); await action() }

test('AC015–021：獨立值、共用值衝突及刪除定義後保留草稿', async ({ browser }, info) => {
  const aContext = await browser.newContext(); const bContext = await browser.newContext()
  const a = await aContext.newPage(); const b = await bContext.newPage()
  await login(a); await login(b, '小林')
  const suffix = randomUUID().slice(0, 6); const common = randomUUID(); const shared = randomUUID()
  const independentName = `職業 ${suffix}`; const sharedName = `共用備註 ${suffix}`
  await send(a, `/api/fields/${common}`, definition(cardsId, independentName))
  await send(a, `/api/fields/${shared}`, definition(cardsId, sharedName, 'Shared'))
  const accountSnapshot = await send(a, '/api/accounts', { displayName: `M2 測試 ${suffix}` }, 'POST')
  const account = accountSnapshot.accounts.find((x: { displayName: string }) => x.displayName === `M2 測試 ${suffix}`)
  await send(a, `/api/accounts/${account.id}/cards`, { displayName: `角色甲 ${suffix}` }, 'POST')
  await send(a, `/api/accounts/${account.id}/cards`, { displayName: `角色乙 ${suffix}` }, 'POST')
  for (const [name, text] of [[`角色甲 ${suffix}`, '戰士'], [`角色乙 ${suffix}`, '法師']]) {
    await a.getByRole('button', { name: `${name} · ${independentName}`, exact: true }).click()
    await a.getByLabel('你的草稿').fill(text); await a.getByRole('button', { name: '保存值', exact: true }).click()
    await expect(a.getByRole('dialog')).toHaveCount(0)
  }
  await expect(b.getByRole('button', { name: `角色甲 ${suffix} · ${independentName}`, exact: true })).toContainText('戰士')
  await expect(b.getByRole('button', { name: `角色乙 ${suffix} · ${independentName}`, exact: true })).toContainText('法師')
  await a.getByRole('button', { name: `角色甲 ${suffix} · ${sharedName}`, exact: true }).click()
  await b.getByRole('button', { name: `角色乙 ${suffix} · ${sharedName}`, exact: true }).click()
  await a.getByLabel('你的草稿').fill('全體同步'); await b.getByLabel('你的草稿').fill('保留我的草稿')
  await a.getByRole('button', { name: '保存值', exact: true }).click()
  await expect(b.getByTestId('latest-value')).toHaveText('全體同步')
  await expect(b.getByLabel('你的草稿')).toHaveValue('保留我的草稿')
  await expect(b.getByRole('button', { name: '保存值', exact: true })).toBeDisabled()
  await b.screenshot({ path: info.outputPath('M2-共用值草稿衝突.png'), fullPage: true })
  await b.getByRole('button', { name: '採用最新版本，保留草稿' }).click()
  await b.getByRole('button', { name: '保存值', exact: true }).click()
  await expect(a.getByRole('button', { name: `角色甲 ${suffix} · ${sharedName}`, exact: true })).toContainText('保留我的草稿')
  await b.getByRole('button', { name: `角色乙 ${suffix} · ${sharedName}`, exact: true }).click()
  await b.getByLabel('你的草稿').fill('欄位刪除也不遺失')
  await send(a, `/api/fields/${shared}/delete`, { expectedVersion: 1 }, 'POST')
  await expect(b.getByText('欄位已被刪除。草稿仍保留，可選取並複製。')).toBeVisible()
  await expect(b.getByLabel('你的草稿')).toHaveValue('欄位刪除也不遺失')
  await aContext.close(); await bContext.close()
})

test('AC018、022：設定頁建立自由表格、數字欄位與可改名視圖', async ({ page }, info) => {
  await login(page); const suffix = randomUUID().slice(0, 6); const name = `備忘表 ${suffix}`
  await page.getByRole('button', { name: '設定', exact: true }).click()
  await acceptPrompt(page, name, () => page.getByRole('button', { name: '新增自由表格', exact: true }).click())
  await page.getByRole('combobox', { name: '資料集', exact: true }).selectOption({ label: name })
  await expect(page.getByRole('heading', { name: '此資料集的分頁' })).toBeVisible()
  await page.getByRole('button', { name: '新增欄位', exact: true }).click()
  await page.getByLabel('欄位名稱', { exact: true }).fill('數量')
  await page.getByRole('combobox', { name: '型別', exact: true }).selectOption('Integer')
  await page.getByRole('button', { name: '保存欄位', exact: true }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await page.screenshot({ path: info.outputPath('M2-欄位設定.png'), fullPage: true })
  await page.getByRole('navigation').getByRole('button', { name, exact: true }).click()
  await acceptPrompt(page, '虛構備忘', () => page.getByRole('button', { name: '新增資料列', exact: true }).click())
  await page.getByRole('button', { name: '虛構備忘 · 數量', exact: true }).click()
  await page.getByLabel('你的草稿').fill('0'); await page.getByRole('button', { name: '保存值', exact: true }).click()
  await expect(page.getByRole('button', { name: '虛構備忘 · 數量', exact: true })).toHaveText('0')
  await page.getByRole('button', { name: '調整視圖', exact: true }).click()
  await page.getByLabel('分頁名稱', { exact: true }).fill(`${name} 新名稱`)
  await page.getByRole('button', { name: '保存視圖', exact: true }).click()
  await expect(page.getByRole('navigation').getByRole('button', { name: `${name} 新名稱`, exact: true })).toBeVisible()
  await page.getByLabel('篩選目前視圖').fill('沒有這筆'); await expect(page.getByText('目前沒有符合條件的資料。')).toBeVisible()
  await page.getByLabel('篩選目前視圖').fill('虛構')
  await page.screenshot({ path: info.outputPath('M2-自由表格.png'), fullPage: true })
  await page.reload(); await page.getByRole('navigation').getByRole('button', { name: `${name} 新名稱`, exact: true }).click()
  await expect(page.getByRole('button', { name: '虛構備忘 · 數量', exact: true })).toHaveText('0')
})

test('各型別的儲存格編輯器可保存並重新讀取選項與關聯', async ({ page }) => {
  await login(page)
  const name = `型別測試 ${randomUUID().slice(0, 6)}`
  await send(page, '/api/collections', { name }, 'POST')
  const config = await (await page.request.get('/api/configuration')).json()
  const collection = config.collections.find((x: { name: string }) => x.name === name)
  await send(page, `/api/collections/${collection.id}/records`, { name: '型別資料列' }, 'POST')
  const option1 = randomUUID(); const option2 = randomUUID()
  const values: [string, unknown][] = [['SingleSelect', option1], ['MultiSelect', [option1, option2]], ['Checkbox', false], ['Marker', true], ['Date', '2026-09-17'], ['DateTime', '2026-09-17T12:34'], ['Number', '1.25'], ['Relation', '10000000-0000-0000-0000-000000000001']]
  for (const [kind] of values) await send(page, `/api/fields/${randomUUID()}`, { ...definition(collection.id, kind, 'Common', kind), options: [{ id: option1, name: '選項甲', color: '#ffffff' }, { id: option2, name: '選項乙', color: '#000000' }], relationCollectionId: kind === 'Relation' ? accountsId : null })
  await page.getByRole('navigation').getByRole('button', { name, exact: true }).click()
  for (const [kind, value] of values) {
    await page.getByRole('button', { name: `型別資料列 · ${kind}`, exact: true }).click()
    const dialog = page.getByRole('dialog')
    if (kind === 'MultiSelect') await dialog.getByRole('listbox').selectOption(value as string[])
    else if (['SingleSelect', 'Relation', 'Checkbox', 'Marker'].includes(kind)) {
      if (typeof value === 'boolean') await dialog.getByRole('combobox').selectOption({ label: value ? '是' : '否' })
      else await dialog.getByRole('combobox').selectOption(String(value))
    } else await page.getByLabel('你的草稿').fill(String(value))
    await page.getByRole('button', { name: '保存值', exact: true }).click()
    await expect(dialog).toHaveCount(0)
    await expect(page.getByRole('button', { name: `型別資料列 · ${kind}`, exact: true })).not.toHaveText('—')
  }
  await page.getByRole('button', { name: '型別資料列 · MultiSelect', exact: true }).click()
  await expect(page.getByRole('listbox')).toHaveValues([option1, option2])
})

test('AC014、022：帳密直接顯示，改帳號別名仍共用既有帳號', async ({ page }, info) => {
  await login(page); const suffix = randomUUID().slice(0, 6); const name = `虛構帳號 ${suffix}`
  const snapshot = await send(page, '/api/accounts', { displayName: name }, 'POST')
  const account = snapshot.accounts.find((x: { displayName: string }) => x.displayName === name)
  await send(page, `/api/accounts/${account.id}/cards`, { displayName: `關聯角色 ${suffix}` }, 'POST')
  await page.getByRole('navigation').getByRole('button', { name: '帳號', exact: true }).click()
  await page.getByRole('button', { name: `${name} · 密碼`, exact: true }).click()
  await expect(page.getByLabel('你的草稿')).toHaveAttribute('type', 'text')
  await page.getByLabel('你的草稿').fill('fictional-visible-password')
  await page.getByRole('button', { name: '保存值', exact: true }).click()
  await expect(page.getByRole('button', { name: `${name} · 密碼`, exact: true })).toHaveText('fictional-visible-password')
  await acceptPrompt(page, `${name} 別名`, () => page.getByRole('button', { name, exact: true }).click())
  await expect(page.getByRole('button', { name: `${name} 別名`, exact: true })).toBeVisible()
  const data = await (await page.request.get(`/api/collections/${accountsId}`)).json()
  expect(data.records.find((x: { name: string }) => x.name === `${name} 別名`).id).toBe(account.id)
  await page.screenshot({ path: info.outputPath('M2-虛構帳密表.png'), fullPage: true })
  await page.getByRole('button', { name: '區域操作', exact: true }).click()
  await expect(page.locator('section.account').filter({ hasText: `${name} 別名` })).toContainText(`關聯角色 ${suffix}`)
})
