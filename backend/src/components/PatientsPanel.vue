<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { newIdempotencyKey, type ClinicalContent, type ClinicalRecord, type Patient } from '@smilelab/client';
import { client, context, useAction } from '../session';
import ToothChart from './ToothChart.vue';
import ServerPager from './ServerPager.vue';
import { useServerPage } from '../serverPage';
const { t, locale } = useI18n(); const { busy, error, run } = useAction();
const selected = ref<Patient | null>(null); const show = ref(false); const editing = ref<ClinicalRecord | null>(null); const previous = ref<string | null>(null);
const directory = useServerPage<Patient>(() => 'patients'); const patients = directory.items;
const notes = useServerPage<ClinicalRecord>(() => 'patients/' + selected.value!.id + '/records'); const records = notes.items;
let createKey = newIdempotencyKey();
const form = reactive<ClinicalContent>({ chiefComplaint: '', history: '', examination: '', assessment: '', plan: '', toothChart: [] });
const canClinical = computed(() => context.value?.roles.includes('doctor'));
const fields: { key: keyof Omit<ClinicalContent, 'toothChart'>; label: string }[] = [{ key: 'chiefComplaint', label: 'complaint' }, { key: 'history', label: 'history' }, { key: 'examination', label: 'examination' }, { key: 'assessment', label: 'assessment' }, { key: 'plan', label: 'plan' }];
const date = (value: string) => new Intl.DateTimeFormat(locale.value === 'en' ? 'en-GB' : 'zh-CN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
const columns = computed(() => [{ title: t('patients.name'), dataIndex: 'displayName' }, { title: t('patients.open'), key: 'open' }]);
async function load() { await run(() => directory.load()) }
async function pagePatients(page: number) { await run(() => directory.load(page)) }
async function pageNotes(page: number) { await run(() => notes.load(page)) }
async function open(patient: Patient) { selected.value = patient; notes.reset(); if (canClinical.value) await run(() => notes.load()) }
function newNote(record?: ClinicalRecord, amend = false) { editing.value = record && !amend ? record : null; previous.value = amend && record ? record.id : null; for (const field of fields) form[field.key] = record?.content[field.key] ?? ''; form.toothChart = record?.content.toothChart?.map(x => ({ ...x })) ?? []; createKey = newIdempotencyKey(); show.value = true }
async function save() {
  if (!selected.value) return;
  const content = Object.fromEntries(Object.entries(form).filter(([, value]) => typeof value === 'string' ? value.trim() : Array.isArray(value) && value.length));
  const result = await run(() => editing.value ? client.dso<ClinicalRecord>('clinical-records/' + editing.value.id, 'PATCH', { version: editing.value.version, content }) : client.dso<ClinicalRecord>('patients/' + selected.value!.id + '/records', 'POST', { content, ...(previous.value ? { previousId: previous.value } : {}) }, createKey));
  if (result) { show.value = false; await open(selected.value) }
}
async function sign(record: ClinicalRecord) { const result = await run(() => client.dso('clinical-records/' + record.id + '/sign', 'POST', { version: record.version })); if (result && selected.value) await open(selected.value) }
onMounted(load);
</script>
<template>
  <div class="page-heading"><div><h2>{{ t('patients.title') }}</h2><p>{{ t('patients.description') }}</p></div><a-button @click="load" :loading="busy">{{ t('refresh') }}</a-button></div><a-alert v-if="error" :message="error" type="error" show-icon class="spaced" />
  <div class="split-view"><div class="surface table-overflow"><a-table :columns="columns" :data-source="patients" row-key="id" :loading="busy" :pagination="false"><template #bodyCell="{ column, record }"><a-button v-if="column.key === 'open'" type="link" @click="open(record)">{{ t('patients.open') }}</a-button></template></a-table><ServerPager :page="directory.page.value" :count="patients.length" :page-size="directory.pageSize" :busy="busy" @change="pagePatients" /></div>
    <div class="surface"><template v-if="selected"><div class="surface-title"><div><h3>{{ selected.displayName }}</h3><p class="footnote">{{ t('patients.recordsDescription') }}</p></div><a-button v-if="canClinical" type="primary" @click="newNote()">{{ t('patients.newRecord') }}</a-button></div><a-alert v-if="!canClinical" :message="t('patients.noAccess')" type="info" show-icon /><a-empty v-else-if="!records.length" :description="t('empty')" />
      <article v-for="record in records" :key="record.id" class="record-card"><div class="record-meta"><a-tag :color="record.status === 'signed' ? 'green' : 'gold'">{{ t('state.' + record.status) }}</a-tag><span>{{ date(record.createdAt) }}</span><span>{{ t('patients.version') }} {{ record.version }}</span></div><p v-if="record.previousId" class="footnote">{{ t('patients.previous') }} · <span class="reference">{{ record.previousId.slice(-8) }}</span></p><dl><template v-for="field in fields" :key="field.key"><div v-if="record.content[field.key]" class="record-field"><dt>{{ t('patients.' + field.label) }}</dt><dd>{{ record.content[field.key] }}</dd></div></template></dl><ToothChart v-if="record.content.toothChart?.length" :model-value="record.content.toothChart" disabled />
        <div class="record-actions"><template v-if="record.status === 'draft' && record.authorId === context?.userId"><a-button @click="newNote(record)">{{ t('patients.edit') }}</a-button><a-popconfirm :title="t('patients.signPrompt')" :ok-text="t('patients.sign')" :cancel-text="t('cancel')" @confirm="sign(record)"><a-button type="primary">{{ t('patients.sign') }}</a-button></a-popconfirm></template><a-button v-if="record.status === 'signed'" @click="newNote(record, true)">{{ t('patients.amendment') }}</a-button></div></article><ServerPager v-if="canClinical" :page="notes.page.value" :count="records.length" :page-size="notes.pageSize" :busy="busy" @change="pageNotes" />
    </template><div v-else class="empty-panel">{{ t('patients.select') }}</div></div>
  </div>
  <a-modal v-model:open="show" :title="editing ? t('patients.edit') : previous ? t('patients.amendment') : t('patients.newRecord')" width="720px" :ok-text="t('save')" :cancel-text="t('cancel')" :confirm-loading="busy" :ok-button-props="{ disabled: !fields.some(f => form[f.key]?.trim()) && !form.toothChart?.length }" @ok="save"><a-form layout="vertical" :disabled="busy"><a-form-item v-for="field in fields" :key="field.key" :label="t('patients.' + field.label)"><a-textarea v-model:value="form[field.key]" :auto-size="{ minRows: 2, maxRows: 5 }" :maxlength="8000" /></a-form-item><ToothChart :model-value="form.toothChart ?? []" @update:model-value="form.toothChart = $event" /></a-form></a-modal>
</template>
