<script setup lang="ts">
import { HubConnectionBuilder, HubConnectionState, type HubConnection } from '@microsoft/signalr'
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue'
import { ensureCsrf, request, write } from './api'
import DataWorkspace from './DataWorkspace.vue'
import PresenceBadges from './PresenceBadges.vue'
import { memberLabel } from './presence'
import { SnapshotSynchronizer } from './synchronization'
import type { CurrentSessionDto as Session, RegionDto as Region, CardDto as Card, AccountDto as Account, WorkspaceSnapshotDto as Snapshot, AuditEventDto as Audit, PresenceSnapshotDto, PresenceCommand, CollaborationSettingsDto, WorkspaceVersionDto } from './contracts.generated'

const session = ref<Session>()
const nickname = ref('')
const snapshot = ref<Snapshot>()
const dataWorkspace = ref<InstanceType<typeof DataWorkspace>>()
const message = ref('')
const busy = ref(false)
const online = ref(false)
const audits = ref<Audit[]>([])
const presence = ref<PresenceSnapshotDto>()
const members = computed(() => presence.value?.members ?? [])
const allCards = computed(() => snapshot.value?.accounts.flatMap(account => account.cards.map(card => ({ account, card }))) ?? [])
let connection: HubConnection | undefined
let disposed = false
let poll: ReturnType<typeof setInterval> | undefined
let pulse: ReturnType<typeof setInterval> | undefined
let retry: ReturnType<typeof setTimeout> | undefined
let heartbeatPending = false
let heartbeatAgain = false
let focus: PresenceCommand = { collectionId: null, recordId: null, fieldId: null, mode: 'Viewing' }

