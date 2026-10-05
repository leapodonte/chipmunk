<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import FullCalendar, { type EventSourceFuncInfo } from '@fullcalendar/vue3';
import themePlugin from '@fullcalendar/vue3/themes/monarch';
import timeGridPlugin from '@fullcalendar/vue3/timegrid';
import dayGridPlugin from '@fullcalendar/vue3/daygrid';
import zhLocale from '@fullcalendar/vue3/locales/zh-cn';
import '@fullcalendar/vue3/skeleton.css';
import '@fullcalendar/vue3/themes/monarch/theme.css';
import '@fullcalendar/vue3/themes/monarch/palettes/green.css';
import { ApiError, newIdempotencyKey, type Appointment, type Doctor, type Slot } from '@smilelab/client';
import { client, context, useAction } from '../session';
import ServerPager from './ServerPager.vue';
import { useServerPage } from '../serverPage';

const { t, locale } = useI18n(); const { busy, error, run } = useAction();
const doctors = ref<Doctor[]>([]); const show = ref(false); const calendarRef = ref<InstanceType<typeof FullCalendar> | null>(null);
const bookings = useServerPage<Appointment>(() => 'appointments'); const appointments = bookings.items;
const availability = useServerPage<Slot>(() => 'slots'); const slots = availability.items;
const form = reactive({ doctorId: '', startsAt: '', endsAt: '' }); let createKey = newIdempotencyKey();
const canSchedule = computed(() => context.value?.roles.some(x => ['doctor', 'clinic_manager', 'regional_manager', 'platform_admin'].includes(x)));
const schedulingDoctors = computed(() => doctors.value.filter(doctor => doctor.canSchedule));
const date = (value: string) => new Intl.DateTimeFormat(locale.value === 'en' ? 'en-GB' : 'zh-CN', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }).format(new Date(value));
const columns = computed(() => [ { title: t('appointments.patient'), dataIndex: 'patientName' }, { title: t('appointments.doctor'), dataIndex: 'doctorName' }, { title: t('appointments.time'), key: 'time' }, { title: t('appointments.status'), key: 'status' }, { title: t('appointments.action'), key: 'actions' } ]);
const calendar = computed(() => ({ plugins: [themePlugin, timeGridPlugin, dayGridPlugin], initialView: 'timeGridWeek', height: 480, locale: locale.value === 'en' ? 'en-gb' : zhLocale, headerToolbar: { left: 'prev,next today', center: 'title', right: 'timeGridWeek,dayGridMonth' }, allDaySlot: false, slotMinTime: '07:00:00', slotMaxTime: '21:00:00', events: calendarEvents }));
async function load() { await run(async () => { const [result] = await Promise.all([client.dso<Doctor[]>('doctors'), bookings.load(), availability.load()]); doctors.value = result; if (!form.doctorId) form.doctorId = schedulingDoctors.value[0]?.id ?? '' }); calendarRef.value?.getApi().refetchEvents() }
async function pageBookings(page: number) { await run(() => bookings.load(page)) }
async function pageSlots(page: number) { await run(() => availability.load(page)) }
async function calendarEvents(info: EventSourceFuncInfo) {
  return await run(async () => {
    const rows: Appointment[] = []; const range = new URLSearchParams({ from: info.startStr, to: info.endStr });
    for (let page = 1; page <= 11; page++) {
      const batch = await client.dso<Appointment[]>(`appointments?${range}&page=${page}&pageSize=100`); rows.push(...batch);
      if (rows.length > 1000) throw new ApiError(400, t('appointments.tooMany'), null);
      if (batch.length < 100) return rows.filter(x => x.status !== 'cancelled').map(x => ({ id: x.id, title: `${x.patientName ?? x.patientId} · ${t('state.' + x.status)}`, start: x.startsAt, end: x.endsAt, backgroundColor: x.status === 'booked' ? '#3c9181' : '#92a996' }));
    }
    throw new ApiError(400, t('appointments.tooMany'), null);
  }) ?? [];
}
watch(locale, () => calendarRef.value?.getApi().refetchEvents());
function open() { show.value = true; createKey = newIdempotencyKey() }
async function create() {
  const result = await run(() => client.dso<Slot>('slots', 'POST', { doctorId: form.doctorId, startsAt: new Date(form.startsAt).toISOString(), endsAt: new Date(form.endsAt).toISOString() }, createKey));
  if (result) { show.value = false; await load() }
}
async function transition(appointment: Appointment, status: string) { const result = await run(() => client.dso<Appointment>('appointments/' + appointment.id + '/status', 'POST', { status, version: appointment.version })); if (result) await load() }
async function closeSlot(slot: Slot) { const result = await run(() => client.dso('slots/' + slot.id + '/close', 'POST', {})); if (result) await load() }
onMounted(load);
</script>

