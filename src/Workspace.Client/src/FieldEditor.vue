<script setup lang="ts">
import { ref } from 'vue'
import { write } from './api'
import type { CollectionDefinition, FieldDto, FieldKind, SaveFieldCommand } from './contracts.generated'
const props = defineProps<{ field?: FieldDto; collectionId: string; collections: CollectionDefinition[]; online: boolean }>()
const emit = defineEmits<{ close: []; saved: [] }>()
const newId = () => crypto.randomUUID(); const id = props.field?.id ?? crypto.randomUUID()
const form = ref<SaveFieldCommand>({ collectionId: props.collectionId, name: '', kind: 'Text', scope: 'Common', options: [], relationCollectionId: null, color: '#526b84', position: 10, required: false, hidden: false, ...JSON.parse(JSON.stringify(props.field ?? {})), expectedVersion: props.field?.version ?? 0 })
const kinds: Record<FieldKind, string> = { Text: '文字', Integer: '整數', Number: '數字', SingleSelect: '單選', MultiSelect: '多選', Checkbox: '核取方塊', Date: '日期', DateTime: '日期時間', Relation: '資料關聯', Marker: '純標記' }
const error = ref(''); const busy = ref(false)
async function save() { busy.value = true; try { await write(`/api/fields/${id}`, form.value); emit('saved') } catch (e) { error.value = (e as Error).message } finally { busy.value = false } }
function move(index: number, offset: number) { const options = form.value.options; const item = options.splice(index, 1)[0]; options.splice(index + offset, 0, item) }
</script>
<template>
  <div class="modal-backdrop"><section class="editor" role="dialog" aria-modal="true" aria-labelledby="field-title">
    <h2 id="field-title">{{ field ? '編輯欄位' : '新增欄位' }}</h2>
    <p>{{ field?.binding === 'Resource' ? '綁定正式區域狀態；調整外觀不改變占用。' : '一般欄位；名稱不會改變核心規則。' }}</p>
    <form @submit.prevent="save">
      <label>欄位名稱<input v-model="form.name" maxlength="80" required></label>
      <label>型別<select v-model="form.kind" :disabled="field?.binding === 'Resource'"><option v-for="(label, key) in kinds" :key="key" :value="key">{{ label }}</option></select></label>
      <label>值範圍<select v-model="form.scope" :disabled="field?.binding === 'Resource'"><option value="Common">共通標籤，各自填值</option><option value="Shared">共通標籤，共用一個值</option><option value="Individual">個別標籤，手動掛載</option></select></label>
      <label v-if="form.kind === 'Relation'">關聯資料集<select v-model="form.relationCollectionId" required><option v-for="collection in collections.filter(x => x.kind !== 'Free')" :key="collection.id" :value="collection.id">{{ collection.name }}</option></select></label>
      <fieldset v-if="form.kind === 'SingleSelect' || form.kind === 'MultiSelect'"><legend>選項（改名保留原值）</legend><div v-for="(option, index) in form.options" :key="option.id" class="option-row"><input v-model="option.name" :aria-label="`選項 ${index + 1}`" maxlength="80" required><input v-model="option.color" type="color" aria-label="選項顏色"><button type="button" :disabled="index === 0" @click="move(index, -1)">↑</button><button type="button" :disabled="index === form.options.length - 1" @click="move(index, 1)">↓</button><button type="button" @click="form.options.splice(index, 1)">移除</button></div><button type="button" @click="form.options.push({ id: newId(), name: '', color: '#526b84' })">新增選項</button></fieldset>
      <div class="form-grid"><label>顏色<input v-model="form.color" type="color"></label><label>順序<input v-model.number="form.position" type="number" required></label></div>
      <label class="check"><input v-model="form.required" type="checkbox" :disabled="field?.binding === 'Resource'">必填</label><label class="check"><input v-model="form.hidden" type="checkbox">隱藏欄位外觀</label>
      <p v-if="error" role="alert" class="error">{{ error }} 草稿仍保留。</p>
      <div class="actions"><button :disabled="busy || !online">保存欄位</button><button type="button" class="secondary" @click="emit('close')">關閉</button></div>
    </form>
  </section></div>
</template>
