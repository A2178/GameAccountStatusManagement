<script setup lang="ts">
const milestones = [
  { name: '環境骨架', detail: '.NET 10、Vue 3、PostgreSQL', state: '進行中' },
  { name: '帳號與卡片', detail: '暱稱工作階段與狀態協調', state: '下一階段' },
  { name: '野外區域', detail: '預約、入場、回村與衝突防護', state: '下一階段' },
]
</script>

<template>
  <main>
    <header class="hero">
      <div>
        <p class="eyebrow">GAME ACCOUNT STATUS MANAGEMENT</p>
        <h1>協作工作區</h1>
        <p class="lead">讓隊友清楚掌握帳號、卡片與野外區域狀態。</p>
      </div>
      <span class="badge">開發預覽</span>
    </header>

    <section aria-labelledby="status-heading">
      <div class="section-title">
        <div>
          <p class="eyebrow">M0 · REPOSITORY</p>
          <h2 id="status-heading">專案已準備啟動</h2>
        </div>
        <p>目前為環境基線，不含正式資料。</p>
      </div>

      <div class="cards">
        <article v-for="milestone in milestones" :key="milestone.name">
          <span class="dot" aria-hidden="true"></span>
          <h3>{{ milestone.name }}</h3>
          <p>{{ milestone.detail }}</p>
          <strong>{{ milestone.state }}</strong>
        </article>
      </div>
    </section>

    <footer>所有預覽資料皆為虛構資料 · Asia/Taipei 04:00 換日</footer>
  </main>
</template>
