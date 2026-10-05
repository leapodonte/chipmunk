import { computed, ref } from 'vue';
import { ApiError, SmilelabClient, type Context } from '@smilelab/client';
import { i18n } from './i18n';
export const token = ref<string | null>(null);
export const context = ref<Context | null>(null);
export const client = new SmilelabClient({ token: () => token.value, language: () => i18n.global.locale.value });
export function clearSession() { token.value = null; context.value = null }
export function useAction() {
  const active = ref(0); const busy = computed(() => active.value > 0); const error = ref('');
  async function run<T>(action: () => Promise<T>): Promise<T | undefined> {
    active.value++; error.value = '';
    try { return await action() }
    catch (e) {
      if (e instanceof ApiError) { error.value = e.message; if (e.status === 401) clearSession() }
      else error.value = i18n.global.t('networkError');
      return undefined;
    } finally { active.value-- }
  }
  return { busy, error, run };
}
