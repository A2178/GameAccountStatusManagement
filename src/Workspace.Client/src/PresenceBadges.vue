<script setup lang="ts">
import { computed } from 'vue'
import { memberLabel, targetMembers } from './presence'
import type { PresenceMemberDto } from './contracts.generated'
const props = defineProps<{ members: PresenceMemberDto[]; self: string; recordId: string; fieldId?: string; shared?: boolean }>()
const people = computed(() => targetMembers(props.members, props.self, props.recordId, props.fieldId, props.shared))
</script>
<template><span v-if="people.length" class="presence-badges" aria-live="polite"><span v-for="person in people" :key="person.participantId" :style="{ color: person.color }">{{ memberLabel(person) }} {{ person.targets.some(t => t.mode === 'Editing' && (shared && fieldId ? t.recordId === null : t.recordId === recordId) && (!fieldId || t.fieldId === fieldId)) ? '編輯中' : '查看中' }}</span></span></template>
