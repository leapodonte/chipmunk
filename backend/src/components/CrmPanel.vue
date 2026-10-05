<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { newIdempotencyKey, type Activity, type Lead } from '@smilelab/client';
import { client, useAction } from '../session';
import ServerPager from './ServerPager.vue';
import { useServerPage } from '../serverPage';
const { t, locale } = useI18n(); const { busy, error, run } = useAction();
const selected = ref<Lead | null>(null); const showLead = ref(false); const showActivity = ref(false);
const directory = useServerPage<Lead>(() => 'crm/leads'); const leads = directory.items;
const followups = useServerPage<Activity>(() => 'crm/leads/' + selected.value!.id + '/activities'); const activities = followups.items;
const title = ref(''); const activity = reactive({ type: 'follow_up', summary: '', dueAt: '' }); let createKey = newIdempotencyKey(); let activityKey = newIdempotencyKey();
const stages = ['new', 'contacted', 'qualified', 'won', 'lost'];
const columns = computed(() => [{ title: t('crm.titleField'), dataIndex: 'title' }, { title: t('crm.stage'), key: 'stage' }, { title: t('crm.open'), key: 'open' }]);
const date = (value: string) => new Intl.DateTimeFormat(locale.value === 'en' ? 'en-GB' : 'zh-CN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
async function load() { await run(() => directory.load()) }
async function pageLeads(page: number) { await run(() => directory.load(page)) }
async function pageActivities(page: number) { await run(() => followups.load(page)) }
async function open(lead: Lead) { selected.value = lead; followups.reset(); await run(() => followups.load()) }
function newLead() { title.value = ''; createKey = newIdempotencyKey(); showLead.value = true }
async function createLead() { const result = await run(() => client.dso<Lead>('crm/leads', 'POST', { title: title.value }, createKey)); if (result) { showLead.value = false; await load(); await open(result) } }
async function stage(value: string) { if (!selected.value) return; const result = await run(() => client.dso<Lead>('crm/leads/' + selected.value!.id, 'PATCH', { version: selected.value!.version, stage: value })); if (result) { selected.value = result; await load() } }
function newActivity() { activity.summary = ''; activity.dueAt = ''; activityKey = newIdempotencyKey(); showActivity.value = true }
async function createActivity() { if (!selected.value) return; const result = await run(() => client.dso<Activity>('crm/leads/' + selected.value!.id + '/activities', 'POST', { type: activity.type, summary: activity.summary, dueAt: new Date(activity.dueAt).toISOString() }, activityKey)); if (result) { showActivity.value = false; await open(selected.value) } }
async function finish(item: Activity) { const result = await run(() => client.dso('crm/activities/' + item.id, 'PATCH', { status: 'done', version: item.version })); if (result && selected.value) await open(selected.value) }
onMounted(load);
</script>
<template>
  <div class="page-heading"><div><h2>{{ t('crm.title') }}</h2><p>{{ t('crm.description') }}</p></div><div class="heading-actions"><a-button @click="load" :loading="busy">{{ t('refresh') }}</a-button><a-button type="primary" @click="newLead">{{ t('crm.newLead') }}</a-button></div></div><a-alert v-if="error" :message="error" type="error" show-icon class="spaced" />
  <div class="split-view"><div class="surface table-overflow"><a-table :columns="columns" :data-source="leads" row-key="id" :loading="busy" :pagination="false"><template #bodyCell="{ column, record }"><a-tag v-if="column.key === 'stage'" :color="record.stage === 'won' ? 'green' : 'default'">{{ t('state.' + record.stage) }}</a-tag><a-button v-else-if="column.key === 'open'" type="link" @click="open(record)">{{ t('crm.open') }}</a-button></template></a-table><ServerPager :page="directory.page.value" :count="leads.length" :page-size="directory.pageSize" :busy="busy" @change="pageLeads" /></div>
    <div class="surface"><template v-if="selected"><div class="surface-title"><h3>{{ selected.title }}</h3><a-select :value="selected.stage" :disabled="busy || ['won', 'lost'].includes(selected.stage)" :options="stages.map(s => ({ value: s, label: t('state.' + s) }))" :aria-label="t('crm.stage')" style="min-width:140px" @change="stage" /></div><p class="footnote">{{ t('crm.boundary') }}</p><div class="surface-title"><h3>{{ t('crm.activities') }}</h3><a-button @click="newActivity">{{ t('crm.newActivity') }}</a-button></div><a-empty v-if="!activities.length" :description="t('empty')" /><article v-for="item in activities" :key="item.id" class="record-card"><div class="record-meta"><a-tag>{{ t('crm.' + item.type) }}</a-tag><span>{{ date(item.dueAt) }}</span><a-tag :color="item.status === 'done' ? 'green' : 'gold'">{{ t('state.' + item.status) }}</a-tag></div><p>{{ item.summary }}</p><a-button v-if="item.status === 'pending'" :disabled="busy" @click="finish(item)">{{ t('crm.finish') }}</a-button></article><ServerPager :page="followups.page.value" :count="activities.length" :page-size="followups.pageSize" :busy="busy" @change="pageActivities" /></template><a-empty v-else :description="t('empty')" /></div>
  </div>
  <a-modal v-model:open="showLead" :title="t('crm.newLead')" :ok-text="t('create')" :cancel-text="t('cancel')" :confirm-loading="busy" :ok-button-props="{ disabled: !title.trim() }" @ok="createLead"><a-form layout="vertical" :disabled="busy"><a-form-item :label="t('crm.titleField')"><a-input v-model:value="title" :maxlength="150" /></a-form-item></a-form><p class="footnote">{{ t('crm.boundary') }}</p></a-modal>
  <a-modal v-model:open="showActivity" :title="t('crm.newActivity')" :ok-text="t('create')" :cancel-text="t('cancel')" :confirm-loading="busy" :ok-button-props="{ disabled: !activity.summary.trim() || !activity.dueAt }" @ok="createActivity"><a-form layout="vertical" :disabled="busy"><a-form-item :label="t('crm.type')"><a-select v-model:value="activity.type" :options="['call', 'follow_up', 'visit'].map(type => ({ value: type, label: t('crm.' + type) }))" /></a-form-item><a-form-item :label="t('crm.summary')"><a-textarea v-model:value="activity.summary" :maxlength="300" /></a-form-item><a-form-item :label="t('crm.due')"><a-input v-model:value="activity.dueAt" type="datetime-local" /></a-form-item></a-form></a-modal>
</template>
