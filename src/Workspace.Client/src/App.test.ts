import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import App from './App.vue'

describe('App', () => {
  it('clearly identifies the preview baseline', () => {
    const wrapper = mount(App)
    expect(wrapper.get('h1').text()).toBe('協作工作區')
    expect(wrapper.text()).toContain('不含正式資料')
  })
})
