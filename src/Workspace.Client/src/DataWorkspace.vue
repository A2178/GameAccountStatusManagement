<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { request, write } from './api'
import { compareValues, displayValue, fieldValue } from './fieldValues'
import FieldEditor from './FieldEditor.vue'
import ValueEditor from './ValueEditor.vue'
import ViewEditor from './ViewEditor.vue'
import PresenceBadges from './PresenceBadges.vue'
import type { PresenceCommand, PresenceMemberDto } from './contracts.generated'
import type { CollectionSnapshotDto, ConfigurationDto, FieldDto, RecordDto, SaveSettingsCommand, ValueDto, ViewDto, WorkspaceSnapshotDto } from './contracts.generated'
const props = defineProps<{ snapshot?: WorkspaceSnapshotDto; online: boolean; members: PresenceMemberDto[]; self: string }>()
const emit = defineEmits<{ refresh: []; focus: [target: PresenceCommand]; syncFailed: [] }>()
const displayNames: Record<string, string> = { Table: '表格', Board: '看板', Dashboard: '簡易儀表板', Panel: '精簡面板' }
const cardsId = '40000000-0000-0000-0000-000000000001'
const config = ref<ConfigurationDto>()
const data = ref<CollectionSnapshotDto>()
const active = ref('coordination')
const settingsCollection = ref(cardsId)
const message = ref(''); const busy = ref(false); const filter = ref('')
const syncError = ref('')
const sort = ref(''); const descending = ref(false)
const fieldEditor = ref<{ field?: FieldDto }>()
const viewEditor = ref<{ view?: ViewDto }>()
const editing = ref<{ field: FieldDto; record: RecordDto; initial: ValueDto }>()
const settingsDraft = ref<SaveSettingsCommand>()
const view = computed(() => config.value?.views.find(x => x.id === active.value))
const collectionId = computed(() => active.value === 'settings' ? settingsCollection.value : view.value?.collectionId ?? cardsId)
const loading = ref(false)
const online = computed(() => props.online && !loading.value && data.value?.collection.id === collectionId.value)
const fields = computed(() => data.value?.fields.filter(x => !x.hidden && !view.value?.hiddenFields.includes(x.id)) ?? [])
const relations = computed<RecordDto[]>(() => [
  ...(props.snapshot?.accounts.map(a => ({ id: a.id, name: a.displayName, accountId: null, stageId: null, version: 0 })) ?? []),
  ...(props.snapshot?.accounts.flatMap(a => a.cards.map(c => ({ id: c.id, name: c.displayName, accountId: a.id, stageId: null, version: 0 }))) ?? [])
])
const relationChoices = computed(() => editing.value?.field.relationCollectionId === cardsId ? relations.value.filter(x => x.accountId) : relations.value.filter(x => !x.accountId))
function value(field: FieldDto, record: RecordDto) { return fieldValue(data.value?.values ?? [], field, record.id) }
function text(field: FieldDto, record: RecordDto) { const v = value(field, record); return v.attached ? displayValue(field, v.value, relations.value, config.value?.regions) : '＋掛上標籤' }
function textById(field: FieldDto, id: string) { const record = data.value?.records.find(x => x.id === id); return record ? text(field, record) : '—' }
function openValue(field: FieldDto, record: RecordDto) {
  if (field.binding !== 'None') { active.value = 'coordination'; return }
  editing.value = JSON.parse(JSON.stringify({ field, record, initial: value(field, record) }))
}
function openById(field: FieldDto, id: string) { const record = data.value?.records.find(x => x.id === id); if (record) openValue(field, record) }
function moveById(id: string, event: Event) { const record = data.value?.records.find(x => x.id === id); if (record) void move(record, event) }
function stageById(id: string) { return data.value?.records.find(x => x.id === id)?.stageId ?? '' }
const records = computed(() => {
  let result = [...(data.value?.records ?? [])]
  if (view.value?.stageId) result = result.filter(x => x.stageId === view.value!.stageId)
  const filterField = data.value?.fields.find(x => x.id === view.value?.filterFieldId)
  const configured = view.value?.filterText.toLocaleLowerCase() ?? ''
  if (configured) result = result.filter(x => (filterField ? text(filterField, x) : x.name).toLocaleLowerCase().includes(configured))
  if (filter.value) result = result.filter(x => [x.name, ...fields.value.map(f => text(f, x))].join(' ').toLocaleLowerCase().includes(filter.value.toLocaleLowerCase()))
  const sortField = data.value?.fields.find(x => x.id === (sort.value || view.value?.sortFieldId))
  return result.sort((a, b) => {
    const va = sortField ? value(sortField, a).value : a.name; const vb = sortField ? value(sortField, b).value : b.name
    const comparison = sortField && !['Integer', 'Number', 'Checkbox', 'Date', 'DateTime'].includes(sortField.kind) ? compareValues(text(sortField, a), text(sortField, b)) : compareValues(va, vb)
    return comparison * ((sort.value ? descending.value : view.value?.descending) ? -1 : 1)
  })
})
const latestField = computed(() => data.value?.fields.find(x => x.id === editing.value?.field.id))
const latestValue = computed(() => editing.value ? fieldValue(data.value?.values ?? [], latestField.value ?? editing.value.field, editing.value.record.id) : undefined)
let pending: Promise<void> | undefined
async function refresh(fresh = false): Promise<void> {
  if (pending) { await pending; if (!fresh) return }
  loading.value = data.value?.collection.id !== collectionId.value
  pending = (async () => {
    config.value = await request<ConfigurationDto>('/api/configuration')
    for (;;) {
      const id = collectionId.value
      const nextData = await request<CollectionSnapshotDto>(`/api/collections/${id}`)
      if (id !== collectionId.value) continue
      data.value = nextData; syncError.value = ''; return
    }
  })().catch(e => { syncError.value = (e as Error).message; emit('syncFailed'); throw e })
    .finally(() => { pending = undefined; loading.value = false })
  return pending
}
defineExpose({ refresh })
function safeRefresh() { void refresh().catch(() => {}) }
function focusRecord(recordId: string | null = null) {
  if (!editing.value) emit('focus', { collectionId: collectionId.value, recordId, fieldId: null, mode: 'Viewing' })
}
watch(collectionId, () => { safeRefresh(); focusRecord() })
watch(editing, item => {
  emit('focus', item
    ? { collectionId: item.field.collectionId, recordId: item.record.id, fieldId: item.field.id, mode: 'Editing' }
    : { collectionId: collectionId.value, recordId: null, fieldId: null, mode: 'Viewing' })
})
watch(active, () => { filter.value = ''; sort.value = ''; settingsDraft.value = undefined })
async function mutate(url: string, body: unknown, method = 'PUT') {
  busy.value = true; message.value = ''
  try { await write(url, body, method); await refresh(); emit('refresh'); message.value = '已保存。'; return true }
  catch (e) { message.value = (e as Error).message; return false }
  finally { busy.value = false }
}
async function saved() { fieldEditor.value = undefined; viewEditor.value = undefined; editing.value = undefined; emit('refresh') }
async function addCollection() { const name = window.prompt('新表格名稱'); if (name) await mutate('/api/collections', { name }, 'POST') }
async function addRecord() { const name = window.prompt('新資料列名稱'); if (name) await mutate(`/api/collections/${collectionId.value}/records`, { name }, 'POST') }
async function rename(target: string, id: string, name: string, version: number) { const changed = window.prompt('新名稱', name); if (changed) await mutate(`/api/names/${target}/${id}`, { name: changed, expectedVersion: version }) }
async function deleteField(field: FieldDto) { if (window.confirm(`移除「${field.name}」欄位外觀？既有區域占用不受影響。`)) await mutate(`/api/fields/${field.id}/delete`, { expectedVersion: field.version }, 'POST') }
async function move(record: RecordDto, event: Event) { const stageId = (event.target as HTMLSelectElement).value; if (stageId) await mutate(`/api/cards/${record.id}/stage`, { stageId, expectedVersion: record.version }) }
async function saveSettings() { if (settingsDraft.value && await mutate('/api/settings', settingsDraft.value)) settingsDraft.value = undefined }
function startSettings() { if (config.value) settingsDraft.value = { ...config.value.settings, expectedVersion: config.value.settings.version } }
</script>
<template>
  <section v-if="config" class="data-workspace">
    <div class="workspace-heading"><h2>{{ config.settings.name }}</h2><small>{{ config.settings.cardLabel }}與帳號共用即時資料</small></div>
    <nav class="tabs" aria-label="工作區分頁"><button :class="{ selected: active === 'coordination' }" @click="active = 'coordination'">區域操作</button><button v-for="item in config.views" :key="item.id" :class="{ selected: active === item.id }" @click="active = item.id">{{ item.name }}</button><button :class="{ selected: active === 'settings' }" @click="active = 'settings'">{{ config.settings.settingsLabel }}</button></nav>
    <p v-if="message" role="status" class="notice">{{ message }}</p>
    <p v-if="syncError" class="error">{{ syncError }}</p>
    <p v-if="!online" class="notice">資料可能過期，重新連線後才能保存；編輯中的草稿會保留。</p>
    <template v-if="active === 'settings'">
      <section class="settings-section"><h2>名稱與用語</h2><button @click="startSettings">編輯工作區用語</button>
        <form v-if="settingsDraft" class="inline-form" @submit.prevent="saveSettings"><label>工作區名稱<input v-model="settingsDraft.name" maxlength="80" required></label><label>卡片稱呼<input v-model="settingsDraft.cardLabel" maxlength="80" required></label><label>設定分頁名稱<input v-model="settingsDraft.settingsLabel" maxlength="80" required></label><button :disabled="busy || !online">保存用語</button></form>
        <div class="settings-list"><div v-for="region in config.regions" :key="region.id"><span>區域 · {{ region.name }}</span><button :disabled="!online" @click="rename('regions', region.id, region.name, region.version)">改名</button></div><div v-for="stage in config.stages" :key="stage.id"><span>階段 · {{ stage.name }}</span><button :disabled="!online" @click="rename('stages', stage.id, stage.name, stage.version)">改名</button></div></div>
      </section>
      <section class="settings-section"><h2>資料集、欄位與視圖</h2><div class="data-toolbar"><label>資料集<select v-model="settingsCollection"><option v-for="collection in config.collections" :key="collection.id" :value="collection.id">{{ collection.name }}</option></select></label><button :disabled="!online" @click="addCollection">新增自由表格</button><button v-if="data" :disabled="!online" @click="rename('collections', data.collection.id, data.collection.name, data.collection.version)">資料集改名</button><button :disabled="!online" @click="fieldEditor = {}">新增欄位</button><button :disabled="!online" @click="viewEditor = {}">新增視圖</button></div>
        <div class="settings-list"><div v-for="field in data?.fields" :key="field.id"><span><i class="field-dot" :style="{ background: field.color }"></i>{{ field.name }} <small>{{ field.binding === 'Resource' ? '正式狀態投影' : field.scope === 'Shared' ? '共用值' : field.scope === 'Individual' ? '個別標籤' : '各自填值' }}{{ field.hidden ? ' · 已隱藏' : '' }}</small></span><div><button @click="fieldEditor = { field }">編輯</button><button :disabled="!online" @click="deleteField(field)">移除</button></div></div></div>
        <h3>此資料集的分頁</h3><div class="settings-list"><div v-for="item in config.views.filter(x => x.collectionId === collectionId)" :key="item.id"><span>{{ item.name }} · {{ displayNames[item.display] }}</span><button @click="viewEditor = { view: item }">編輯視圖</button></div></div>
      </section>
    </template>
    <template v-else>
      <div v-if="active !== 'coordination'" class="data-toolbar"><h2>{{ view?.name }}</h2><input v-model="filter" aria-label="篩選目前視圖" placeholder="搜尋這個視圖"><label>排序<select v-model="sort"><option value="">視圖預設</option><option value="name">名稱</option><option v-for="field in fields" :key="field.id" :value="field.id">{{ field.name }}</option></select></label><label class="check"><input v-model="descending" type="checkbox">倒序</label><button v-if="data?.collection.kind === 'Free'" :disabled="!online" @click="addRecord">新增資料列</button><button v-if="view" @click="viewEditor = { view }">調整視圖</button></div>
      <slot v-if="active === 'coordination' || (view?.display === 'Board' && data?.collection.kind === 'Cards')" :fields="fields" :open-value="openById" :value-text="textById" :record-ids="active === 'coordination' ? undefined : records.map(x => x.id)" :card-label="config.settings.cardLabel" :stages="config.stages" :move-stage="moveById" :stage-id="stageById" :focus-record="focusRecord"></slot>
      <template v-else-if="data">
        <div v-if="view?.display === 'Dashboard'" class="metrics"><article><strong>{{ records.length }}</strong><span>符合篩選的資料</span></article><article><strong>{{ fields.length }}</strong><span>顯示欄位</span></article><article v-for="field in fields.filter(x => x.kind === 'Integer' || x.kind === 'Number')" :key="field.id"><strong>{{ records.reduce((sum, r) => sum + Number(value(field, r).value ?? 0), 0) }}</strong><span>{{ field.name }} · {{ field.scope === 'Shared' ? '共用值逐列加總' : '合計' }}</span></article></div>
        <div v-if="view?.display === 'Board'" class="cards"><article v-for="record in records" :key="record.id" class="card" tabindex="0" @focusin="focusRecord(record.id)"><h3>{{ record.name }}</h3><PresenceBadges :members="members" :self="self" :record-id="record.id" /><button v-for="field in fields" :key="field.id" class="field-chip" @click="openValue(field, record)">{{ field.name }}：{{ text(field, record) }}<PresenceBadges :members="members" :self="self" :record-id="record.id" :field-id="field.id" :shared="field.scope === 'Shared'" /></button></article></div>
        <div v-else class="table-scroll" :class="{ compact: view?.display === 'Panel' }"><table><thead><tr><th>名稱</th><th v-if="data.collection.kind === 'Cards'">所屬帳號／階段</th><th v-for="field in fields" :key="field.id"><i class="field-dot" :style="{ background: field.color }"></i>{{ field.name }}<small v-if="field.scope === 'Shared'"> · 共用</small></th><th v-if="data.collection.kind === 'Cards'">區域操作</th></tr></thead><tbody><tr v-for="record in records" :key="record.id" :data-record="record.id" @focusin="focusRecord(record.id)"><th><button class="plain" :disabled="!online" @click="rename(data.collection.kind === 'Accounts' ? 'accounts' : data.collection.kind === 'Cards' ? 'cards' : 'records', record.id, record.name, record.version)">{{ record.name }}</button><PresenceBadges :members="members" :self="self" :record-id="record.id" /></th><td v-if="data.collection.kind === 'Cards'"><span>{{ snapshot?.accounts.find(x => x.id === record.accountId)?.displayName }}</span><select :value="record.stageId ?? ''" :disabled="busy || !online" aria-label="移動階段" @change="move(record, $event)"><option value="" disabled>未指定</option><option v-for="stage in config.stages" :key="stage.id" :value="stage.id">{{ stage.name }}</option></select></td><td v-for="field in fields" :key="field.id"><button class="cell" :aria-label="`${record.name} · ${field.name}`" @click="openValue(field, record)">{{ text(field, record) }}</button><PresenceBadges :members="members" :self="self" :record-id="record.id" :field-id="field.id" :shared="field.scope === 'Shared'" /></td><td v-if="data.collection.kind === 'Cards'"><button @click="active = 'coordination'">預約／回村</button></td></tr></tbody></table><p v-if="records.length === 0" class="empty">目前沒有符合條件的資料。</p></div>
      </template>
    </template>
    <FieldEditor v-if="fieldEditor" :field="fieldEditor.field" :collection-id="collectionId" :collections="config.collections" :online="online" @close="fieldEditor = undefined" @saved="saved" />
    <ViewEditor v-if="viewEditor" :view="viewEditor.view" :collection-id="collectionId" :fields="data?.fields ?? []" :config="config" :online="online" @close="viewEditor = undefined" @saved="saved" />
    <ValueEditor v-if="editing && latestValue" :field="editing.field" :initial="editing.initial" :latest-field="latestField" :latest="latestValue" :record="editing.record" :relations="relationChoices" :online="online" :members="members" :self="self" @close="editing = undefined" @saved="saved" @refresh="safeRefresh" />
  </section>
</template>
