/**
 * TaskFlow 本地 API 代理
 * 用法：node local_proxy.mjs [端口] [目标域名]
 * 示例：node local_proxy.mjs 9876 api.312800.xyz
 */

import http from 'node:http';
import https from 'node:https';

const PORT = parseInt(process.argv[2]) || 9876;
const TARGET_HOST = process.argv[3] || 'api.312800.xyz';

const server = http.createServer((req, res) => {
  let body = '';
  req.on('data', chunk => body += chunk);
  req.on('end', () => {
    const targetPath = req.url;
    console.log(`[${new Date().toLocaleTimeString()}] ${req.method} ${targetPath}`);

    // 只保留必要的请求头，不转发客户端的原始头部
    const headers = {
      'Host': TARGET_HOST,
      'Content-Type': 'application/json',
      'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36',
      'Accept': 'application/json, text/event-stream',
      'Accept-Encoding': 'gzip, deflate, br',
    };

    // 转发 Authorization 头
    if (req.headers['authorization']) {
      headers['Authorization'] = req.headers['authorization'];
    }

    if (body) {
      headers['Content-Length'] = Buffer.byteLength(body);
    }

    const options = {
      hostname: TARGET_HOST,
      port: 443,
      path: targetPath,
      method: req.method,
      headers: headers,
    };

    const proxyReq = https.request(options, (proxyRes) => {
      console.log(`[${new Date().toLocaleTimeString()}] <- ${proxyRes.statusCode}`);
      
      const respHeaders = { ...proxyRes.headers };
      delete respHeaders['content-encoding'];
      delete respHeaders['transfer-encoding'];
      
      res.writeHead(proxyRes.statusCode, respHeaders);
      proxyRes.pipe(res);
    });

    proxyReq.on('error', (err) => {
      console.error(`[${new Date().toLocaleTimeString()}] Error: ${err.message}`);
      res.writeHead(502, { 'Content-Type': 'text/plain' });
      res.end(`Proxy Error: ${err.message}`);
    });

    if (body) proxyReq.write(body);
    proxyReq.end();
  });
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`PROXY_READY ${PORT} ${TARGET_HOST}`);
});
