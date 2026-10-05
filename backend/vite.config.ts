import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
export default defineConfig({ plugins: [vue()], base: '/workspace/', server: { proxy: { '/api': 'https://app.smilelab.ai' } }, build: { sourcemap: false } });
