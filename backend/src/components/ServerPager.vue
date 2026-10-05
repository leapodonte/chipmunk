<script setup lang="ts">
import { useI18n } from 'vue-i18n';
defineProps<{ page: number; count: number; pageSize: number; busy: boolean }>();
defineEmits<{ change: [page: number] }>();
const { t } = useI18n();
</script>
<template>
  <div class="server-pager"><a-button size="small" :disabled="busy || page <= 1" @click="$emit('change', page - 1)">{{ t('pagination.previous') }}</a-button><span>{{ t('pagination.page', { page }) }}</span><a-button size="small" :disabled="busy || count < pageSize || page >= 10000" @click="$emit('change', page + 1)">{{ t('pagination.next') }}</a-button></div>
</template>
<style scoped>.server-pager{display:flex;align-items:center;justify-content:center;gap:12px;margin-top:18px;font-size:12px;color:#74867b;white-space:nowrap}</style>
