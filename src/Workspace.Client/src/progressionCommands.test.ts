import { expect, it } from 'vitest'
import { command, pendingKey, pendingOperation, workspaceTime } from './progressionCommands'
it('a persisted operation retains the same request and payload after reload, isolated by participant', () => {
  sessionStorage.clear()
  const pending = { cardId: 'card', cardName: '虛構卡片', command: command('RecordConversion', 3, { amount: 300 }) }
  sessionStorage.setItem(pendingKey('a'), JSON.stringify(pending))
  expect(pendingOperation(sessionStorage, 'a')).toEqual(pending)
  expect(pendingOperation(sessionStorage, 'b')).toBeUndefined()
  expect(command('RecordConversion', 3, { amount: 300 }).requestId).not.toBe(pending.command.requestId)
})
it('time display uses the workspace zone with date and seconds across midnight', () => {
  expect(workspaceTime('2026-12-31T20:01:02Z', 'Asia/Taipei')).toContain('2027/01/01')
  expect(workspaceTime('2026-12-31T20:01:02Z', 'Asia/Taipei')).toContain('04:01:02')
  expect(workspaceTime(null, 'Asia/Taipei')).toBe('—')
})