function receivePresence(value: PresenceSnapshotDto) {
  if (!presence.value || value.epoch !== presence.value.epoch || value.version >= presence.value.version) presence.value = value
}
async function heartbeat() {
  if (heartbeatPending) { heartbeatAgain = true; return }
  if (connection?.state !== HubConnectionState.Connected) return
  heartbeatPending = true
  try {
    do {
      heartbeatAgain = false
      receivePresence(await connection.invoke<PresenceSnapshotDto>('Heartbeat', focus))
    } while (heartbeatAgain && connection.state === HubConnectionState.Connected)
  } catch { /* The next heartbeat/reconciliation repairs missed presence. */ }
  finally { heartbeatPending = false }
}
function setFocus(value: PresenceCommand) { focus = value; void heartbeat() }
async function freshSession() {
  const next = await request<Session>('/api/session')
  if (!next.canReadAudit) audits.value = []
  session.value = next
}
const synchronizer = new SnapshotSynchronizer(async () => {
  await freshSession()
  const next = await request<Snapshot>('/api/snapshot')
  snapshot.value = next
  await nextTick()
  await dataWorkspace.value?.refresh(true)
  if (session.value?.canReadAudit) {
    try { audits.value = await request<Audit[]>('/api/audit') }
    catch { audits.value = []; await freshSession() }
  }
  receivePresence(await request<PresenceSnapshotDto>('/api/presence'))
  return next.version
}, async () => (await request<WorkspaceVersionDto>('/api/version')).version,
ready => { online.value = ready && !disposed && connection?.state === HubConnectionState.Connected })
async function refresh(minimum = 0) { if (session.value && !disposed) await synchronizer.refresh(minimum) }
function reconcile() {
  if (!session.value || disposed) return
  if (connection?.state === HubConnectionState.Disconnected) void connect()
  else void refresh().catch(() => { online.value = false })
}
function disconnected() { synchronizer.invalidate(); presence.value = undefined }
function scheduleRetry() { clearTimeout(retry); if (!disposed) retry = setTimeout(() => void connect(), 3000) }
async function connected() { await refresh(); await heartbeat() }
async function connect() {
  if (disposed) return
  if (!connection) {
    connection = new HubConnectionBuilder().withUrl(new URL('/hubs/workspace', window.location.href).toString()).withAutomaticReconnect().build()
    connection.on('snapshotChanged', (version: number) => { void refresh(version).catch(() => { online.value = false }) })
    connection.on('presenceChanged', receivePresence)
    connection.on('sessionChanged', () => {
      // Remove cached identity/audit information before loading the new authority.
      audits.value = []; presence.value = undefined; synchronizer.invalidate()
      void freshSession().then(ensureCsrf).then(connected).catch(() => { online.value = false })
    })
    connection.onreconnecting(disconnected)
    connection.onreconnected(() => { void connected().catch(() => { online.value = false }) })
    connection.onclose(() => { disconnected(); scheduleRetry() })
  }
  if (connection.state !== HubConnectionState.Disconnected) return
  try { await connection.start(); await connected() }
  catch { online.value = false; scheduleRetry() }
}
async function start() {
  const settings = await request<CollaborationSettingsDto>('/api/collaboration/settings')
  clearInterval(poll); clearInterval(pulse)
  poll = setInterval(reconcile, settings.reconcileSeconds * 1000)
  pulse = setInterval(() => void heartbeat(), settings.heartbeatSeconds * 1000)
  await connect()
}
async function login(name = nickname.value) {
  busy.value = true; message.value = ''
  try {
    await ensureCsrf()
    session.value = await write<Session>('/api/session', { nickname: name }, 'POST')
    await ensureCsrf(); await start()
  } catch (error) { message.value = error instanceof Error ? error.message : '無法進入工作區。' }
  finally { busy.value = false }
}
async function renameSession() {
  const name = window.prompt('新的暱稱', session.value?.nickname); if (!name) return
  busy.value = true
  try {
    session.value = await write<Session>('/api/session/nickname', { nickname: name })
    audits.value = []; presence.value = undefined
    await ensureCsrf(); await refresh(); await heartbeat()
  } catch (error) { message.value = (error as Error).message }
  finally { busy.value = false }
}
async function saveSnapshot(path: string, body: object, method = 'POST') {
  if (!online.value) return
  busy.value = true; message.value = ''
  try {
    const saved = await write<Snapshot>(path, body, method)
    await refresh(saved.version); message.value = '狀態已保存。'
  } catch (error) { message.value = error instanceof Error ? error.message : '操作失敗。'; void refresh().catch(() => { online.value = false }) }
  finally { busy.value = false }
}
async function command(account: Account, card: Card, action: 'reserve' | 'enter' | 'cancel' | 'return-home', regionId?: string) {
  const body = action === 'reserve' || action === 'enter'
    ? { cardId: card.id, regionId, expectedAccountVersion: account.coordinationVersion }
    : { cardId: card.id, expectedAccountVersion: account.coordinationVersion, expectedReservationVersion: card.reservation?.version }
  await saveSnapshot(`/api/cards/${card.id}/${action}`, body)
}
async function setUsage(card: Card, status: Card['usageStatus']) { await saveSnapshot(`/api/cards/${card.id}/usage`, { status, assignMeAsPrimaryOperator: status === 'InUse' }, 'PUT') }
async function createAccount() { const displayName = window.prompt('新帳號的顯示名稱'); if (displayName) await saveSnapshot('/api/accounts', { displayName }) }
async function createCard(account: Account) { const displayName = window.prompt(`在「${account.displayName}」新增卡片`); if (displayName) await saveSnapshot(`/api/accounts/${account.id}/cards`, { displayName }) }
function drag(cardId: string, event: DragEvent) { event.dataTransfer?.setData('text/card-id', cardId) }
async function drop(region: Region, event: DragEvent) {
  const cardId = event.dataTransfer?.getData('text/card-id'); const item = allCards.value.find(value => value.card.id === cardId)
  if (item && !item.card.reservation) await command(item.account, item.card, 'reserve', region.id)
}
onMounted(async () => {
  window.addEventListener('focus', reconcile); window.addEventListener('online', reconcile); window.addEventListener('offline', disconnected)
  try { await ensureCsrf(); await freshSession() } catch { return }
  try { await start() } catch (error) { message.value = (error as Error).message }
})
onUnmounted(() => {
  disposed = true; clearInterval(poll); clearInterval(pulse); clearTimeout(retry)
  window.removeEventListener('focus', reconcile); window.removeEventListener('online', reconcile); window.removeEventListener('offline', disconnected)
  void connection?.stop()
})
</script>


