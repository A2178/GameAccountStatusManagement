<script setup lang="ts">
import { ref } from 'vue'
import { write } from './api'
import type { ConfigurationDto, FieldDto, SaveViewCommand, ViewDto } from './contracts.generated'
const props = defineProps<{ view?: ViewDto; collectionId: string; fields: FieldDto[]; config: ConfigurationDto; online: boolean }>()
const emit = defineEmits<{ close: []; saved: [] }>()
const id = props.view?.id ?? crypto.randomUUID()
const form = ref<SaveViewCommand>({ collectionId: props.collectionId, name: '', display: 'Table', position: 10, stageId: null, hiddenFields: [], sortFieldId: null, descending: false, filterFieldId: null, filterText: '', ...JSON.parse(JSON.stringify(props.view ?? {})), expectedVersion: props.view?.version ?? 0 })
const error = ref(''); const busy = ref(false)
async function save() { busy.value = true; try { await write(`/api/views/${id}`, form.value); emit('saved') } catch (e) { error.value = (e as Error).message } finally { busy.value = false } }
</script>
<template>
  <div class="modal-backdrop"><section class="editor" role="dialog" aria-modal="true" aria-labelledby="view-title"><h2 id="view-title">{{ view ? '編輯視圖' : '新增視圖' }}</h2>
    <form @submit.prevent="save"><label>分頁名稱<input v-model="form.name" maxlength="80" required></label>
      <label>顯示方式<select v-model="form.display"><option value="Table">表格</option><option value="Board">看板</option><option value="Dashboard">簡易儀表板</option><option value="Panel">精簡面板</option></select></label>
      <label>分頁順序<input v-model.number="form.position" type="number" required></label>
      <label v-if="config.collections.find(x => x.id === collectionId)?.kind === 'Cards'">階段篩選<select v-model="form.stageId"><option :value="null">全部</option><option v-for="stage in config.stages" :key="stage.id" :value="stage.id">{{ stage.name }}</option></select></label>
      <fieldset><legend>隱藏欄位</legend><label v-for="field in fields" :key="field.id" class="check"><input v-model="form.hiddenFields" type="checkbox" :value="field.id">{{ field.name }}</label></fieldset>
      <label>排序欄位<select v-model="form.sortFieldId"><option :value="null">名稱</option><option v-for="field in fields" :key="field.id" :value="field.id">{{ field.name }}</option></select></label><label class="check"><input v-model="form.descending" type="checkbox">倒序</label>
      <label>篩選欄位<select v-model="form.filterFieldId"><option :value="null">名稱</option><option v-for="field in fields" :key="field.id" :value="field.id">{{ field.name }}</option></select></label><label>包含文字<input v-model="form.filterText"></label>
      <p v-if="error" role="alert" class="error">{{ error }} 草稿仍保留。</p><div class="actions"><button :disabled="busy || !online">保存視圖</button><button type="button" class="secondary" @click="emit('close')">關閉</button></div>
    </form></section></div>
</template>