<template>
  <div class="page-heading"><div><h2>{{ t('appointments.title') }}</h2><p>{{ t('appointments.description') }}</p></div><div class="heading-actions"><a-button @click="load" :loading="busy">{{ t('refresh') }}</a-button><a-button v-if="canSchedule" type="primary" @click="open">{{ t('appointments.newSlot') }}</a-button></div></div>
  <a-alert v-if="error" :message="error" type="error" show-icon class="spaced" />
  <div class="surface"><div class="surface-title"><h3>{{ t('appointments.schedule') }}</h3></div><FullCalendar ref="calendarRef" :options="calendar" class="calendar" /></div>
  <div class="surface table-overflow"><a-table :columns="columns" :data-source="appointments" row-key="id" :loading="busy" :pagination="false"><template #bodyCell="{ column, record }"><span v-if="column.key === 'time'">{{ date(record.startsAt) }}</span><a-tag v-else-if="column.key === 'status'" :color="record.status === 'booked' ? 'cyan' : 'default'">{{ t('state.' + record.status) }}</a-tag><a-space v-else-if="column.key === 'actions'"><a-button v-if="record.status === 'booked'" size="small" @click="transition(record, 'arrived')">{{ t('appointments.arrive') }}</a-button><a-button v-if="record.status === 'arrived'" size="small" @click="transition(record, 'completed')">{{ t('appointments.complete') }}</a-button><a-popconfirm v-if="['booked', 'arrived'].includes(record.status)" :title="t('appointments.cancel')" :ok-text="t('save')" :cancel-text="t('cancel')" @confirm="transition(record, 'cancelled')"><a-button size="small" type="text" danger>{{ t('cancel') }}</a-button></a-popconfirm></a-space></template></a-table><ServerPager :page="bookings.page.value" :count="appointments.length" :page-size="bookings.pageSize" :busy="busy" @change="pageBookings" /></div>
  <div class="surface table-overflow"><div class="surface-title"><h3>{{ t('appointments.slots') }}</h3></div><a-table :data-source="slots" row-key="id" :pagination="false"><a-table-column :title="t('appointments.doctor')"><template #default="{ record }">{{ doctors.find(d => d.id === record.doctorId)?.name ?? record.doctorId }}</template></a-table-column><a-table-column :title="t('appointments.start')"><template #default="{ record }">{{ date(record.startsAt) }}</template></a-table-column><a-table-column :title="t('appointments.end')"><template #default="{ record }">{{ date(record.endsAt) }}</template></a-table-column><a-table-column v-if="canSchedule" :title="t('appointments.action')"><template #default="{ record }"><a-button v-if="record.available && schedulingDoctors.some(d => d.id === record.doctorId)" size="small" @click="closeSlot(record)">{{ t('close') }}</a-button></template></a-table-column></a-table><ServerPager :page="availability.page.value" :count="slots.length" :page-size="availability.pageSize" :busy="busy" @change="pageSlots" /><p class="footnote">{{ t('appointments.timezone') }}</p></div>
  <a-modal v-model:open="show" :title="t('appointments.newSlot')" :ok-text="t('create')" :cancel-text="t('cancel')" :confirm-loading="busy" :ok-button-props="{ disabled: !form.doctorId || !form.startsAt || !form.endsAt }" @ok="create"><a-form layout="vertical"><a-form-item :label="t('appointments.doctor')"><a-select v-model:value="form.doctorId" :options="schedulingDoctors.map(d => ({ label: d.name, value: d.id }))" /></a-form-item><div class="form-row"><a-form-item :label="t('appointments.start')"><a-input v-model:value="form.startsAt" type="datetime-local" /></a-form-item><a-form-item :label="t('appointments.end')"><a-input v-model:value="form.endsAt" type="datetime-local" /></a-form-item></div></a-form><p class="footnote">{{ t('appointments.timezone') }}</p></a-modal>
</template>
