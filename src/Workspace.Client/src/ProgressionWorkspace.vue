<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ApiError, write } from './api'
import { command, pendingKey, pendingOperation, workspaceTime, type PendingProgression } from './progressionCommands'
import type { ActivityDto, ProgressionCommand, ProgressionOperation, ProgressionProfile, ProgressionResultDto, ProgressionSnapshotDto, SaveProfileCommand, SaveProgressionSettingsCommand, SaveTransitionCommand } from './contracts.generated'
const props = defineProps<{ data: ProgressionSnapshotDto; online: boolean; self: string }>()
const emit = defineEmits<{ refresh: []; focus: [recordId: string] }>()
const pending = ref<PendingProgression | undefined>(pendingOperation(sessionStorage, props.self))
const selected = ref(pending.value?.cardId ?? ''); const archived = ref(false); const settingsOpen = ref(false)
const busy = ref(false); const message = ref(''); const error = ref('')
const committedVersion = ref(0)
const card = computed(() => props.data.cards.find(x => x.cardId === selected.value))
const cards = computed(() => props.data.cards.filter(x => archived.value || !x.archivedAt))
const cycle = computed(() => card.value?.cycles.find(x => !x.invalidatedAt))
const activeActivity = computed(() => card.value?.activities.find(x => !x.endedAt))
const disabled = computed(() => !props.online || busy.value || !!pending.value || props.data.version < committedVersion.value)
const statuses: Record<string, string> = { NotConfigured: '尚未設定職業', Accumulating: '累積中', Waiting: '等待換日', Eligible: '可投入', Active: '已啟用資格', NeedsRequalification: '需要重刷', Disqualified: '永久失格', Archived: '已封存' }
const requirements: Record<string, string> = { None: '無額外條件', Level: '等級達標', Task: '等級及任務完成', Qualification: '本輪可投入' }
const kinds = { Income: '活動所得', Conversion: '功勳兌換', Correction: '累積更正' }
const draft = ref({ profileId: '', level: 0, taskItems: 0, meritBalance: 0, expectedVersion: 0 })
const income = ref<number>(); const conversion = ref<number>(); const correctedTotal = ref<number>(); const correctionReason = ref(''); const creditVersion = ref(0)
const channel = ref(''); const actualEntry = ref(''); const invalidReason = ref(''); const remainingMerit = ref(0); const replacementName = ref('')
const correction = ref<{ activity: ActivityDto; channel: string; occurredAt: string; reason: string }>()
const profileDraft = ref<(SaveProfileCommand & { id: string })>()
const rules = ref<SaveProgressionSettingsCommand>()
const transition = ref<(SaveTransitionCommand & { id: string })>()
function time(value: string | null) { return workspaceTime(value, props.data.settings.timeZoneId) }
function loadDraft() {
  if (!card.value) return
  draft.value = { profileId: card.value.profileId ?? '', level: card.value.level, taskItems: card.value.taskItems, meritBalance: card.value.meritBalance, expectedVersion: card.value.version }
  correctedTotal.value = card.value.cumulativeCredits; creditVersion.value = card.value.creditVersion
}
watch(() => props.data.cards, () => { if (!selected.value && cards.value[0]) selected.value = cards.value[0].cardId }, { immediate: true })
watch(selected, id => {
  loadDraft(); correction.value = undefined; error.value = ''; message.value = ''
  income.value = undefined; conversion.value = undefined; channel.value = ''; actualEntry.value = ''
  invalidReason.value = ''; remainingMerit.value = 0; replacementName.value = ''; correctionReason.value = ''
  emit('focus', id)
}, { immediate: true })
async function sendPending() {
  if (!pending.value || !props.online || busy.value) return
  busy.value = true; error.value = ''; message.value = ''
  try {
    const result = await write<ProgressionResultDto>(`/api/cards/${pending.value.cardId}/progression`, pending.value.command, 'POST')
    // Wait for the committed snapshot before enabling another versioned command.
    committedVersion.value = result.workspaceVersion
    if (pending.value.command.operation === 'Archive') archived.value = true
    sessionStorage.removeItem(pendingKey(props.self)); pending.value = undefined
    message.value = '操作已保存。'; emit('refresh')
  } catch (e) {
    error.value = (e as Error).message
    // A definite rejection did not commit. Unknown transport/5xx results retain the exact request.
    if (e instanceof ApiError && e.status && e.status >= 400 && e.status < 500 && e.status !== 408 && e.status !== 429) {
      sessionStorage.removeItem(pendingKey(props.self)); pending.value = undefined; emit('refresh')
    }
  } finally { busy.value = false }
}
async function execute(operation: ProgressionOperation, values: Partial<ProgressionCommand> = {}) {
  if (!card.value || disabled.value) return
  const next = { cardId: card.value.cardId, cardName: card.value.name, command: command(operation, card.value.version, values) }
  // Store before sending so a reload or lost response cannot generate a second operation.
  try { sessionStorage.setItem(pendingKey(props.self), JSON.stringify(next)) } catch { error.value = '無法保留重試識別，請允許此頁面的工作階段儲存後再操作。'; return }
  pending.value = next; await sendPending()
}
function reportProgress() { void execute('ReportProgress', { ...draft.value, profileId: draft.value.profileId || null }) }
function startActivity() { void execute('StartActivity', { channel: channel.value, occurredAt: actualEntry.value ? new Date(actualEntry.value).toISOString() : null }) }
function endActivity(activity: ActivityDto) { void execute('EndActivity', { activityId: activity.id, expectedActivityVersion: activity.version }) }
function startCorrection(activity: ActivityDto) { correction.value = { activity, channel: activity.channel, occurredAt: activity.occurredAt, reason: '' } }
async function correctActivity() {
  if (!correction.value) return
  const occurredAt = new Date(correction.value.occurredAt)
  if (Number.isNaN(occurredAt.getTime()) || !/(Z|[+-]\d{2}:\d{2})$/i.test(correction.value.occurredAt)) {
    error.value = '請輸入有效時間並包含時區，例如 2026-09-19T04:00:00+08:00。'; return
  }
  await execute('CorrectActivity', { activityId: correction.value.activity.id, expectedActivityVersion: correction.value.activity.version, channel: correction.value.channel,
    occurredAt: occurredAt.toISOString(), reason: correction.value.reason })
  if (!error.value) correction.value = undefined
}
function confirmState(operation: 'Requalify' | 'Disqualify' | 'InvalidateQualification' | 'Archive') {
  if (window.confirm(operation === 'Archive' ? '封存這張卡片並保留歷史？必須先結束活動及解除野外占用。' : operation === 'Disqualify' ? '確認永久失格？此旗標無法透過改功勳或階段恢復。' : '確認使本輪資格失效？既有所得與養成成果會保留。')) void execute(operation, { reason: invalidReason.value, meritBalance: remainingMerit.value })
}
async function configure(path: string, body: unknown) {
  if (disabled.value) return
  busy.value = true; error.value = ''
  try { await write(path, body); profileDraft.value = undefined; rules.value = undefined; transition.value = undefined; message.value = '設定已保存。'; emit('refresh') }
  catch (e) { error.value = (e as Error).message }
  finally { busy.value = false }
}
function editProfile(profile?: ProgressionProfile) {
  profileDraft.value = profile ? { id: profile.id, name: profile.name, levelTarget: profile.levelTarget, taskItemTarget: profile.taskItemTarget, meritTarget: profile.meritTarget, expectedVersion: profile.version }
    : { id: crypto.randomUUID(), name: '', levelTarget: 1, taskItemTarget: 0, meritTarget: 1, expectedVersion: 0 }
}
</script>
<template>
  <section class="progression-workspace">
    <div class="data-toolbar"><h2>養成與活動</h2><label>選擇卡片<select v-model="selected"><option value="" disabled>請選擇</option><option v-for="item in cards" :key="item.cardId" :value="item.cardId">{{ item.name }}{{ item.archivedAt ? '（已封存）' : '' }}</option></select></label><label class="check"><input v-model="archived" type="checkbox">包含封存卡片</label><button @click="settingsOpen = !settingsOpen">職業與流程設定</button></div>
    <p class="muted">{{ data.settings.timeZoneId }} · 伺服器時間 {{ time(data.serverTime) }} · 每日 {{ String(data.settings.resetHour).padStart(2, '0') }}:00 換日</p>
    <p v-if="error" role="alert" class="error">{{ error }}</p><p v-if="message" role="status" class="notice">{{ message }}</p>
    <section v-if="pending" class="settings-section pending-operation"><h3>上次操作結果待確認 · {{ pending.cardName }}</h3><p>請保留此頁面。重新連線後使用原請求確認，避免重複計算。</p><button :disabled="busy || !online" @click="sendPending">使用原請求確認結果</button></section>
    <section v-if="settingsOpen" class="settings-section">
      <h3>職業門檻</h3><p>依實際遊戲規則設定；表單初始數字是輸入起點，需由你確認保存。修改設定不會重寫已達標的資格輪次。</p>
      <button :disabled="disabled" @click="editProfile()">新增職業門檻</button>
      <div class="settings-list"><div v-for="profile in data.profiles" :key="profile.id"><span>{{ profile.name }} · 等級 {{ profile.levelTarget }}／道具 {{ profile.taskItemTarget }}／功勳 {{ profile.meritTarget }}</span><button @click="editProfile(profile)">調整門檻</button></div></div>
      <form v-if="profileDraft" class="inline-form" @submit.prevent="configure(`/api/progression/profiles/${profileDraft.id}`, profileDraft)">
        <label>職業名稱<input v-model="profileDraft.name" maxlength="80" required></label><label>目標等級<input v-model.number="profileDraft.levelTarget" type="number" min="1" step="1" required></label><label>目標道具數<input v-model.number="profileDraft.taskItemTarget" type="number" min="0" step="1" required></label><label>目標功勳<input v-model.number="profileDraft.meritTarget" type="number" min="1" step="1" required></label><button :disabled="disabled">保存職業門檻</button>
      </form>
      <h3>時區與活動規則</h3><button @click="rules = { ...data.settings, expectedVersion: data.settings.version }">調整活動規則</button>
      <form v-if="rules" class="inline-form" @submit.prevent="configure('/api/progression/settings', rules)"><label>工作區時區<input v-model="rules.timeZoneId" placeholder="Asia/Taipei" required></label><label>重刷返回階段<select v-model="rules.accumulationStageId"><option v-for="stage in data.stages" :key="stage.id" :value="stage.id">{{ stage.name }}</option></select></label><label class="check"><input v-model="rules.conversionRequiresActiveActivity" type="checkbox">兌換時必須仍在活動中</label><button :disabled="disabled">保存活動規則</button></form>
      <h3>階段轉換</h3><div class="settings-list"><div v-for="stage in data.stages" :key="stage.id"><span>{{ stage.name }} · {{ requirements[stage.entryRequirement] }}</span><button @click="transition = { id: stage.id, requirement: stage.entryRequirement, allowedFromStageIds: JSON.parse(stage.allowedFromStageIdsJson), expectedVersion: stage.version }">設定轉換</button></div></div>
      <form v-if="transition" class="inline-form" @submit.prevent="configure(`/api/progression/stages/${transition.id}`, transition)"><label>進入條件<select v-model="transition.requirement"><option v-for="(label, key) in requirements" :key="key" :value="key">{{ label }}</option></select></label><label>允許來源階段（不選代表不限）<select v-model="transition.allowedFromStageIds" multiple><option v-for="stage in data.stages" :key="stage.id" :value="stage.id">{{ stage.name }}</option></select></label><button :disabled="disabled">保存階段轉換</button></form>
    </section>
    <template v-if="card">
      <div class="metrics"><article><strong>{{ statuses[card.qualificationStatus] }}</strong><span>參與資格</span></article><article><strong data-testid="merit-balance">{{ card.meritBalance }}</strong><span>已確認功勳</span></article><article><strong data-testid="cumulative-credits">{{ card.cumulativeCredits }}</strong><span>累積取得金幣</span></article></div>
      <p v-if="cycle" class="notice">達標：{{ time(cycle.qualifiedAt) }} · 可投入：{{ time(cycle.eligibleFrom) }}（達標時區 {{ cycle.timeZoneId }}，門檻 {{ cycle.thresholdSnapshot.name }}：{{ cycle.thresholdSnapshot.meritTarget }}）</p>
      <p v-if="card.replacesCardId">接替：{{ data.cards.find(x => x.cardId === card!.replacesCardId)?.name ?? '舊卡片' }}，所得與資格獨立。</p>
      <section v-if="!card.archivedAt" class="settings-section"><h3>確認養成進度</h3><p>記錄最後確認的數值即可。重複保存達標值不會重設等待時間；兌換後資格仍保留。</p>
        <p v-if="draft.expectedVersion !== card.version" class="notice">資料已更新；目前等級 {{ card.level }}、道具 {{ card.taskItems }}、功勳 {{ card.meritBalance }}。草稿仍保留，保存前請核對。</p><button @click="loadDraft">載入最新進度</button>
        <form class="inline-form" @submit.prevent="reportProgress"><label>職業門檻<select v-model="draft.profileId" required><option value="" disabled>先在職業與流程設定建立</option><option v-for="profile in data.profiles" :key="profile.id" :value="profile.id">{{ profile.name }}</option></select></label><label>目前等級<input v-model.number="draft.level" type="number" min="0" step="1" required></label><label>已取得道具<input v-model.number="draft.taskItems" type="number" min="0" step="1" required></label><label>最後確認功勳<input v-model.number="draft.meritBalance" type="number" min="0" step="1" required></label><button :disabled="disabled">保存養成進度</button></form>
      </section>
      <section v-if="!card.archivedAt" class="settings-section"><h3>目前活動</h3>
        <p v-if="activeActivity">分流 {{ activeActivity.channel }} · {{ time(activeActivity.occurredAt) }}<span v-if="activeActivity.operatorName"> · 操作者 {{ activeActivity.operatorName }}</span><button :disabled="disabled" @click="endActivity(activeActivity)">回報結束活動</button></p>
        <form v-else class="inline-form" @submit.prevent="startActivity"><label>分流<input v-model="channel" maxlength="80" required></label><label>補填實際入場時間（本機時區，可留空）<input v-model="actualEntry" type="datetime-local" step="1"></label><button :disabled="disabled || !['Eligible', 'Active'].includes(card.qualificationStatus)">回報已進入活動</button></form>
        <p>資格由伺服器驗證。活動分流與野外區域占用分開，結束活動不代表已回村。</p>
      </section>
      <section class="settings-section"><h3>所得與兌換</h3><p>僅記錄已完成的遊戲操作；累積取得量不因購買裝備扣除。</p>
        <form v-if="!card.archivedAt" class="inline-form" @submit.prevent="execute('RecordIncome', { amount: income })"><label>本次活動所得<input v-model.number="income" type="number" min="1" step="1" required></label><button :disabled="disabled">記錄所得</button></form>
        <form v-if="!card.archivedAt" class="inline-form" @submit.prevent="execute('RecordConversion', { amount: conversion })"><label>已完成兌換量（1:1）<input v-model.number="conversion" type="number" min="1" step="1" required></label><button :disabled="disabled || card.qualificationStatus !== 'Active'">記錄已完成兌換</button></form>
        <details><summary>更正累積取得量</summary><p>更正有版本檢查與原因紀錄；不是記錄支出。</p><button @click="correctedTotal = card.cumulativeCredits; creditVersion = card.creditVersion">載入最新累積值</button><form class="inline-form" @submit.prevent="execute('CorrectCredits', { amount: correctedTotal, expectedCreditVersion: creditVersion, reason: correctionReason })"><label>正確累積值<input v-model.number="correctedTotal" type="number" min="0" step="1" required></label><label>更正原因<input v-model="correctionReason" maxlength="80" required></label><button :disabled="disabled">保存累積更正</button></form></details>
      </section>
      <section v-if="!card.archivedAt" class="settings-section"><h3>資格與卡片處理</h3><div class="inline-form"><label>處理原因<input v-model="invalidReason" maxlength="80"></label><label>重刷後確認功勳<input v-model.number="remainingMerit" type="number" min="0" step="1"></label><button :disabled="disabled || !invalidReason" @click="confirmState('Requalify')">回報需要重刷</button><button :disabled="disabled || !invalidReason" @click="confirmState('InvalidateQualification')">撤銷本輪資格</button><button :disabled="disabled || !invalidReason" @click="confirmState('Disqualify')">回報永久失格</button><button :disabled="disabled" @click="confirmState('Archive')">封存卡片</button></div></section>
      <section v-else class="settings-section"><h3>已封存 · {{ time(card.archivedAt) }}</h3><form class="inline-form" @submit.prevent="execute('CreateReplacement', { replacementName })"><label>接替卡片名稱<input v-model="replacementName" maxlength="80" required></label><button :disabled="disabled">建立接替卡片</button></form></section>
      <details class="settings-section"><summary>活動與所得紀錄</summary><div v-for="activity in card.activities" :key="activity.id" class="activity-history"><p>分流 {{ activity.channel }} · 進入 {{ time(activity.occurredAt) }} · 回報 {{ time(activity.recordedAt) }} · {{ activity.endedAt ? `結束 ${time(activity.endedAt)}` : '進行中' }}<span v-if="activity.operatorName"> · {{ activity.operatorName }}</span></p><button :disabled="disabled || !!card.archivedAt" @click="startCorrection(activity)">更正入場紀錄</button></div>
        <form v-if="correction" class="inline-form" @submit.prevent="correctActivity"><label>更正分流<input v-model="correction.channel" required maxlength="80"></label><label>實際進入時間（含時區）<input v-model="correction.occurredAt" required placeholder="2026-09-19T04:00:00+08:00"></label><label>補填原因<input v-model="correction.reason" required maxlength="80"></label><button :disabled="disabled">保存入場更正</button></form>
        <p v-for="entry in card.entries" :key="entry.id">{{ time(entry.recordedAt) }} · {{ kinds[entry.kind] }} {{ entry.amount }} · 累積 {{ entry.totalAfter }}<span v-if="entry.reason"> · {{ entry.reason }}</span></p>
        <p v-for="item in card.cycles" :key="item.id">資格輪次 · {{ time(item.qualifiedAt) }} → {{ time(item.eligibleFrom) }}<span v-if="item.invalidatedAt"> · 已失效 {{ time(item.invalidatedAt) }}：{{ item.invalidationReason }}</span></p>
      </details>
    </template>
  </section>
</template>
