import { createApp } from 'vue';
import { Alert, Button, ConfigProvider, Empty, Form, Input, Modal, Popconfirm, Segmented, Select, Space, Table, Tag } from 'ant-design-vue';
import 'ant-design-vue/dist/reset.css';
import App from './App.vue';
import { i18n } from './i18n';
import './style.css';
const app = createApp(App).use(i18n);
for (const component of [Alert, Button, ConfigProvider, Empty, Form, Input, Modal, Popconfirm, Segmented, Select, Space, Table, Tag]) app.use(component);
app.mount('#app');
