<script setup lang="ts">
import { HubConnectionBuilder, HubConnectionState, type HubConnection } from '@microsoft/signalr'
import { computed, onMounted, ref } from 'vue'

type Session = { participantId: string; nickname: string; canReadAudit: boolean }
type Region = { id: string; displayName: string }
type Reservation = { id: string; regionId: string; regionName: string; state: 'Reserved' | 'Occupied'; version: number }
type Card = { id: string; accountId: string; displayName: string; usageStatus: 'Available' | 'InUse' | 'NotInUse'; primaryOperatorName?: string; reservation?: Reservation }
type Account = { id: string; displayName: string; coordinationVersion: number; cards: Card[] }
type Snapshot = { version: number; regions: Region[]; accounts: Account[] }
type Audit = { id: string; occurredAt: string; description: string }

const session = ref<Session>()
const nickname = ref('')
const snapshot = ref<Snapshot>()
const csrf = ref('')
const message = ref('')
const busy = ref(false)
const online = ref(false)
const audits = ref<Audit[]>([])
const allCards = computed(() => snapshot.value?.accounts.flatMap(account => account.cards.map(card => ({ account, card }))) ?? [])
let connection: HubConnection | undefined
let handlersRegistered = false

async function ensureCsrf() { csrf.value = (await request<{ token: string }>('/api/session/csrf')).token }
async function request<T>(url: string, options: RequestInit = {}): Promise<T> {
  const response = await fetch(url, { credentials: 'same-origin', ...options, headers: { 'Content-Type': 'application/json', ...(csrf.value ? { 'X-CSRF-TOKEN': csrf.value } : {}), ...options.headers } })
  if (!response.ok) { const body = await response.json().catch(() => ({ message: '伺服器目前無法處理要求。' })) as { message?: string }; throw new Error(body.message ?? `要求失敗（${response.status}）`) }
  return response.json() as Promise<T>
}
async function login(name = nickname.value) {
  busy.value = true; message.value = ''
  try {
    await ensureCsrf()
    session.value = await request<Session>('/api/session', { method: 'POST', body: JSON.stringify({ nickname: name }) })
    await ensureCsrf(); await refresh(); await connect()
  } catch (error) { message.value = error instanceof Error ? error.message : '無法進入工作區。' }
  finally { busy.value = false }
}
async function refresh() {
  if (!session.value) return
  snapshot.value = await request<Snapshot>('/api/snapshot')
  if (session.value.canReadAudit) audits.value = await request<Audit[]>('/api/audit')
}
async function connect() {
  connection ??= new HubConnectionBuilder()
    .withUrl(new URL('/hubs/workspace', window.location.href).toString())
    .withAutomaticReconnect()
    .build()
  const activeConnection = connection
  if (activeConnection.state === HubConnectionState.Disconnected) {
    if (!handlersRegistered) {
      activeConnection.on('snapshotChanged', async (version: number) => { if (!snapshot.value || version > snapshot.value.version) await refresh() })
      activeConnection.onreconnecting(() => { online.value = false })
      activeConnection.onreconnected(async () => { online.value = true; await refresh() })
      activeConnection.onclose(() => { online.value = false; window.setTimeout(connect, 3000) })
      handlersRegistered = true
    }
    try { await activeConnection.start(); online.value = true } catch { online.value = false; window.setTimeout(connect, 3000) }
  }
}
async function command(account: Account, card: Card, action: 'reserve' | 'enter' | 'cancel' | 'return-home', regionId?: string) {
  busy.value = true; message.value = ''
  try {
    const body = action === 'reserve' || action === 'enter'
      ? { cardId: card.id, regionId, expectedAccountVersion: account.coordinationVersion }
      : { cardId: card.id, expectedAccountVersion: account.coordinationVersion, expectedReservationVersion: card.reservation?.version }
    snapshot.value = await request<Snapshot>(`/api/cards/${card.id}/${action}`, { method: 'POST', body: JSON.stringify(body) })
    message.value = '狀態已保存。'
  } catch (error) { await refresh(); message.value = error instanceof Error ? error.message : '操作失敗，畫面已重新整理。' }
  finally { busy.value = false }
}
async function setUsage(card: Card, status: Card['usageStatus']) {
  busy.value = true
  try { snapshot.value = await request<Snapshot>(`/api/cards/${card.id}/usage`, { method: 'PUT', body: JSON.stringify({ status, assignMeAsPrimaryOperator: status === 'InUse' }) }) }
  catch (error) { await refresh(); message.value = error instanceof Error ? error.message : '狀態更新失敗。' }
  finally { busy.value = false }
}
async function createAccount() {
  const displayName = window.prompt('新帳號的顯示名稱'); if (!displayName) return
  await create('/api/accounts', { displayName })
}
async function createCard(account: Account) {
  const displayName = window.prompt(`在「${account.displayName}」新增卡片`); if (!displayName) return
  await create(`/api/accounts/${account.id}/cards`, { displayName })
}
async function create(path: string, body: object) {
  busy.value = true; message.value = ''
  try { snapshot.value = await request<Snapshot>(path, { method: 'POST', body: JSON.stringify(body) }); message.value = '狀態已保存。' }
  catch (error) { await refresh(); message.value = error instanceof Error ? error.message : '新增失敗。' }
  finally { busy.value = false }
}
function drag(cardId: string, event: DragEvent) { event.dataTransfer?.setData('text/card-id', cardId) }
async function drop(region: Region, event: DragEvent) {
  const cardId = event.dataTransfer?.getData('text/card-id'); const item = allCards.value.find(value => value.card.id === cardId)
  if (item && !item.card.reservation) await command(item.account, item.card, 'reserve', region.id)
}

