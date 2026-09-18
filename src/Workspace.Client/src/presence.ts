import type { PresenceMemberDto } from './contracts.generated'
export function targetMembers(members: PresenceMemberDto[], self: string, recordId: string, fieldId?: string, shared = false) {
  return members.filter(member => member.participantId !== self && member.targets.some(target =>
    (shared && fieldId ? target.recordId === null : target.recordId === recordId) &&
    (!fieldId || target.fieldId === fieldId)))
}
export function memberLabel(member: PresenceMemberDto) { return `${member.nickname} #${member.shortCode}` }
