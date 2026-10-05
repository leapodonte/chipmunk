<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { newIdempotencyKey, type Doctor, type OrderProduct, type SharedOrder } from '@smilelab/client';
import { client, context, useAction } from '../session';
import { useServerPage } from '../serverPage';
import ServerPager from './ServerPager.vue';
const { t, locale } = useI18n(); const { busy, error, run } = useAction();
const directory = useServerPage<SharedOrder>(() => 'orders'); const orders = directory.items;
const selected = ref<SharedOrder | null>(null); const products = ref<OrderProduct[]>([]); const doctors = ref<Doctor[]>([]);
const showRequest = ref(false); const action = ref('');
const request = reactive({ productCode: 'retainer_pair', doctorId: 'd_001', quantity: 1, requestText: '', shippingAddress: { recipient: '', phone: '', address: '' } });
const fields = reactive({ productionSpec: '', reason: '', batchRef: '', carrier: '', trackingNumber: '', checks: { identity: false, specification: false, finish: false, packaging: false } });
const patient = computed(() => context.value?.roles.includes('patient'));
const columns = computed(() => [{ title: t('orders.reference'), dataIndex: 'id', key: 'id' }, { title: t('orders.product'), key: 'product' }, { title: t('orders.status'), key: 'status' }, { title: t('orders.amount'), key: 'amount' }, { title: '', key: 'actions' }]);
const money = (order: { amountMinor: number; currency: string }) => new Intl.NumberFormat(locale.value === 'en' ? 'en' : 'zh-CN', { style: 'currency', currency: order.currency }).format(order.amountMinor / 100);
let timer: ReturnType<typeof setInterval>; let sequence = 0;
let command = { hash: '', key: '' };
function key(path: string, body: unknown) { const hash = path + JSON.stringify(body); if (command.hash !== hash) command = { hash, key: newIdempotencyKey() }; return command.key }
async function load() { await run(() => directory.load()) }
async function open(order: Pick<SharedOrder, 'id'>) { const current = ++sequence; const result = await run(() => client.dso<SharedOrder>('orders/' + order.id)); if (result && current === sequence) selected.value = result }
async function page(value: number) { await run(() => directory.load(value)) }
async function refresh() {
  if (busy.value || showRequest.value || action.value || !selected.value) return;
  const id = selected.value.id; const current = sequence;
  const result = await run(() => client.dso<SharedOrder>('orders/' + id));
  if (result && current === sequence && selected.value?.id === id) { const changed = result.version !== selected.value.version; selected.value = result; if (changed) await load() }
}
function startRequest() { command = { hash: '', key: '' }; showRequest.value = true }
async function create() {
  const body = JSON.parse(JSON.stringify(request));
  const result = await run(() => client.dso<SharedOrder>('orders', 'POST', body, key('orders', body)));
  if (result) { showRequest.value = false; selected.value = result; sequence++; await load() }
}
function choose(name: string) {
  action.value = name; command = { hash: '', key: '' };
  fields.productionSpec = ''; fields.reason = ''; fields.batchRef = selected.value?.batchRef || ''; fields.carrier = ''; fields.trackingNumber = '';
  fields.checks = { identity: false, specification: false, finish: false, packaging: false };
}
async function perform() {
  if (!selected.value) return;
  const body: Record<string, unknown> = { version: selected.value.version };
  if (action.value === 'doctor_approve') body.productionSpec = fields.productionSpec;
  if (action.value === 'doctor_reject' || action.value === 'qa_fail') body.reason = fields.reason;
  if (action.value.startsWith('qa_')) body.checks = { ...fields.checks };
  if (action.value === 'pay_demo') body.confirmSimulation = true;
  if (action.value === 'start_manufacturing') body.batchRef = fields.batchRef;
  if (action.value === 'ship') { body.carrier = fields.carrier; body.trackingNumber = fields.trackingNumber }
  const path = 'orders/' + selected.value.id + '/actions/' + action.value;
  const result = await run(() => client.dso<SharedOrder>(path, 'POST', body, key(path, body)));
  if (result) { selected.value = result; action.value = ''; await load() }
  else { const message = error.value; action.value = ''; await open(selected.value); error.value = message }
}
onMounted(async () => {
  await load();
  const reference = new URLSearchParams(location.search).get('order');
  if (reference && /^order_[a-f0-9]{32}$/.test(reference)) await open({ id: reference });
  if (patient.value) await run(async () => { products.value = await client.dso<OrderProduct[]>('orders/products'); doctors.value = await client.dso<Doctor[]>('doctors') });
  timer = setInterval(refresh, 5000);
});
onUnmounted(() => { clearInterval(timer); sequence++ });
</script>

