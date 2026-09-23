import { expect, it } from 'vitest'
import { targetMembers } from './presence'
import type { PresenceMemberDto } from './contracts.generated'
it('shared cell editors appear across rows, while independent cell editors stay on their row', () => {
  const shared: PresenceMemberDto = { participantId: 'a', nickname: '同名', shortCode: 'aaa', color: '#ffffff', targets: [{ collectionId: 'c', recordId: null, fieldId: 'shared', mode: 'Editing', label: '共用' }] }
  const common: PresenceMemberDto = { ...shared, participantId: 'b', targets: [{ collectionId: 'c', recordId: 'row1', fieldId: 'common', mode: 'Editing', label: '個別' }] }
  expect(targetMembers([shared, common], 'self', 'row2', 'shared', true)).toEqual([shared])
  expect(targetMembers([shared, common], 'self', 'row2', 'common')).toEqual([])
  expect(targetMembers([shared, common], 'b', 'row1', 'common')).toEqual([])
})
