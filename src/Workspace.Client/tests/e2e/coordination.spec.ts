import { expect, test } from '@playwright/test'

test('兩個工作階段競爭同帳號不同區域時只有一方成功', async ({ browser }, testInfo) => {
  const firstContext = await browser.newContext()
  const secondContext = await browser.newContext()
  const first = await firstContext.newPage()
  const second = await secondContext.newPage()
  await Promise.all([first.goto('/'), second.goto('/')])
  await Promise.all([first.getByRole('button', { name: '小明', exact: true }).click(), second.getByRole('button', { name: '小林', exact: true }).click()])
  await Promise.all([expect(first.getByText('區域協調看板')).toBeVisible(), expect(second.getByText('區域協調看板')).toBeVisible()])

  const firstCard = first.locator('article.card').filter({ hasText: '青鳥一號' })
  const secondCard = second.locator('article.card').filter({ hasText: '青鳥二號' })
  await Promise.all([
    firstCard.getByRole('button', { name: '預約迷霧森林' }).click(),
    secondCard.getByRole('button', { name: '預約赤色峽谷' }).click(),
  ])

  await expect(first.getByText(/狀態已保存|資料已由其他人更新/)).toBeVisible()
  await expect(second.getByText(/狀態已保存|資料已由其他人更新/)).toBeVisible()
  const successes = await Promise.all([first.getByText('狀態已保存。').count(), second.getByText('狀態已保存。').count()])
  expect(successes.filter(Boolean)).toHaveLength(1)
  await first.screenshot({ path: testInfo.outputPath('雙工作階段區域衝突.png'), fullPage: true })
  await firstContext.close(); await secondContext.close()
})