onMounted(async () => {
  try { await ensureCsrf(); session.value = await request<Session>('/api/session'); await refresh(); await connect() } catch { session.value = undefined }
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
      <header class="topbar"><div><p class="eyebrow">FIELD COORDINATION</p><h1>區域協調看板</h1></div><div class="identity"><span :class="['status', online ? 'ok' : 'off']">{{ online ? '即時同步中' : '連線中斷，將自動重連' }}</span><strong>{{ session.nickname }}</strong></div></header>
      <p v-if="message" :class="message === '狀態已保存。' ? 'notice' : 'error'">{{ message }}</p>
      <div class="toolbar"><button :disabled="busy" @click="createAccount">新增帳號</button></div>
      <section class="regions"><article v-for="region in snapshot?.regions" :key="region.id" class="region" @dragover.prevent @drop="drop(region, $event)"><span class="dot"></span><h2>{{ region.displayName }}</h2><p>將尚未預約的卡片拖曳到這裡</p></article></section>
      <section v-for="account in snapshot?.accounts" :key="account.id" class="account">
        <div class="account-title"><div><p class="eyebrow">ACCOUNT</p><h2>{{ account.displayName }}</h2></div><div><small>協調版本 {{ account.coordinationVersion }}</small><button class="add-card" :disabled="busy" @click="createCard(account)">新增卡片</button></div></div>
        <div class="cards"><article v-for="card in account.cards" :key="card.id" class="card" draggable="true" @dragstart="drag(card.id, $event)">
          <div><span class="pill">{{ card.usageStatus === 'InUse' ? '使用中' : card.usageStatus === 'NotInUse' ? '未使用' : '可使用' }}</span><h3>{{ card.displayName }}</h3><p v-if="card.primaryOperatorName">主要操作者：{{ card.primaryOperatorName }}</p></div>
          <div v-if="card.reservation" class="reservation"><strong>{{ card.reservation.regionName }}</strong><span>{{ card.reservation.state === 'Reserved' ? '已預約' : '已入場' }}</span></div>
          <div class="actions">
            <template v-if="!card.reservation"><button v-for="region in snapshot?.regions" :key="region.id" :disabled="busy" @click="command(account, card, 'reserve', region.id)">預約{{ region.displayName }}</button></template>
            <button v-else-if="card.reservation.state === 'Reserved'" :disabled="busy" @click="command(account, card, 'enter', card.reservation.regionId)">回報入場</button>
            <button v-if="card.reservation?.state === 'Reserved'" class="secondary" :disabled="busy" @click="command(account, card, 'cancel')">取消預約</button>
            <button v-if="card.reservation?.state === 'Occupied'" :disabled="busy" @click="command(account, card, 'return-home')">回報回村</button>
          </div>
          <select :value="card.usageStatus" :disabled="busy" @change="setUsage(card, ($event.target as HTMLSelectElement).value as Card['usageStatus'])"><option value="Available">可使用</option><option value="InUse">使用中（由我操作）</option><option value="NotInUse">未使用</option></select>
        </article></div>
      </section>
      <section v-if="session.canReadAudit" class="audit"><p class="eyebrow">ADMIN ONLY</p><h2>最近操作紀錄</h2><p v-for="event in audits" :key="event.id">{{ new Date(event.occurredAt).toLocaleString('zh-TW', { hour12: false }) }}　{{ event.description }}</p></section>
    </template>
  </main>
</template>
