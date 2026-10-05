<script setup lang="ts">
import { computed, defineAsyncComponent, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import zhCN from 'ant-design-vue/es/locale/zh_CN';
import enUS from 'ant-design-vue/es/locale/en_US';
import type { Clinic, Context, Login, Role } from '@smilelab/client';
import { client, token, context, clearSession, useAction } from './session';
const AppointmentsPanel = defineAsyncComponent(() => import('./components/AppointmentsPanel.vue'));
const PatientsPanel = defineAsyncComponent(() => import('./components/PatientsPanel.vue'));
const CrmPanel = defineAsyncComponent(() => import('./components/CrmPanel.vue'));
const OperationsPanel = defineAsyncComponent(() => import('./components/OperationsPanel.vue'));

const { t, locale } = useI18n();
const { busy, error, run } = useAction();
const identity = ref('staff:doctor'); const key = ref(''); const clinics = ref<Clinic[]>([]);
const view = ref('appointments');
const has = (...roles: Role[]) => context.value?.roles.some(x => roles.includes(x)) ?? false;
const views = computed(() => [
  ...(has('doctor', 'assistant', 'clinic_manager', 'regional_manager', 'platform_admin') ? ['appointments'] : []),
  ...(has('doctor', 'consultant', 'clinic_manager', 'regional_manager', 'platform_admin') ? ['patients'] : []),
  ...(has('doctor', 'consultant', 'clinic_manager', 'regional_manager', 'platform_operator', 'platform_admin') ? ['crm'] : []),
  ...(has('platform_operator', 'platform_admin') ? ['operations'] : []),
]);
const component = computed(() => ({ appointments: AppointmentsPanel, patients: PatientsPanel, crm: CrmPanel, operations: OperationsPanel })[view.value as 'appointments']);
const currentClinic = computed(() => clinics.value.find(x => x.id === context.value?.clinicId)?.name ?? context.value?.clinicId);

async function login() {
  const result = await run(async () => {
    const auth = await client.mini<Login>('auth/staff-login', 'POST', { identity: identity.value }, { 'X-Staff-Dev-Key': key.value });
    token.value = auth.token;
    try {
      context.value = await client.dso<Context>('context');
      clinics.value = await client.dso<Clinic[]>('clinics/mine');
      view.value = views.value[0] ?? 'appointments';
      return true;
    } catch (e) { clearSession(); throw e }
  });
  if (result) key.value = '';
}
async function switchClinic(value: string) {
  await run(async () => {
    await client.dso('context/switch', 'POST', { clinicId: value });
    context.value = await client.dso<Context>('context');
    if (!views.value.includes(view.value)) view.value = views.value[0] ?? 'appointments';
  });
}
async function logout() { if (busy.value) return; await run(() => client.mini('auth/logout', 'POST', {})); clearSession(); key.value = '' }
</script>

<template>
  <a-config-provider :auto-insert-space-in-button="false" :locale="locale === 'zh-Hans' ? zhCN : enUS" :theme="{ token: { colorPrimary: '#197a71', borderRadius: 9, fontFamily: 'Inter, system-ui, sans-serif' } }">
    <div v-if="!context" class="login-shell">
      <div class="login-story"><div class="brand-mark">S</div><p class="eyebrow">{{ t('workspace') }}</p><h1>{{ t('brand') }}</h1><p class="story-line">{{ t('tagline') }}</p><div class="story-orbit"></div><span class="demo-badge">{{ t('demo') }}</span></div>
      <div class="login-form-wrap"><a-button class="language-button" @click="locale = locale === 'zh-Hans' ? 'en' : 'zh-Hans'">{{ t('language') }}</a-button><div class="login-form"><h2>{{ t('loginTitle') }}</h2><p class="muted">{{ t('loginDescription') }}</p><a-alert v-if="error" :message="error" type="error" show-icon class="spaced" />
        <a-form layout="vertical" :model="{ identity, key }" @finish="login"><a-form-item :label="t('identity')"><a-select v-model:value="identity" :options="[{ value: 'staff:doctor', label: t('doctor') }, { value: 'staff:manager', label: t('manager') }, { value: 'staff:consultant', label: t('consultant') }, { value: 'staff:operator', label: t('operator') }]" /></a-form-item><a-form-item :label="t('staffKey')"><a-input-password v-model:value="key" autocomplete="off" :aria-label="t('staffKey')" /></a-form-item><a-button type="primary" html-type="submit" block size="large" :loading="busy" :disabled="!key">{{ t('login') }}</a-button></a-form>
      </div></div>
    </div>
    <div v-else class="workspace-shell">
      <aside class="sidebar"><div class="sidebar-brand"><span class="brand-mark small">S</span><div><strong>{{ t('brand') }}</strong><small>{{ t('workspace') }}</small></div></div><nav><button v-for="item in views" :key="item" :class="['nav-link', { active: view === item }]" @click="view = item"><span class="nav-dot"></span>{{ t('nav.' + item) }}</button></nav><div class="sidebar-footer"><span class="status-dot"></span>{{ t('demo') }}</div></aside>
      <main class="main-content"><header class="topbar"><div><span class="eyebrow">{{ t('clinic') }}</span><a-select :value="context.clinicId" :options="clinics.map(c => ({ value: c.id, label: c.name }))" :aria-label="t('clinic')" :disabled="busy || clinics.length < 2" class="clinic-select" @change="switchClinic" /></div><div class="topbar-actions"><a-button type="text" @click="locale = locale === 'zh-Hans' ? 'en' : 'zh-Hans'">{{ t('language') }}</a-button><a-button @click="logout">{{ t('logout') }}</a-button></div></header><div class="page-content"><a-alert v-if="error" :message="error" type="error" show-icon class="spaced" /><component :is="component" :key="context.clinicId + ':' + view" :clinic-name="currentClinic" /></div></main>
    </div>
  </a-config-provider>
</template>