<template>
  <div class="page-heading"><div><h2>{{ t('orders.title') }}</h2><p>{{ t('orders.description') }}</p></div><a-space><a-button @click="load" :loading="busy">{{ t('refresh') }}</a-button><a-button v-if="patient" type="primary" @click="startRequest">{{ t('orders.create') }}</a-button></a-space></div>
  <a-alert type="info" show-icon :message="t('orders.demo')" class="spaced" />
  <a-alert v-if="error" type="error" :message="error" show-icon class="spaced" />
  <div class="surface table-overflow"><a-table :columns="columns" :data-source="orders" :pagination="false" :loading="busy" row-key="id"><template #bodyCell="{ column, record }"><span v-if="column.key === 'product'">{{ t('orders.products.' + record.productCode) }}</span><a-tag v-else-if="column.key === 'status'">{{ t('orders.states.' + record.status) }}</a-tag><span v-else-if="column.key === 'amount'">{{ money(record) }}</span><a-button v-else-if="column.key === 'actions'" @click="open(record)">{{ t('orders.open') }}</a-button></template></a-table><ServerPager :page="directory.page.value" :count="orders.length" :page-size="directory.pageSize" :busy="busy" @change="page" /></div>
  <section v-if="selected" class="surface order-detail spaced" :data-order-id="selected.id">
    <p class="eyebrow">{{ t('orders.authority') }} · {{ t('orders.synchronized') }}</p><h3>{{ selected.id }}</h3>
    <div class="stats-row"><div class="stat-card"><div>{{ t('orders.status') }}</div><strong data-testid="order-status">{{ t('orders.states.' + selected.status) }}</strong></div><div class="stat-card"><div>{{ t('orders.next') }}</div><strong>{{ t('orders.roles.' + selected.nextRole) }}</strong></div><div class="stat-card"><div>{{ t('orders.amount') }}</div><strong>{{ money(selected) }}</strong></div></div>
    <p>{{ t('orders.product') }}: {{ t('orders.products.' + selected.productCode) }} × {{ selected.quantity }} · {{ t('orders.version') }} {{ selected.version }}</p>
    <p v-if="selected.requestText">{{ t('orders.request') }}: {{ selected.requestText }}</p><p v-if="selected.productionSpec">{{ t('orders.specification') }}: {{ selected.productionSpec }}</p>
    <p v-if="selected.shippingAddress">{{ t('orders.address') }}: {{ selected.shippingAddress.recipient }} · {{ selected.shippingAddress.phone }} · {{ selected.shippingAddress.address }}</p>
    <p v-if="selected.batchRef">{{ t('orders.batch') }}: {{ selected.batchRef }}</p><p v-if="selected.trackingNumber">{{ t('orders.carrier') }}: {{ selected.carrier }} · {{ t('orders.tracking') }}: {{ selected.trackingNumber }}</p>
    <p v-if="selected.payment">{{ t('orders.payment') }}: {{ selected.payment.receiptId }} · {{ money(selected.payment) }}</p>
    <a-space wrap><a-button v-for="name in selected.allowedActions" :key="name" :data-action="name" :disabled="busy" @click="choose(name)">{{ t('orders.actions.' + name) }}</a-button></a-space>
    <h3 class="spaced">{{ t('orders.timeline') }}</h3><ol class="order-timeline"><li v-for="event in selected.timeline" :key="event.version"><strong>{{ t('orders.actions.' + event.action) }}</strong><span> · {{ t('orders.roles.' + event.actorRole) }} · {{ new Date(event.createdAt).toLocaleString(locale === 'en' ? 'en' : 'zh-CN') }}</span><p>{{ t('orders.states.' + event.toStatus) }}</p><p v-if="event.details.reasonText">{{ event.details.reasonText }}</p></li></ol>
  </section><a-empty v-else class="surface spaced" :description="t('orders.select')" />
  <a-modal :open="showRequest" :title="t('orders.create')" :ok-text="t('orders.create')" :cancel-text="t('cancel')" :confirm-loading="busy" @cancel="showRequest = false" @ok="create"><a-form layout="vertical" :model="request">
    <a-form-item :label="t('orders.product')"><a-select v-model:value="request.productCode" :options="products.map(p => ({ value: p.code, label: t('orders.products.' + p.code) + ' · ' + money({ amountMinor: p.priceMinor, currency: p.currency }) }))" /></a-form-item><a-form-item :label="t('orders.doctor')"><a-select v-model:value="request.doctorId" :options="doctors.map(d => ({ value: d.id, label: d.name }))" /></a-form-item>
    <a-form-item :label="t('orders.quantity')"><a-input v-model:value="request.quantity" type="number" :min="1" :max="4" @change="request.quantity = Number(request.quantity)" /></a-form-item><a-form-item :label="t('orders.request')"><a-textarea v-model:value="request.requestText" :maxlength="2000" /></a-form-item>
    <a-form-item :label="t('orders.recipient')"><a-input v-model:value="request.shippingAddress.recipient" :maxlength="100" /></a-form-item><a-form-item :label="t('orders.phone')"><a-input v-model:value="request.shippingAddress.phone" :maxlength="32" /></a-form-item><a-form-item :label="t('orders.address')"><a-textarea v-model:value="request.shippingAddress.address" :maxlength="500" /></a-form-item>
  </a-form></a-modal>
  <a-modal :open="!!action" :title="t('orders.actions.' + action)" :ok-text="t('orders.confirm')" :cancel-text="t('cancel')" :confirm-loading="busy" @cancel="action = ''" @ok="perform"><a-form layout="vertical" :model="fields">
    <a-alert v-if="action === 'pay_demo'" type="warning" :message="t('orders.demo')" show-icon />
    <a-form-item v-if="action === 'doctor_approve'" :label="t('orders.specification')"><a-textarea v-model:value="fields.productionSpec" :maxlength="2000" /></a-form-item>
    <a-form-item v-if="action === 'doctor_reject' || action === 'qa_fail'" :label="t('orders.reason')"><a-textarea v-model:value="fields.reason" :maxlength="300" /></a-form-item>
    <a-form-item v-if="action === 'start_manufacturing'" :label="t('orders.batch')"><a-input v-model:value="fields.batchRef" :maxlength="100" /></a-form-item>
    <fieldset v-if="action.startsWith('qa_')" class="qa-checks"><legend>{{ t('orders.checks') }}</legend><label v-for="check in (['identity', 'specification', 'finish', 'packaging'] as const)" :key="check"><input type="checkbox" v-model="fields.checks[check]" />{{ t('orders.' + (check === 'specification' ? 'specificationCheck' : check)) }}</label></fieldset>
    <a-form-item v-if="action === 'ship'" :label="t('orders.carrier')"><a-input v-model:value="fields.carrier" :maxlength="100" /></a-form-item><a-form-item v-if="action === 'ship'" :label="t('orders.tracking')"><a-input v-model:value="fields.trackingNumber" :maxlength="100" /></a-form-item>
  </a-form></a-modal>
</template>
