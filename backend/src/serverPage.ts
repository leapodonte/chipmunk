import { ref, shallowRef } from 'vue';
import { client } from './session';

// 页面切换只在响应成功后提交；迟到的旧请求不能覆盖新选择的数据。
export function useServerPage<T>(path: () => string, pageSize = 20) {
  const items = shallowRef<T[]>([]); const page = ref(1); let sequence = 0;
  async function load(target = page.value) {
    const request = ++sequence; const endpoint = path();
    const result = await client.dso<T[]>(endpoint + (endpoint.includes('?') ? '&' : '?') + `page=${target}&pageSize=${pageSize}`);
    if (request === sequence) { items.value = result; page.value = target }
  }
  function reset() { sequence++; page.value = 1; items.value = [] }
  return { items, page, pageSize, load, reset };
}
