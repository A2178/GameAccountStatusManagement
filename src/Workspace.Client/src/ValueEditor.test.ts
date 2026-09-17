import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import ValueEditor from './ValueEditor.vue'
import { compareValues, fieldValue } from './fieldValues'
import type { FieldDto, ValueDto } from './contracts.generated'
vi.mock('./api', () => ({ write: vi.fn(), ApiError: class extends Error {} }))
const field: FieldDto = { id: 'f', collectionId: 'c', name: '功勳備註', kind: 'Integer', scope: 'Common', binding: 'None', options: [], relationCollectionId: null, color: '#526b84', position: 0, required: false, hidden: false, version: 1 }
const initial: ValueDto = { fieldId: 'f', recordId: 'r', value: 0, attached: true, version: 1 }
function editor() { return mount(ValueEditor, { props: { field, initial, latest: initial, latestField: field, record: { id: 'r', name: '甲', accountId: null, stageId: null, version: 1 }, relations: [], online: true } }) }
describe('儲存格草稿與版本', () => {
  it('遠端更新不覆蓋本地草稿，明確比較後才能再保存', async () => {
    const wrapper = editor(); await wrapper.get('input').setValue('9')
    await wrapper.setProps({ latest: { ...initial, value: 5, version: 2 } })
    expect((wrapper.get('input').element as HTMLInputElement).value).toBe('9')
    expect(wrapper.get('[data-testid="latest-value"]').text()).toBe('5')
    expect(wrapper.get('button:not([type])').attributes('disabled')).toBeDefined()
    await wrapper.findAll('button').find(x => x.text() === '採用最新版本，保留草稿')!.trigger('click')
    expect(wrapper.get('button:not([type])').attributes('disabled')).toBeUndefined()
    expect((wrapper.get('input').element as HTMLInputElement).value).toBe('9')
  })
  it('定義被刪除仍保留草稿並禁止保存', async () => {
    const wrapper = editor(); await wrapper.get('input').setValue('123')
    await wrapper.setProps({ latestField: undefined })
    expect(wrapper.text()).toContain('欄位已被刪除')
    expect((wrapper.get('input').element as HTMLInputElement).value).toBe('123')
    expect(wrapper.get('button:not([type])').attributes('disabled')).toBeDefined()
  })
  it('數字排序、空值和個別掛載具有不同意義', () => {
    expect(compareValues(2, 10)).toBeLessThan(0)
    expect(compareValues(null, 0)).toBeLessThan(0)
    expect(fieldValue([], field, 'new').value).toBeNull()
    expect(fieldValue([], { ...field, scope: 'Individual' }, 'new').attached).toBe(false)
    expect(fieldValue([{ ...initial, recordId: null }], { ...field, scope: 'Shared' }, 'any').value).toBe(0)
  })
})