<template>
  <main>
    <section v-if="!session" class="login">
      <p class="eyebrow">PRIVATE WORKSPACE</p><h1>今天用什麼暱稱？</h1>
      <p>不需要密碼；每次新工作階段都會取得獨立身分。</p>
      <form @submit.prevent="login()"><input v-model="nickname" maxlength="80" placeholder="輸入暱稱" autofocus><button :disabled="busy">進入工作區</button></form>
      <div class="preview"><span>虛構預覽：</span><button @click="login('小明')">小明</button><button @click="login('小林')">小林</button><button @click="login('Admin')">Admin</button></div>
      <p v-if="message" class="error">{{ message }}</p>
    </section>

    <template v-else>
      <header class="topbar"><div><p class="eyebrow">FIELD COORDINATION</p><h1>區域協調看板</h1></div><div class="identity"><span :class="['status', online ? 'ok' : 'off']">{{ online ? '即時同步中' : '連線中斷，將自動重連' }}</span><strong>{{ session.nickname }}</strong><button class="secondary" :disabled="busy || !online" @click="renameSession">更改暱稱</button></div></header>
      <p v-if="message" :class="message === '狀態已保存。' ? 'notice' : 'error'">{{ message }}</p>
      <section class="presence-roster" aria-label="在線成員"><h2>在線成員 <small>{{ members.length }}</small></h2><p v-if="!online" class="muted">正在確認在線狀態…</p><ul><li v-for="person in members" :key="person.participantId" :data-participant="person.participantId"><strong :style="{ color: person.color }">{{ memberLabel(person) }}{{ person.participantId === session.participantId ? '（你）' : '' }}</strong><span v-for="(target, index) in person.targets" :key="index">{{ target.mode === 'Editing' ? '編輯中' : '查看中' }} · {{ target.label }}</span></li></ul></section>
      <DataWorkspace ref="dataWorkspace" :snapshot="snapshot" :online="online" :members="members" :self="session.participantId" @refresh="reconcile" @focus="setFocus" @sync-failed="online = false" v-slot="{ fields, openValue, valueText, recordIds, cardLabel, stages, moveStage, stageId, focusRecord }">
      <div class="toolbar"><button :disabled="busy || !online" @click="createAccount">新增帳號</button></div>
      <section class="regions"><article v-for="region in snapshot?.regions" :key="region.id" class="region" @dragover.prevent @drop="drop(region, $event)"><span class="dot"></span><h2>{{ region.displayName }}</h2><p>將尚未預約的卡片拖曳到這裡</p></article></section>
      <section v-for="account in snapshot?.accounts" :key="account.id" class="account">
        <div class="account-title"><div><p class="eyebrow">ACCOUNT</p><h2>{{ account.displayName }}</h2></div><div><small>協調版本 {{ account.coordinationVersion }}</small><button class="add-card" :disabled="busy || !online" @click="createCard(account)">新增{{ cardLabel }}</button></div></div>
        <div class="cards"><article v-for="card in account.cards.filter(c => !recordIds || recordIds.includes(c.id))" :key="card.id" class="card" tabindex="0" @focusin="focusRecord(card.id)" draggable="true" @dragstart="drag(card.id, $event)">
          <div><span class="pill">{{ card.usageStatus === 'InUse' ? '使用中' : card.usageStatus === 'NotInUse' ? '未使用' : '可使用' }}</span><h3>{{ card.displayName }}</h3><PresenceBadges :members="members" :self="session.participantId" :record-id="card.id" /><p v-if="card.primaryOperatorName">主要操作者：{{ card.primaryOperatorName }}</p></div>
          <div v-if="card.reservation" class="reservation"><strong>{{ card.reservation.regionName }}</strong><span>{{ card.reservation.state === 'Reserved' ? '已預約' : '已入場' }}</span></div>
          <div class="field-chips"><button v-for="field in fields" :key="field.id" class="field-chip" :style="{ borderLeftColor: field.color }" :aria-label="`${card.displayName} · ${field.name}`" @click="openValue(field, card.id)">{{ field.name }}：{{ valueText(field, card.id) }}<PresenceBadges :members="members" :self="session.participantId" :record-id="card.id" :field-id="field.id" :shared="field.scope === 'Shared'" /></button></div>
          <div class="actions">
            <template v-if="!card.reservation"><button v-for="region in snapshot?.regions" :key="region.id" :disabled="busy || !online" @click="command(account, card, 'reserve', region.id)">預約{{ region.displayName }}</button></template>
            <button v-else-if="card.reservation.state === 'Reserved'" :disabled="busy || !online" @click="command(account, card, 'enter', card.reservation.regionId)">回報入場</button>
            <button v-if="card.reservation?.state === 'Reserved'" class="secondary" :disabled="busy || !online" @click="command(account, card, 'cancel')">取消預約</button>
            <button v-if="card.reservation?.state === 'Occupied'" :disabled="busy || !online" @click="command(account, card, 'return-home')">回報回村</button>
          </div>
          <select :value="card.usageStatus" :disabled="busy || !online" @change="setUsage(card, ($event.target as HTMLSelectElement).value as Card['usageStatus'])"><option value="Available">可使用</option><option value="InUse">使用中（由我操作）</option><option value="NotInUse">未使用</option></select>
          <label>階段<select :value="stageId(card.id)" :disabled="busy || !online" @change="moveStage(card.id, $event)"><option value="" disabled>未指定</option><option v-for="stage in stages" :key="stage.id" :value="stage.id">{{ stage.name }}</option></select></label>
        </article></div>
      </section>
      </DataWorkspace>
      <section v-if="session.canReadAudit" class="audit"><p class="eyebrow">ADMIN ONLY</p><h2>最近操作紀錄</h2><p v-for="event in audits" :key="event.id">{{ new Date(event.occurredAt).toLocaleString('zh-TW', { hour12: false }) }}　{{ event.description }}</p></section>
    </template>
  </main>
</template>
