<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import type { AuditEntry, OutboxEvent, OperationsSummary } from '@smilelab/client';
import { client, useAction } from '../session';
import ServerPager from './ServerPager.vue';
import { useServerPage } from '../serverPage';
const { t, locale } = useI18n(); const { busy, error, run } = useAction();
const summary = ref<OperationsSummary | null>(null); const retry = ref<OutboxEvent | null>(null); const reason = ref('');
const deliveries = useServerPage<OutboxEvent>(() => 'operations/outbox'); const events = deliveries.items;
const history = useServerPage<AuditEntry>(() => 'operations/audit'); const audit = history.items;
const columns = computed(() => [{ title: t('appointments.status'), key: 'status' }, { title: t('operations.reference'), dataIndex: 'aggregateId' }, { title: t('operations.attempts'), dataIndex: 'attempts' }, { title: t('operations.created'), key: 'created' }, { title: t('operations.action'), key: 'actions' }]);
const auditColumns = computed(() => [{ title: t('operations.action'), dataIndex: 'action' }, { title: t('operations.resource'), dataIndex: 'resourceId' }, { title: t('operations.actor'), dataIndex: 'actorId' }, { title: t('operations.created'), key: 'created' }]);
const date = (value: string) => new Intl.DateTimeFormat(locale.value === 'en' ? 'en-GB' : 'zh-CN', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value));
async function load() { await run(async () => { const [result] = await Promise.all([client.dso<OperationsSummary>('operations/summary'), deliveries.load(), history.load()]); summary.value = result }) }
async function pageEvents(page: number) { await run(() => deliveries.load(page)) }
async function pageAudit(page: number) { await run(() => history.load(page)) }
async function replay() { if (!retry.value) return; const result = await run(() => client.dso('operations/outbox/' + retry.value!.id + '/retry', 'POST', { reason: reason.value })); if (result) { retry.value = null; reason.value = ''; await load() } }
onMounted(load);
</script>
<template>
  <div class="page-heading"><div><h2>{{ t('operations.title') }}</h2><p>{{ t('operations.description') }}</p></div><a-button @click="load" :loading="busy">{{ t('refresh') }}</a-button></div><a-alert v-if="error" :message="error" type="error" show-icon class="spaced" />
  <div v-if="summary" class="stats-row"><div class="stat-card"><div class="stat-label">{{ t('operations.patients') }}</div><div class="stat-value">{{ summary.patients }}</div></div><div class="stat-card"><div class="stat-label">{{ t('operations.booked') }}</div><div class="stat-value">{{ summary.bookedAppointments }}</div></div><div class="stat-card"><div class="stat-label">{{ t('operations.drafts') }}</div><div class="stat-value">{{ summary.draftRecords }}</div></div></div>
  <div class="surface table-overflow"><div class="surface-title"><h3>{{ t('operations.events') }}</h3></div><a-table :columns="columns" :data-source="events" row-key="id" :loading="busy" :pagination="false"><template #bodyCell="{ column, record }"><a-tag v-if="column.key === 'status'" :color="record.status === 'dead_letter' ? 'red' : record.status === 'delivered' ? 'green' : 'gold'">{{ t('state.' + record.status) }}</a-tag><span v-else-if="column.key === 'created'">{{ date(record.createdAt) }}</span><a-button v-else-if="column.key === 'actions' && record.status === 'dead_letter'" size="small" @click="retry = record; reason = ''">{{ t('operations.retry') }}</a-button></template></a-table><ServerPager :page="deliveries.page.value" :count="events.length" :page-size="deliveries.pageSize" :busy="busy" @change="pageEvents" /></div>
  <div class="surface table-overflow"><div class="surface-title"><h3>{{ t('operations.audit') }}</h3></div><a-table :columns="auditColumns" :data-source="audit" row-key="id" :pagination="false"><template #bodyCell="{ column, record }"><span v-if="column.key === 'created'">{{ date(record.createdAt) }}</span></template></a-table><ServerPager :page="history.page.value" :count="audit.length" :page-size="history.pageSize" :busy="busy" @change="pageAudit" /></div>
  <a-modal :open="!!retry" :title="t('operations.retry')" :ok-text="t('operations.retry')" :cancel-text="t('cancel')" :confirm-loading="busy" :ok-button-props="{ disabled: !reason.trim() }" @cancel="retry = null" @ok="replay"><p>{{ t('operations.retryPrompt') }}</p><a-form layout="vertical"><a-form-item :label="t('operations.reason')"><a-textarea v-model:value="reason" :maxlength="300" /></a-form-item></a-form></a-modal>
</template>
