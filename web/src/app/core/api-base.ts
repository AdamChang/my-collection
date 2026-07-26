/**
 * 開發時由 proxy.conf.json 轉發到 localhost:5080，
 * 部署時由 nginx 反代到 api 容器。前端永遠只認 /api。
 */
export const API_BASE = '/api';
