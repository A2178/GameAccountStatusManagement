import type { ProgressionCommand, ProgressionOperation } from './contracts.generated'
export type PendingProgression = { cardId: string; cardName: string; command: ProgressionCommand }
export const pendingKey = (participantId: string) => `workspace:pending-progression:${participantId}`
export function command(operation: ProgressionOperation, expectedVersion: number, values: Partial<ProgressionCommand> = {}): ProgressionCommand {
  return { requestId: crypto.randomUUID(), operation, expectedVersion, profileId: null, level: 0, taskItems: 0, meritBalance: 0,
    amount: 0, expectedCreditVersion: 0, activityId: null, expectedActivityVersion: 0, channel: '', occurredAt: null, reason: '', replacementName: '', ...values }
}
export function pendingOperation(storage: Storage, participantId: string): PendingProgression | undefined {
  try {
    const json = storage.getItem(pendingKey(participantId)); if (!json) return
    const value = JSON.parse(json) as PendingProgression; if (value.cardId && value.command?.requestId) return value
  } catch { /* Storage denial or a malformed local draft cannot crash the workspace. */ }
}
export function workspaceTime(value: string | null, timeZone: string) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('zh-TW', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' }).format(new Date(value))
}
