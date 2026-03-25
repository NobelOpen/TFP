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
      'Accept-Encoding': 'identity',
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
      console.log(`[${new Date().toLocaleTimeString()}] <- ${proxyRes.statusCode} ${proxyRes.headers['content-type'] || ''}`);
      
      const respHeaders = { ...proxyRes.headers };
      // 只删除 content-encoding（因为我们已要求 identity 不压缩）
      // 保留 transfer-encoding 以支持 chunked 流式传输
      delete respHeaders['content-encoding'];
      
      res.writeHead(proxyRes.statusCode, respHeaders);
      // 禁用 Nagle 算法，减少小数据包的合并延迟（对 SSE 逐事件推送至关重要）
      res.socket?.setNoDelay(true);

      // 手动逐块转发，每收到一个 chunk 立即写入并 flush，不使用 pipe（pipe 有内部缓冲）
      proxyRes.on('data', (chunk) => {
        res.write(chunk);
      });
      proxyRes.on('end', () => {
        res.end();
      });
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
