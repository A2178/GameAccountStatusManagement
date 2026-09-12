import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import App from './App.vue'

describe('App', () => {
  it('提供無密碼暱稱入口與虛構預覽使用者', () => {
    const wrapper = mount(App)
    expect(wrapper.get('h1').text()).toBe('今天用什麼暱稱？')
    expect(wrapper.text()).toContain('不需要密碼')
    expect(wrapper.text()).toContain('小明')
    expect(wrapper.text()).toContain('小林')
    expect(wrapper.text()).toContain('Admin')
  })
})
