<script setup lang="ts">
import { computed, ref, shallowRef } from 'vue'
import { ApiError, write } from './api'
import { displayValue } from './fieldValues'
import PresenceBadges from './PresenceBadges.vue'
import type { PresenceMemberDto } from './contracts.generated'
import type { FieldDto, JsonValue, RecordDto, SaveValueCommand, ValueDto } from './contracts.generated'
const props = defineProps<{ field: FieldDto; initial: ValueDto; latestField?: FieldDto; latest: ValueDto; record: RecordDto; relations: RecordDto[]; online: boolean; members: PresenceMemberDto[]; self: string }>()
const emit = defineEmits<{ close: []; saved: []; refresh: [] }>()
const draft = shallowRef<JsonValue>(JSON.parse(JSON.stringify(props.initial.value)))
const expected = ref(props.initial.version)
const definition = ref(JSON.parse(JSON.stringify(props.field)) as FieldDto)
const error = ref('')
const busy = ref(false)
const serverCurrent = shallowRef<ValueDto>()
const current = computed(() => serverCurrent.value && serverCurrent.value.version > props.latest.version ? serverCurrent.value : props.latest)
const changed = computed(() => !props.latestField || current.value.version !== expected.value || props.latestField.version !== definition.value.version)
const multiDraft = computed<string[]>({ get: () => Array.isArray(draft.value) ? draft.value.filter((x): x is string => typeof x === 'string') : [], set: value => { draft.value = value } })
const dateInput = computed(() => {
  if (!draft.value) return ''
  const date = new Date(String(draft.value)); return Number.isNaN(date.getTime()) ? '' : new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 19)
})
function input(event: Event) {
  const value = (event.target as HTMLInputElement).value
  draft.value = value === '' ? null : definition.value.kind === 'Integer' || definition.value.kind === 'Number' ? Number(value) : value
}
function rebase() {
  if (!props.latestField) return
  definition.value = JSON.parse(JSON.stringify(props.latestField)) as FieldDto; expected.value = current.value.version; error.value = ''
}
async function save(attached = true) {
  busy.value = true; error.value = ''
  try {
    const body: SaveValueCommand = { value: draft.value, expectedVersion: expected.value, expectedDefinitionVersion: definition.value.version, attached }
    await write(`/api/fields/${definition.value.id}/${definition.value.scope === 'Shared' ? 'shared' : `records/${props.record.id}`}`, body)
    emit('saved')
  } catch (e) { error.value = e instanceof Error ? e.message : '保存失敗'; if (e instanceof ApiError && e.current) serverCurrent.value = e.current; emit('refresh') }
  finally { busy.value = false }
}
</script>
<template>
  <div class="modal-backdrop"><section class="editor" role="dialog" aria-modal="true" aria-labelledby="value-title">
    <h2 id="value-title">{{ field.name }} · {{ record.name }}</h2>
    <PresenceBadges :members="members" :self="self" :record-id="record.id" :field-id="field.id" :shared="field.scope === 'Shared'" />
    <p v-if="field.scope === 'Shared'" class="notice">這是共用值，保存後會更新此資料集的所有資料列。</p>
    <form @submit.prevent="save()">
      <label>你的草稿
        <select v-if="definition.kind === 'SingleSelect' || definition.kind === 'Relation'" v-model="draft"><option :value="null">空值</option><option v-for="option in definition.kind === 'Relation' ? relations : definition.options" :key="option.id" :value="option.id">{{ option.name }}</option></select>
        <select v-else-if="definition.kind === 'MultiSelect'" v-model="multiDraft" multiple><option v-for="option in definition.options" :key="option.id" :value="option.id">{{ option.name }}</option></select>
        <select v-else-if="definition.kind === 'Checkbox' || definition.kind === 'Marker'" v-model="draft"><option :value="null">空值</option><option :value="true">是</option><option :value="false">否</option></select>
        <input v-else-if="definition.kind === 'DateTime'" type="datetime-local" step="1" :value="dateInput" @input="draft = ($event.target as HTMLInputElement).value ? new Date(($event.target as HTMLInputElement).value).toISOString() : null">
        <input v-else :type="definition.kind === 'Date' ? 'date' : ['Integer', 'Number'].includes(definition.kind) ? 'number' : 'text'" :step="definition.kind === 'Integer' ? '1' : 'any'" :value="draft ?? ''" @input="input" autofocus>
      </label>
      <p v-if="changed" class="notice">{{ latestField ? '資料或欄位定義已更新。你的草稿仍保留，請先比較。' : '欄位已被刪除。草稿仍保留，可選取並複製。' }}</p>
      <p>最新值：<strong data-testid="latest-value">{{ displayValue(latestField ?? field, current.value, relations) }}</strong></p>
      <button v-if="changed && latestField" type="button" class="secondary" @click="rebase">採用最新版本，保留草稿</button>
      <p v-if="error" role="alert" class="error">{{ error }}</p>
      <div class="actions"><button :disabled="busy || !online || changed">保存值</button><button type="button" class="secondary" @click="draft = null">清空草稿</button><button v-if="field.scope === 'Individual' && initial.attached" type="button" :disabled="busy || !online || changed" class="secondary" @click="save(false)">移除此標籤</button><button type="button" class="secondary" @click="emit('close')">關閉</button></div>
    </form>
  </section></div>
</template>
